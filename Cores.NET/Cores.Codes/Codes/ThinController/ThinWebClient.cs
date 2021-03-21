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

#if CORES_CODES_THINCONTROLLER

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

using System.Runtime.CompilerServices;

namespace IPA.Cores.Codes
{
    // ThinWebClient 設定 (JSON 設定ファイルで動的に設定変更可能な設定項目)
    [Serializable]
    public sealed class ThinWebClientSettings : INormalizable
    {
        public string ABC = "";
        public bool ProxyPortListenAllowAny = false;
        public string GuacdHostname = "dn-ttguacd1.sec.softether.co.jp";
        public int GuacdPort = 4822;

        public ThinWebClientSettings()
        {
        }

        public void Normalize()
        {
        }
    }

#pragma warning disable CS1998 // 非同期メソッドは、'await' 演算子がないため、同期的に実行されます
    // ThinWebClient の動作をカスタマイズ可能なフック抽象クラス
    public abstract class ThinWebClientHookBase
    {
    }
#pragma warning restore CS1998 // 非同期メソッドは、'await' 演算子がないため、同期的に実行されます

    public class ThinWebClientModelStart
    {
        public string? Pcid { get; set; } = "dn-ttwin1";
    }

    public abstract class ThinWebClientModelSessionBase
    {
        public string? RequestId { get; set; }
        public ThinClientConnectOptions? ConnectOptions { get; set; }
    }

    public class ThinWebClientModelSessionAuth : ThinWebClientModelSessionBase
    {
        public ThinClientAuthRequest? Request { get; set; }
        public ThinClientAuthResponse? Response { get; set; }
    }

    public class ThinWebClientController : Controller
    {
        public ThinWebClient Client { get; }

        public ThinWebClientController(ThinWebClient client)
        {
            this.Client = client;
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> StartAsync(ThinWebClientModelStart form)
        {
            string? pcid = form?.Pcid._NonNullTrim();

            if (this._IsPostBack())
            {
                if (pcid._IsFilled())
                {
                    var tc = this.Client.CreateThinClient();

                    var clientIp = Request.HttpContext.Connection.RemoteIpAddress._UnmapIPv4()!;
                    string clientFqdn = await Client.DnsResolver.GetHostNameSingleOrIpAsync(clientIp);

                    // セッションの開始
                    var session = tc.StartConnect(new ThinClientConnectOptions(pcid, clientIp, clientFqdn));
                    string sessionId = session.SessionId;

                    // セッション ID をもとにした URL にリダイレクト
                    return Redirect($"/ThinWebClient/Session/{sessionId}/");
                }
            }

            return View(form);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> SessionAsync(string? id, string? requestid, string? formtype, string? password)
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

                if (requestid._IsFilled() && formtype._IsFilled())
                {
                    IDialogResponseData? responseData = null;
                    switch (formtype.ToLower())
                    {
                        case "auth":
                            responseData = new ThinClientAuthResponse { Username = "", Password = password._NonNull() };
                            break;
                    }
                    if (responseData != null)
                    {
                        session.SetResponseData(requestid, responseData);
                    }
                }

                var req = await session.GetNextRequestAsync(cancel: cancel);

                if (req != null)
                {
                    session.SendHeartBeat(req.RequestId);

                    switch (req.RequestData)
                    {
                        case ThinClientAuthRequest authReq:
                            ThinWebClientModelSessionAuth page = new ThinWebClientModelSessionAuth
                            {
                                RequestId = req.RequestId,
                                ConnectOptions = connectOptions,
                                Request = authReq,
                            };

                            return View("SessionAuth", page);

                        case ThinClientAcceptReadyNotification ready:
                            ready.ListenEndPoint?.ToString()._Debug();
                            req.SetResponseDataEmpty();
                            break;

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

        public ThinClient CreateThinClient()
        {
            AddressFamily listenFamily = AddressFamily.InterNetwork;
            IPAddress listenAddress = IPAddress.Loopback;

            if (this.SettingsFastSnapshot.ProxyPortListenAllowAny)
            {
                listenAddress = IPAddress.Any;
            }

            ThinClient tc = new ThinClient(new ThinClientOptions(new WideTunnelOptions("DESK", nameof(ThinWebClient), StrList("https://pc34.sehosts.com/thincontrol/")), this.SessionManager,
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

