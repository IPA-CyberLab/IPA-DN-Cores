// IPA Cores.NET
// 
// Copyright (c) 2018- IPA CyberLab.
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
    public static partial class CoresConfig
    {
        public static partial class TcpIpSystemSettings
        {
            public static readonly Copenhagen<int> LocalHostPossibleGlobalIpAddressListCacheLifetime = 5 * 60 * 1000;
        }

        public static partial class TcpIpStackDefaultSettings
        {
            public static readonly Copenhagen<int> ConnectTimeout = 15 * 1000;
            public static readonly Copenhagen<int> DnsTimeout = 15 * 1000;
        }
    }

    public abstract class NetworkConnectParam
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

    public class TcpConnectParam : NetworkConnectParam
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

    public class TcpListenParam
    {
        public NetTcpListenerAcceptedProcCallback AcceptCallback { get; }
        public IReadOnlyList<IPEndPoint> EndPointsList { get; }

        static IPEndPoint[] PortsToEndPoints(int[] ports)
        {
            List<IPEndPoint> ret = new List<IPEndPoint>();

            foreach (int port in ports)
            {
                ret.Add(new IPEndPoint(IPAddress.Any, port));
                ret.Add(new IPEndPoint(IPAddress.IPv6Any, port));
            }

            return ret.ToArray();
        }

        public TcpListenParam(NetTcpListenerAcceptedProcCallback acceptCallback, params int[] ports)
            : this(acceptCallback, PortsToEndPoints(ports)) { }

        public TcpListenParam(NetTcpListenerAcceptedProcCallback acceptCallback, params IPEndPoint[] endPoints)
        {
            this.EndPointsList = endPoints.ToList();
            this.AcceptCallback = acceptCallback;
        }

        public TcpListenParam(EnsureSpecial compatibleWithKestrel, NetTcpListenerAcceptedProcCallback acceptCallback, IPEndPoint endPoint)
        {
            List<IPEndPoint> ret = new List<IPEndPoint>();

            if (endPoint.Address == IPAddress.IPv6Any)
            {
                ret.Add(new IPEndPoint(IPAddress.Any, endPoint.Port));
                ret.Add(new IPEndPoint(IPAddress.IPv6Any, endPoint.Port));
            }
            else
            {
                ret.Add(endPoint);
            }

            this.EndPointsList = ret;
            this.AcceptCallback = acceptCallback;
        }
    }

    [Flags]
    public enum DnsQueryOptions
    {
        Default = 0,
        IPv4Only = 1,
        IPv6Only = 2,
    }

    public abstract class DnsQueryParamBase
    {
        public DnsQueryOptions Options { get; }
        public int Timeout { get; }

        public DnsQueryParamBase(DnsQueryOptions options = DnsQueryOptions.Default, int timeout = -1)
        {
            if (timeout <= 0)
                timeout = CoresConfig.TcpIpStackDefaultSettings.DnsTimeout;

            this.Timeout = timeout;
        }
    }

    public class DnsGetIpQueryParam : DnsQueryParamBase
    {
        public string Hostname { get; }

        public DnsGetIpQueryParam(string hostname, DnsQueryOptions options = DnsQueryOptions.Default, int timeout = -1) : base(options, timeout)
        {
            if (hostname._IsEmpty()) throw new ArgumentException("hostname is empty.");
            this.Hostname = hostname._NonNullTrim();
        }
    }

    public class DnsResponse
    {
        public DnsQueryParamBase Query { get; }
        public IReadOnlyList<IPAddress> IPAddressList { get; }

        public DnsResponse(DnsQueryParamBase query, IPAddress[] addressList)
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

    public abstract class TcpIpSystemHostInfo
    {
        public virtual int InfoVersion { get; protected set; }
        public virtual string HostName { get; protected set; }
        public virtual string DomainName { get; protected set; }
        public virtual string FqdnHostName => HostName + (string.IsNullOrEmpty(DomainName) ? "" : "." + DomainName);
        public virtual bool IsIPv4Supported { get; protected set; }
        public virtual bool IsIPv6Supported { get; protected set; }
        public virtual IReadOnlyList<IPAddress> IPAddressList { get; protected set; }
    }

    public class TcpIpSystemParam : NetworkSystemParam
    {
        public TcpIpSystemParam(string name) : base(name) { }
    }

    public interface ITcpConnectableSystem
    {
        Task<ConnSock> ConnectAsync(TcpConnectParam param, CancellationToken cancel = default);
    }

    public abstract partial class TcpIpSystem : NetworkSystemBase, ITcpConnectableSystem
    {
        protected new TcpIpSystemParam Param => (TcpIpSystemParam)base.Param;

        protected abstract TcpIpSystemHostInfo GetHostInfoImpl();
        protected abstract NetTcpProtocolStubBase CreateTcpProtocolStubImpl(TcpConnectParam param, CancellationToken cancel);
        protected abstract Task<DnsResponse> QueryDnsImplAsync(DnsQueryParamBase param, CancellationToken cancel);
        protected abstract NetTcpListener CreateListenerImpl(NetTcpListenerAcceptedProcCallback acceptedProc);

        AsyncCache<HashSet<IPAddress>> LocalHostPossibleGlobalIpAddressListCache;

        public TcpIpSystem(TcpIpSystemParam param) : base(param)
        {
            LocalHostPossibleGlobalIpAddressListCache = new AsyncCache<HashSet<IPAddress>>(CoresConfig.TcpIpSystemSettings.LocalHostPossibleGlobalIpAddressListCacheLifetime, CacheFlags.IgnoreUpdateError,
                GetLocalHostPossibleGlobalIpAddressListMainAsync);
        }

        public TcpIpSystemHostInfo GetHostInfo() => GetHostInfoImpl();

        public async Task<ConnSock> ConnectAsync(TcpConnectParam param, CancellationToken cancel = default)
        {
            using (CreatePerTaskCancellationToken(out CancellationToken opCancel, cancel))
            {
                await param.ResolveDestHostnameIfNecessaryAsync(this, opCancel);

                using (EnterCriticalCounter())
                {
                    NetTcpProtocolStubBase tcp = CreateTcpProtocolStubImpl(param, this.GrandCancel);

                    try
                    {
                        await tcp.ConnectAsync(new IPEndPoint(param.DestIp, param.DestPort), param.ConnectTimeout, opCancel);

                        ConnSock sock = new ConnSock(tcp);
                        try
                        {
                            this.AddToOpenedSockList(sock, LogTag.SocketConnected);

                            return sock;
                        }
                        catch
                        {
                            await sock._DisposeWithCleanupSafeAsync();
                            throw;
                        }
                    }
                    catch
                    {
                        await tcp._DisposeWithCleanupSafeAsync();
                        throw;
                    }
                }
            }
        }
        public ConnSock Connect(TcpConnectParam param, CancellationToken cancel = default)
            => ConnectAsync(param, cancel)._GetResult();

        public NetTcpListener CreateListener(TcpListenParam param)
        {
            var hostInfo = GetHostInfo();

            using (EnterCriticalCounter())
            {
                NetTcpListener ret = CreateListenerImpl((listener, sock) =>
                {
                    this.AddToOpenedSockList(sock, LogTag.SocketAccepted);

                    return param.AcceptCallback(listener, sock);
                });

                foreach (IPEndPoint ep in param.EndPointsList)
                {
                    try
                    {
                        if (hostInfo.IsIPv4Supported && ep.AddressFamily == AddressFamily.InterNetwork) ret.Add(ep.Port,  IPVersion.IPv4, ep.Address);
                    }
                    catch { }

                    try
                    {
                        if (hostInfo.IsIPv6Supported && ep.AddressFamily == AddressFamily.InterNetworkV6) ret.Add(ep.Port, IPVersion.IPv6, ep.Address);
                    }
                    catch { }
                }

                return ret;
            }
        }

        public async Task<DnsResponse> QueryDnsAsync(DnsQueryParamBase param, CancellationToken cancel = default)
        {
            using (CreatePerTaskCancellationToken(out CancellationToken opCancel, cancel))
            {
                using (EnterCriticalCounter())
                {
                    return await QueryDnsImplAsync(param, opCancel);
                }
            }
        }
        public DnsResponse QueryDns(DnsQueryParamBase param, CancellationToken cancel = default)
            => QueryDnsAsync(param, cancel)._GetResult();

        async Task<HashSet<IPAddress>> GetLocalHostPossibleGlobalIpAddressListMainAsync(CancellationToken cancel)
        {
            HashSet<IPAddress> ret = new HashSet<IPAddress>();

            TcpIpSystemHostInfo info = this.GetHostInfo();

            foreach (IPAddress addr in info.IPAddressList)
            {
                IPAddressType ipType = addr._GetIPAddressType();
                if (ipType.Bit(IPAddressType.GlobalIp))
                {
                    ret.Add(addr);
                }
            }

            using (GetMyIpClient myip = new GetMyIpClient(this))
            {
                try
                {
                    IPAddress ipv4 = await myip.GetMyIpAsync(IPVersion.IPv4, cancel);

                    if (ipv4._GetIPAddressType().Bit(IPAddressType.GlobalIp))
                    {
                        ret.Add(ipv4);
                    }
                }
                catch { }

                try
                {
                    IPAddress ipv6 = await myip.GetMyIpAsync(IPVersion.IPv6, cancel);

                    if (ipv6._GetIPAddressType().Bit(IPAddressType.GlobalIp))
                    {
                        ret.Add(ipv6);
                    }
                }
                catch { }
            }

            return ret;
        }

        public Task<HashSet<IPAddress>> GetLocalHostPossibleIpAddressListAsync(CancellationToken cancel = default)
            => LocalHostPossibleGlobalIpAddressListCache.GetAsync(cancel);
    }
}

