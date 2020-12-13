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
    // ThinController 設定 (アプリの起動時にコード中から設定可能な設定項目)
    public static class ThinControllerBasicSettings
    {
        public static Action<HttpRequestRateLimiterOptions<HttpRequestRateLimiterHashKeys.SrcIPAddress>> ConfigureHttpRequestRateLimiterOptions = _ => { };
    }

    // ThinController 設定 (JSON 設定ファイルで動的に設定変更可能な設定項目)
    [Serializable]
    public sealed class ThinControllerSettings : INormalizable
    {
        public List<string> WpcPathList = new List<string>();

        public string DbConnectionString_Read = "";
        public string DbConnectionString_Write = "";

        public ThinControllerSettings()
        {
        }

        public void Normalize()
        {
            if (this.WpcPathList._IsEmpty())
            {
                this.WpcPathList = new List<string>();

                this.WpcPathList.Add("/widecontrol/");
                this.WpcPathList.Add("/thincontrol/");
            }

            if (this.DbConnectionString_Read._IsEmpty())
            {
                this.DbConnectionString_Read = "Data Source=127.0.0.1;Initial Catalog=THIN;Persist Security Info=True;User ID=thin_read;Password=password1;";
            }

            if (this.DbConnectionString_Write._IsEmpty())
            {
                this.DbConnectionString_Write = "Data Source=127.0.0.1;Initial Catalog=THIN;Persist Security Info=True;User ID=thin_write;Password=password2;";
            }
        }
    }

    public class ThinController : AsyncService
    {
        public class ThinControllerSessionClientInfo
        {
            public ThinControllerServiceType ServiceType { get; }

            public HttpEasyContextBox? Box { get; }

            public string ClientPhysicalIp { get; }
            public string ClientIp { get; }
            public string FlagStr { get; }
            public bool IsProxyMode { get; }

            public ThinControllerSessionFlags Flags { get; }

            public ThinControllerSessionClientInfo(HttpEasyContextBox box, ThinControllerServiceType serviceType)
            {
                this.ServiceType = serviceType;

                this.Box = box;

                this.ClientPhysicalIp = box.RemoteEndpoint.Address.ToString();

                if (this.ServiceType == ThinControllerServiceType.ApiServiceForGateway)
                {
                    this.ClientIp = box.Request.Headers._GetStrFirst("X-WG-Proxy-SrcIP", this.ClientPhysicalIp);

                    if (this.ClientPhysicalIp != this.ClientIp)
                    {
                        this.IsProxyMode = true;
                    }
                }
                else
                {
                    this.ClientIp = this.ClientPhysicalIp;
                }

                this.FlagStr = box.QueryStringList._GetStrFirst("flag");

                if (this.FlagStr._InStr("limited", true))
                {
                    this.Flags |= ThinControllerSessionFlags.LimitedMode;
                }
            }
        }

        public class ThinControllerSession : IDisposable, IAsyncDisposable
        {
            static RefLong IdSeed = new RefLong();

            public string Uid { get; }
            public ThinController Controller { get; }
            public ThinControllerSessionClientInfo ClientInfo { get; }

            public string FunctionName { get; private set; } = "Unknown";

            public ThinControllerSession(ThinController controller, ThinControllerSessionClientInfo clientInfo)
            {
                try
                {
                    this.Uid = Str.NewUid("REQ", '_') + "_" + IdSeed.Increment().ToString("D12");
                    this.Controller = controller;
                    this.ClientInfo = clientInfo;
                }
                catch
                {
                    this._DisposeSafe();
                    throw;
                }
            }

            // テスト
            public WpcResult ProcTest(WpcPack req, CancellationToken cancel)
            {
                Pack p = new Pack();

                p.AddStr("test", req.Pack["1"].Int64Value.ToString());

                return new WpcResult(VpnErrors.ERR_SECURITY_ERROR);
            }

            // 通信テスト
            public WpcResult ProcCommCheck(WpcPack req, CancellationToken cancel)
            {
                Pack p = new Pack();

                p.AddStr("retstr", req.Pack["str"].StrValue._NonNull());

                return NewWpcResult(p);
            }

            // Gate から: セッションリストの報告
            public WpcResult ProcReportSessionList(WpcPack req, CancellationToken cancel)
            {
                Pack p = req.Pack;

                int numSessions = p["NumSession"].SIntValueSafeNum;

                Dictionary<string, ThinSession> sessionList = new Dictionary<string, ThinSession>();

                for (int i = 0; i < numSessions; i++)
                {
                    ThinSession sess = new ThinSession
                    {
                        Msid = p["Msid", i].StrValueNonNull,
                        SessionId = p["SessionId", i].DataValueHexStr,
                        EstablishedDateTime = Util.ConvertDateTime(p["EstablishedDateTime", i].Int64Value).ToLocalTime(),
                        IpAddress = p["IpAddress", i].StrValueNonNull,
                        HostName = p["Hostname", i].StrValueNonNull,
                        NumClients = p["NumClients", i].SIntValue,
                        ServerMask64 = p["ServerMask64", i].Int64Value,
                    };

                    sess.Validate();

                    sessionList.TryAdd(sess.SessionId, sess);
                }

                ThinGate gate = new ThinGate
                {
                    GateId = p["GateId"].DataValueHexStr,
                    IpAddress = this.ClientInfo.ClientPhysicalIp,
                    Port = p["Port"].SIntValue,
                    HostName = this.ClientInfo.ClientPhysicalIp,
                    Performance = p["Performance"].SIntValue,
                    NumSessions = sessionList.Count,
                    Build = p["Build"].SIntValue,
                    MacAddress = p["MacAddress"].StrValueNonNull,
                    OsInfo = p["OsInfo"].StrValueNonNull,
                };

                int numSc = (int)Math.Min(p.GetCount("SC_SessionId"), 65536);

                Dictionary<string, HashSet<string>> sessionAndClientTable = new Dictionary<string, HashSet<string>>();
                for (int i = 0; i < numSc; i++)
                {
                    string sessionId = p["SC_SessionId", i].DataValueHexStr;
                    string clientId = p["SC_ClientID", i].DataValueHexStr;

                    if (sessionId.Length == 40 && clientId.Length == 40)
                    {
                        sessionId = sessionId.ToUpper();
                        clientId = clientId.ToUpper();

                        sessionAndClientTable._GetOrNew(sessionId, () => new HashSet<string>()).Add(clientId);
                    }
                }

                foreach (var item in sessionAndClientTable)
                {
                    var sess = sessionList._GetOrDefault(item.Key);
                    if (sess != null) sess.NumClientsUnique++;
                }

                var now = DtNow;

                Controller.SessionManager.UpdateGateAndReportSessions(now, now.AddMilliseconds(p["EntryExpires"].SIntValue), gate, sessionList.Values);

                var ret = NewWpcResult();

                ret.AdditionalInfo.Add("Gate", gate._GetObjectDumpForJsonFriendly());

                string? secretKey = Controller.Db.GetVarString("ControllerGateSecretKey");
                if (secretKey._IsFilled()) ret.Pack.AddStr("ControllerGateSecretKey", secretKey);

                return ret;
            }

            // Gate から: 1 本のセッションの追加の報告
            public WpcResult ProcReportSessionAdd(WpcPack req, CancellationToken cancel)
            {
                Pack p = req.Pack;

                int i = 0;
                ThinSession sess = new ThinSession
                {
                    Msid = p["Msid", i].StrValueNonNull,
                    SessionId = p["SessionId", i].DataValueHexStr,
                    EstablishedDateTime = Util.ConvertDateTime(p["EstablishedDateTime", i].Int64Value).ToLocalTime(),
                    IpAddress = p["IpAddress", i].StrValueNonNull,
                    HostName = p["Hostname", i].StrValueNonNull,
                    NumClients = p["NumClients", i].SIntValue,
                    ServerMask64 = p["ServerMask64", i].Int64Value,
                };

                sess.Validate();

                // gate のフィールド内容は UpdateGateAndAddSession でそれほど参照されないので適当で OK
                ThinGate gate = new ThinGate
                {
                    GateId = p["GateId"].DataValueHexStr,
                    IpAddress = this.ClientInfo.ClientPhysicalIp,
                    Port = p["Port"].SIntValue,
                    HostName = this.ClientInfo.ClientPhysicalIp,
                    Performance = p["Performance"].SIntValue,
                    NumSessions = 0,
                    Build = p["Build"].SIntValue,
                    MacAddress = p["MacAddress"].StrValueNonNull,
                    OsInfo = p["OsInfo"].StrValueNonNull,
                };

                sess.NumClientsUnique = sess.NumClients == 0 ? 0 : 1;

                var now = DtNow;

                Controller.SessionManager.UpdateGateAndAddSession(now, now.AddMilliseconds(p["EntryExpires"].SIntValue), gate, sess);

                var ret = NewWpcResult();

                ret.AdditionalInfo.Add("Gate", gate._GetObjectDumpForJsonFriendly());
                ret.AdditionalInfo.Add("AddSession", sess._GetObjectDumpForJsonFriendly());

                string? secretKey = Controller.Db.GetVarString("ControllerGateSecretKey");
                if (secretKey._IsFilled()) ret.Pack.AddStr("ControllerGateSecretKey", secretKey);

                return ret;
            }

            // Gate から: 1 本のセッションの削除の報告
            public WpcResult ProcReportSessionDel(WpcPack req, CancellationToken cancel)
            {
                Pack p = req.Pack;

                string sessionId = p["SessionId"].DataValueHexStr;

                // gate のフィールド内容は UpdateGateAndAddSession でそれほど参照されないので適当で OK
                ThinGate gate = new ThinGate
                {
                    GateId = p["GateId"].DataValueHexStr,
                    IpAddress = this.ClientInfo.ClientPhysicalIp,
                    Port = p["Port"].SIntValue,
                    HostName = this.ClientInfo.ClientPhysicalIp,
                    Performance = p["Performance"].SIntValue,
                    NumSessions = 0,
                    Build = p["Build"].SIntValue,
                    MacAddress = p["MacAddress"].StrValueNonNull,
                    OsInfo = p["OsInfo"].StrValueNonNull,
                };

                var now = DtNow;

                bool ok = Controller.SessionManager.TryUpdateGateAndDeleteSession(now, now.AddMilliseconds(p["EntryExpires"].SIntValue), gate, sessionId, out ThinSession? session);

                var ret = NewWpcResult();

                ret.AdditionalInfo.Add("Gate", gate._GetObjectDumpForJsonFriendly());

                if (ok && session != null)
                {
                    ret.AdditionalInfo.Add("DeleteSession", session._GetObjectDumpForJsonFriendly());
                }

                string? secretKey = Controller.Db.GetVarString("ControllerGateSecretKey");
                if (secretKey._IsFilled()) ret.Pack.AddStr("ControllerGateSecretKey", secretKey);

                return ret;
            }

            public async Task<WpcResult> ProcessWpcRequestCoreAsync(string wpcRequestString, CancellationToken cancel = default)
            {
                try
                {
                    // WPC リクエストをパースする
                    WpcPack req = WpcPack.Parse(wpcRequestString, false);

                    // 関数名を取得する
                    this.FunctionName = req.Pack["Function"].StrValue._NonNull();
                    if (this.FunctionName._IsEmpty()) this.FunctionName = "Unknown";

                    // 関数を実行する
                    switch (this.FunctionName.ToLower())
                    {
                        case "test": return ProcTest(req, cancel);
                        case "commcheck": return ProcCommCheck(req, cancel);
                        case "reportsessionlist": return ProcReportSessionList(req, cancel);
                        case "reportsessionadd": return ProcReportSessionAdd(req, cancel);
                        case "reportsessiondel": return ProcReportSessionDel(req, cancel);

                        default:
                            // 適切な関数が見つからない
                            return NewWpcResult(VpnErrors.ERR_DESK_RPC_PROTOCOL_ERROR);
                    }
                }
                catch (Exception ex)
                {
                    // エラー発生
                    return NewWpcResult(ex);
                }
            }

            public async Task<string> ProcessWpcRequestAsync(string wpcRequestString, CancellationToken cancel = default)
            {
                // メイン処理の実行
                WpcResult err = await ProcessWpcRequestCoreAsync(wpcRequestString, cancel);

                err._PostAccessLog(ThinControllerConsts.AccessLogTag);

                if (err.IsError)
                {
                    err._Error();
                }

                // WPC 応答文字列の回答
                WpcPack wp = err.ToWpcPack();

                return wp.ToPacketString();
            }

            // WPC 応答にクライアント情報を付加
            protected void SetWpcResultAdditionalInfo(WpcResult result)
            {
                var clientInfo = this.ClientInfo;

                // 追加クライアント情報
                var c = result.ClientInfo;

                c.Add("SessionUid", this.Uid);


                c.Add("ServiceType", clientInfo.ServiceType.ToString());

                c.Add("ClientPhysicalIp", clientInfo.ClientPhysicalIp);
                c.Add("ClientIp", clientInfo.ClientIp);
                c.Add("ClientPort", clientInfo.Box?.RemoteEndpoint.Port.ToString() ?? "");
                c.Add("FlagStr", clientInfo.FlagStr);
                c.Add("Flags", clientInfo.Flags.ToString());
                c.Add("IsProxyMode", clientInfo.IsProxyMode.ToString());

                // 追加情報
                var a = result.AdditionalInfo;

                a.Add("FunctionName", this.FunctionName);
            }

            // OK WPC 応答の生成
            protected WpcResult NewWpcResult(Pack? pack = null)
            {
                WpcResult ret = new WpcResult(pack);

                SetWpcResultAdditionalInfo(ret);

                return ret;
            }

            // 通常エラー WPC 応答の生成
            protected WpcResult NewWpcResult(VpnErrors errorCode, Pack? pack = null, string? additionalErrorStr = null, [CallerFilePath] string filename = "", [CallerLineNumber] int line = 0, [CallerMemberName] string? caller = null)
            {
                WpcResult ret = new WpcResult(errorCode, pack, additionalErrorStr, filename, line, caller);

                SetWpcResultAdditionalInfo(ret);

                return ret;
            }

            // 例外エラー WPC 応答の生成
            protected WpcResult NewWpcResult(Exception ex, Pack? pack = null)
            {
                WpcResult ret = new WpcResult(ex, pack);

                SetWpcResultAdditionalInfo(ret);

                return ret;
            }

            // 解放系
            public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
            Once DisposeFlag;
            public async ValueTask DisposeAsync()
            {
                if (DisposeFlag.IsFirstCall() == false) return;
                await DisposeInternalAsync();
            }
            protected virtual void Dispose(bool disposing)
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;
                DisposeInternalAsync()._GetResult();
            }
            Task DisposeInternalAsync()
            {
                // Here
                return TR();
            }
        }

        // Hive
        readonly HiveData<ThinControllerSettings> SettingsHive;

        // 設定へのアクセスを容易にするための自動プロパティ
        CriticalSection ManagedSettingsLock => SettingsHive.DataLock;
        ThinControllerSettings ManagedSettings => SettingsHive.ManagedData;

        public ThinControllerSettings SettingsFastSnapshot => SettingsHive.CachedFastSnapshot;

        // データベース
        public ThinDatabase Db { get; }
        public ThinSessionManager SessionManager { get; }

        public ThinController(ThinControllerSettings settings, Func<ThinControllerSettings>? getDefaultSettings = null)
        {
            try
            {
                this.SettingsHive = new HiveData<ThinControllerSettings>(
                    Hive.SharedLocalConfigHive,
                    "ThinController",
                    getDefaultSettings,
                    HiveSyncPolicy.AutoReadWriteFile,
                    HiveSerializerSelection.RichJson);

                this.Db = new ThinDatabase(this);
                this.SessionManager = new ThinSessionManager();
            }
            catch (Exception ex)
            {
                this._DisposeSafe(ex);
                throw;
            }
        }

        public class HandleWpcParam
        {
            public ThinControllerServiceType ServiceType;
        }

        readonly RefInt CurrentConcurrentProcess = new RefInt();

        public async Task<HttpResult> HandleWpcAsync(HttpEasyContextBox box, object? param2)
        {
            CancellationToken cancel = box.Cancel;
            HandleWpcParam param = (HandleWpcParam)param2!;

            // エンドユーザー用サービスポイントの場合、最大同時処理リクエスト数を制限する
            bool limitMaxConcurrentProcess = param.ServiceType == ThinControllerServiceType.ApiServiceForUsers;

            if (limitMaxConcurrentProcess)
            {
                // カウント加算
                int cur = this.CurrentConcurrentProcess.Increment();
                if (cur > ThinControllerConsts.MaxConcurrentWpcRequestProcessingForUsers)
                {
                    // 最大数超過
                    this.CurrentConcurrentProcess.Decrement();
                    return new HttpErrorResult(Consts.HttpStatusCodes.TooManyRequests, $"Too many WPC concurrent requests ({cur} > {ThinControllerConsts.MaxConcurrentWpcRequestProcessingForUsers})");
                }
            }

            try
            {
                // クライアント情報
                ThinControllerSessionClientInfo clientInfo = new ThinControllerSessionClientInfo(box, param.ServiceType);

                // WPC リクエスト文字列の受信
                string requestWpcString = await box.Request._RecvStringContentsAsync((int)Pack.MaxPackSize * 2, cancel: cancel);

                // WPC 処理のためのセッションの作成
                var session = new ThinControllerSession(this, clientInfo);

                // WPC 処理の実施
                string responseWpcString = await session.ProcessWpcRequestAsync(requestWpcString, cancel);

                // WPC 結果の応答
                return new HttpStringResult(responseWpcString);
            }
            catch (Exception ex)
            {
                // WPC 処理中にエラー発生
                return new HttpErrorResult(Consts.HttpStatusCodes.InternalServerError, $"Internal Server Error: {ex.Message}");
            }
            finally
            {
                // カウント減算
                if (limitMaxConcurrentProcess)
                {
                    this.CurrentConcurrentProcess.Decrement();
                }
            }
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ThinControllerServiceType serviceType)
        {
            app.Use((context, next) =>
            {
                string path = context.Request.Path.Value._NonNullTrim();

                if (this.SettingsFastSnapshot.WpcPathList.Where(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)).Any())
                {
                    HandleWpcParam param = new HandleWpcParam
                    {
                        ServiceType = serviceType,
                    };

                    return HttpResult.EasyRequestHandler(context, param, HandleWpcAsync);
                }

                return next();
            });
        }

        protected override async Task CleanupImplAsync(Exception? ex)
        {
            try
            {
                await this.Db._DisposeSafeAsync();

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

