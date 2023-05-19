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
    NetTcpListener? Listener = null;

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

    async Task MainLoopAsync(CancellationToken cancel = default)
    {
        await Task.CompletedTask;

        this.Listener = LocalNet.CreateTcpListener(new TcpListenParam(async (listener, sock) =>
        {
            string body = Lfs.ReadStringFromFile(Env.AppRootDir._CombinePath("TelnetBody.txt"));

            StringWriter tmp1 = new StringWriter();

            tmp1.NewLine = Str.CrLf_Str;

            tmp1.WriteLine();

            foreach (var line in body._GetLines())
            {
                tmp1.WriteLine(" " + line);
            }

            await using var st = sock.GetStream();

            StreamWriter w = new StreamWriter(st, Str.ShiftJisEncoding);
            w.NewLine = Str.CrLf_Str;
            w.AutoFlush = true;

            body = tmp1.ToString();

            foreach (char c in body)
            {
                ((int)c)._Print();

                await w.WriteAsync(c);
                await Task.Delay(Util.RandSInt15() % 100);
            }

            Memory<byte> recvBuf = new byte[1];

            while (true)
            {
                await st.ReadAsync(recvBuf);
            }

        }, ports: new int[] { 23 }));
    }

    protected async override Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            if (Listener != null)
            {
                await Listener.DisposeAsync(ex);
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

