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
    // ThinController 設定 (アプリの起動時にコード中から設定可能な設定項目)
    public static class ThinControllerBasicSettings
    {
        public static Action<HttpRequestRateLimiterOptions<HttpRequestRateLimiterHashKeys.SrcIPAddress>> ConfigureHttpRequestRateLimiterOptionsForUsers = _ => { };
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

                this.WpcPathList.Add("/thincontrol/");
                this.WpcPathList.Add("/widecontrol/");
            }

            if (this.DbConnectionString_Read._IsEmpty())
            {
                // デフォルトダミー文字列 (安全)
                this.DbConnectionString_Read = "Data Source=127.0.0.1;Initial Catalog=THIN;Persist Security Info=True;User ID=thin_read;Password=password1;";
            }

            if (this.DbConnectionString_Write._IsEmpty())
            {
                // デフォルトダミー文字列 (安全)
                this.DbConnectionString_Write = "Data Source=127.0.0.1;Initial Catalog=THIN;Persist Security Info=True;User ID=thin_write;Password=password2;";
            }
        }
    }

#pragma warning disable CS1998 // 非同期メソッドは、'await' 演算子がないため、同期的に実行されます
    // ThinController の動作をカスタマイズ可能なフック抽象クラス
    public abstract class ThinControllerHookBase
    {
        public virtual async Task<string?> DetermineMachineGroupNameAsync(ThinControllerSession session, CancellationToken cancel = default) => null;

        public virtual async Task<string> GenerateFqdnForProxyAsync(string ipAddress, ThinControllerSession session, CancellationToken cancel = default)
        {
            if (IPAddress.TryParse(ipAddress, out IPAddress? ip) && ip.AddressFamily == AddressFamily.InterNetwork)
            {
                byte[] b = ip.GetAddressBytes();

                string fqdn;
                int rand = Util.RandSInt31() % 120;

                if (rand < 40)
                {
                    fqdn = string.Format("{0}.{1}.{2}.{3}.xip.io",
                        b[0], b[1], b[2], b[3]);
                }
                else if (rand < 80)
                {
                    fqdn = string.Format("{0}.{1}.{2}.{3}.nip.io",
                        b[0], b[1], b[2], b[3]);
                }
                else
                {
                    fqdn = string.Format("{0}.{1}.{2}.{3}.sslip.io",
                        b[0], b[1], b[2], b[3]);
                }

                return fqdn;
            }

            return ipAddress;
        }

        public virtual async Task<string?> DetermineConnectionProhibitedAsync(ThinController controller, bool isClientConnectMode, string serverIp, string? clientIp, ThinDbMachine serverMachine) => null;
    }
