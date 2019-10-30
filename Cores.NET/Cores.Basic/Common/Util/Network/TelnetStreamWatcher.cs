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
using System.IO;
using System.Runtime.Serialization;

#pragma warning disable CA2235 // Mark all non-serializable fields

namespace IPA.Cores.Basic
{
    public class TelnetStreamWatcherOptions
    {
        public TcpIpSystem TcpIp { get; }
        public Func<IPAddress, bool> IPAccessFilter { get; }
        public IReadOnlyList<IPEndPoint> EndPoints { get; }

        public TelnetStreamWatcherOptions(Func<IPAddress, bool> ipAccessFilter, TcpIpSystem? tcpIp, params IPEndPoint[] endPoints)
        {
            if (ipAccessFilter == null) ipAccessFilter = new Func<IPAddress, bool>((address) => true);
            if (tcpIp == null) tcpIp = LocalNet;

            this.IPAccessFilter = ipAccessFilter;
            this.EndPoints = endPoints.ToList();
            this.TcpIp = tcpIp;
        }
    }

    public abstract class TelnetStreamWatcherBase : AsyncService
    {
        public TelnetStreamWatcherOptions Options { get; }

        readonly NetTcpListener Listener;

        TcpIpSystem TcpIp => Options.TcpIp;

        protected abstract Task<PipePoint> SubscribeImplAsync();
        protected abstract Task UnsubscribeImplAsync(PipePoint pipe);

        public TelnetStreamWatcherBase(TelnetStreamWatcherOptions options)
        {
            this.Options = options;

            Listener = TcpIp.CreateListener(new TcpListenParam(async (listener, sock) =>
            {
                try
                {
                    Con.WriteDebug($"TelnetStreamWatcher({this.ToString()}: Connected: {sock.EndPointInfo._GetObjectDump()}");

                    using (var destStream = sock.GetStream())
                    {
                        if (sock.Info.Ip.RemoteIPAddress == null || Options.IPAccessFilter(sock.Info.Ip.RemoteIPAddress) == false)
                        {
                            Con.WriteDebug($"TelnetStreamWatcher({this.ToString()}: Access denied: {sock.EndPointInfo._GetObjectDump()}");

                            await destStream.WriteAsync("You are not allowed to access to this service.\r\n\r\n"._GetBytes_Ascii());

                            await Task.Delay(100);

                            return;
                        }

                        using (var pipePoint = await SubscribeImplAsync())
                        {
                            // ソケットから Enter キー入力を待機する
                            Task keyInputTask = TaskUtil.StartAsyncTaskAsync(async () =>
                            {
                                using StreamReader lineReader = new StreamReader(destStream);

                                while (true)
                                {
                                    string? line = await lineReader.ReadLineAsync();

                                    if (line == null) break;

                                    Dbg.WriteLine($"Manual GC is called by the administrator.");

                                    long start = FastTick64.Now;
                                    Dbg.GcCollect();
                                    long end = FastTick64.Now;

                                    long spentTime = end - start;

                                    Dbg.WriteLine($"Manual GC Took Time: {spentTime} msecs.");
                                }
                            });

                            try
                            {
                                // ソケットに対して、pipePoint のストリームをそのまま非同期で流し込む
                                using (var pipeStub = pipePoint.GetNetAppProtocolStub())
                                using (var srcStream = pipeStub.GetStream())
                                {
                                    await srcStream.CopyToAsync(destStream, sock.GrandCancel);
                                }
                            }
                            finally
                            {
                                await UnsubscribeImplAsync(pipePoint);

                                await pipePoint.CleanupAsync(new DisconnectedException());

                                await keyInputTask._TryAwait(noDebugMessage: true);
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
    public class TelnetLocalLogWatcherConfig : INormalizable
    {
        [DataMember]
        public string? Filters;

        public void Normalize()
        {
            this.Filters = this.Filters._NonNullTrim();
        }
    }

    public class TelnetLocalLogWatcher : TelnetStreamWatcherBase
    {
        public readonly static StaticModule Module = new StaticModule(InitModule, FreeModule);
        static Singleton<HiveData<TelnetLocalLogWatcherConfig>> _ConfigSingleton = null!;
        static HiveData<TelnetLocalLogWatcherConfig> Config => _ConfigSingleton;

        static void InitModule()
        {
            _ConfigSingleton = new Singleton<HiveData<TelnetLocalLogWatcherConfig>>(() =>
            {
                return new HiveData<TelnetLocalLogWatcherConfig>(Hive.SharedLocalConfigHive, "DebugSettings/LocalLogWatcher",
                () =>
                {
                    return new TelnetLocalLogWatcherConfig() { Filters = LocalLogRouter.BufferedLogRoute.KindHash.ToArray()._Combine(",") };
                },
                 HiveSyncPolicy.None);
            });
        }

        static void FreeModule()
        {
            _ConfigSingleton._DisposeSafe();
            _ConfigSingleton = null!;
        }

        public TelnetLocalLogWatcher(TelnetStreamWatcherOptions options) : base(options)
        {
        }

        protected override async Task<PipePoint> SubscribeImplAsync()
        {
            await Config.SyncWithStorageAsync(HiveSyncFlags.LoadFromFile, true);

            lock (Config.DataLock)
            {
                LocalLogRouter.BufferedLogRoute.SetKind(Config.ManagedData.Filters);
            }

            return LocalLogRouter.BufferedLogRoute.Subscribe();
        }

        protected override Task UnsubscribeImplAsync(PipePoint pipe)
        {
            LocalLogRouter.BufferedLogRoute.Unsubscribe(pipe);
            return Task.CompletedTask;
        }
    }
}

