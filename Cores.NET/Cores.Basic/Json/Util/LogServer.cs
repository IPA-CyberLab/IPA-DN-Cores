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
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace IPA.Cores.Basic
{
    static partial class CoresConfig
    {
        public static partial class LogServerSettings
        {
            public static readonly Copenhagen<int> DefaultPort = 3003;

            public static readonly Copenhagen<int> DefaultRecvTimeout = 2000;
            public static readonly Copenhagen<int> DefaultSendKeepAliveInterval = 1000;

            public static readonly Copenhagen<int> MaxDataSize = (64 * 1024 * 1024); // 64MB
        }
    }

    [Flags]
    enum LogProtocolDataType
    {
        StandardLog = 0,
    }

    abstract class LogServerOptionsBase : SslServerOptions
    {
        public readonly Copenhagen<int> RecvTimeout = CoresConfig.LogServerSettings.DefaultRecvTimeout;
        public readonly Copenhagen<int> SendKeepAliveInterval = CoresConfig.LogServerSettings.DefaultSendKeepAliveInterval;

        public LogServerOptionsBase(TcpIpSystem tcpIpSystem, PalSslServerAuthenticationOptions sslAuthOptions, params IPEndPoint[] endPoints)
            : base(tcpIpSystem, sslAuthOptions, endPoints.Any() ? endPoints : IPUtil.GenerateListeningEndPointsList(false, CoresConfig.LogServerSettings.DefaultPort))
        {
        }
    }

    abstract class LogServerBase : SslServerBase
    {
        public const int MagicNumber = 0x415554a4;
        public const int ServerVersion = 1;

        protected new LogServerOptionsBase Options => (LogServerOptionsBase)base.Options;

        public LogServerBase(LogServerOptionsBase options) : base(options)
        {
        }

        protected override async Task SslAcceptedImplAsync(NetTcpListenerPort listener, SslSock sock)
        {
            sock.AttachHandle.SetStreamReceiveTimeout(this.Options.RecvTimeout);

            using (PipeStream st = sock.GetStream())
            {
                int magicNumber = await st.ReceiveSInt32Async();
                if (magicNumber != MagicNumber) throw new ApplicationException($"Invalid magicNumber = 0x{magicNumber:X}");

                int clientVersion = await st.ReceiveSInt32Async();

                MemoryBuffer<byte> sendBuffer = new MemoryBuffer<byte>();
                sendBuffer.WriteSInt32(ServerVersion);
                await st.SendAsync(sendBuffer);

                while (true)
                {
                    LogProtocolDataType type = (LogProtocolDataType)await st.ReceiveSInt32Async();

                    switch (type)
                    {
                        case LogProtocolDataType.StandardLog:
                            {
                                int size = await st.ReceiveSInt32Async();

                                if (size > CoresConfig.LogServerSettings.MaxDataSize)
                                    throw new ApplicationException($"size > MaxDataSize. size = {size}");

                                using (MemoryHelper.FastAllocMemoryWithUsing(size, out Memory<byte> data))
                                {
                                    await st.ReceiveAllAsync(data);

                                    await LogDataReceivedInternal(data);
                                }

                                break;
                            }

                        default:
                            throw new ApplicationException("Invalid LogProtocolDataType");
                    }
                }
            }
        }

        async Task LogDataReceivedInternal(Memory<byte> data)
        {
        }
    }
}

#endif  // CORES_BASIC_JSON

