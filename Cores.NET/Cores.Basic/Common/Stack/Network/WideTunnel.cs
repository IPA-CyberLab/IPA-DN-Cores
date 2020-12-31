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
// WideTunnel Protocol Stack

#if true

#pragma warning disable CA2235 // Mark all non-serializable fields

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
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Net;
using System.Net.Sockets;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;

namespace IPA.Cores.Basic
{
    public static partial class CoresConfig
    {
        public static partial class WtcConfig
        {
            public static readonly Copenhagen<int> PseudoVer = 9999;
            public static readonly Copenhagen<int> PseudoBuild = 9999;

            public static readonly Copenhagen<int> WpcTimeoutMsec = 15 * 1000;
        }
    }

    public class WtcOptions : NetMiddleProtocolOptionsBase
    {
    }

    public class NetWtcProtocolStack : NetMiddleProtocolStackBase
    {
        public new WtcOptions Options => (WtcOptions)base.Options;

        PipeStream LowerStream;
        PipeStream UpperStream;

        public NetWtcProtocolStack(PipePoint lower, PipePoint? upper, NetMiddleProtocolOptionsBase options, CancellationToken cancel = default) : base(lower, upper, options, cancel)
        {
            this.LowerStream = this.LowerAttach.GetStream();
            this.UpperStream = this.UpperAttach.GetStream();
        }

        Once Started;

        public async Task StartWtcAsync(CancellationToken cancel = default)
        {
            await Task.CompletedTask;

            if (Started.IsFirstCall() == false)
            {
                throw new ApplicationException("Already started.");
            }
        }

        protected override async Task CleanupImplAsync(Exception? ex)
        {
            try
            {
                await this.LowerStream._DisposeSafeAsync();
                await this.UpperStream._DisposeSafeAsync();
            }
            finally
            {
                await base.CleanupImplAsync(ex);
            }
        }
    }

    public class WtcConnectOptions
    {
        public TcpIpSystem TcpIp { get; }
        public WtcOptions WtcOptions { get; }
        public PalSslClientAuthenticationOptions SslOptions { get; }

        public WtcConnectOptions(WtcOptions? wtcOptions = null, PalSslClientAuthenticationOptions? sslOptions = null, TcpIpSystem? tcpIp = null)
        {
            this.TcpIp = tcpIp ?? LocalNet;
            this.WtcOptions = wtcOptions ?? new WtcOptions();

            if (sslOptions == null)
            {
                this.SslOptions = new PalSslClientAuthenticationOptions(true);
            }
            else
            {
                this.SslOptions = (PalSslClientAuthenticationOptions)sslOptions.Clone();
            }
        }
    }

    public class WtcSocket : MiddleConnSock
    {
        protected new NetWtcProtocolStack Stack => (NetWtcProtocolStack)base.Stack;

        public WtcSocket(ConnSock lowerSock, WtcOptions? options = null) : base(new NetWtcProtocolStack(lowerSock.UpperPoint, null, options._FilledOrDefault(new WtcOptions())))
        {
        }

        public async Task StartWtcAsync(CancellationToken cancel = default)
            => await Stack.StartWtcAsync(cancel);

        //public static async Task<WtcSocket> ConnectAsync(WtcConnectOptions? options = null, CancellationToken cancel = default)
        //{
        //    if (options == null) options = new WtcConnectOptions();
        //}
    }

    [Flags]
    public enum WideTunnelClientOptions : uint
    {
        None = 0,
        WoL = 1,
    }

    [Flags]
    public enum ProxyType
    {
        Direct = 0,
        Http,
        Socks,
    }

    public class WtConnectParam
    {
        public string HostName = "";
        public string HostNameForProxy = "";
        public int Port;
        public ProxyType ProxyType;
        public string ProxyHostname = "";
        public int ProxyPort;
        public string ProxyUsername = "";
        public string ProxyPassword = "";
        public string ProxyUserAgent = "";
        public string MsgForServer = "";
        public bool MsgForServerOnce;
        public long SessionLifeTime;
        public string SessionLifeTimeMsg = "";

        // Client 用
        public ReadOnlyMemory<byte> SessionId;
        public ulong ServerMask64;
        public bool CacheUsed;
    }

    public class WideTunnelOptions
    {
        public string SvcName { get; }
        public ReadOnlyMemory<byte> ClientId { get; }
        public IEnumerable<string> EntryUrlList { get; }
        public TcpIpSystem TcpIp { get; }
        public WebApiOptions? WebApiOptions { get; }

        public ReadOnlyMemory<byte> GenNewClientId() => Secure.Rand(20);

        public WideTunnelOptions(string svcName, IEnumerable<string> entryUrlList, ReadOnlyMemory<byte> clientId = default, WebApiOptions? webApiOptions = null, TcpIpSystem? tcpIp = null)
        {
            if (clientId.IsEmpty) clientId = GenNewClientId();
            if (webApiOptions == null) webApiOptions = new WebApiOptions(
                new WebApiSettings
                {
                    AllowAutoRedirect = false,
                    Timeout = CoresConfig.WtcConfig.WpcTimeoutMsec,
                },
                tcpIp, false);

            if (clientId.Length != 20) throw new ArgumentException($"{nameof(clientId)} must be 20 bytes.");
            if (tcpIp == null) tcpIp = LocalNet;

            SvcName = svcName._NonNullTrim();
            ClientId = clientId;
            this.EntryUrlList = entryUrlList;
            this.TcpIp = tcpIp;
            this.WebApiOptions = webApiOptions;
        }
    }

