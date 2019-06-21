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

#if CORES_BASIC_WEBSERVER

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Microsoft.AspNetCore.Http;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Helper.Basic
{
    static class WebServerHelper
    {
        public static Task _SendStringContents(this HttpResponse h, string body, string contentsType = "text/plain; charset=UTF-8", Encoding encoding = null, CancellationToken cancel = default(CancellationToken))
        {
            if (encoding == null) encoding = Str.Utf8Encoding;
            h.ContentType = contentsType;
            byte[] ret_data = encoding.GetBytes(body);
            return h.Body.WriteAsync(ret_data, 0, ret_data.Length, cancel);
        }

        public static async Task<string> _RecvStringContents(this HttpRequest h, int maxRequestBodyLen = int.MaxValue, Encoding encoding = null, CancellationToken cancel = default(CancellationToken))
        {
            if (encoding == null) encoding = Str.Utf8Encoding;
            return (await h.Body._ReadToEndAsync(maxRequestBodyLen, cancel))._GetString_UTF8();
        }
    }
}

namespace IPA.Cores.Basic
{
    static partial class StandardMainFunctions
    {
        public static class AspNet
        {
            public static int DoMain<TStartup>(CoresLibOptions coresOptions, HttpServerOptions httpServerOptions = null, params string[] args) where TStartup : class
            {
                if (httpServerOptions == null)
                    httpServerOptions = new HttpServerOptions();

                CoresLib.Init(coresOptions, args);

                try
                {
                    //using (HttpServer<TStartup> httpServer = new HttpServer<TStartup>(httpServerOptions))
                    //{
                    //    Con.ReadLine("Enter to exit>");
                    //}
                    return ConsoleService.EntryPoint(Env.CommandLine, "AspNetApp", typeof(AspNet));
                }
                finally
                {
                    if (CoresLib.Free()?.LeakCheckerResult.HasLeak ?? false && Dbg.IsConsoleDebugMode)
                    {
                        Console.ReadKey();
                    }
                }
            }

            [ConsoleCommand(
                "IPA.Cores for .NET Core: ASP.NET Core Host Process",
                "[/IN:infile] [/OUT:outfile] [/CSV] [/CMD command_line...]",
                "This is the AspNet Test Program.",
                "IN:This will specify the text file 'infile' that contains the list of commands that are automatically executed after the connection is completed. If the /IN parameter is specified, this program will terminate automatically after the execution of all commands in the file are finished. If the file contains multiple-byte characters, the encoding must be Unicode (UTF-8). This cannot be specified together with /CMD (if /CMD is specified, /IN will be ignored).",
                "OUT:If the optional command 'commands...' is included after /CMD, that command will be executed after the connection is complete and this program will terminate after that. This cannot be specified together with /IN (if specified together with /IN, /IN will be ignored). Specify the /CMD parameter after all other parameters.",
                "CMD:If the optional command 'commands...' is included after /CMD, that command will be executed after the connection is complete and this program will terminate after that. This cannot be specified together with /IN (if specified together with /IN, /IN will be ignored). Specify the /CMD parameter after all other parameters.",
                "CSV:You can specify this option to enable CSV outputs. Results of each command will be printed in the CSV format. It is useful for processing the results by other programs."
                )]
            static int AspNetApp(ConsoleService c, string cmdName, string str)
            {
                c.WriteLine($"Copyright (c) 2018-{DateTimeOffset.Now.Year} IPA CyberLab. All Rights Reserved.");
                c.WriteLine("");

                ConsoleParam[] args =
                {
                    new ConsoleParam("IN", null, null, null, null),
                    new ConsoleParam("OUT", null, null, null, null),
                    new ConsoleParam("CMD", null, null, null, null),
                    new ConsoleParam("CSV", null, null, null, null),
                };

                ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args, noErrorOnUnknownArg: true);

                string cmdline = vl["CMD"].StrValue;

                ConsoleService cs = c;

                while (cs.DispatchCommand(cmdline, "AspNet>", typeof(AspNetAppCommands), null))
                {
                    if (Str.IsEmptyStr(cmdline) == false)
                    {
                        break;
                    }
                }

                return cs.RetCode;
            }

            public static class AspNetAppCommands
            {
                [ConsoleCommand(
                    "Start or stop the AspNet daemon",
                    "AspNet [command]",
                    "Start or stop the AspNet daemon",
                    @"[command]:The control command.

[UNIX / Windows common commands]
start        - Start the daemon in the background mode.
stop         - Stop the running daemon in the background mode.
show         - Show the real-time log by the background daemon.
test         - Start the daemon in the foreground testing mode.

[Windows specific commands]
winstart     - Start the daemon as a Windows service.
winstop      - Stop the running daemon as a Windows service.
wininstall   - Install the daemon as a Windows service.
winuninstall - Uninstall the daemon as a Windows service.")]
                static int AspNet(ConsoleService c, string cmdName, string str)
                {
                    return DaemonCmdLineTool.EntryPoint(c, cmdName, str, new AspNetDaemon());
                }
            }


            class AspNetDaemon : Daemon
            {
                public AspNetDaemon() : base(new DaemonOptions("AspNet", "AspNet Service", true, telnetLogWatcherPort: 8023))
                {
                }

                protected override async Task StartImplAsync(object param)
                {
                    Con.WriteLine("AspNet Service: Starting...");
                    await Task.Delay(500);
                    Con.WriteLine("AspNet Service: Started.");
                }

                protected override async Task StopImplAsync(object param)
                {
                    Con.WriteLine("AspNet Service: Stopping...");
                    await Task.Delay(3000);
                    Con.WriteLine("AspNet Service: Stopped.");
                }
            }


        }
    }
}

#endif // CORES_BASIC_WEBSERVER

