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
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.TestDev;

public class TelnetDocServerDaemonApp : AsyncServiceWithMainLoop
{
    NetTcpListener? RawListener = null;
    NetTcpListener? SslListener = null;

    public TelnetDocServerDaemonApp()
    {
        try
        {
            this.StartMainLoop(MainLoopAsync);
        }
        catch
        {
            this.DisposeAsync();
            throw;
        }
    }

    readonly HiveData<HiveKeyValue> Settings = Hive.LocalAppSettingsEx["TelnetDocServerAccessCounter"];

    public class Hatsugen
    {
        public string Line = "";
        public string SrcHost = "";
        public DateTimeOffset Dt;
        public string Ip = "";
        public int Port;
    }

    readonly ConcurrentHashSet<ConcurrentQueue<Hatsugen>> HatsugenQueueList = new ConcurrentHashSet<ConcurrentQueue<Hatsugen>>();

    async Task ResponseMainAsync(CancellationToken cancel, NetTcpListenerPort listener, Stream st, string replaceStrEncoding, string replaceStrVersion, Encoding encoding, ConnSock sock)
    {
        var hatsugenReadQueue = new ConcurrentQueue<Hatsugen>();

        HatsugenQueueList.Add(hatsugenReadQueue);

        try
        {
            long access_counter = 0;
            await Settings.AccessDataAsync(true, async k =>
            {
                long c = k.GetSInt64("Counter");

                c++;

                k.SetSInt64("Counter", c);

                access_counter = c;

                await Task.CompletedTask;
            });

            string counter2 = access_counter.ToString()._Normalize(false, false, true);
            string counter1 = access_counter.ToString()._Normalize(false, false, false);

            string fn = Env.AppRootDir._CombinePath("TelnetBody.txt");

            DateTimeOffset lastUpdate = Util.ZeroDateTimeOffsetValue;

            try
            {
                var info = await Lfs.GetFileMetadataAsync(fn);
                lastUpdate = info.LastWriteTime ?? Util.ZeroDateTimeOffsetValue;
            }
            catch { }

            var dt = lastUpdate.LocalDateTime;

            string[] youbi =
            {
                "日", "月", "火", "水", "木", "金", "土",
            };

            var updateStr = dt.ToString("yyyy/MM/dd") + " (" + youbi[(int)dt.DayOfWeek] + ") " + dt.ToString("HH:mm");

            string body = Lfs.ReadStringFromFile(fn);

            body = body._ReplaceStr("_COUNTER2_", counter2);
            body = body._ReplaceStr("_COUNTER1_", counter1);
            body = body._ReplaceStr("_ENCODING_", replaceStrEncoding);
            body = body._ReplaceStr("_VERSION_", replaceStrVersion);
            body = body._ReplaceStr("_UPDATE_", updateStr);

            StringWriter tmp1 = new StringWriter();

            tmp1.NewLine = Str.CrLf_Str;

            tmp1.WriteLine();

            foreach (var line in body._GetLines())
            {
                tmp1.WriteLine(" " + line);
            }

            st.ReadTimeout = st.WriteTimeout = 5 * 60 * 1000;

            StreamWriter w = new StreamWriter(st, encoding);
            w.NewLine = Str.CrLf_Str;
            w.AutoFlush = true;

            body = tmp1.ToString();

            var recvTask = TaskUtil.StartAsyncTaskAsync(async () =>
            {
                try
                {
                    var lineReader = new BinaryLineReader(st);

                    while (true)
                    {
                        var bytes = await lineReader.ReadSingleLineAsync(1000, cancel);

                        if (bytes == null)
                        {
                            break;
                        }

                        string line = "";

                        if (bytes.HasValue)
                        {
                            line = Str.DecodeStringAutoDetect(bytes.Value.Span, out _, true).Trim();

                            line = Str.ShiftJisEncoding.GetBytes(line)._GetString_ShiftJis(true).Trim();

                            StringBuilder sb = new StringBuilder();

                            foreach (var c in line)
                            {
                                if (Str.IsPrintable(c))
                                {
                                    sb.Append(c);
                                }
                            }

                            line = sb.ToString().Trim();

                            if (line._IsFilled() &&
                                Str.ShiftJisEncoding.GetBytes(line).Length != line.Length &&
                                line._InStri(": ") == false &&
                                line._InStri("??") == false &&
                                line._IsSamei("q") == false &&
                                line.StartsWith("get", StringComparison.OrdinalIgnoreCase) == false &&
                                line.StartsWith("post", StringComparison.OrdinalIgnoreCase) == false &&
                                line.StartsWith("head", StringComparison.OrdinalIgnoreCase) == false &&
                                line._InStri("exit") == false &&
                                line._InStri("quit") == false &&
                                line._InStri("logout") == false)
                            {
                                line = line._NormalizeSoftEther(true);

                                line = line.Replace("「", "『");
                                line = line.Replace("」", "』");

                                var clientInfo = sock.EndPointInfo;

                                var fqdn = await LocalNet.DnsResolver.GetHostNameOrIpAsync(clientInfo.RemoteIP, cancel);

                                var tokens = fqdn._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, '.');

                                List<string> tokens2 = new List<string>();

                                for (int i = 0; i < tokens.Length; i++)
                                {
                                    string tmp1 = tokens[i];
                                    if (i == 0)
                                    {
                                        StringBuilder sb1 = new StringBuilder();
                                        foreach (char c in tmp1)
                                        {
                                            if (c == '-' || c == ':')
                                            {
                                                sb1.Append(c);
                                            }
                                            else
                                            {
                                                sb1.Append('*');
                                            }
                                        }
                                        tmp1 = sb1.ToString();
                                    }
                                    tokens2.Add(tmp1);
                                }

                                Hatsugen item = new Hatsugen
                                {
                                    Dt = DtOffsetNow,
                                    Line = line,
                                    SrcHost = Str.CombineFqdn(tokens2.ToArray()),
                                    Ip = clientInfo.RemoteIP?.ToString() ?? "",
                                    Port = clientInfo.RemotePort,
                                };

                                item._PostAccessLog("Hatsugen");

                                var queueArray = HatsugenQueueList.ToArray();

                                foreach (var q in queueArray)
                                {
                                    q.Enqueue(item);
                                }
                            }
                        }
                    }
                }
                catch { }
            });

            try
            {
                foreach (char c in body)
                {
                    await SendQueuedLinesIfExistsAsync(w, hatsugenReadQueue);

                    await w.WriteAsync(c);
                    await Task.Delay(Util.RandSInt15() % 100);
                }

                Memory<byte> recvBuf = new byte[1];

                while (true)
                {
                    if (recvTask.IsCompleted || sock.IsCanceled)
                    {
                        break;
                    }

                    await SendQueuedLinesIfExistsAsync(w, hatsugenReadQueue);

                    await Task.Delay(Util.RandSInt15() % 256);
                }

                async Task SendQueuedLinesIfExistsAsync(StreamWriter dst, ConcurrentQueue<Hatsugen> queue)
                {
                    while (queue.TryDequeue(out var item))
                    {
                        StringWriter w = new StringWriter();

                        w.NewLine = Str.CrLf_Str;

                        w.WriteLine();

                        string[] youbi =
                        {
                            "日", "月", "火", "水", "木", "金", "土",
                        };

                        string dtStr = dt.ToString("MM/dd") + " (" + youbi[(int)dt.DayOfWeek] + ") " + dt.ToString("HH:mm:ss");

                        w.WriteLine("");
                        w.WriteLine($">>「 {item.Line} 」(チャット通信 - {dtStr} by {item.SrcHost} 君) <<");
                        w.WriteLine("");

                        await dst.WriteAsync(w.ToString());
                    }
                }
            }
            finally
            {
                await recvTask._TryAwait(true);
            }
        }
        finally
        {
            HatsugenQueueList.Remove(hatsugenReadQueue);
        }
    }

    async Task MainLoopAsync(CancellationToken cancel = default)
    {
        await Task.CompletedTask;

        this.RawListener = LocalNet.CreateTcpListener(new TcpListenParam(async (listener, sock) =>
        {
            await using var st = sock.GetStream();

            await ResponseMainAsync(cancel, listener, st, "Shift_JIS Encoding to view this page", "Pure TELNET (TCP Port 23) edition", Str.ShiftJisEncoding, sock);
        }, ports: new int[] { 23 }));

        this.SslListener = LocalNet.CreateTcpListener(new TcpListenParam(async (listener, sock) =>
        {
            var cert = GlobalCertVault.GetGlobalCertVault().CertificateStoreSelector("_dummy_", false);

            await using SslSock sslSock = new SslSock(sock);

            await sslSock.StartSslServerAsync(new PalSslServerAuthenticationOptions(cert.X509Certificate, true, null), cancel);

            await using var st = sslSock.GetStream();

            await ResponseMainAsync(cancel, listener, st, "UTF-8 Encoding to view this page    ", "TELNET over TLS/SSL (TCP Port 992) secure edition", Str.Utf8Encoding, sock);
        }, ports: new int[] { 992 }));
    }

    protected async override Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            if (RawListener != null)
            {
                await RawListener.DisposeAsync(ex);
            }
            if (SslListener != null)
            {
                await SslListener.DisposeAsync(ex);
            }
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