    public class VpnException : CoresException
    {
        public VpnErrors Error { get; }

        public Pack? Pack { get; }

        public VpnException(VpnErrors error, Pack? pack = null) : base($"{error.ToString()}")
        {
            this.Error = error;
            this.Pack = pack?.Clone() ?? null;
        }

        public bool IsCommuncationError => this.Error.IsCommunicationError();
    }

    public class WideTunnel : AsyncService
    {
        public WideTunnelOptions Options { get; }

        public TcpIpSystem TcpIp => Options.TcpIp;

        public WideTunnel(WideTunnelOptions options)
        {
            this.Options = options;
        }

        public bool CheckValidationCallback(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
        {
            // TODO: cert check
            return true;
        }

        public async Task<WtConnectParam> WideClientConnectInnerAsync(string pcid, WideTunnelClientOptions clientOptions = WideTunnelClientOptions.None, CancellationToken cancel = default)
        {
            // TODO: cache

            Pack r = new Pack();
            r.AddStr("SvcName", Options.SvcName);
            r.AddStr("Pcid", pcid);
            r.AddSInt("Ver", CoresConfig.WtcConfig.PseudoVer);
            r.AddSInt("Build", CoresConfig.WtcConfig.PseudoBuild);
            r.AddInt("ClientOptions", (uint)clientOptions);
            r.AddData("ClientId", Options.ClientId);

            var p = await WtWpcCall("ClientConnect", r, cancel);
            p.ThrowIfError();

            WtConnectParam c = new WtConnectParam
            {
                HostName = p["Hostname"].StrValueNonNullCheck,
                HostNameForProxy = p["HostNameForProxy"].StrValueNonNullCheck,
                Port = p["Port"].SIntValue,
                SessionId = p["SessionId"].DataValueNonNull,
                ServerMask64 = p["ServerMask64"].Int64Value,
            };

            if (c.HostName._IsSamei("<<!!samehost!!>>"))
            {
                string hostTmp = p["__remote_hostname"].StrValueNonNull;
                int portTmp = p["__remote_port"].SIntValue;
                if (hostTmp._IsFilled() && portTmp != 0)
                {
                    c.HostName = hostTmp;
                    c.Port = portTmp;
                }
            }

            c.CacheUsed = false;

            return c;
        }

        public async Task<Pack> WtWpcCall(string funciontName, Pack requestPack, CancellationToken cancel = default)
        {
            long secs = Util.ConvertDateTime(DtUtcNow) / (long)Consts.Intervals.WtEntranceUrlTimeUpdateMsecs;

            Exception? firstCommError = null;

            // EntryList 一覧にある URL をシャッフルして順序を決める
            List<string> thisTimeEntryList = this.Options.EntryUrlList._Shuffle().ToList();

            // 順に試行
            foreach (string url in thisTimeEntryList)
            {
                cancel.ThrowIfCancellationRequested();

                Pack requestPackCopy = requestPack.Clone();

                string url2 = url._ReplaceStr("__TIME__", secs.ToString(), false);

                $"Trying for the URL: {url2}"._Debug();

                try
                {
                    Pack pRet = await WtWpcCallInner(funciontName, requestPackCopy, url2, cancel);

                    // エラーの場合は例外を発生させる
                    pRet.ThrowIfError();

                    // OK。コレを返す
                    return pRet;
                }
                catch (Exception ex)
                {
                    if (ex._IsVpnCommuncationError() == false)
                    {
                        // 通信エラー以外のエラー (すなわちサーバー側のエラー) が返ってきた
                        throw;
                    }

                    // 最初のエラーは保存しておく
                    if (firstCommError == null)
                    {
                        firstCommError = ex;
                    }
                }
            }

            // 最初の通信エラーを throw する
            if (firstCommError != null)
            {
                throw firstCommError;
            }

            // エラーがない。おかしいな
            throw new VpnException(VpnErrors.ERR_INTERNAL_ERROR);
        }

        async Task<Pack> WtWpcCallInner(string functionName, Pack requestPack, string url, CancellationToken cancel = default)
        {
            requestPack.AddStr("function", functionName);

            WpcPack wpcPostPack = new WpcPack(requestPack);

            var wpcPostBuffer = wpcPostPack.ToPacketBinary().Span.ToArray();

            using var http = new WebApi(this.Options.WebApiOptions, this.CheckValidationCallback);

            var webRet = await http.SimplePostDataAsync(url, wpcPostBuffer, cancel, Consts.MimeTypes.FormUrlEncoded);

            WpcPack wpcResponsePack = WpcPack.Parse(webRet.ToString(), false);

            var retPack = wpcResponsePack.Pack;

            Uri uri = url._ParseUrl();

            retPack.AddStr("__remote_hostname", uri.Host);
            retPack.AddSInt("__remote_port", uri.Port);

            return retPack;
        }
    }
}

#endif

