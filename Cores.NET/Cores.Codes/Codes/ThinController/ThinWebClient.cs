// IPA Cores.NET
// 
// Copyright (c) 2019- IPA CyberLab.
// Copyright (c) 2003-2018 Daiyuu Nobori.
// Copyright (c) 2013-2018 SoftEther VPN Project, University of Tsukuba, Japan.
// All Rights Reserved.
// 
// License: The Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
// 
// THIS SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
// 
// THIS SOFTWARE IS DEVELOPED IN JAPAN, AND DISTRIBUTED FROM JAPAN, UNDER
// JAPANESE LAWS. YOU MUST AGREE IN ADVANCE TO USE, COPY, MODIFY, MERGE, PUBLISH,
// DISTRIBUTE, SUBLICENSE, AND/OR SELL COPIES OF THIS SOFTWARE, THAT ANY
// JURIDICAL DISPUTES WHICH ARE CONCERNED TO THIS SOFTWARE OR ITS CONTENTS,
// AGAINST US (IPA CYBERLAB, DAIYUU NOBORI, SOFTETHER VPN PROJECT OR OTHER
// SUPPLIERS), OR ANY JURIDICAL DISPUTES AGAINST US WHICH ARE CAUSED BY ANY KIND
// OF USING, COPYING, MODIFYING, MERGING, PUBLISHING, DISTRIBUTING, SUBLICENSING,
// AND/OR SELLING COPIES OF THIS SOFTWARE SHALL BE REGARDED AS BE CONSTRUED AND
// CONTROLLED BY JAPANESE LAWS, AND YOU MUST FURTHER CONSENT TO EXCLUSIVE
// JURISDICTION AND VENUE IN THE COURTS SITTING IN TOKYO, JAPAN. YOU MUST WAIVE
// ALL DEFENSES OF LACK OF PERSONAL JURISDICTION AND FORUM NON CONVENIENS.
// PROCESS MAY BE SERVED ON EITHER PARTY IN THE MANNER AUTHORIZED BY APPLICABLE
// LAW OR COURT RULE.

// Author: Daiyuu Nobori
// Description

#if CORES_CODES_THINWEBCLIENT

using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Net.WebSockets;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;


using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Codes;
using IPA.Cores.Helper.Codes;
using static IPA.Cores.Globals.Codes;

using IPA.Cores.Web;
using IPA.Cores.Helper.Web;
using static IPA.Cores.Globals.Web;

using IPA.Cores.Helper.GuaHelper;

using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Mvc.Rendering;
using IPA.App.ThinWebClientApp;

namespace IPA.Cores.Codes
{
    // ThinWebClient 設定 (JSON 設定ファイルで動的に設定変更可能な設定項目)
    [Serializable]
    public sealed class ThinWebClientSettings : INormalizable
    {
        public bool Debug_EnableGuacdMode = false;

        public bool Debug_GuacdMode_ProxyPortListenAllowAny = false;
        public string Debug_GuacdMode_GuacdHostname = "";
        public int Debug_GuacdPort;

        public List<string> ThinControllerUrlList = new List<string>();

        public ThinWebClientSettings()
        {
        }

        public void Normalize()
        {
            if (this.ThinControllerUrlList.Count == 0)
            {
                this.ThinControllerUrlList.Add("https://specify_address_here.example.org/thincontrol/");
            }

            if (this.Debug_GuacdMode_GuacdHostname._IsEmpty())
            {
                this.Debug_GuacdMode_GuacdHostname = "none";
            }

            if (this.Debug_GuacdPort == 0)
            {
                this.Debug_GuacdPort = 4822;
            }
        }
    }

#pragma warning disable CS1998 // 非同期メソッドは、'await' 演算子がないため、同期的に実行されます
    // ThinWebClient の動作をカスタマイズ可能なフック抽象クラス
    public abstract class ThinWebClientHookBase
    {
    }
#pragma warning restore CS1998 // 非同期メソッドは、'await' 演算子がないため、同期的に実行されます

    // サーバー接続プロファイル
    public class ThinWebClientProfile : INormalizable
    {
        public string Pcid { get; set; } = ""; // 接続先コンピュータ ID
        public GuaPreference Preference { get; set; } = new GuaPreference(); // 接続設定

