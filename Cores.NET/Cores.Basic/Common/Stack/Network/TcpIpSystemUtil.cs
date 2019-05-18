// IPA Cores.NET
// 
// Copyright (c) 2018-2019 IPA CyberLab.
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

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    abstract class SpeculativeConnectorBase
    {
        protected class Attempt
        {
            public ITcpConnectableSystem System { get; }
            public TcpConnectParam Param { get; }
            public int PostWaitMsecs { get; }

            public Attempt(ITcpConnectableSystem system, TcpConnectParam param, int postWaitMsecs)
            {
                this.System = system;
                this.Param = param;
                this.PostWaitMsecs = postWaitMsecs;
            }

            public async Task<ConnSock> ConnectAsyncInternal(SpeculativeConnectorBase connector, CancellationToken cancel = default)
            {
                try
                {
                    cancel.ThrowIfCancellationRequested();
                    ConnSock tcp = await this.System.ConnectAsync(this.Param, cancel);

                    if (PostWaitMsecs >= 1)
                    {
                        await connector.PostWaitEvent.WaitAsync(this.PostWaitMsecs, cancel);
                    }

                    return tcp;
                }
                catch (Exception ex)
                {
                    if (PostWaitMsecs == 0)
                    {
                        connector.PostWaitEvent.Set();
                    }

                    connector.ExceptionList.Add(ex, ex is GetIpAddressFamilyMismatchException ? 1 : 100);
                    throw;
                }
            }
        }

        readonly WeightedExceptionList ExceptionList = new WeightedExceptionList();

        AsyncManualResetEvent PostWaitEvent = new AsyncManualResetEvent();

        List<Attempt> AttemptList = new List<Attempt>();

        public TcpConnectParam BasicParam { get; }

        public SpeculativeConnectorBase(TcpConnectParam basicParam)
        {
            this.BasicParam = basicParam;
        }

        protected void AddAttempt(Attempt attempt)
        {
            AttemptList.Add(attempt);
        }

        Once Flag;

        public async Task<ConnSock> ConnectAsync(CancellationToken cancel = default)
        {
            if (Flag.IsFirstCall() == false) throw new ApplicationException("ConnectAsync() has been already called.");

            List<Task<ConnSock>> taskList = new List<Task<ConnSock>>();

            foreach (var attempt in AttemptList)
            {
                taskList.Add(attempt.ConnectAsyncInternal(this, cancel));
            }

            if (taskList.Count == 0)
            {
                throw new ApplicationException("SpeculativeConnectorBase: The attempt list is empty.");
            }

            while (true)
            {
                var okTask = taskList.Where(x => x.IsCompletedSuccessfully).FirstOrDefault();
                if (okTask != null)
                {
                    // OK
                    PostWaitEvent.Set();
                    taskList.ForEach(x => x._TryWait(true));
                    taskList.Where(x => x != okTask).Where(x => x.IsCompletedSuccessfully)._DoForEach(x => x.Result._DisposeSafe());
                    return okTask._GetResult();
                }

                taskList.Where(x => x.IsCanceled || x.IsFaulted).ToArray()._DoForEach(x => taskList.Remove(x));

                if (taskList.Count == 0)
                {
                    // Error
                    throw this.ExceptionList.GetException();
                }

                await TaskUtil.WaitObjectsAsync(taskList.ToArray(), exceptions: ExceptionWhen.None);
            }
        }
    }

    class IPv4V6DualStackSpeculativeConnector : SpeculativeConnectorBase
    {
        public IPv4V6DualStackSpeculativeConnector(TcpConnectParam basicParam, TcpIpSystem system) : base(basicParam)
        {
            if (basicParam.DestHostname._IsEmpty())
            {
                // IP address is specified
                AddAttempt(new Attempt(system, basicParam, 0));
                return;
            }

            // Hostname is specified
            var hostInfo = system.GetHostInfo();

            if (hostInfo.IsIPv4Supported)
            {
                AddAttempt(new Attempt(system,new TcpConnectParam(basicParam.DestHostname, basicParam.DestPort, AddressFamily.InterNetwork,
                    connectTimeout: basicParam.ConnectTimeout, dnsTimeout: basicParam.DnsTimeout), 0));
            }

            if (hostInfo.IsIPv6Supported)
            {
                AddAttempt(new Attempt(system, new TcpConnectParam(basicParam.DestHostname, basicParam.DestPort, AddressFamily.InterNetworkV6,
                    connectTimeout: basicParam.ConnectTimeout, dnsTimeout: basicParam.DnsTimeout), 0));
            }
        }
    }


    class GetIpAddressFamilyMismatchException : ApplicationException
    {
        public GetIpAddressFamilyMismatchException(string message) : base(message) { }
    }

    abstract partial class TcpIpSystem
    {
        public async Task<IPAddress> GetIpAsync(string hostname, AddressFamily? addressFamily = null, int timeout = -1, CancellationToken cancel = default)
        {
            DnsResponse res = await this.QueryDnsAsync(new DnsGetIpQueryParam(hostname, timeout: timeout));

            var ret = res.IPAddressList.Where(x => x.AddressFamily == AddressFamily.InterNetwork || x.AddressFamily == AddressFamily.InterNetworkV6)
                .Where(x => addressFamily == null || x.AddressFamily == addressFamily).FirstOrDefault();

            if (ret == null)
            {
                throw new GetIpAddressFamilyMismatchException($"The hostname \"{hostname}\" has no {addressFamily._ToIPv4v6String()} address.");
            }

            return ret;
        }

        public IPAddress GetIp(string hostname, AddressFamily? addressFamily = null, int timeout = -1, CancellationToken cancel = default)
            => GetIpAsync(hostname, addressFamily, timeout, cancel)._GetResult();

        public async Task<ConnSock> ConnectIPv4v6DualAsync(TcpConnectParam param, CancellationToken cancel = default)
        {
            IPv4V6DualStackSpeculativeConnector connector = new IPv4V6DualStackSpeculativeConnector(param, this);

            return await connector.ConnectAsync(cancel);
        }
        public ConnSock ConnectIPv4v6Dual(TcpConnectParam param, CancellationToken cancel = default)
            => ConnectIPv4v6DualAsync(param, cancel)._GetResult();
    }

    class MiddleSock : NetSock
    {
        public MiddleSock(NetSock LowerSock, NetProtocolBase protocolStack) : base(protocolStack) { }
    }

    class MiddleConnSock : ConnSock
    {
        public MiddleConnSock(ConnSock LowerSock, NetProtocolBase protocolStack) : base(protocolStack) { }
    }

    class SslSock : MiddleConnSock
    {
        public LogDefSslSession SslSessionInfo { get; private set; }

        protected new NetSslProtocolStack Stack => (NetSslProtocolStack)base.Stack;

        public SslSock(ConnSock lowerStreamSock) : base(lowerStreamSock, new NetSslProtocolStack(lowerStreamSock.UpperEnd, null, null))
        {
        }

        public void UpdateSslSessionInfo()
        {
            this.SslSessionInfo = new LogDefSslSession()
            {
                IsServerMode = this.Info?.Ssl?.IsServerMode ?? false,
                SslProtocol = this.Info?.Ssl?.SslProtocol,
                CipherAlgorithm = this.Info?.Ssl?.CipherAlgorithm,
                CipherStrength = this.Info?.Ssl?.CipherStrength ?? 0,
                HashAlgorithm = this.Info?.Ssl?.HashAlgorithm,
                HashStrength = this.Info?.Ssl?.HashStrength ?? 0,
                KeyExchangeAlgorithm = this.Info?.Ssl?.KeyExchangeAlgorithm,
                KeyExchangeStrength = this.Info?.Ssl?.KeyExchangeStrength ?? 0,
                LocalCertificateInfo = this.Info?.Ssl?.LocalCertificate?.ToString(),
                LocalCertificateHashSHA1 = this.Info?.Ssl?.LocalCertificate?.HashSHA1,
                RemoteCertificateInfo = this.Info?.Ssl?.RemoteCertificate?.ToString(),
                RemoteCertificateHashSHA1 = this.Info?.Ssl?.RemoteCertificate?.HashSHA1,
            };
        }

        public async Task StartSslClientAsync(PalSslClientAuthenticationOptions sslClientAuthenticationOptions, CancellationToken cancellationToken = default)
        {
            await Stack.SslStartClientAsync(sslClientAuthenticationOptions, cancellationToken);
        }
        public void StartSslClient(PalSslClientAuthenticationOptions sslClientAuthenticationOptions, CancellationToken cancellationToken = default)
            => StartSslClientAsync(sslClientAuthenticationOptions, cancellationToken)._GetResult();

        public async Task StartSslServerAsync(PalSslServerAuthenticationOptions sslServerAuthenticationOptions, CancellationToken cancellationToken = default)
        {
            await Stack.SslStartServerAsync(sslServerAuthenticationOptions, cancellationToken);
        }
        public void StartSslServer(PalSslServerAuthenticationOptions sslServerAuthenticationOptions, CancellationToken cancellationToken = default)
            => StartSslServerAsync(sslServerAuthenticationOptions, cancellationToken)._GetResult();
    }

    static class SslSockHelper
    {
        public static async Task<SslSock> SslStartClientAsync(this ConnSock baseSock, PalSslClientAuthenticationOptions sslOptions, CancellationToken cancel = default)
        {
            SslSock ret = new SslSock(baseSock);

            await ret.StartSslClientAsync(sslOptions, cancel);

            return ret;
        }
        public static SslSock SslStartClient(this ConnSock baseSock, PalSslClientAuthenticationOptions sslOptions, CancellationToken cancel = default)
            => SslStartClientAsync(baseSock, sslOptions, cancel)._GetResult();
    }
}

