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
    abstract partial class TcpIpSystem
    {
        public async Task<IPAddress> GetIpAsync(string hostname, AddressFamily? addressFamily = null, int timeout = -1, CancellationToken cancel = default)
        {
            DnsResponse res = await this.QueryDnsAsync(new DnsGetIpQueryParam(hostname, timeout: timeout));

            return res.IPAddressList.Where(x => x.AddressFamily == AddressFamily.InterNetwork || x.AddressFamily == AddressFamily.InterNetworkV6)
                .Where(x => addressFamily == null || x.AddressFamily == addressFamily).First();
        }

        public IPAddress GetIp(string hostname, AddressFamily? addressFamily = null, int timeout = -1, CancellationToken cancel = default)
            => GetIpAsync(hostname, addressFamily, timeout, cancel).GetResult();
    }

    class MiddleSock : NetworkSock
    {
        public MiddleSock(NetworkSock LowerSock, FastProtocolBase protocolStack) : base(LowerSock.Lady, protocolStack) { }
    }

    class SslSock : MiddleSock
    {
        protected new FastSslProtocolStack Stack => (FastSslProtocolStack)base.Stack;

        public SslSock(StreamSock lowerStreamSock) : base(lowerStreamSock, new FastSslProtocolStack(lowerStreamSock.Lady, lowerStreamSock.UpperEnd, null, null))
        {
        }

        public async Task StartSslClientAsync(PalSslClientAuthenticationOptions sslClientAuthenticationOptions, CancellationToken cancellationToken = default)
        {
            await Stack.SslStartClientAsync(sslClientAuthenticationOptions, cancellationToken);
        }

        public void StartSslClient(PalSslClientAuthenticationOptions sslClientAuthenticationOptions, CancellationToken cancellationToken = default)
            => StartSslClientAsync(sslClientAuthenticationOptions, cancellationToken).GetResult();
    }
}