        public void Normalize()
        {
            this.Pcid = Str.NormalizeString(this.Pcid, false, true, false, true);
            this.Preference.Normalize();
        }

        public ThinWebClientProfile CloneAsDefault()
        {
            ThinWebClientProfile ret = this._CloneWithJson();

            ret.Pcid = "";

            ret.Preference = ret.Preference.CloneAsDefault();

            ret.Normalize();

            return ret;
        }
    }

    // ヒストリ
    public class ThinWebClientHistory
    {
        public List<ThinWebClientProfile> Items = new List<ThinWebClientProfile>();

        public void Add(ThinWebClientProfile profile)
        {
            var clone = profile._CloneWithJson();
            clone.Normalize();
            if (clone.Pcid._IsEmpty()) return;

            var deleteList = this.Items.Where(x => x.Pcid._IsSamei(clone.Pcid)).ToList();
            deleteList.ForEach(x => this.Items.Remove(x));

            this.Items.Add(clone);

            while (this.Items.Count >= 1 && this.Items.Count > ThinWebClientConsts.MaxHistory)
            {
                this.Items.RemoveAt(0);
            }
        }

        public void Clear()
        {
            this.Items.Clear();
        }

        public void SaveToCookie(Controller c, AspNetCookieOptions? options = null)
        {
            for (int i = 0; i < ThinWebClientConsts.MaxHistory; i++)
            {
                var item = this.Items.ElementAtOrDefault(i);
                string tagName = $"thin_history_{i:D4}";
                if (item != null)
                {
                    c._EasySaveCookie(tagName, item, options, true);
                }
                else
                {
                    c._EasyDeleteCookie(tagName);
                }
            }
        }

        public static ThinWebClientHistory LoadFromCookie(Controller c)
        {
            ThinWebClientHistory ret = new ThinWebClientHistory();

            for (int i = 0; i < ThinWebClientConsts.MaxHistory; i++)
            {
                string tagName = $"thin_history_{i:D4}";
                var data = c._EasyLoadCookie<ThinWebClientProfile>(tagName, true);
                if (data != null)
                {
                    ret.Items.Add(data);
                }
            }

            return ret;
        }
    }

    public class ThinWebClientModelIndex
    {
        public ThinWebClientProfile CurrentProfile { get; set; } = new ThinWebClientProfile();

        // リサイズ方式の選択肢
        public List<SelectListItem> ResizeMethodItems { get; }

        // キーボードの選択肢
        public List<SelectListItem> KeyboardLayoutItems { get; }

        // History 選択肢
        public List<SelectListItem> HistoryItems { get; private set; } = new List<SelectListItem>();

        // 選択されている History
        public string? SelectedHistory { get; set; }

        public bool FocusToPcid { get; set; } = false;

        public ThinWebClientModelIndex()
        {
            this.ResizeMethodItems = new List<SelectListItem>();
            foreach (var item in GuaHelper.GetResizeMethodList())
            {
                this.ResizeMethodItems.Add(new SelectListItem(item.Item2, item.Item1));
            }

            this.KeyboardLayoutItems = new List<SelectListItem>();
            foreach (var item in GuaHelper.GetKeyboardLayoutList())
            {
                this.KeyboardLayoutItems.Add(new SelectListItem(item.Item2, item.Item1));
            }
        }

        public void FillHistory(ThinWebClientHistory history)
        {
            this.HistoryItems = new List<SelectListItem>();

            this.HistoryItems.Add(new SelectListItem("", ""));

            foreach (var item in history.Items.AsEnumerable().Reverse())
            {
                this.HistoryItems.Add(new SelectListItem(item.Pcid, $"/?id={item.Pcid._MakeVerySafeAsciiOnlyNonSpaceFileName()}"));
            }
        }
    }

    public abstract class ThinWebClientModelSessionBase
    {
        public string? SessionId { get; set; }
        public string? RequestId { get; set; }
        public ThinClientConnectOptions? ConnectOptions { get; set; }
        public ThinWebClientProfile? Profile { get; set; }
    }

    public class ThinWebClientModelOtp : ThinWebClientModelSessionBase
    {
    }

