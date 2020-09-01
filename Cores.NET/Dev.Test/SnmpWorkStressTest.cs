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

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Net;
using System.Net.Sockets;

namespace IPA.TestDev
{
    static class SnmpWorkStressTestClass
    {
        public static void StartServer(int port)
        {
            SnmpWorkConfig.DefaultPollingIntervalSecs.TrySet(50);
            SnmpWorkConfig.DefaultPingTarget.TrySet("ping4.test.sehosts.com=IPv4 Internet,ping6.test.sehosts.com=IPv6 Internet,8.8.8.8,8.8.4.4,130.158.6.51,1.2.3.4");
            SnmpWorkConfig.DefaultSpeedTarget.TrySet("none");
            SnmpWorkConfig.DefaultPktLossIntervalMsec.TrySet(50);
            SnmpWorkConfig.DefaultPktLossTimeoutMsecs.TrySet(100);

            var host = new SnmpWorkHost(port);

            host.Register("Temperature", 101_00000, new SnmpWorkFetcherTemperature(host));
            host.Register("Ram", 102_00000, new SnmpWorkFetcherMemory(host));
            host.Register("Disk", 103_00000, new SnmpWorkFetcherDisk(host));
            host.Register("Net", 104_00000, new SnmpWorkFetcherNetwork(host));

            host.Register("Ping", 105_00000, new SnmpWorkFetcherPing(host));
            host.Register("Quality", 107_00000, new SnmpWorkFetcherPktQuality(host));
            host.Register("Bird", 108_00000, new SnmpWorkFetcherBird(host));

            Con.WriteLine("SnmpWorkDaemon: Started.");
        }

        public static RefLong count = 0;

        public static void StartStressTest(int port)
        {
            int num = 50;

            for (int i = 0; i < num; i++)
            {
                ThreadObj.Start((p) =>
                {
                    while (true)
                    {
                        try
                        {
                            WebRequest req = HttpWebRequest.Create($"http://127.{port % 255}.{i}.1:{port}/?method=GetAll");

                            using var res = req.GetResponse();

                            //using var web = new WebApi(new WebApiOptions());

                            //var ret = await web.SimpleQueryAsync(WebMethods.GET, $"http://127.0.0.1:{port}/?method=GetAll");

                            count.Increment();
                        }
                        catch (Exception ex)
                        {
                            ex.Message._Print();
                        }
                        //Sleep(Util.RandSInt31() % 100 + 50);
                    }
                });
            }

            for (int i = 0; i < num; i++)
            {
                ThreadObj.Start((p) =>
                {
                    while (true)
                    {
                        try
                        {
                            {
                                using TcpClient tc = new TcpClient($"127.{port % 255}.{i}.1", port);
                            }
                        }
                        catch (Exception ex)
                        {
                            ex.Message._Print();
                        }
                        Sleep(Util.RandSInt31() % 100 + 50);
                    }
                });
            }

            for (int i = 0; i < num; i++)
            {
                ThreadObj.Start((p) =>
                {
                    while (true)
                    {
                        try
                        {
                            {
                                using TcpClient tc = new TcpClient($"127.{port % 255}.{i}.1", port);

                                Sleep(Util.RandSInt31() % 1000);
                            }
                        }
                        catch (Exception ex)
                        {
                            ex.Message._Print();
                        }
                        Sleep(Util.RandSInt31() % 100 + 50);
                    }
                });
            }
        }
    }

    partial class TestDevCommands
    {
        [ConsoleCommand(
            "SnmpWorkStressTest command",
            "SnmpWorkStressTest [num]",
            "This is a test command.",
            "[num]: number")]
        static int SnmpWorkStressTest(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args =
            {
                new ConsoleParam("[num]", null, null, null, null),
            };

            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            int num = vl.DefaultParam.IntValue;

            if (num == 0) num = 1;

            "Starting servers..."._Print();

            for (int i = 8000; i < (8000 + num); i++)
            {
                SnmpWorkStressTestClass.StartServer(i);
                i._Print();
            }

            "All servers started."._Print();

            "Starting tests..."._Print();

            for (int i = 8000; i < (8000 + num); i++)
            {
                SnmpWorkStressTestClass.StartStressTest(i);
                i._Print();
            }

            "All tests started."._Print();

            int memCount = 0;

            long lastCount = 0;

            while (true)
            {
                try
                {
                    memCount++;

                    bool flag = (memCount % 10) == 0;

                    long currentCount = SnmpWorkStressTestClass.count.Value;
                    long diff = currentCount - lastCount;
                    lastCount = currentCount;

                    CoresRuntimeStat stat = new CoresRuntimeStat();
                    stat.Refresh();
                    stat._Print();
                    $"{DateTime.Now._ToDtStr()}: {currentCount._ToString3()} ({diff._ToString3()})"._Print();
                    Con.WriteLine();

                    int randSize = Util.RandSInt31() % 10000000 + 1;

                    Memory<byte> tmp = new byte[randSize];
                    Limbo.ObjectVolatileSlow = tmp;


                    var span = tmp.Span;
                    for (int i = 0; i < randSize; i++)
                    {
                        span[i] = (byte)i;
                    }

                    if (flag) Dbg.GcCollect();

                    Limbo.ObjectVolatileSlow = null;
                    tmp = default;

                    if (flag) Dbg.GcCollect();

                    Sleep(1000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"SnmpWorkStressTest Main Function: {ex.ToString()}");
                }
            }
        }

    }
}