class TelnetDocServerDaemon : Daemon
{
    TelnetDocServerDaemonApp? app = null;

    public TelnetDocServerDaemon() : base(new DaemonOptions("TelnetDocServerDaemon", "TelnetDocServerDaemon Service", true))
    {
    }

    protected override async Task StartImplAsync(DaemonStartupMode startupMode, object? param)
    {
        Con.WriteLine("TelnetDocServerDaemon: Starting...");

        app = new TelnetDocServerDaemonApp();

        await Task.CompletedTask;

        try
        {
            Con.WriteLine("TelnetDocServerDaemon: Started.");
        }
        catch
        {
            await app._DisposeSafeAsync();
            app = null;
            throw;
        }
    }

    protected override async Task StopImplAsync(object? param)
    {
        Con.WriteLine("TelnetDocServerDaemon: Stopping...");

        if (app != null)
        {
            await app.DisposeWithCleanupAsync();

            app = null;
        }

        Con.WriteLine("TelnetDocServerDaemon: Stopped.");
    }
}

partial class TestDevCommands
{
    [ConsoleCommand(
        "Start or stop the TelnetDocServerDaemon daemon",
        "TelnetDocServerDaemon [command]",
        "Start or stop the TelnetDocServerDaemon daemon",
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
    static int TelnetDocServerDaemon(ConsoleService c, string cmdName, string str)
    {
        return DaemonCmdLineTool.EntryPoint(c, cmdName, str, new TelnetDocServerDaemon(), new DaemonSettings());
    }

}

