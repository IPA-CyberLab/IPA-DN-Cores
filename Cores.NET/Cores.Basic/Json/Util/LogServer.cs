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

#if CORES_BASIC_JSON

using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    static partial class CoresConfig
    {
        public static partial class LogServerSettings
        {
            public static readonly Copenhagen<int> DefaultPort = 3003;
        }
    }

    class LogServerOptions
    {
        public IReadOnlyList<IPEndPoint> EndPoints { get; }
        public TcpIpSystem TcpIpSystem { get; }
        public CertSelectorCallback CertSelector { get; }

        public static readonly IPEndPoint[] DefaultEndPoints = IPUtil.GenerateListeningEndPointsList(false, CoresConfig.LogServerSettings.DefaultPort);

        public LogServerOptions(TcpIpSystem tcpIpSystem, params IPEndPoint[] endPoints)
        {
            this.TcpIpSystem = tcpIpSystem;
            this.EndPoints = endPoints.ToList();
        }
    }

    class LogServer : AsyncService
    {
        public LogServerOptions Options { get; }

        public LogServer(LogServerOptions options)
        {
            this.Options = options;

            NetTcpListener listener = this.Options.TcpIpSystem.CreateListener(new TcpListenParam(ListenerCallbackAsync, this.Options.EndPoints.ToArray()));

            this.AddIndirectDisposeLink(listener);
        }

        async Task ListenerCallbackAsync(NetTcpListenerPort listener, ConnSock newSock)
        {
            await Task.CompletedTask;
        }
    }
}

#endif  // CORES_BASIC_JSON