    public class ThinWebClientModelSessionAuthPassword : ThinWebClientModelSessionBase
    {
        public ThinClientAuthRequest? Request { get; set; }
        public ThinClientAuthResponse? Response { get; set; }
    }

    public class ThinWebClientModelRemote : ThinWebClientModelSessionBase
    {
        public string? WebSocketUrl { get; set; }
        public string? ConnectPacketData { get; set; }
        public ThinSvcType SvcType { get; set; }
    }

    public class ThinWebClientController : Controller
    {
        public ThinWebClient Client { get; }
        public PageContext Page { get; }

        public StrTableLanguage Language => Page.Language;
        public StrTable StrTable => Page.StrTable;

        public ThinWebClientController(ThinWebClient client, PageContext page)
        {
            this.Client = client;
            this.Page = page;

            this.Page.SetLanguageList(client.LanguageList);
            this.Page.SetLanguageByHttpString("ja");
        }

        protected AspNetCookieOptions GetCookieOption() => new AspNetCookieOptions(domain: ""); // Cookie のドメイン名は空文字 "" とする。これが最も安全である。参照: https://blog.tokumaru.org/2011/10/cookiedomain.html

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> IndexAsync(ThinWebClientModelIndex form, string? id, string? deleteAll, string? button_wol)
        {
            ThinWebClientProfile? historySelectedProfile = null;

            ThinWebClientProfile profile = form.CurrentProfile;
            profile.Normalize();

            ThinWebClientHistory history = ThinWebClientHistory.LoadFromCookie(this);

            if (this._IsPostBack())
            {
                if (profile.Pcid._IsFilled())
                {
                    // 現在のプロファイルの保存
                    this._EasySaveCookie("thin_current_profile", profile.CloneAsDefault(), GetCookieOption(), true);

                    // ヒストリへの追加
                    history.Add(profile);
                    history.SaveToCookie(this, GetCookieOption());

                    var tc = this.Client.CreateThinClient();

                    var clientIp = Request.HttpContext.Connection.RemoteIpAddress._UnmapIPv4()!;
                    var clientPort = Request.HttpContext.Connection.RemotePort;
                    string clientFqdn = await Client.DnsResolver.GetHostNameSingleOrIpAsync(clientIp);

                    if (button_wol._ToBool() == false)
                    {
                        // 普通の接続
                        WideTunnelClientOptions wideOptions = new WideTunnelClientOptions(WideTunnelClientFlags.None, clientIp.ToString(), clientFqdn, clientPort);

                        // セッションの開始
                        var session = tc.StartConnect(new ThinClientConnectOptions(profile.Pcid, clientIp, clientFqdn, this.Client.SettingsFastSnapshot.Debug_EnableGuacdMode, wideOptions, profile.Preference, profile._CloneWithJson()));
                        string sessionId = session.SessionId;

                        // セッション ID をもとにした URL にリダイレクト
                        return Redirect($"/ThinWebClient/Session/{sessionId}/?id={profile.Pcid._MakeVerySafeAsciiOnlyNonSpaceFileName()}");
                    }
                    else
                    {
                        // WoL 信号の発射
                    }
                }
            }
            else
            {
                if (deleteAll._ToBool())
                {
                    // History をすべて消去するよう指示された
                    // Cookie の History をすべて消去する
                    history.Clear();
                    history.SaveToCookie(this, GetCookieOption());

                    // トップページにリダイレクトする
                    return Redirect("/");
                }
                else if (id._IsFilled())
                {
                    // History から履歴を指定された。id を元に履歴からプロファイルを読み出す
                    historySelectedProfile = history.Items.Where(h => h.Pcid._IsSamei(id)).FirstOrDefault();
                }

                if (historySelectedProfile == null)
                {
                    // デフォルト値
                    profile = this._EasyLoadCookie<ThinWebClientProfile>("thin_current_profile", true) ?? new ThinWebClientProfile();
                }
                else
                {
                    // History で選択された値
                    profile = historySelectedProfile;
                }

                profile.Normalize();
                form.CurrentProfile = profile;

                // GET の場合は必ず PCID 入力ボックスをフォーカスする
                form.FocusToPcid = true;
            }

            form.FillHistory(history);

            if (historySelectedProfile != null)
            {
                form.SelectedHistory = form.HistoryItems.Where(x => x.Text._IsSamei(historySelectedProfile.Pcid)).FirstOrDefault()?.Value ?? "";
            }

            return View(form);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> RemoteAsync(string? id)
        {
            var cancel = Request._GetRequestCancellationToken();
            id = id._NonNullTrim();

            // 指定されたセッション ID を元に検索
            var session = this.Client.SessionManager.GetSessionById(id);

            if (session != null)
            {
                ThinClientConnectOptions connectOptions = (ThinClientConnectOptions)session.Param!;
                ThinWebClientProfile profile = (ThinWebClientProfile)connectOptions.AppParams!;

                var req = session.GetFinalAnswerRequest();

                if (req != null)
                {
                    ThinWebClientModelRemote main = new ThinWebClientModelRemote()
                    {
                        ConnectOptions = connectOptions,
                        WebSocketUrl = connectOptions.WebSocketUrl,
                        SessionId = session.SessionId,
                        Profile = profile,
                        SvcType = connectOptions.ConnectedSvcType!.Value,
                        ConnectPacketData = connectOptions.ConnectPacketData,
                    };

                    return View(main);
                }
            }

            await TaskCompleted;
            return Redirect("/");
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> SessionAsync(string? id, string? requestid, string? formtype, string? password, string? username, string ?otp)
        {
            var cancel = Request._GetRequestCancellationToken();
            id = id._NonNullTrim();

            // 指定されたセッション ID を元に検索
            var session = this.Client.SessionManager.GetSessionById(id);

            if (session == null)
            {
                // セッション ID が見つからない。トップページに戻る
                return Redirect("/");
            }
            else
            {
                ThinClientConnectOptions connectOptions = (ThinClientConnectOptions)session.Param!;
                ThinWebClientProfile profile = (ThinWebClientProfile)connectOptions.AppParams!;

                if (requestid._IsFilled() && formtype._IsFilled())
                {
                    IDialogResponseData? responseData = null;
                    switch (formtype)
                    {
                        case "SessionAuthPassword":
                            responseData = new ThinClientAuthResponse { Username = "", Password = password._NonNull() };
                            break;

                        case "SessionAuthAdvanced":
                            responseData = new ThinClientAuthResponse { Username = username._NonNull(), Password = password._NonNull() };
                            break;

                        case "SessionOtp":
                            responseData = new ThinClientOtpResponse { Otp = otp._NonNull() };
                            break;
                    }
                    if (responseData != null)
                    {
                        session.SetResponseData(requestid, responseData);
                    }
                }

                L_GET_NEXT:
                var req = await session.GetNextRequestAsync(cancel: cancel);

                if (req != null)
                {
                    session.SendHeartBeat(req.RequestId);

                    switch (req.RequestData)
                    {
                        case ThinClientAuthRequest authReq:
                            switch (authReq.AuthType)
                            {
                                case ThinAuthType.None:
                                    session.SetResponseData(req.RequestId, new ThinClientAuthResponse());
                                    goto L_GET_NEXT;

                                case ThinAuthType.Password:
                                    ThinWebClientModelSessionAuthPassword page = new ThinWebClientModelSessionAuthPassword
                                    {
                                        SessionId = session.SessionId,
                                        RequestId = req.RequestId,
                                        ConnectOptions = connectOptions,
                                        Request = authReq,
                                        Profile = profile._CloneWithJson(),
                                    };

                                    return View("SessionAuthPassword", page);

                                case ThinAuthType.Advanced:
                                    ThinWebClientModelSessionAuthPassword page2 = new ThinWebClientModelSessionAuthPassword
                                    {
                                        SessionId = session.SessionId,
                                        RequestId = req.RequestId,
                                        ConnectOptions = connectOptions,
                                        Request = authReq,
                                        Profile = profile._CloneWithJson(),
                                    };

                                    return View("SessionAuthAdvanced", page2);

                                default:
                                    throw new CoresException($"authReq.AuthType = {authReq.AuthType}: Unsupported auth type.");
                            }

                        case ThinClientOtpRequest otpReq:
                            ThinWebClientModelOtp page3 = new ThinWebClientModelOtp
                            {
                                SessionId = session.SessionId,
                                RequestId = req.RequestId,
                                ConnectOptions = connectOptions,
                                Profile = profile._CloneWithJson(),
                            };

                            return View("SessionOtp", page3);

                        case ThinClientAcceptReadyNotification ready:
                            if (connectOptions.DebugGuacMode)
                            {
                                connectOptions.UpdateConnectedSvcType(ready.FirstConnection!.SvcType);
                            }
                            else
                            {
                                connectOptions.UpdateConnectedSvcType(ready.SvcType!.Value);
                                ready.WebSocketFullUrl._NotEmptyCheck();
                                connectOptions.UpdateWebSocketUrl(ready.WebSocketFullUrl);
                                connectOptions.UpdateConnectPacketData(ready.ConnectPacketData);
                            }

                            if (ready.WebSocketFullUrl._IsFilled())
                            {
                                $"ready.WebSocketFullUrl = {ready.WebSocketFullUrl?.ToString()}"._Debug();
                            }
                            else
                            {
                                $"ready.ListenEndPoint = {ready.ListenEndPoint?.ToString()}"._Debug();
                            }
                            req.SetResponseDataEmpty();

                            return Redirect($"/ThinWebClient/Remote/{session.SessionId}/");

                        case ThinClientInspectRequest inspect:
                            ThinClientInspectResponse insRes = new ThinClientInspectResponse
                            {
                                AntiVirusOk = true,
                                Ticket = "",
                                WindowsUpdateOk = true,
                                MacAddressList = Str.NormalizeMac(profile.Preference.MacAddress, style: MacAddressStyle.Windows),
                            };

                            session.SetResponseData(req.RequestId, insRes);
                            goto L_GET_NEXT;

                        default:
                            throw new CoresException($"Unknown request data: {req.RequestData.GetType().ToString()}");
                    }
                }
                else
                {
                    Dbg.Where();
                }
            }

            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> SendHeartBeatAsync(string sessionId, string requestId)
        {
            sessionId = sessionId._NonNullTrim();
            requestId = requestId._NonNullTrim();

            string ret = "error";

            if (sessionId._IsFilled() && requestId._IsFilled())
            {
                if (this.Client.SessionManager.SendHeartBeat(sessionId, requestId))
                {
                    ret = "ok";
                }
            }

            await TaskCompleted;

            return new TextActionResult(ret);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> SessionHealthCheckAsync(string sessionId)
        {
            sessionId = sessionId._NonNullTrim();

            string ret = "error";

            if (sessionId._IsFilled())
            {
                if (this.Client.SessionManager.CheckSessionHealth(sessionId))
                {
                    ret = "ok";
                }
            }

            await TaskCompleted;

            return new TextActionResult(ret);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        [HttpGet("/ws")]
        [HttpPost("/ws")]
        public async Task AcceptWebSocketAsync(string? id, string? width, string? height)
        {
            await using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken cancel, this._GetRequestCancellationToken(), this.Client.GrandCancel))
            {
                if (HttpContext.WebSockets.IsWebSocketRequest)
                {
                    // 指定されたセッション ID を元に検索
                    var session = this.Client.SessionManager.GetSessionById(id._NonNull());

                    if (session != null)
                    {
                        ThinClientConnectOptions connectOptions = (ThinClientConnectOptions)session.Param!;
                        ThinWebClientProfile profile = (ThinWebClientProfile)connectOptions.AppParams!;

                        if (connectOptions.DebugGuacMode == false)
                        {
                            throw new CoresLibException("connectOptions.DebugGuacMode == false");
                        }

                        var req = session.GetFinalAnswerRequest();

                        if (req?.RequestData is ThinClientAcceptReadyNotification ready)
                        {
                            var pref = profile.Preference._CloneWithJson();

                            // ws 接続時に width と height がパラメータとして指定されていた場合は、preference の内容を更新する
                            int widthInt = width._ToInt();
                            int heightInt = height._ToInt();

                            if (widthInt >= 1 && heightInt >= 1)
                            {
                                pref.ScreenWidth = widthInt;
                                pref.ScreenHeight = heightInt;
                            }
                            
                            await using var guaClient = new GuaClient(
                                new GuaClientSettings(
                                    Client.SettingsFastSnapshot.Debug_GuacdMode_GuacdHostname,
                                    Client.SettingsFastSnapshot.Debug_GuacdPort,
                                    ready.FirstConnection!.SvcType.ToString().StrToGuaProtocol(),
                                    "", ready.ListenEndPoint!.Port,
                                    //"dn-ttwin1.sec.softether.co.jp", 3389, // testtest
                                    pref));

                            var readyPacket = await guaClient.StartAsync(cancel);

                            await using var gcStream = guaClient.Stream._NullCheck();

                            using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync("guacamole");

                            //if (true)
                            //{
                            //    MemoryStream ms = new MemoryStream();
                            //    await readyPacket.SendPacketAsync(ms);
                            //    //var tmp = ms.ToArray();
                            //    var tmp = "6.hello;"._GetBytes_Ascii();
                            //    await webSocket.SendAsync(tmp, WebSocketMessageType.Text, true, cancel);
                            //}

                            await GuaWebSocketUtil.RelayBetweenWebSocketAndStreamDuplex(gcStream, webSocket, cancel: cancel);

                            return;
                        }
                    }
                }

                HttpContext.Response.StatusCode = 400;
            }
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new AspNetErrorModel(this));
        }
    }

    public class ThinWebClient : AsyncService
    {
        // Hive
        readonly HiveData<ThinWebClientSettings> SettingsHive;

        // 設定へのアクセスを容易にするための自動プロパティ
        CriticalSection ManagedSettingsLock => SettingsHive.DataLock;
        ThinWebClientSettings ManagedSettings => SettingsHive.ManagedData;

        public ThinWebClientSettings SettingsFastSnapshot => SettingsHive.CachedFastSnapshot;

        public DnsResolver DnsResolver { get; }

        public DateTimeOffset BootDateTime { get; } = DtOffsetNow;

        public long BootTick { get; } = TickNow;

        public ThinWebClientHookBase Hook { get; }

        public DialogSessionManager SessionManager { get; }

        public StrTableLanguageList LanguageList { get; private set; } = null!;

        public ThinWebClient(ThinWebClientSettings settings, ThinWebClientHookBase hook, Func<ThinWebClientSettings>? getDefaultSettings = null)
        {
            try
            {
                this.Hook = hook;

                this.SettingsHive = new HiveData<ThinWebClientSettings>(
                    Hive.SharedLocalConfigHive,
                    "ThinWebClient",
                    getDefaultSettings,
                    HiveSyncPolicy.AutoReadWriteFile,
                    HiveSerializerSelection.RichJson);

                this.DnsResolver = new DnsClientLibBasedDnsResolver(new DnsResolverSettings());

                this.SessionManager = new DialogSessionManager(new DialogSessionManagerOptions(sessionIdPrefix: "thin"), cancel: this.GrandCancel);
            }
            catch (Exception ex)
            {
                this._DisposeSafe(ex);
                throw;
            }
        }

        public void SetLanguageList(StrTableLanguageList list)
        {
            this.LanguageList = list;
        }

        public ThinClient CreateThinClient()
        {
            AddressFamily listenFamily = AddressFamily.InterNetwork;
            IPAddress listenAddress = IPAddress.Loopback;

            if (this.SettingsFastSnapshot.Debug_GuacdMode_ProxyPortListenAllowAny)
            {
                listenAddress = IPAddress.Any;
            }

            ThinClient tc = new ThinClient(new ThinClientOptions(new WideTunnelOptions("DESK", nameof(ThinWebClient), this.SettingsFastSnapshot.ThinControllerUrlList), this.SessionManager,
                listenFamily, listenAddress));

            return tc;
        }

        protected override async Task CleanupImplAsync(Exception? ex)
        {
            try
            {
                await this.DnsResolver._DisposeSafeAsync();

                await this.SessionManager._DisposeSafeAsync();

                this.SettingsHive._DisposeSafe();
            }
            finally
            {
                await base.CleanupImplAsync(ex);
            }
        }
    }
}

#endif

