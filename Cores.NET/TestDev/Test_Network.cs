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

#pragma warning disable CS0162
#pragma warning disable CS0219

namespace IPA.TestDev
{
    partial class TestDevCommands
    {
        [ConsoleCommandMethod(
            "Net command",
            "Net [arg]",
            "Net test")]
        static int Net(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args = { };
            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            var hostInfo = LocalNet.GetHostInfo();

            //Net_Test1_PlainTcp_Client();

            //Net_Test2_Ssl_Client();

            //Net_Test3_PlainTcp_Server();

            //Net_Test4_SpeedTest_Client();

            //Net_Test5_SpeedTest_Server();

            //Net_Test6_DualStack_Client();

            Net_Test8_Http_Upload_Async().GetResult();

            return 0;
        }

        static async Task Net_Test8_Http_Upload_Async()
        {
            string url = "https://httpbin.org/anything";

            MemoryBuffer<byte> uploadData = new MemoryBuffer<byte>("Hello World".GetBytes_Ascii());
            var stream = uploadData.AsDirectStream();
            stream.SeekToBegin();

            using (WebApi api = new WebApi())
            {
                Dbg.Where();
                var res = await api.HttpSendRecvDataAsync(new WebSendRecvRequest(WebApiMethods.POST, url, uploadStream: stream));
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

                        Con.WriteLine($"{total.ToString3()} / {res.DownloadContentLength.GetValueOrDefault().ToString3()}");
                    }
                }
                downloadData.Span.GetString_Ascii().Print();
                Dbg.Where();
            }
        }

        static async Task Net_Test7_Http_Download_Async()
        {
            //string url = "https://codeload.github.com/xelerance/xl2tpd/zip/masterz";
            string url = "http://speed.softether.com/001.1Mbytes.dat";

            using (WebApi api = new WebApi())
            {
                Dbg.Where();
                var res = await api.HttpSendRecvDataAsync(new WebSendRecvRequest(WebApiMethods.GET, url));
                using (MemoryHelper.FastAllocMemoryWithUsing<byte>(4 * 1024 * 1024, out Memory<byte> tmp))
                {
                    long total = 0;
                    while (true)
                    {
                        int r = await res.DownloadStream.ReadAsync(tmp);
                        if (r <= 0) break;
                        total += r;

                        Con.WriteLine($"{total.ToString3()} / {res.DownloadContentLength.GetValueOrDefault().ToString3()}");
                    }
                }
                Dbg.Where();
            }
        }

        static void Net_Test6_DualStack_Client()
        {
            string hostname = "www.google.com";

            using (var tcp = LocalNet.ConnectIPv4v6Dual(new TcpConnectParam(hostname, 443, connectTimeout: 5 * 1000)))
            {
                tcp.Info.GetValue<ILayerInfoIpEndPoint>().RemoteIPAddress.AddressFamily.ToString().Print();

                using (SslSock ssl = new SslSock(tcp))
                {
                    var sslClientOptions = new PalSslClientAuthenticationOptions()
                    {
                        TargetHost = hostname,
                        ValidateRemoteCertificateProc = (cert) => { return true; },
                    };

                    ssl.StartSslClient(sslClientOptions);

                    var st = ssl.GetStream().NetworkStream;

                    var w = new StreamWriter(st);
                    var r = new StreamReader(st);

                    w.WriteLine("GET / HTTP/1.0");
                    w.WriteLine($"HOST: {hostname}");
                    w.WriteLine();
                    w.WriteLine();
                    w.Flush();

                    while (true)
                    {
                        string s = r.ReadLine();
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
            AsyncTester tester = new AsyncTester(true);

            var server = new SpeedTestServer(LocalNet, tester.CancelToken, 9821);

            Task t = server.RunServerAsync();

            tester.EnterKeyPrompt();

            t.GetResult();
        }

        static void Net_Test4_SpeedTest_Client()
        {
            string hostname = "speed.coe.ad.jp";

            var client = new SpeedTestClient(LocalNet, LocalNet.GetIp(hostname), 9821, 32, 15000, SpeedTestModeFlag.Upload);

            client.RunClientAsync().GetResult().PrintAsJson();
        }

        static void Net_Test3_PlainTcp_Server()
        {
            using (AsyncCleanuperLady lady = new AsyncCleanuperLady())
            {
                var listener = LocalNet.CreateListener(lady, new TcpListenParam(
                    async (listener2, sock) =>
                    {
                        var stream = sock.GetStream().NetworkStream;
                        StreamWriter w = new StreamWriter(stream);
                        while (true)
                        {
                            w.WriteLine(DateTimeOffset.Now.ToDtStr(true));
                            await w.FlushAsync();
                            await Task.Delay(100);
                        }
                    },
                    9821));

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
                    var sslClientOptions = new PalSslClientAuthenticationOptions()
                    {
                        TargetHost = hostname,
                        ValidateRemoteCertificateProc = (cert) => { return true; },
                    };

                    ssl.StartSslClient(sslClientOptions);

                    var st = ssl.GetStream().NetworkStream;

                    var w = new StreamWriter(st);
                    var r = new StreamReader(st);

                    w.WriteLine("GET / HTTP/1.0");
                    w.WriteLine($"HOST: {hostname}");
                    w.WriteLine();
                    w.WriteLine();
                    w.Flush();

                    while (true)
                    {
                        string s = r.ReadLine();
                        if (s == null)
                        {
                            break;
                        }

                        Con.WriteLine(s);
                    }

                    sock.Disconnect();
                }
            }
        }

        static void Net_Test1_PlainTcp_Client()
        {
            while (true)
            {
                ConnSock sock = LocalNet.Connect(new TcpConnectParam("dnobori.cs.tsukuba.ac.jp", 80));
                {
                    var st = sock.GetStream().NetworkStream;

                    var w = new StreamWriter(st);
                    var r = new StreamReader(st);

                    w.WriteLine("GET / HTTP/1.0");
                    w.WriteLine("HOST: dnobori.cs.tsukuba.ac.jp");
                    w.WriteLine();
                    w.WriteLine();
                    w.Flush();

                    while (true)
                    {
                        string s = r.ReadLine();
                        if (s == null)
                        {
                            break;
                        }

                        Con.WriteLine(s);
                    }

                    sock.Disconnect();
                }
            }
        }
    }
}
