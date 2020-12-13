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

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Collections.Immutable;

namespace IPA.Cores.Basic
{
    public static partial class CoresConfig
    {
        public static partial class DnsResolverDefaults
        {
            public static readonly Copenhagen<int> TimeoutOneQueryMsecs = 250;
            public static readonly Copenhagen<int> NumTry = 6;
            public static readonly Copenhagen<int> MinCacheTimeoutMsecs = 3600 * 1000;
            public static readonly Copenhagen<int> MaxCacheTimeoutMsecs = 6 * 3600 * 1000;
            public static readonly Copenhagen<int> ReverseLookupInternalCacheTimeoutMsecs = 3600 * 1000;
        }
    }

    [Flags]
    public enum DnsResolverFlags : ulong
    {
        None = 0,
        UseSystemDnsClientSettings = 1,
        UdpOnly = 2,
        TcpOnly = 4,
        RoundRobinServers = 8,
        DisableCache = 16,
        ThrowDnsError = 32,
        DisableRecursion = 64,

        Default = UseSystemDnsClientSettings | RoundRobinServers,
    }

    public class DnsResolverSettings
    {
        public TcpIpSystem TcpIp { get; }
        public DnsResolverFlags Flags { get; }
        public int TimeoutOneQuery { get; }
        public int NumTry { get; }
        public int MinCacheTimeout { get; }
        public int MaxCacheTimeout { get; }
        public IReadOnlyList<IPEndPoint> DnsServersList { get; }
        public int ReverseLookupIntervalCacheTimeoutMsecs { get; }

        public DnsResolverSettings(TcpIpSystem? tcpIp = null, DnsResolverFlags flags = DnsResolverFlags.Default,
            int timeoutOneQuery = -1, int numTry = -1, int minCacheTimeout = -1, int maxCacheTimeout = -1, IEnumerable<IPEndPoint>? dnsServersList = null,
            int reverseLookupInternalCacheTimeoutMsecs = -1)
        {
            if (tcpIp == null) tcpIp = LocalNet;
            if (timeoutOneQuery <= 0) timeoutOneQuery = CoresConfig.DnsResolverDefaults.TimeoutOneQueryMsecs;
            if (numTry <= 0) numTry = CoresConfig.DnsResolverDefaults.NumTry;
            if (minCacheTimeout <= 0) minCacheTimeout = CoresConfig.DnsResolverDefaults.MinCacheTimeoutMsecs;
            if (maxCacheTimeout <= 0) maxCacheTimeout = CoresConfig.DnsResolverDefaults.MaxCacheTimeoutMsecs;
            if (reverseLookupInternalCacheTimeoutMsecs < 0) reverseLookupInternalCacheTimeoutMsecs = CoresConfig.DnsResolverDefaults.ReverseLookupInternalCacheTimeoutMsecs;

            this.TcpIp = tcpIp;
            this.Flags = flags;
            this.TimeoutOneQuery = timeoutOneQuery;
            this.NumTry = numTry;
            this.MinCacheTimeout = minCacheTimeout;
            this.MaxCacheTimeout = maxCacheTimeout;
            this.ReverseLookupIntervalCacheTimeoutMsecs = reverseLookupInternalCacheTimeoutMsecs;
            if (dnsServersList != null)
            {
                this.DnsServersList = dnsServersList.ToList();
            }
            else
            {
                this.DnsServersList = new List<IPEndPoint>();
            }
        }
    }

    public class DnsAdditionalResults
    {
        public bool IsError { get; set; }
        public bool IsNotFound { get; set; }
    }

    public abstract class DnsResolver : AsyncService
    {
        public abstract bool IsAvailable { get; }

        public DnsResolverSettings Settings { get; }

        protected abstract Task<IEnumerable<string>?> GetHostNameImplAsync(IPAddress ip, Ref<DnsAdditionalResults>? additional = null, CancellationToken cancel = default);

        public DnsResolver(DnsResolverSettings? setting = null)
        {
            try
            {
                if (setting == null) setting = new DnsResolverSettings();

                this.Settings = setting;
            }
            catch
            {
                this._DisposeSafe();
                throw;
            }
        }

        public async Task<List<string>?> GetHostNameAsync(string ip, Ref<DnsAdditionalResults>? additional = null, CancellationToken cancel = default)
        {
            try
            {
                return await GetHostNameAsync(ip._ToIPAddress(), additional, cancel);
            }
            catch
            {
                return null;
            }
        }


        public async Task<List<string>?> GetHostNameAsync(IPAddress? ip, Ref<DnsAdditionalResults>? additional = null, CancellationToken cancel = default)
        {
            try
            {
                if (ip == null) return null;

                var ipType = ip._GetIPAddressType();

                if (ipType.Bit(IPAddressType.Loopback))
                {
                    return "localhost"._SingleList();
                }

                List<string>? ret = new List<string>();

                IEnumerable<string>? tmp = null;

                tmp = await GetHostNameImplAsync(ip, additional, cancel);

                if (tmp != null)
                {
                    foreach (string fqdn in tmp)
                    {
                        string a = fqdn.Trim().TrimEnd('.');

                        if (a._IsFilled())
                        {
                            if (ret.Contains(a, StrComparer.IgnoreCaseComparer) == false)
                            {
                                ret.Add(a);
                            }
                        }
                    }
                }

                if (ret.Any() == false)
                {
                    ret = null;
                }

                return ret;
            }
            catch
            {
                return null;
            }
        }

        public async Task<string> GetHostNameSingleOrIpAsync(IPAddress? ip, CancellationToken cancel = default)
        {
            if (ip == null) return "";

            try
            {
                var res = await GetHostNameAsync(ip, null, cancel);

                if (res._IsEmpty())
                {
                    return ip.ToString();
                }

                return res.First();
            }
            catch
            {
                return ip.ToString();
            }
        }
        public Task<string> GetHostNameSingleOrIpAsync(string? ip, CancellationToken cancel = default)
            => GetHostNameSingleOrIpAsync(ip._ToIPAddress(noExceptionAndReturnNull: true), cancel);

        public static DnsResolver CreateDnsResolverIfSupported(DnsResolverSettings? settings)
        {
#if CORES_BASIC_MISC
            return new DnsClientLibBasedDnsResolver(settings);
#else // CORES_BASIC_MISC
            return new UnimplementedDnsResolver(settings);
#endif // CORES_BASIC_MISC
        }
    }

    public class UnimplementedDnsResolver : DnsResolver
    {
        public override bool IsAvailable => false;

        protected override Task<IEnumerable<string>?> GetHostNameImplAsync(IPAddress ip, Ref<DnsAdditionalResults>? additional = null, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }
    }

}

#endif