#pragma warning restore CS1998 // 非同期メソッドは、'await' 演算子がないため、同期的に実行されます

    public class ThinControllerSessionClientInfo
    {
        public ThinControllerServiceType ServiceType { get; }

        public HttpEasyContextBox? Box { get; }

        public string ClientPhysicalIp { get; }
        public string ClientIp { get; }
        public string ClientIpFqdn { get; private set; }
        public QueryStringList HttpQueryStringList { get; }
        public bool IsProxyMode { get; }

        public bool IsAuthed => AuthedMachine != null;
        public ThinDbMachine? AuthedMachine { get; private set; }
        public string MachineGroupName { get; private set; } = "";

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

            this.HttpQueryStringList = box.QueryStringList;

            this.ClientIpFqdn = this.ClientIp;
        }

        public async Task ResolveClientIpFqdnIfPossibleAsync(DnsResolver resolver, CancellationToken cancel = default)
        {
            if (this.ClientIpFqdn != this.ClientIp) return;

            this.ClientIpFqdn = await resolver.GetHostNameSingleOrIpAsync(this.ClientIp, cancel);
        }

        public async Task SetAuthedAsync(ThinControllerSession session, ThinDbMachine machine, CancellationToken cancel)
        {
            machine._NullCheck(nameof(machine));

            this.AuthedMachine = machine;
            this.MachineGroupName = (await session.Controller.Hook.DetermineMachineGroupNameAsync(session, cancel))._NonNullTrim()._FilledOrDefault("Default");
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

        // 設定文字列の取得
        public WpcResult ProcGetEnvStr(WpcPack req, CancellationToken cancel)
        {
            string name = "Env_" + req.Pack["Name"].StrValueNonNull;
            string s = Controller.Db.GetVarString(name)._NonNullTrim();

            var ret = NewWpcResult();
            var p = ret.Pack;
            p.AddStr("Ret", s);

            ret.AdditionalInfo.Add("Name", name);
            ret.AdditionalInfo.Add("Ret", s);

            return ret;
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

        // WOL MAC アドレス取得
        public async Task<WpcResult> ProcClientGetWolMacList(WpcPack req, CancellationToken cancel)
        {
            var q = req.Pack;

            // svcName を取得 (正規化)
            string svcName = NormalizeSvcName(q["SvcName"].StrValueNonNull);
            if (svcName._IsEmpty()) return NewWpcResult(VpnErrors.ERR_SVCNAME_NOT_FOUND);

            string pcid = q["Pcid"].StrValueNonNull;

            // svcName と PCID から Machine を取得
            var machine = await Controller.Db.SearchMachineByPcidAsync(svcName, pcid, cancel);

            if (machine == null)
            {
                // PCID が見つからない
                var ret2 = NewWpcResult(VpnErrors.ERR_PCID_NOT_FOUND);
                ret2.AdditionalInfo.Add("SvcName", svcName);
                ret2.AdditionalInfo.Add("Pcid", pcid);
                return ret2;
            }

            var ret = NewWpcResult();
            var p = ret.Pack;
            p.AddStr("wol_maclist", machine.WOL_MACLIST._NonNullTrim());

            ret.AdditionalInfo.Add("SvcName", machine.SVC_NAME);
            ret.AdditionalInfo.Add("Pcid", machine.PCID);
            ret.AdditionalInfo.Add("Msid", machine.MSID);
            ret.AdditionalInfo.Add("WoL_MacList", machine.WOL_MACLIST._NonNullTrim());

            return ret;
        }

        // クライアントからの接続要求を処理
        public async Task<WpcResult> ProcClientConnectAsync(WpcPack req, CancellationToken cancel)
        {
            var q = req.Pack;

            // svcName を取得 (正規化)
            string svcName = NormalizeSvcName(q["SvcName"].StrValueNonNull);
            if (svcName._IsEmpty()) return NewWpcResult(VpnErrors.ERR_SVCNAME_NOT_FOUND);

            string pcid = q["Pcid"].StrValueNonNull;
            int ver = q["Ver"].SIntValue;
            int build = q["Build"].SIntValue;
            ulong clientFlags = q["ClientFlags"].Int64Value;
            ulong clientOptions = q["ClientOptions"].Int64Value;

            // svcName と PCID から Machine を取得
            var machine = await Controller.Db.SearchMachineByPcidAsync(svcName, pcid, cancel);

            if (machine == null)
            {
                // PCID が見つからない
                var ret2 = NewWpcResult(VpnErrors.ERR_PCID_NOT_FOUND);
                ret2.AdditionalInfo.Add("SvcName", svcName);
                ret2.AdditionalInfo.Add("Pcid", pcid);
                return ret2;
            }

            // 行政情報システム適合モードかどうか
            bool isLimitedMode = machine.LAST_FLAG._InStr("limited", true);

            // 接続先の Gate と Session を取得
            var gateAndSession = Controller.SessionManager.SearchServerSessionByMsid(machine.MSID);

            if (gateAndSession == null)
            {
                // PCID はデータベースに存在するがセッションが存在しない
                var ret2 = NewWpcResult(VpnErrors.ERR_DEST_MACHINE_NOT_EXISTS);
                ret2.AdditionalInfo.Add("SvcName", svcName);
                ret2.AdditionalInfo.Add("Pcid", pcid);
                return ret2;
            }

            // IP アドレス等を元にした接続禁止を行なう場合、禁止の旨のメッセージを応答する
            string? prohibitedMessage = await Controller.Hook.DetermineConnectionProhibitedAsync(this.Controller, true, gateAndSession.B.IpAddress, this.ClientInfo.ClientIp, machine);

            if (prohibitedMessage._IsFilled())
            {
                // 接続禁止メッセージを応答
                var ret2 = NewWpcResult(VpnErrors.ERR_RECV_MSG);
                ret2.Pack.AddUniStr("Msg", prohibitedMessage);
                ret2.AdditionalInfo.Add("SvcName", svcName);
                ret2.AdditionalInfo.Add("Pcid", pcid);
                ret2.AdditionalInfo.Add("MsgForClient", prohibitedMessage);
                return ret2;
            }

            if ((clientOptions & 1) != 0)
            {
                // WoL クライアントである
                if ((gateAndSession.B.ServerMask64 & 128) == 0)
                {
                    // トリガー PC のバージョンが WoL トリガー機能がない古いバージョンである
                    var ret2 = NewWpcResult(VpnErrors.ERR_WOL_TRIGGER_NOT_SUPPORTED);
                    ret2.AdditionalInfo.Add("SvcName", svcName);
                    ret2.AdditionalInfo.Add("Pcid", pcid);
                    return ret2;
                }
            }

            var ret = NewWpcResult();
            var p = ret.Pack;

            string fqdn = (await Controller.Hook.GenerateFqdnForProxyAsync(gateAndSession.A.IpAddress, this, cancel))._FilledOrDefault(gateAndSession.A.IpAddress);

            p.AddStr("Hostname", gateAndSession.A.IpAddress);
            p.AddInt("Port", (uint)gateAndSession.A.Port);
            p.AddStr("HostnameForProxy", fqdn);
            p.AddData("SessionId", gateAndSession.B.SessionId._GetHexBytes());

            ulong serverMask64 = gateAndSession.B.ServerMask64;
            if (isLimitedMode)
                serverMask64 |= 256; // 行政情報システム適合モード (ThinController が勝手に付ける)
            p.AddInt64("ServerMask64", serverMask64);

            ret.AdditionalInfo.Add("SvcName", machine.SVC_NAME);
            ret.AdditionalInfo.Add("Pcid", machine.PCID);
            ret.AdditionalInfo.Add("Msid", machine.MSID);
            ret.AdditionalInfo.Add("GateHostname", gateAndSession.A.IpAddress);
            ret.AdditionalInfo.Add("GateHostnameForProxy", fqdn);
            ret.AdditionalInfo.Add("GatePort", gateAndSession.A.Port.ToString());

            return ret;
        }

        string NormalizeSvcName(string svcName)
        {
            if (svcName._IsEmpty()) return "";
            svcName = Controller.Db.MemDb?.SvcBySvcName._GetOrDefault(svcName)?.SVC_NAME ?? "";
            if (svcName._IsEmpty()) return "";
            return svcName;
        }

        // PCID の変更
        public async Task<WpcResult> ProcRenameMachine(WpcPack req, CancellationToken cancel)
        {
            var notAuthedErr = RequireMachineAuth(); if (notAuthedErr != null) return notAuthedErr; // 認証を要求

            var q = req.Pack;
            string newPcid = q["NewName"].StrValueNonNull;

            // 変更の実行
            var now = DtNow;
            var err = await Controller.Db.RenamePcidAsync(this.ClientInfo.AuthedMachine!.MSID, newPcid, now, cancel);
            if (err != VpnErrors.ERR_NO_ERROR)
            {
                // 登録エラー
                var ret2 = NewWpcResult(err);
                ret2.AdditionalInfo.Add("OldPcid", this.ClientInfo.AuthedMachine!.PCID);
                ret2.AdditionalInfo.Add("NewPcid", newPcid);
                return ret2;
            }

            var ret = NewWpcResult();
            var p = ret.Pack;

            ret.AdditionalInfo.Add("OldPcid", this.ClientInfo.AuthedMachine!.PCID);
            ret.AdditionalInfo.Add("NewPcid", newPcid);

            return ret;
        }

        // サーバーの登録
        public async Task<WpcResult> ProcRegistMachine(WpcPack req, CancellationToken cancel)
        {
            if (req.HostKey._IsEmpty() || req.HostSecret2._IsEmpty())
            {
                return NewWpcResult(VpnErrors.ERR_PROTOCOL_ERROR);
            }

            var q = req.Pack;

            // svcName を取得 (正規化)
            string svcName = NormalizeSvcName(q["SvcName"].StrValueNonNull);
            if (svcName._IsEmpty()) return NewWpcResult(VpnErrors.ERR_SVCNAME_NOT_FOUND);

            string pcid = q["Pcid"].StrValueNonNull;

            // データベースエラー時は処理禁止
            if (Controller.Db.IsDatabaseConnected == false)
            {
                return NewWpcResult(VpnErrors.ERR_TEMP_ERROR);
            }

            // PCID チェック
            var err = ThinController.CheckPCID(pcid);
            if (err != VpnErrors.ERR_NO_ERROR)
            {
                // PCID 不正
                var ret2 = NewWpcResult(err);
                ret2.AdditionalInfo.Add("SvcName", svcName);
                ret2.AdditionalInfo.Add("Pcid", pcid);
                return ret2;
            }

            // PCID の簡易存在検査 (メモリ上とデータベース上のいずれにも存在しないことを確認)
            // データベースにトランザクションはかけていないが、一意インデックスで一意性は保証されるので
            // この軽いチェックのみで問題はない
            if (await Controller.Db.SearchMachineByPcidAsync(svcName, pcid, cancel) != null)
            {
                // 既に存在する
                var ret2 = NewWpcResult(VpnErrors.ERR_PCID_ALREADY_EXISTS);
                ret2.AdditionalInfo.Add("SvcName", svcName);
                ret2.AdditionalInfo.Add("Pcid", pcid);
                return ret2;
            }

            // MSID の生成
            string msid = ThinController.GenerateMsid(req.HostKey, req.HostSecret2);

            // 登録の実行
            var now = DtNow;
            err = await Controller.Db.RegisterMachineAsync(svcName, msid, q["Pcid"].StrValueNonNull, req.HostKey, req.HostSecret2, now, this.ClientInfo.ClientIp, this.ClientInfo.ClientIpFqdn, cancel);
            if (err != VpnErrors.ERR_NO_ERROR)
            {
                // 登録エラー
                var ret2 = NewWpcResult(err);
                ret2.AdditionalInfo.Add("SvcName", svcName);
                ret2.AdditionalInfo.Add("Pcid", pcid);
                ret2.AdditionalInfo.Add("HostKey", req.HostKey);
                ret2.AdditionalInfo.Add("Msid", msid);
                return ret2;
            }

            var ret = NewWpcResult();
            var p = ret.Pack;

            ret.AdditionalInfo.Add("SvcName", svcName);
            ret.AdditionalInfo.Add("Pcid", pcid);
            ret.AdditionalInfo.Add("HostKey", req.HostKey);
            ret.AdditionalInfo.Add("Msid", msid);

            return ret;
        }

        // PCID 候補を取得
        public async Task<WpcResult> ProcGetPcidCandidate(WpcPack req, CancellationToken cancel)
        {
            var q = req.Pack;

            // svcName を取得 (正規化)
            string svcName = NormalizeSvcName(q["SvcName"].StrValueNonNull);
            if (svcName._IsEmpty()) return NewWpcResult(VpnErrors.ERR_SVCNAME_NOT_FOUND);

            // データベースエラー時は処理禁止
            if (Controller.Db.IsDatabaseConnected == false)
            {
                return NewWpcResult(VpnErrors.ERR_TEMP_ERROR);
            }

            string fqdn = await Controller.DnsResolver.GetHostNameSingleOrIpAsync(this.ClientInfo.ClientIp, cancel);
            string machineName = q["MachineName"].StrValueNonNull;
            string computerName = q["ComputerName"].StrValueNonNull;
            string userName = q["UserName"].StrValueNonNull;

            string candidate = await Controller.GeneratePcidCandidateAsync(svcName, fqdn, machineName, userName, computerName, cancel);

            var ret = NewWpcResult();
            var p = ret.Pack;
            p.AddStr("Ret", candidate);

            ret.AdditionalInfo.Add("Candidate", candidate);
            ret.AdditionalInfo.Add("svcName", svcName);
            ret.AdditionalInfo.Add("fqdn", fqdn);
            ret.AdditionalInfo.Add("machineName", machineName);
            ret.AdditionalInfo.Add("userName", userName);
            ret.AdditionalInfo.Add("computerName", computerName);

            return ret;
        }

        // サーバーからの接続要求を処理
        public async Task<WpcResult> ProcServerConnectAsync(WpcPack req, CancellationToken cancel)
        {
            var notAuthedErr = RequireMachineAuth(); if (notAuthedErr != null) return notAuthedErr; // 認証を要求

            // 設定変数の "PreferredGateIpRange_Group_<グループ名>" に書かれている ACL を取得する
            string preferredIpAcl = Controller.Db.GetVarString($"PreferredGateIpRange_Group_{this.ClientInfo.MachineGroupName}")._NonNullTrim();
            bool forceAcl = false;
            if (preferredIpAcl._TryTrimStartWith(out preferredIpAcl, StringComparison.OrdinalIgnoreCase, "[force]"))
            {
                // 先頭が [force] で始まっていれば、強制適用
                forceAcl = true;
            }
            var acl = EasyIpAcl.GetOrCreateCachedIpAcl(preferredIpAcl);

            // 最良の Gate を選択
            var bestGate = Controller.SessionManager.SelectBestGateForServer(acl, Controller.Db.MemDb?.MaxSessionsPerGate ?? 0, !forceAcl);

            if (bestGate == null)
            {
                // 適合 Gate なし (混雑?)
                return NewWpcResult(VpnErrors.ERR_NO_GATE_CAN_ACCEPT);
            }

            // 選択された Gate のセッション数を仮に 1 つ増加させる
            Interlocked.Increment(ref bestGate.NumSessions);

            ulong expires = (ulong)Util.ConvertDateTime(DtUtcNow.AddMinutes(20));

            var gateIdData = bestGate.GateId._GetHexBytes();

            MemoryBuffer<byte> b = new MemoryBuffer<byte>();
            b.Write(ClientInfo.AuthedMachine!.MSID._GetBytes_Ascii());
            b.WriteUInt64(expires);
            b.Write(gateIdData);

            var ret = NewWpcResult();
            var p = ret.Pack;
            p.AddStr("Msid", ClientInfo.AuthedMachine!.MSID);
            p.AddData("GateId", gateIdData);
            p.AddInt64("Expires", expires);

            // 署名
            MemoryBuffer<byte> signSrc = new MemoryBuffer<byte>();
            signSrc.Write((Controller.Db.MemDb?.ControllerGateSecretKey ?? "")._GetBytes_Ascii());
            signSrc.Write(b.Span);
            byte[] sign = Secure.HashSHA1(signSrc.Span);
            p.AddData("Signature2", sign);
            p.AddStr("Pcid", this.ClientInfo.AuthedMachine!.PCID);
            p.AddStr("Hostname", bestGate.IpAddress);
            string fqdn = (await Controller.Hook.GenerateFqdnForProxyAsync(bestGate.IpAddress, this, cancel))._FilledOrDefault(bestGate.IpAddress);
            p.AddStr("HostnameForProxy", fqdn);
            p.AddInt("Port", (uint)bestGate.Port);

            // IP アドレス等を元にした接続禁止を行なう場合、禁止の旨のメッセージを応答する (接続処理自体は成功させる)
            string? prohibitedMessage = await Controller.Hook.DetermineConnectionProhibitedAsync(this.Controller, false, this.ClientInfo.ClientIp, null, this.ClientInfo.AuthedMachine!);

            if (prohibitedMessage._IsFilled())
            {
                p.AddUniStr("MsgForServer", prohibitedMessage);
                p.AddInt("MsgForServerOnce", 0);
                ret.AdditionalInfo.Add("MsgForServer", prohibitedMessage);
            }

            ret.AdditionalInfo.Add("SvcName", this.ClientInfo.AuthedMachine!.SVC_NAME);
            ret.AdditionalInfo.Add("Pcid", this.ClientInfo.AuthedMachine!.PCID);
            ret.AdditionalInfo.Add("Msid", this.ClientInfo.AuthedMachine!.MSID);
            ret.AdditionalInfo.Add("GateHostname", bestGate.IpAddress);
            ret.AdditionalInfo.Add("GateHostnameForProxy", fqdn);
            ret.AdditionalInfo.Add("GatePort", bestGate.Port.ToString());

            return ret;
        }


        // サーバーが未登録の場合は登録を要求する
        WpcResult? RequireMachineAuth()
        {
            if (this.ClientInfo.IsAuthed == false)
            {
                return NewWpcResult(VpnErrors.ERR_NO_INIT_CONFIG);
            }
            return null;
        }

        // Gate のセキュリティ検査
        async Task GateSecurityCheckAsync(WpcPack req, CancellationToken cancel)
        {
            string gateKey = req.Pack["GateKey"].StrValueNonNull;

            bool ok = false;

            if (gateKey._IsFilled())
            {
                ok = Controller.Db.GetVars("GateKeyList")?.Where(x => x.VAR_VALUE1 == gateKey).Any() ?? false;
            }

            if (ok == false)
            {
                throw new CoresException($"The specified gateKey '{gateKey}' is invalid.");
            }

            // Gate の場合 IP 逆引きの実行
            await this.ClientInfo.ResolveClientIpFqdnIfPossibleAsync(Controller.DnsResolver, cancel);
        }

        // Gate から: セッションリストの報告
        public async Task<WpcResult> ProcReportSessionListAsync(WpcPack req, CancellationToken cancel)
        {
            await GateSecurityCheckAsync(req, cancel);

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

                sess.Normalize();
                sess.Validate();

                sessionList.TryAdd(sess.SessionId, sess);
            }

            ThinGate gate = new ThinGate
            {
                GateId = p["GateId"].DataValueHexStr,
                IpAddress = this.ClientInfo.ClientIp,
                Port = p["Port"].SIntValue,
                HostName = this.ClientInfo.ClientIpFqdn,
                Performance = p["Performance"].SIntValue,
                NumSessions = sessionList.Count,
                Build = p["Build"].SIntValue,
                MacAddress = p["MacAddress"].StrValueNonNull,
                OsInfo = p["OsInfo"].StrValueNonNull,
                UltraCommitId = p["UltraCommitId"].StrValueNonNull,
                CurrentTime = p["CurrentTime"].DateTimeValue.ToLocalTime(),
                BootTick = p["BootTick"].Int64Value._ToTimeSpanMSecs(),
            };

            gate.Normalize();
            gate.Validate();

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

            bool exists = false;
            if (Controller.SessionManager.GateTable.TryGetValue(gate.GateId, out ThinGate? currentGate))
            {
                if (DtNow <= currentGate.Expires)
                {
                    exists = true;
                }
            }
            if (exists == false)
            {
                // この Gate がまだ GateTable に存在しない。初めての登録試行であるため、この Gate に本当に到達可能かどうかチェックする
                if (Controller.Db.GetVarBool("TestGateTcpReachability"))
                {
                    using var tcp = new TcpClient();
                    tcp.NoDelay = true;
                    tcp.ReceiveTimeout = tcp.ReceiveTimeout = 5 * 1000;
                    await tcp.ConnectAsync(gate.IpAddress, gate.Port, cancel);
                    await using var stream = tcp.GetStream();
                    await using var sslStream = new SslStream(stream, false, (sender, cert, chain, err) => true);
                    stream.ReadTimeout = 5 * 1000;
                    stream.WriteTimeout = 5 * 1000;
                    sslStream.ReadTimeout = 5 * 1000;
                    sslStream.WriteTimeout = 5 * 1000;
                    await sslStream.AuthenticateAsClientAsync(
                        new SslClientAuthenticationOptions()
                        {
                            AllowRenegotiation = false,
                            CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck,
                            EnabledSslProtocols = CoresConfig.SslSettings.DefaultSslProtocolVersions,
                            TargetHost = gate.IpAddress.ToString(),
                        }, cancel
                        );
                }
            }

            var now = DtNow;

            Controller.SessionManager.UpdateGateAndReportSessions(now, now.AddMilliseconds(p["EntryExpires"].SIntValue), gate, sessionList.Values);

            var ret = NewWpcResult();

            ret.AdditionalInfo.Add("Gate", gate._GetObjectDumpForJsonFriendly());

            AddGateSettingsToPack(ret.Pack);

            return ret;
        }

        // Gate から: 1 本のセッションの追加の報告
        public async Task<WpcResult> ProcReportSessionAddAsync(WpcPack req, CancellationToken cancel)
        {
            await GateSecurityCheckAsync(req, cancel);

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

            sess.Normalize();
            sess.Validate();

            // gate のフィールド内容は UpdateGateAndAddSession でそれほど参照されないので適当で OK
            ThinGate gate = new ThinGate
            {
                GateId = p["GateId"].DataValueHexStr,
                IpAddress = this.ClientInfo.ClientIp,
                Port = p["Port"].SIntValue,
                HostName = this.ClientInfo.ClientIpFqdn,
                Performance = p["Performance"].SIntValue,
                NumSessions = 0,
                Build = p["Build"].SIntValue,
                MacAddress = p["MacAddress"].StrValueNonNull,
                OsInfo = p["OsInfo"].StrValueNonNull,
                UltraCommitId = p["UltraCommitId"].StrValueNonNull,
                CurrentTime = p["CurrentTime"].DateTimeValue.ToLocalTime(),
                BootTick = p["BootTick"].Int64Value._ToTimeSpanMSecs(),
            };

            gate.Normalize();
            gate.Validate();

            sess.NumClientsUnique = sess.NumClients == 0 ? 0 : 1;

            var now = DtNow;

            Controller.SessionManager.UpdateGateAndAddSession(now, now.AddMilliseconds(p["EntryExpires"].SIntValue), gate, sess);

            var ret = NewWpcResult();

            ret.AdditionalInfo.Add("Gate", gate._GetObjectDumpForJsonFriendly());
            ret.AdditionalInfo.Add("AddSession", sess._GetObjectDumpForJsonFriendly());

            AddGateSettingsToPack(ret.Pack);

            return ret;
        }

        // Gate から: 1 本のセッションの削除の報告
        public async Task<WpcResult> ProcReportSessionDelAsync(WpcPack req, CancellationToken cancel)
        {
            await GateSecurityCheckAsync(req, cancel);

            Pack p = req.Pack;

            string sessionId = p["SessionId"].DataValueHexStr;

            // gate のフィールド内容は UpdateGateAndAddSession でそれほど参照されないので適当で OK
            ThinGate gate = new ThinGate
            {
                GateId = p["GateId"].DataValueHexStr,
                IpAddress = this.ClientInfo.ClientIp,
                Port = p["Port"].SIntValue,
                HostName = this.ClientInfo.ClientIpFqdn,
                Performance = p["Performance"].SIntValue,
                NumSessions = 0,
                Build = p["Build"].SIntValue,
                MacAddress = p["MacAddress"].StrValueNonNull,
                OsInfo = p["OsInfo"].StrValueNonNull,
                UltraCommitId = p["UltraCommitId"].StrValueNonNull,
                CurrentTime = p["CurrentTime"].DateTimeValue.ToLocalTime(),
                BootTick = p["BootTick"].Int64Value._ToTimeSpanMSecs(),
            };

            gate.Normalize();
            gate.Validate();

            var now = DtNow;

            bool ok = Controller.SessionManager.TryUpdateGateAndDeleteSession(now, now.AddMilliseconds(p["EntryExpires"].SIntValue), gate, sessionId, out ThinSession? session);

            var ret = NewWpcResult();

            ret.AdditionalInfo.Add("Gate", gate._GetObjectDumpForJsonFriendly());

            if (ok && session != null)
            {
                ret.AdditionalInfo.Add("DeleteSession", session._GetObjectDumpForJsonFriendly());
            }

            AddGateSettingsToPack(ret.Pack);

            return ret;
        }

        // Gate にとって有用な設定を Gate に返送する
        void AddGateSettingsToPack(Pack p)
        {
            string? secretKey = Controller.Db.GetVarString("ControllerGateSecretKey");
            if (secretKey._IsFilled()) p.AddStr("ControllerGateSecretKey", secretKey);

            var db = Controller.Db.MemDb;
            if (db != null)
            {
                var varsList = db.VarList;
                var names = varsList.Where(x => x.VAR_NAME.StartsWith("GateSettings_Int_", StringComparison.OrdinalIgnoreCase)).Select(x => x.VAR_NAME).Distinct(StrComparer.IgnoreCaseComparer);

                names._DoForEach(x =>
                {
                    var item = db.VarByName._GetOrDefault(x)?.FirstOrDefault();
                    if (item != null)
                    {
                        p.AddInt(item.VAR_NAME, (uint)item.VAR_VALUE1._ToInt());
                    }
                });
            }
        }

        public async Task<WpcResult> ProcessWpcRequestCoreAsync(string wpcRequestString, CancellationToken cancel = default)
        {
            try
            {
                if (Controller.Db.IsLoaded == false)
                {
                    throw new CoresException("Controller.DB is not loaded yet.");
                }

                // WPC リクエストをパースする
                WpcPack req = WpcPack.Parse(wpcRequestString, false);

                // 関数名を取得する
                this.FunctionName = req.Pack["Function"].StrValue._NonNull();
                if (this.FunctionName._IsEmpty()) this.FunctionName = "Unknown";

                if (FunctionName.StartsWith("report", StringComparison.OrdinalIgnoreCase) == false)
                {
                    // Gate からの要求以外の場合は、認証を実施する
                    if (req.HostKey._IsFilled() && req.HostSecret2._IsFilled())
                    {
                        var authedMachine = await Controller.Db.AuthMachineAsync(req.HostKey, req.HostSecret2, cancel);

                        if (authedMachine != null)
                        {
                            // 認証成功
                            await this.ClientInfo.SetAuthedAsync(this, authedMachine, cancel);
                        }
                    }
                }

                cancel.ThrowIfCancellationRequested();

                // 関数を実行する
                switch (this.FunctionName.ToLower())
                {
                    // テスト系
                    case "test": return ProcTest(req, cancel);
                    case "commcheck": return ProcCommCheck(req, cancel);

                    // Gate 系
                    case "reportsessionlist": return await ProcReportSessionListAsync(req, cancel);
                    case "reportsessionadd": return await ProcReportSessionAddAsync(req, cancel);
                    case "reportsessiondel": return await ProcReportSessionDelAsync(req, cancel);

                    // サーバー系
                    case "getpcidcandidate": return await ProcGetPcidCandidate(req, cancel);
                    case "registmachine": return await ProcRegistMachine(req, cancel);
                    case "renamemachine": return await ProcRenameMachine(req, cancel);
                    case "serverconnect": return await ProcServerConnectAsync(req, cancel);

                    // クライアント系
                    case "clientconnect": return await ProcClientConnectAsync(req, cancel);
                    case "clientgetwolmaclist": return await ProcClientGetWolMacList(req, cancel);

                    // 共通系
                    case "getenvstr": return ProcGetEnvStr(req, cancel);

                    default:
                        // 適切な関数が見つからない
                        return NewWpcResult(VpnErrors.ERR_NOT_SUPPORTED);
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
            c.Add("HttpQueryStringList", clientInfo.HttpQueryStringList.ToString());
            c.Add("IsProxyMode", clientInfo.IsProxyMode.ToString());

            c.Add("IsAuthed", clientInfo.IsAuthed.ToString());

            if (clientInfo.IsAuthed)
            {
                c.Add("AuthedMachineMsid", clientInfo.AuthedMachine!.MSID);
                c.Add("AuthedMachineGroupName", clientInfo.MachineGroupName);
            }

            // 追加情報
            var a = result.AdditionalInfo;

            a.Add("FunctionName", this.FunctionName);
        }

        // OK WPC 応答の生成
        public WpcResult NewWpcResult(Pack? pack = null)
        {
            WpcResult ret = new WpcResult(pack);

            SetWpcResultAdditionalInfo(ret);

            return ret;
        }

        // 通常エラー WPC 応答の生成
        public WpcResult NewWpcResult(VpnErrors errorCode, Pack? pack = null, string? additionalErrorStr = null, [CallerFilePath] string filename = "", [CallerLineNumber] int line = 0, [CallerMemberName] string? caller = null)
        {
            WpcResult ret = new WpcResult(errorCode, pack, additionalErrorStr, filename, line, caller);

            SetWpcResultAdditionalInfo(ret);

            return ret;
        }

        // 例外エラー WPC 応答の生成
        public WpcResult NewWpcResult(Exception ex, Pack? pack = null)
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

    public class ThinController : AsyncService
    {

        // Hive
        readonly HiveData<ThinControllerSettings> SettingsHive;

        // 設定へのアクセスを容易にするための自動プロパティ
        CriticalSection ManagedSettingsLock => SettingsHive.DataLock;
        ThinControllerSettings ManagedSettings => SettingsHive.ManagedData;

        public ThinControllerSettings SettingsFastSnapshot => SettingsHive.CachedFastSnapshot;

        public DnsResolver DnsResolver { get; }

        public DateTimeOffset BootDateTime { get; } = DtOffsetNow;

        public ThinControllerHookBase Hook { get; }

        // データベース
        public ThinDatabase Db { get; }
        public ThinSessionManager SessionManager { get; }

        public ThinController(ThinControllerSettings settings, ThinControllerHookBase hook, Func<ThinControllerSettings>? getDefaultSettings = null)
        {
            try
            {
                this.Hook = hook;

                this.SettingsHive = new HiveData<ThinControllerSettings>(
                    Hive.SharedLocalConfigHive,
                    "ThinController",
                    getDefaultSettings,
                    HiveSyncPolicy.AutoReadWriteFile,
                    HiveSerializerSelection.RichJson);

                this.DnsResolver = new DnsClientLibBasedDnsResolver(new DnsResolverSettings());

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

        // 管理用ビューを取得
        public ThinAdminView GetAdminView()
        {
            SessionManager.DeleteOldGate(DtNow);

            ThinAdminView ret = new ThinAdminView();

            ret.BootDateTime = this.BootDateTime;

            ret.GatesList = this.SessionManager.GateTable.Values.OrderBy(x => x.IpAddress, StrComparer.IpAddressStrComparer).ThenBy(x => x.GateId);

            ret.VarsList = this.Db.MemDb?.VarList.OrderBy(x => x.VAR_NAME).ThenBy(x => x.VAR_ID) ?? null;

            ret.MachinesList = this.Db.MemDb?.MachineList.OrderBy(x => x.CREATE_DATE) ?? null;

            return ret;
        }

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
                int maxValue = this.CurrentValue_ControllerMaxConcurrentWpcRequestProcessingForUsers;
                if (cur > maxValue)
                {
                    // 最大数超過
                    this.CurrentConcurrentProcess.Decrement();
                    return new HttpErrorResult(Consts.HttpStatusCodes.TooManyRequests, $"Too many WPC concurrent requests ({cur} > {maxValue})");
                }
            }

            try
            {
                // クライアント情報
                ThinControllerSessionClientInfo clientInfo = new ThinControllerSessionClientInfo(box, param.ServiceType);

                // WPC リクエスト文字列の受信
                string requestWpcString = "";
                if (box.Method == WebMethods.POST)
                {
                    requestWpcString = await box.Request._RecvStringContentsAsync((int)Pack.MaxPackSize * 2, cancel: cancel);
                }

                if (requestWpcString._IsFilled())
                {
                    // WPC 処理のためのセッションの作成
                    var session = new ThinControllerSession(this, clientInfo);

                    // WPC 処理の実施
                    string responseWpcString = await session.ProcessWpcRequestAsync(requestWpcString, cancel);

                    // WPC 結果の応答
                    return new HttpStringResult(responseWpcString);
                }
                else
                {
                    // プロトコルエラー (リクエストがない)
                    WpcResult result = new WpcResult(VpnErrors.ERR_PROTOCOL_ERROR);
                    return new HttpStringResult(result.ToWpcPack().ToPacketString());
                }
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
            app.Use(async (context, next) =>
            {
                if (serviceType == ThinControllerServiceType.ApiServiceForGateway)
                {
                    // Gateway 宛のポートに通信が来たら IP ACL を確認する
                    var addr = context.Connection.RemoteIpAddress!._UnmapIPv4();
                    if (EasyIpAcl.Evaluate(this.Db.GetVarString("GatewayServicePortIpAcl"), addr) == EasyIpAclAction.Deny)
                    {
                        await using var errResult = new HttpErrorResult(Consts.HttpStatusCodes.Forbidden, $"Your IP address '{addr}' is not allowed to access to this endpoint.");

                        await context.Response._SendHttpResultAsync(errResult, context._GetRequestCancellationToken());

                        return;
                    }
                }

                string path = context.Request.Path.Value._NonNullTrim();

                if (this.SettingsFastSnapshot.WpcPathList.Where(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)).Any())
                {
                    HandleWpcParam param = new HandleWpcParam
                    {
                        ServiceType = serviceType,
                    };

                    await HttpResult.EasyRequestHandler(context, param, HandleWpcAsync);

                    return;
                }

                await next();
            });
        }

        protected override async Task CleanupImplAsync(Exception? ex)
        {
            try
            {
                await this.Db._DisposeSafeAsync();

                await this.DnsResolver._DisposeSafeAsync();

                this.SettingsHive._DisposeSafe();
            }
            finally
            {
                await base.CleanupImplAsync(ex);
            }
        }

        // 設定値プロパティ集
        // ここで、this.Db はまだ null である可能性があるため、? を付けること
        public int CurrentValue_ControllerMaxConcurrentWpcRequestProcessingForUsers 
            => (this.Db?.MemDb?.ControllerMaxConcurrentWpcRequestProcessingForUsers)._ZeroOrDefault(ThinControllerConsts.Default_ControllerMaxConcurrentWpcRequestProcessingForUsers);

        public int CurrentValue_ControllerDbFullReloadIntervalMsecs 
            => (this.Db?.MemDb?.ControllerDbFullReloadIntervalMsecs)._ZeroOrDefault(ThinControllerConsts.Default_ControllerDbFullReloadIntervalMsecs, max: ThinControllerConsts.Max_ControllerDbReadFullReloadIntervalMsecs);

        public int CurrentValue_ControllerDbWriteUpdateIntervalMsecs 
            => (this.Db?.MemDb?.ControllerDbWriteUpdateIntervalMsecs)._ZeroOrDefault(ThinControllerConsts.Default_ControllerDbWriteUpdateIntervalMsecs, max: ThinControllerConsts.Max_ControllerDbWriteUpdateIntervalMsecs);

        public int CurrentValue_ControllerDbBackupFileWriteIntervalMsecs 
            => (this.Db?.MemDb?.ControllerDbBackupFileWriteIntervalMsecs)._ZeroOrDefault(ThinControllerConsts.Default_ControllerDbBackupFileWriteIntervalMsecs, max: ThinControllerConsts.Max_ControllerDbBackupFileWriteIntervalMsecs);

        // ユーティリティ関数系

        // 最近発行した PCID 候補のキャッシュ
        readonly FastCache<string, int> RecentPcidCandidateCache = new FastCache<string, int>(10 * 60 * 1000, comparer: StrComparer.IgnoreCaseComparer);

        public void AddPcidToRecentPcidCandidateCache(string pcid)
        {
            RecentPcidCandidateCache.Add(pcid, 1);
        }

        // PCID の候補の決定
        public async Task<string> GeneratePcidCandidateAsync(string svcName, string dns1, string dns2, string username, string machineName, CancellationToken cancel)
        {
            string s = await GeneratePcidCandidateCoreAsync(svcName, dns1, dns2, username, machineName, cancel);

            RecentPcidCandidateCache.Add(s, 1);

            return s;
        }

        async Task<string> GeneratePcidCandidateCoreAsync(string svcName, string dns1, string dns2, string username, string machineName, CancellationToken cancel)
        {
            int i;
            if (dns1._IsSamei("localhost")) dns1 = "";
            if (dns2._IsSamei("localhost")) dns2 = "";
            if (machineName._IsSamei("localhost")) machineName = "";
            var domain1 = new Basic.Legacy.LegacyDomainUtil(dns1);
            var domain2 = new Basic.Legacy.LegacyDomainUtil(dns2);
            string d1 = ConvertStrToSafeForPcid(domain1.ProperString);
            string d2 = ConvertStrToSafeForPcid(domain2.ProperString);
            if (dns1._IsStrIP())
            {
                d1 = "";
            }
            if (dns2._IsStrIP())
            {
                d2 = "";
            }
            username = ConvertStrToSafeForPcid(username).Trim();
            i = machineName.IndexOf(".");
            if (i != -1)
            {
                machineName = machineName.Substring(0, i);
            }
            machineName = ConvertStrToSafeForPcid(machineName).Trim();
            if (machineName.Length > 8)
            {
                machineName = machineName.Substring(0, 8);
            }

            if (d1 != "")
            {
                return await GeneratePcidCandidateStringCoreAsync(svcName, d1, cancel);
            }

            if (Str.IsStrInList(username, "", "administrator", "system") == false)
            {
                return await GeneratePcidCandidateStringCoreAsync(svcName, username, cancel);
            }

            if (d2 != "")
            {
                return await GeneratePcidCandidateStringCoreAsync(svcName, d2, cancel);
            }

            if (machineName != "")
            {
                return await GeneratePcidCandidateStringCoreAsync(svcName, machineName, cancel);
            }

            string key = Str.ByteToHex(Secure.Rand(8));
            return await GeneratePcidCandidateStringCoreAsync(svcName, key, cancel);
        }

        // 候補名の生成
        async Task<string> GeneratePcidCandidateStringCoreAsync(string svcName, string key, CancellationToken cancel)
        {
            ulong i;
            uint n = 0;
            while (true)
            {
                n++;

                i = 1 + Secure.RandUInt31() % 99999999;
                if (n >= 100)
                {
                    i = Secure.RandUInt63() % 9999999999UL + n;
                }
                if (n >= 200)
                {
                    i = Secure.RandUInt63() % 999999999999UL + n;
                }
                if (n >= 1000)
                {
                    throw new CoresLibException("(n >= 1000)");
                }

                string pcidCandidate = GeneratePcidCandidateStringFromKeyAndNumber(key, i);

                if (RecentPcidCandidateCache.ContainsKey(pcidCandidate) == false)
                {
                    if (await this.Db.SearchMachineByPcidAsync(svcName, pcidCandidate, cancel) == null)
                    {
                        return pcidCandidate;
                    }
                }
            }
        }

        // PCID 候補生成
        public static string GeneratePcidCandidateStringFromKeyAndNumber(string key, ulong id)
        {
            key = key.ToLower().Trim();
            string ret = key + "-" + id.ToString();

            ret._SliceHead(ThinControllerConsts.MaxPcidLen);

            return ret;
        }

        // PCID に使用できる文字列にコンバート
        public static string ConvertStrToSafeForPcid(string str)
        {
            string ret = "";

            foreach (char c in str)
            {
                if (IsSafeCharForPcid(c))
                {
                    ret += c;
                }
            }

            if (ret.Length > 20)
            {
                ret = ret.Substring(0, 20);
            }

            return ret;
        }

        // PCID に使用できる文字かどうかチェック
        public static bool IsSafeCharForPcid(char c)
        {
            if ('a' <= c && c <= 'z')
            {
                return true;
            }
            else if ('A' <= c && c <= 'Z')
            {
                return true;
            }
            else if (c == '_' || c == '-')
            {
                return true;
            }
            else if ('0' <= c && c <= '9')
            {
                return true;
            }
            return false;
        }

        // PCID のチェック
        public static VpnErrors CheckPCID(string pcid)
        {
            int i, len;
            len = pcid.Length;
            if (len == 0)
            {
                return VpnErrors.ERR_PCID_NOT_SPECIFIED;
            }
            if (len > ThinControllerConsts.MaxPcidLen)
            {
                return VpnErrors.ERR_PCID_TOO_LONG;
            }
            for (i = 0; i < len; i++)
            {
                if (IsSafeCharForPcid(pcid[i]) == false)
                {
                    return VpnErrors.ERR_PCID_INVALID;
                }
            }

            return VpnErrors.ERR_NO_ERROR;
        }

        // MSID の生成
        public static string GenerateMsid(string certhash, string svcName)
        {
            byte[] hex = certhash._GetHexBytes();
            if (hex.Length != 20)
            {
                throw new ArgumentException("certHash");
            }
            return "MSID-" + svcName + "-" + Str.ByteToHex(hex);
        }

    }
}

#endif

