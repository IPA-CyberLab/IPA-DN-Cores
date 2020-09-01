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

namespace IPA.TestDev
{
    static class SnmpWorkStressTestClass
    {
        static void CgiServerStressTest_Server2(int id)
        {
            //SnmpWorkConfig.DefaultPollingIntervalSecs.TrySet(1);
            //SnmpWorkConfig.DefaultPingTarget.TrySet("ping4.test.sehosts.com=IPv4 Internet,ping6.test.sehosts.com=IPv6 Internet,8.8.8.8,8.8.4.4,130.158.6.51,1.2.3.4");
            //SnmpWorkConfig.DefaultSpeedTarget.TrySet("none");
            //SnmpWorkConfig.DefaultPktLossIntervalMsec.TrySet(10);
            //SnmpWorkConfig.DefaultPktLossTimeoutMsecs.TrySet(100);

            var host = new SnmpWorkHost(id);

            host.Register("Temperature", 101_00000, new SnmpWorkFetcherTemperature(host));
            host.Register("Ram", 102_00000, new SnmpWorkFetcherMemory(host));
            host.Register("Disk", 103_00000, new SnmpWorkFetcherDisk(host));
            host.Register("Net", 104_00000, new SnmpWorkFetcherNetwork(host));

            host.Register("Ping", 105_00000, new SnmpWorkFetcherPing(host));
            host.Register("Quality", 107_00000, new SnmpWorkFetcherPktQuality(host));
            host.Register("Bird", 108_00000, new SnmpWorkFetcherBird(host));

            Con.WriteLine("SnmpWorkDaemon: Started.");
        }

        public static void TestMain(int id)
        {
            RefLong count = 0;

            CgiServerStressTest_Server2(id);

            //Sleep(Timeout.Infinite);

            for (int i = 0; i < 1; i++)
            {
                Task t = AsyncAwait(async () =>
                {
                    while (true)
                    {
                        await Task.Yield();

                        try
                        {
                            using var web = new WebApi(new WebApiOptions());

                            var ret = await web.SimpleQueryAsync(WebMethods.GET, $"http://127.0.0.1:{id}/?method=GetAll");

                            count++;
                        }
                        catch (Exception ex)
                        {
                            ex.Message._Print();
                        }
                    }
                });
            }

            while (true)
            {
                $"ID={id}: {count}"._Print();

                Sleep(1000);
            }
        }
    }

    partial class TestDevCommands
    {
        [ConsoleCommand(
            "SnmpWorkStressTest command",
            "SnmpWorkStressTest [id]",
            "This is a test command.",
            "[id]:ID number")]
        static int SnmpWorkStressTest(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args =
            {
                new ConsoleParam("[id]", null, null, null, null),
            };

            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            int id = vl.DefaultParam.IntValue;

            if (id == 0) id = 1;

            SnmpWorkStressTestClass.TestMain(id);

            return 0;
        }
    }
}

