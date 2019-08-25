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
using System.IO;
using System.IO.Enumeration;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

#pragma warning disable CS0162
#pragma warning disable CS0219

namespace IPA.TestDev
{
    class TestHttpServerBuilder : HttpServerStartupBase
    {
        public TestHttpServerBuilder(IConfiguration configuration) : base(configuration)
        {
        }

        protected override void ConfigureImpl_BeforeHelper(HttpServerStartupConfig cfg, IApplicationBuilder app, IHostingEnvironment env, IApplicationLifetime lifetime)
        {
        }

        protected override void ConfigureImpl_AfterHelper(HttpServerStartupConfig cfg, IApplicationBuilder app, IHostingEnvironment env, IApplicationLifetime lifetime)
        {
        }
    }

    partial class TestDevCommands
    {
        [ConsoleCommand(
            "SslCertListCollector command",
            "SslCertListCollector [dnsZonesDir] [/OUT:outDir]",
            "SslCertListCollector command")]
        static int SslCertListCollector(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args =
            {
                new ConsoleParam("[dnsZonesDir]", ConsoleService.Prompt, "dnsZonesDir: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("OUT", ConsoleService.Prompt, "outDir: ", ConsoleService.EvalNotEmpty, null),
            };
            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            string dirZonesDir = vl.DefaultParam.StrValue;
            string outDir = vl["OUT"].StrValue;

            var dirList = Lfs.EnumDirectory(dirZonesDir, false);
            if (dirList.Where(x => x.IsFile && (IgnoreCaseTrim)Lfs.PathParser.GetExtension(x.Name) == ".dns").Any() == false)
            {
                // 指定されたディレクトリに *.dns ファイルが 1 つもない場合は子ディレクトリ名をソートして一番大きいものを選択する
                dirZonesDir = dirList.OrderByDescending(x => x.Name).Where(x => x.IsDirectory).Select(x => x.FullPath).First();
            }

            Con.WriteLine($"Target directory: '{dirZonesDir}'");

            // 1. DNS ゾーンファイルを入力してユニークな FQDN レコードの一覧を生成する
            DnsFlattenUtil flat = new DnsFlattenUtil();

            foreach (FileSystemEntity ent in Lfs.EnumDirectory(dirZonesDir, true))
            {
                if (ent.IsFile)
                {
                    string fn = ent.FullPath;
                    fn._Print();

                    flat.InputZoneFile(Lfs.PathParser.GetFileNameWithoutExtension(fn), Lfs.ReadDataFromFile(fn).Span);
                }
            }

            // 2. FQDN の一覧を入力して FQDN と IP アドレスのペアの一覧を生成する
            DnsIpPairGeneratorUtil pairGenerator = new DnsIpPairGeneratorUtil(100, flat.FqdnSet);

            List<SniHostnameIpAddressPair> list = pairGenerator.ExecuteAsync()._GetResult().ToList();

            // 3. FQDN と IP アドレスのペアの一覧を入力して SSL 証明書一覧を出力する
            SslCertCollectorUtil col = new SslCertCollectorUtil(1000, list);

            IReadOnlyList<SslCertCollectorItem> ret = col.ExecuteAsync()._GetResult();

            XmlAndXsd xmlData = Util.GenerateXmlAndXsd(ret);

            string dir = outDir;

            Lfs.WriteDataToFile(Lfs.PathParser.Combine(dir, xmlData.XmlFileName), xmlData.XmlData, flags: FileFlags.AutoCreateDirectory);
            Lfs.WriteDataToFile(Lfs.PathParser.Combine(dir, xmlData.XsdFileName), xmlData.XsdData, flags: FileFlags.AutoCreateDirectory);

            return 0;
        }

        [ConsoleCommand(
            "TcpStressServer command",
            "TcpStressServer [port]",
            "TcpStressServer test")]
        static int TcpStressServer(ConsoleService c, string cmdName, string str)
        {
            // .NET 3.0 @ Linux, cpu 56 cores で csproj に以下を投入して TcpStressClient で大量に接続をして
            // GC で短時間フリーズ現象が発生しなければ OK!!
            // .NET 2.1, 2.2 は現象が発生する。
            // 
            // <ServerGarbageCollection>true</ServerGarbageCollection>
            // <ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>

            ConsoleParam[] args = { };
            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            int port = 80;

            if (vl.DefaultParam.StrValue._IsEmpty() == false)
            {
                port = vl.DefaultParam.IntValue;
            }

            Net_Test12_AcceptLoop2(port);

            return 0;
        }

        [ConsoleCommand(
            "TcpStressClient command",
            "TcpStressClient [url]",
            "TcpStressClient test")]
        static int TcpStressClient(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args =
            {
                new ConsoleParam("[url]"),
            };
            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            string url = vl.DefaultParam.StrValue;

            if (url._IsEmpty())
            {
                url = "http://dn-lxd-vm2-test1/favicon.ico";
            }

            int count = 0;
            if (true)
            {
                while (true)
                {
                    try
                    {
                        while (true)
                        {
                            using (WebApi api = new WebApi(new WebApiOptions(new WebApiSettings { SslAcceptAnyCerts = true })))
                            {
                                count++;
                                Con.WriteLine($"Count : {count}");
                                long start = Time.HighResTick64;
                                var ret = api.SimpleQueryAsync(WebMethods.GET, url)._GetResult();
                                long end = Time.HighResTick64;

                                Con.WriteLine(end - start);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ex._Debug();
                        Thread.Sleep(10);
                    }
                }
                return 0;
            }


            return 0;
        }

        [ConsoleCommand(
            "Net command",
            "Net [arg]",
            "Net test")]
        static int Net(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args = { };
            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            TcpIpSystemHostInfo hostInfo = LocalNet.GetHostInfo();

            //Net_Test1_PlainTcp_Client();
            //return 0;

            //Net_Test2_Ssl_Client();

            //Net_Test3_PlainTcp_Server();
            //Net_Test3_2_PlainTcp_Server_AcceptQueue();
            //return 0;

            ////while (true)
            //{
            //    try
            //    {
            //        Net_Test4_SpeedTest_Client();
            //    }
            //    catch (Exception ex)
            //    {
            //        ex.ToString()._Print();
            //    }
            //}

            //Net_Test5_SpeedTest_Server();

            //Net_Test6_DualStack_Client();

            //Net_Test7_Http_Download_Async()._GetResult();

            //Net_Test8_Http_Upload_Async()._GetResult();

            //Net_Test9_WebServer();

            //Net_Test10_SslServer();

            //Net_Test11_AcceptLoop();

            //Net_Test12_AcceptLoop2();

            //Net_Test13_WebSocketClientAsync()._GetResult();

            //Net_Test14_WebSocketClient2Async()._GetResult();

            return 0;
        }

        class SslServerTest : SslServerBase
        {
            public SslServerTest(SslServerOptions options) : base(options)
            {
            }

            protected override async Task SslAcceptedImplAsync(NetTcpListenerPort listener, SslSock sock)
            {
                using (var stream = sock.GetStream())
                using (var r = new StreamReader(stream))
                using (var w = new StreamWriter(stream))
                {
                    while (true)
                    {
                        string? recv = await r.ReadLineAsync();
                        if (recv == null)
                            return;

                        Con.WriteLine(recv);

                        await w.WriteLineAsync("[" + recv + "]\r\n");
                        await w.FlushAsync();
                    }
                }
            }
        }

        static async Task Net_Test14_WebSocketClient2Async()
        {
            string target = "wss://echo.websocket.org";

            using (WebSocket ws = await WebSocket.ConnectAsync(target))
            {
                using (var stream = ws.GetStream())
                using (var r = new StreamReader(stream))
                using (var w = new StreamWriter(stream))
                {
                    w.AutoFlush = true;

                    for (int i = 1; i < 3; i++)
                    {
                        string src = Str.MakeCharArray((char)('a' + (i % 25)), i * 1000);

                        Con.WriteLine(src.Length);

                        //await w.WriteLineAsync(src);

                        await stream.WriteAsync((src + "\r\n")._GetBytes_Ascii());

                        string? dst = await r.ReadLineAsync();

                        //Con.WriteLine(dst);

                        Con.WriteLine(i);

                        Debug.Assert(src == dst);
                    }
                }
            }
        }

        static async Task Net_Test13_WebSocketClientAsync()
        {
            using (ConnSock sock = LocalNet.Connect(new TcpConnectParam("echo.websocket.org", 80)))
            {
                using (WebSocket ws = new WebSocket(sock))
                {
                    await ws.StartWebSocketClientAsync("wss://echo.websocket.org");

                    using (var stream = ws.GetStream())
                    using (var r = new StreamReader(stream))
                    using (var w = new StreamWriter(stream))
                    {
                        w.AutoFlush = true;

                        for (int i = 1; i < 20; i++)
                        {
                            string src = Str.MakeCharArray((char)('a' + (i % 25)), i * 100);

                            Con.WriteLine(src.Length);

                            //await w.WriteLineAsync(src);

                            await stream.WriteAsync((src + "\r\n")._GetBytes_Ascii());

                            string? dst = await r.ReadLineAsync();

                            //Con.WriteLine(dst);

                            Con.WriteLine(i);

                            Debug.Assert(src == dst);
                        }
                    }
                }
            }
        }

        static void Net_Test10_SslServer()
        {
            SslServerOptions opt = new SslServerOptions(LocalNet, new PalSslServerAuthenticationOptions()
            {
                AllowAnyClientCert = true,
                ServerCertificate = DevTools.TestSampleCert,
            },
            IPUtil.GenerateListeningEndPointsList(false, 444));

            using (SslServerTest svr = new SslServerTest(opt))
            {
                Con.ReadLine("Enter to quit:");
            }
        }

        static void Net_Test9_WebServer()
        {
            var cfg = new HttpServerOptions();
            using (HttpServer<TestHttpServerBuilder> svr = new HttpServer<TestHttpServerBuilder>(cfg, "Hello"))
            {
                Con.ReadLine(">");
            }
        }

        static async Task Net_Test8_Http_Upload_Async()
        {
            string url = "https://httpbin.org/anything";

            MemoryBuffer<byte> uploadData = new MemoryBuffer<byte>("Hello World"._GetBytes_Ascii());
            var stream = uploadData._AsDirectStream();
            stream._SeekToBegin();

            using (WebApi api = new WebApi())
            {
                Dbg.Where();
                var res = await api.HttpSendRecvDataAsync(new WebSendRecvRequest(WebMethods.POST, url, uploadStream: stream));
                MemoryBuffer<byte> downloadData = new MemoryBuffer<byte>();
                using (MemoryHelper.FastAllocMemoryWithUsing<byte>(4 * 1024 * 1024, out Memory<byte> tmp))
                {
                    long total = 0;
                    while (true)
                    {
                        int r = await res.DownloadStream.ReadAsync(tmp);
                        if (r <= 0) break;

                        total += r;

                        downloadData.Write(tmp.Slice(0, r));

                        Con.WriteLine($"{total._ToString3()} / {res.DownloadContentLength.GetValueOrDefault()._ToString3()}");
                    }
                }
                downloadData.Span._GetString_Ascii()._Print();
                Dbg.Where();
            }
        }

        static async Task Net_Test7_Http_Download_Async()
        {
            //string url = "https://codeload.github.com/xelerance/xl2tpd/zip/master";
            //string url = "http://speed.softether.com/001.1Mbytes.dat";
            string url = "http://speed.softether.com/008.10Tbytes.dat";

            for (int j = 0; j < 1; j++)
            {
                using (WebApi api = new WebApi())
                {
                    //for (int i = 0; ; i++)
                    {
                        Dbg.Where();
                        var res = await api.HttpSendRecvDataAsync(new WebSendRecvRequest(WebMethods.GET, url));
                        using (MemoryHelper.FastAllocMemoryWithUsing<byte>(4 * 1024 * 1024, out Memory<byte> tmp))
                        {
                            long total = 0;
                            while (true)
                            {
                                int r = await res.DownloadStream.ReadAsync(tmp);
                                if (r <= 0) break;
                                total += r;

                                //Con.WriteLine($"{total._ToString3()} / {res.DownloadContentLength.GetValueOrDefault()._ToString3()}");
                            }
                        }
                        Dbg.Where();
                    }
                }
            }

            await Task.Delay(100);
        }

        static void Net_Test6_DualStack_Client()
        {
            string hostname = "www.google.com";

            using (var tcp = LocalNet.ConnectIPv4v6Dual(new TcpConnectParam(hostname, 443, connectTimeout: 5 * 1000)))
            {
                tcp.Info.GetValue<ILayerInfoIpEndPoint>().RemoteIPAddress!.AddressFamily.ToString()._Print();

                using (SslSock ssl = new SslSock(tcp))
                {
                    var sslClientOptions = new PalSslClientAuthenticationOptions()
                    {
                        TargetHost = hostname,
                        ValidateRemoteCertificateProc = (cert) => { return true; },
                    };

                    ssl.StartSslClient(sslClientOptions);

                    var st = ssl.GetStream();

                    var w = new StreamWriter(st);
                    var r = new StreamReader(st);

                    w.WriteLine("GET / HTTP/1.0");
                    w.WriteLine($"HOST: {hostname}");
                    w.WriteLine();
                    w.WriteLine();
                    w.Flush();

                    while (true)
                    {
                        string? s = r.ReadLine();
                        if (s == null)
                        {
                            break;
                        }

                        Con.WriteLine(s);
                    }
                }
            }
        }

        static void Net_Test5_SpeedTest_Server()
        {
            using (var server = new SpeedTestServer(LocalNet, 9821))
            {
                Con.ReadLine("Enter to stop>");
            }
        }

        static void Net_Test4_SpeedTest_Client()
        {
            string hostname = "speed.coe.ad.jp";

            CancellationTokenSource cts = new CancellationTokenSource();

            var client = new SpeedTestClient(LocalNet, LocalNet.GetIp(hostname), 9821, 1, 30000, SpeedTestModeFlag.Download, cts.Token);

            var task = client.RunClientAsync();

            //Con.ReadLine("Enter to stop>");

            ////int wait = 2000 + Util.RandSInt32_Caution() % 1000;
            ////Con.WriteLine("Waiting for " + wait);
            ////ThreadObj.Sleep(wait);

            //Con.WriteLine("Stopping...");
            //cts._TryCancelNoBlock();

            task._GetResult()._PrintAsJson();

            Con.WriteLine("Stopped.");
        }

        static void Net_Test12_AcceptLoop2(int port = 80)
        {

            new ThreadObj(param =>
            {
                ThreadObj.Current.Thread.Priority = System.Threading.ThreadPriority.Highest;
                int last = 0;
                while (true)
                {
                    int value = Environment.TickCount;
                    int span = value - last;
                    last = value;
                    long mem = mem = GC.GetTotalMemory(false);
                    try
                    {
                        Console.WriteLine("tick: " + span + "   mem = " + mem / 1024 / 1024 + "    sock = " + LocalNet.GetOpenedSockCount());
                    }
                    catch { }
                    ThreadObj.Sleep(100);
                }
            });

            if (true)
            {
                NetTcpListener listener = LocalNet.CreateListener(new TcpListenParam(
                        async (listener2, sock) =>
                        {
                            while (true)
                            {
                                var stream = sock.GetStream();
                                StreamReader r = new StreamReader(stream);

                                while (true)
                                {
                                    string? line = await r.ReadLineAsync();

                                    if (line._IsEmpty())
                                    {
                                        break;
                                    }
                                }
                                int segmentSize = 400;
                                int numSegments = 1000;
                                int totalSize = segmentSize * numSegments;

                                string ret =
                                $@"HTTP/1.1 200 OK
Content-Length: {totalSize}

";

                                await stream.WriteAsync(ret._GetBytes_Ascii());

                                byte[] buf = Util.Rand(numSegments);
                                for (int i = 0; i < numSegments; i++)
                                {
                                    await stream.WriteAsync(buf);
                                }
                            }
                        },
                        port));

                listener.HideAcceptProcError = true;

                ThreadObj.Sleep(-1);
            }
        }

        static bool test11_flag = false;
        static void Net_Test11_AcceptLoop()
        {
            if (test11_flag == false)
            {
                test11_flag = true;

                new ThreadObj(param =>
                {
                    ThreadObj.Current.Thread.Priority = System.Threading.ThreadPriority.Highest;
                    int last = 0;
                    while (true)
                    {
                        int value = Environment.TickCount;
                        int span = value - last;
                        last = value;
                        Console.WriteLine("tick: " + span);
                        ThreadObj.Sleep(100);
                    }
                });
            }

            using (var listener = LocalNet.CreateListener(new TcpListenParam(
                    async (listener2, sock) =>
                    {
                        while (true)
                        {
                            var stream = sock.GetStream();
                            StreamReader r = new StreamReader(stream);

                            while (true)
                            {
                                string? line = await r.ReadLineAsync();

                                if (line._IsEmpty())
                                {
                                    break;
                                }
                            }
                            int segmentSize = 400;
                            int numSegments = 1000;
                            int totalSize = segmentSize * numSegments;

                            string ret =
                            $@"HTTP/1.1 200 OK
Content-Length: {totalSize}

";

                            await stream.WriteAsync(ret._GetBytes_Ascii());

                            byte[] buf = Util.Rand(numSegments);
                            for (int i = 0; i < numSegments; i++)
                            {
                                await stream.WriteAsync(buf);
                            }
                        }
                    },
                    80)))
            {
                Con.ReadLine(" > ");
            }
        }

        static void Net_Test3_PlainTcp_Server()
        {
            using (var listener = LocalNet.CreateListener(new TcpListenParam(
                    async (listener2, sock) =>
                    {
                        sock.StartPCapRecorder(new PCapFileEmitter(new PCapFileEmitterOptions(new FilePath(@"c:\tmp\190611\" + Str.DateTimeToStrShortWithMilliSecs(DateTime.Now) + ".pcapng", flags: FileFlags.AutoCreateDirectory))));
                        var stream = sock.GetStream();
                        StreamWriter w = new StreamWriter(stream);
                        while (true)
                        {
                            w.WriteLine(DateTimeOffset.Now._ToDtStr(true));
                            await w.FlushAsync();
                            await Task.Delay(100);
                        }
                    },
                    9821)))
            {
                Con.ReadLine(">");
            }
        }

        static void Net_Test3_2_PlainTcp_Server_AcceptQueue()
        {
            using (var listener = LocalNet.CreateListener(new TcpListenParam(null, 9821)))
            {
                Task acceptTask = TaskUtil.StartAsyncTaskAsync(async () =>
                {
                    // Accept ループタスク
                    while (true)
                    {
                        ConnSock sock = await listener.AcceptNextSocketFromQueueUtilAsync();

                        TaskUtil.StartAsyncTaskAsync(async (obj) =>
                        {
                            using (ConnSock s = (ConnSock)obj!)
                            {
                                var stream = s.GetStream();
                                StreamWriter w = new StreamWriter(stream);
                                while (true)
                                {
                                    w.WriteLine(DateTimeOffset.Now._ToDtStr(true));
                                    await w.FlushAsync();
                                    await Task.Delay(100);
                                }
                            }
                        }, sock)._LaissezFaire();
                    }
                });

                Con.ReadLine(">");
            }
        }

        static void Net_Test2_Ssl_Client()
        {
            string hostname = "www.google.co.jp";

            using (ConnSock sock = LocalNet.Connect(new TcpConnectParam(hostname, 443)))
            {
                using (SslSock ssl = new SslSock(sock))
                {
                    //ssl.StartPCapRecorder(new PCapFileEmitter(new PCapFileEmitterOptions(new FilePath(@"c:\tmp\190610\test1.pcapng", flags: FileFlags.AutoCreateDirectory), false)));
                    var sslClientOptions = new PalSslClientAuthenticationOptions()
                    {
                        TargetHost = hostname,
                        ValidateRemoteCertificateProc = (cert) => { return true; },
                    };

                    ssl.StartSslClient(sslClientOptions);

                    var st = ssl.GetStream();

                    var w = new StreamWriter(st);
                    var r = new StreamReader(st);

                    w.WriteLine("GET / HTTP/1.0");
                    w.WriteLine($"HOST: {hostname}");
                    w.WriteLine();
                    w.WriteLine();
                    w.Flush();

                    while (true)
                    {
                        string? s = r.ReadLine();
                        if (s == null)
                        {
                            break;
                        }

                        Con.WriteLine(s);
                    }
                }
            }
        }

        static void Net_Test1_PlainTcp_Client()
        {
            for (int i = 0; i < 1; i++)
            {
                ConnSock sock = LocalNet.Connect(new TcpConnectParam("dnobori.cs.tsukuba.ac.jp", 80));

                sock.StartPCapRecorder(new PCapFileEmitter(new PCapFileEmitterOptions(new FilePath(@"c:\tmp\190610\test1.pcapng", flags: FileFlags.AutoCreateDirectory), true)));
                {
                    var st = sock.GetStream();
                    //sock.DisposeSafe();
                    var w = new StreamWriter(st);
                    var r = new StreamReader(st);

                    w.WriteLine("GET /ja/ HTTP/1.0");
                    w.WriteLine("HOST: dnobori.cs.tsukuba.ac.jp");
                    w.WriteLine();
                    w.Flush();

                    while (true)
                    {
                        string? s = r.ReadLine();
                        if (s == null)
                        {
                            break;
                        }

                        Con.WriteLine(s);
                    }

                    st.Dispose();
                }
            }
        }
    }
}
