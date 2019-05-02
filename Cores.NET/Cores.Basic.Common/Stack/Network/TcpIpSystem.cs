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
    static partial class CoresConfig
    {
        public static partial class TcpIpStackDefaultSettings
        {
            public static readonly Copenhagen<int> ConnectTimeout = 15 * 1000;
            public static readonly Copenhagen<int> DnsTimeout = 15 * 1000;
        }
    }

    abstract class NetworkConnectParam
    {
        public IPAddress DestIp { get; private set; }
        public int DestPort { get; private set; }
        public IPAddress SrcIp { get; }
        public int SrcPort { get; }

        public bool NeedToSolveDestHostname { get; private set; }
        public string DestHostname { get; }
        public int DnsTimeout { get; }

        public AddressFamily AddressFamily => DestIp.AddressFamily;

        public NetworkConnectParam(string destHostname, int destPort, AddressFamily addressFamily = AddressFamily.InterNetwork, int srcPort = 0, int dnsTimeout = -1)
            : this(destHostname, destPort, GetAnyIPAddress(addressFamily), srcPort, dnsTimeout) { }

        public NetworkConnectParam(string destHostname, int destPort, IPAddress srcIp, int srcPort = 0, int dnsTimeout = -1)
            : this(GetAnyIPAddress(srcIp.AddressFamily), destPort, srcIp, srcPort)
        {
            if (dnsTimeout <= 0)
                dnsTimeout = CoresConfig.TcpIpStackDefaultSettings.DnsTimeout;

            this.NeedToSolveDestHostname = true;
            this.DestHostname = destHostname;
            this.DnsTimeout = dnsTimeout;
        }

        public NetworkConnectParam(IPAddress destIp, int destPort, IPAddress srcIp = null, int srcPort = 0)
        {
            if (destIp == null) throw new ArgumentNullException("destIp");
            if (CheckAddressFamily(destIp) == false) throw new ArgumentOutOfRangeException("destIp.AddressFamily");
            if (CheckPortRange(destPort) == false) throw new ArgumentOutOfRangeException("destPort");

            if (srcIp != null)
            {
                if (srcIp.AddressFamily != destIp.AddressFamily) throw new ArgumentException("srcIp.AddressFamily != destIp.AddressFamily");
            }
            else
            {
                if (destIp.AddressFamily == AddressFamily.InterNetwork)
                    srcIp = IPAddress.Any;
                else if (destIp.AddressFamily == AddressFamily.InterNetworkV6)
                    srcIp = IPAddress.IPv6Any;
                else
                    Debug.Assert(false, "Unsupported destIp.AddressFamily");
            }

            if (srcPort != 0 && CheckPortRange(srcPort) == false) throw new ArgumentOutOfRangeException("srcPort");

            this.DestIp = destIp;
            this.DestPort = destPort;
            this.SrcIp = srcIp;
            this.SrcPort = srcPort;
        }

        public static bool CheckPortRange(int port)
        {
            if (port < 1 || port > 65535) return false;
            return true;
        }

        public static bool CheckAddressFamily(IPAddress addr) => CheckAddressFamily(addr.AddressFamily);
        public static bool CheckAddressFamily(AddressFamily af)
        {
            if (af == AddressFamily.InterNetwork || af == AddressFamily.InterNetworkV6)
                return true;

            return false;
        }

        public static IPAddress GetAnyIPAddress(AddressFamily family)
        {
            if (family == AddressFamily.InterNetwork)
                return IPAddress.Any;
            else if (family == AddressFamily.InterNetworkV6)
                return IPAddress.IPv6Any;
            else
                throw new ArgumentOutOfRangeException("family");
        }

        public async Task ResolveDestHostnameIfNecessaryAsync(TcpIpSystem system, CancellationToken cancel = default)
        {
            if (this.NeedToSolveDestHostname == false) return;

            IPAddress ip = await system.GetIpAsync(this.DestHostname, this.AddressFamily, this.DnsTimeout, cancel);
            this.DestIp = ip;
            this.NeedToSolveDestHostname = false;
        }
    }

    class TcpConnectParam : NetworkConnectParam
    {
        public int ConnectTimeout { get; }

        public TcpConnectParam(IPAddress destIp, int destPort, IPAddress srcIp = null, int srcPort = 0, int connectTimeout = -1)
            : base(destIp, destPort, srcIp, srcPort)
        {
            if (connectTimeout <= 0) connectTimeout = CoresConfig.TcpIpStackDefaultSettings.ConnectTimeout;

            this.ConnectTimeout = connectTimeout;
        }

        public TcpConnectParam(string destHostname, int destPort, IPAddress srcIp, int srcPort = 0, int connectTimeout = -1, int dnsTimeout = -1)
            : base(destHostname, destPort, srcIp, srcPort, dnsTimeout)
        {
            if (connectTimeout <= 0) connectTimeout = CoresConfig.TcpIpStackDefaultSettings.ConnectTimeout;

            this.ConnectTimeout = connectTimeout;
        }

        public TcpConnectParam(string destHostname, int destPort, AddressFamily addressFamily = AddressFamily.InterNetwork, int srcPort = 0, int connectTimeout = -1, int dnsTimeout = -1)
            : base(destHostname, destPort, addressFamily, srcPort, dnsTimeout)
        {
            if (connectTimeout <= 0) connectTimeout = CoresConfig.TcpIpStackDefaultSettings.ConnectTimeout;

            this.ConnectTimeout = connectTimeout;
        }
    }

    class TcpListenParam
    {
        public FastTcpListenerAcceptedProcCallback AcceptCallback { get; }
        public IReadOnlyList<int> PortsList { get; }

        public TcpListenParam(FastTcpListenerAcceptedProcCallback acceptCallback, params int[] ports)
        {
            this.PortsList = new List<int>(ports);
            this.AcceptCallback = acceptCallback;
        }
    }

    [Flags]
    enum DnsQueryOptions
    {
        None = 0,
        IPv4Only = 1,
        IPv6Only = 2,
    }

    class DnsQueryParam
    {
        public DnsQueryOptions Options { get; }
        public int Timeout { get; }

        public DnsQueryParam(DnsQueryOptions options = DnsQueryOptions.None, int timeout = -1)
        {
            if (timeout <= 0)
                timeout = CoresConfig.TcpIpStackDefaultSettings.DnsTimeout;

            this.Timeout = timeout;
        }
    }

    class DnsGetIpQueryParam : DnsQueryParam
    {
        public string Hostname { get; }

        public DnsGetIpQueryParam(string hostname, DnsQueryOptions options = DnsQueryOptions.None, int timeout = -1) : base(options, timeout)
        {
            if (hostname.IsEmpty()) throw new ArgumentException("hostname is empty.");
            this.Hostname = hostname.NonNullTrim();
        }
    }

    class DnsResponse
    {
        public DnsQueryParam Query { get; }
        public IReadOnlyList<IPAddress> IPAddressList { get; }

        public DnsResponse(DnsQueryParam query, IPAddress[] addressList)
        {
            this.Query = query;

            IEnumerable<IPAddress> filtered = addressList;

            if (this.Query.Options.Bit(DnsQueryOptions.IPv4Only))
                filtered = filtered.Where(x => x.AddressFamily == AddressFamily.InterNetwork);

            if (this.Query.Options.Bit(DnsQueryOptions.IPv6Only))
                filtered = filtered.Where(x => x.AddressFamily == AddressFamily.InterNetworkV6);

            this.IPAddressList = new List<IPAddress>(filtered);
        }
    }

    abstract class TcpIpSystemHostInfo
    {
        public virtual int InfoVersion { get; protected set; }
        public virtual string HostName { get; protected set; }
        public virtual string DomainName { get; protected set; }
        public virtual string FqdnHostName => HostName + (string.IsNullOrEmpty(DomainName) ? "" : "." + DomainName);
        public virtual bool IsIPv4Supported { get; protected set; }
        public virtual bool IsIPv6Supported { get; protected set; }
        public virtual IReadOnlyList<IPAddress> IPAddressList { get; protected set; }
    }

    class TcpIpSystemParam : NetworkSystemParam { }

    delegate Task TcpIpAcceptCallbackAsync(Listener listener, ConnSock newSock);

    interface ITcpConnectableSystem
    {
        Task<ConnSock> ConnectAsync(TcpConnectParam param, CancellationToken cancel = default);
    }

    abstract partial class TcpIpSystem : NetworkSystemBase, ITcpConnectableSystem
    {
        protected new TcpIpSystemParam Param => (TcpIpSystemParam)base.Param;

        protected abstract TcpIpSystemHostInfo GetHostInfoImpl();
        protected abstract FastTcpProtocolStubBase CreateTcpProtocolStubImpl(AsyncCleanuperLady lady, TcpConnectParam param, CancellationToken cancel);
        protected abstract Task<DnsResponse> QueryDnsImplAsync(DnsQueryParam param, CancellationToken cancel);
        protected abstract FastTcpListenerBase CreateListenerImpl(AsyncCleanuperLady lady, TcpListenParam param);

        public TcpIpSystem(AsyncCleanuperLady lady, TcpIpSystemParam param) : base(lady, param)
        {
            try
            {
            }
            catch
            {
                Lady.DisposeSafe();
                throw;
            }
        }

        public TcpIpSystemHostInfo GetHostInfo() => GetHostInfoImpl();

        public async Task<ConnSock> ConnectAsync(TcpConnectParam param, CancellationToken cancel = default)
        {
            using (CreatePerTaskCancellationToken(out CancellationToken opCancel, cancel))
            {
                await param.ResolveDestHostnameIfNecessaryAsync(this, opCancel);

                using (EnterCriticalCounter())
                {
                    AsyncCleanuperLady lady = new AsyncCleanuperLady();

                    try
                    {
                        FastTcpProtocolStubBase tcp = CreateTcpProtocolStubImpl(lady, param, this.GrandCancel);

                        await tcp.ConnectAsync(new IPEndPoint(param.DestIp, param.DestPort), param.ConnectTimeout, opCancel);

                        ConnSock sock = new ConnSock(lady, tcp);

                        this.AddToOpenedSockList(sock);

                        sock.AddOnDispose(() => this.RemoveFromOpenedSockList(sock));

                        return sock;
                    }
                    catch
                    {
                        await lady;
                        throw;
                    }
                }
            }
        }
        public ConnSock Connect(TcpConnectParam param, CancellationToken cancel = default)
            => ConnectAsync(param, cancel).GetResult();

        public FastTcpListenerBase CreateListener(AsyncCleanuperLady lady, TcpListenParam param)
        {
            var hostInfo = GetHostInfo();

            using (EnterCriticalCounter())
            {
                FastTcpListenerBase ret = CreateListenerImpl(lady, param);

                foreach (int port in param.PortsList)
                {
                    if (hostInfo.IsIPv4Supported) ret.Add(port, IPVersion.IPv4);
                    if (hostInfo.IsIPv6Supported) ret.Add(port, IPVersion.IPv6);
                }

                return ret;
            }
        }

        public async Task<DnsResponse> QueryDnsAsync(DnsQueryParam param, CancellationToken cancel = default)
        {
            using (CreatePerTaskCancellationToken(out CancellationToken opCancel, cancel))
            {
                using (EnterCriticalCounter())
                {
                    return await QueryDnsImplAsync(param, opCancel);
                }
            }
        }
        public DnsResponse QueryDns(DnsQueryParam param, CancellationToken cancel = default)
            => QueryDnsAsync(param, cancel).GetResult();
    }
}

