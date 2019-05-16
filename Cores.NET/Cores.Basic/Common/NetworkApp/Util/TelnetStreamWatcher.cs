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
using System.IO;
using System.Runtime.Serialization;

namespace IPA.Cores.Basic
{
    class TelnetStreamWatcherOptions
    {
        public TcpIpSystem TcpIpSystem { get; }
        public Func<IPAddress, bool> IPAccessFilter { get; }
        public IReadOnlyList<IPEndPoint> EndPoints { get; }

        public TelnetStreamWatcherOptions(Func<IPAddress, bool> ipAccessFilter, TcpIpSystem tcpIpSystem, params IPEndPoint[] endPoints)
        {
            if (ipAccessFilter == null) ipAccessFilter = new Func<IPAddress, bool>((address) => true);
            if (tcpIpSystem == null) tcpIpSystem = LocalNet;

            this.IPAccessFilter = ipAccessFilter;
            this.EndPoints = endPoints.ToList();
            this.TcpIpSystem = tcpIpSystem;
        }
    }

    abstract class TelnetStreamWatcherBase : AsyncService
    {
        public TelnetStreamWatcherOptions Options { get; }

        readonly NetTcpListenerBase Listener;

        TcpIpSystem Net => Options.TcpIpSystem;

        protected abstract Task<PipeEnd> SubscribeImplAsync();
        protected abstract Task UnsubscribeImplAsync(PipeEnd pipe);

        public TelnetStreamWatcherBase(TelnetStreamWatcherOptions options)
        {
            this.Options = options;

            Listener = Net.CreateListener(new TcpListenParam(async (listener, sock) =>
            {
                try
                {
                    Con.WriteDebug($"TelnetStreamWatcher({this.ToString()}: Connected: {sock.EndPointInfo._GetObjectDump()}");

                    using (var destStream = sock.GetStream())
                    {
                        if (Options.IPAccessFilter(sock.Info.Ip.RemoteIPAddress) == false)
                        {
                            Con.WriteDebug($"TelnetStreamWatcher({this.ToString()}: Access denied: {sock.EndPointInfo._GetObjectDump()}");

                            await destStream.WriteAsync("You are not allowed to access to this service.\r\n\r\n"._GetBytes_Ascii());

                            await Task.Delay(100);

                            return;
                        }

                        using (var pipeEnd = await SubscribeImplAsync())
                        {
                            try
                            {
                                using (var pipeStub = pipeEnd.GetNetAppProtocolStub())
                                using (var srcStream = pipeStub.GetStream())
                                {
                                    await srcStream.CopyToAsync(destStream, sock.GrandCancel);
                                }
                            }
                            finally
                            {
                                await UnsubscribeImplAsync(pipeEnd);

                                await pipeEnd.CleanupAsync(new DisconnectedException());
                            }
                        }
                    }
                }
                finally
                {
                    Con.WriteDebug($"TelnetStreamWatcher({this.ToString()}: Disconnected: {sock.EndPointInfo._GetObjectDump()}");
                }
            }, this.Options.EndPoints.ToArray()));

            this.AddIndirectDisposeLink(this.Listener);
        }
    }

    [Serializable]
    [DataContract]
    class TelnetLocalLogWatcherConfig : INormalizable
    {
        [DataMember]
        public string Filters;

        public void Normalize()
        {
            this.Filters = this.Filters._NonNullTrim();
        }
    }

    class TelnetLocalLogWatcher : TelnetStreamWatcherBase
    {
        public readonly static StaticModule Module = new StaticModule(InitModule, FreeModule);
        static HiveData<TelnetLocalLogWatcherConfig> Config;

        static void InitModule()
        {
            Config = new HiveData<TelnetLocalLogWatcherConfig>(Hive.SharedConfigHive, "DebugSettings/LocalLogWatcher",
                () =>
                {
                    return new TelnetLocalLogWatcherConfig() { Filters = LocalLogRouter.BufferedLogRoute.KindHash.ToArray()._Combine(",") };
                },
                 HiveSyncPolicy.None);
        }

        static void FreeModule()
        {
        }

        public TelnetLocalLogWatcher(TelnetStreamWatcherOptions options) : base(options)
        {
        }

        protected override async Task<PipeEnd> SubscribeImplAsync()
        {
            await Config.SyncWithStorageAsync(HiveSyncFlags.LoadFromFile, true);

            lock (Config.DataLock)
            {
                LocalLogRouter.BufferedLogRoute.SetKind(Config.Data.Filters);
            }

            return LocalLogRouter.BufferedLogRoute.Subscribe();
        }

        protected override Task UnsubscribeImplAsync(PipeEnd pipe)
        {
            LocalLogRouter.BufferedLogRoute.Unsubscribe(pipe);
            return Task.CompletedTask;
        }
    }
}

