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

#if CORES_BASIC_DAEMON

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Diagnostics;
using System.ServiceProcess;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Net;

namespace IPA.Cores.Helper.Basic
{
    static class DaemonHelper
    {
        public static void Start(this Daemon daemon, object param = null) => daemon.StartAsync(param)._GetResult();
        public static void Stop(this Daemon daemon, bool silent = false) => daemon.StopAsync(silent)._GetResult();
    }
}

namespace IPA.Cores.Basic
{
    static partial class CoresConfig
    {
        public static partial class DaemonSettings
        {
            public static readonly Copenhagen<int> DefaultStopTimeout = 60 * 1000;
            public static readonly Copenhagen<int> StartExecTimeout = 15 * 1000;
        }
    }

    abstract class Daemon
    {
        public DaemonOptions Options { get; }
        public string Name => Options.Name;

        public DaemonStatus Status { get; private set; }
        public FastEventListenerList<Daemon, DaemonStatus> StatusChangedEvent { get; }

        CriticalSection StatusLock = new CriticalSection();

        AsyncLock AsyncLock = new AsyncLock();

        SingleInstance SingleInstance = null;

        IHolder Leak;

        object Param = null;
        
        public Daemon(DaemonOptions options)
        {
            this.Options = options;
            this.Status = DaemonStatus.Stopped;
            this.StatusChangedEvent = new FastEventListenerList<Daemon, DaemonStatus>();
        }

        protected abstract Task StartImplAsync(object param);
        protected abstract Task StopImplAsync(object param);

        public bool IsInstanceRunning()
        {
            try
            {
                SingleInstance = new SingleInstance($"svc_instance_{Options.Name}", true);
                SingleInstance._DisposeSafe();
                return false;
            }
            catch { }

            return true;
        }
    
        public async Task StartAsync(object param = null)
        {
            await Task.Yield();
            using (await AsyncLock.LockWithAwait())
            {
                Leak = LeakChecker.Enter(LeakCounterKind.StartDaemon);

                try
                {
                    if (this.Status != DaemonStatus.Stopped)
                        throw new ApplicationException($"The status of the daemon \"{Options.Name}\" ({Options.FriendlyName}) is '{this.Status}'.");

                    if (this.Options.SingleInstance)
                    {
                        try
                        {
                            SingleInstance = new SingleInstance($"svc_instance_{Options.Name}", true);
                        }
                        catch
                        {
                            throw new ApplicationException($"Another instance of the daemon \"{Options.Name}\" ({Options.FriendlyName}) has been already running.");
                        }
                    }

                    Con.WriteLine($"Starting the daemon \"{Options.Name}\" ({Options.FriendlyName}) ...");

                    this.Status = DaemonStatus.Starting;
                    this.StatusChangedEvent.Fire(this, this.Status);

                    try
                    {
                        await StartImplAsync(param);
                    }
                    catch (Exception ex)
                    {
                        Con.WriteError($"Starting the daemon \"{Options.Name}\" ({Options.FriendlyName}) failed.");
                        Con.WriteError($"Error: {ex.ToString()}");

                        this.Status = DaemonStatus.Stopped;
                        this.StatusChangedEvent.Fire(this, this.Status);
                        throw;
                    }

                    Con.WriteLine($"The daemon \"{Options.Name}\" ({Options.FriendlyName}) is now running.");

                    this.Param = param;

                    this.Status = DaemonStatus.Running;
                    this.StatusChangedEvent.Fire(this, this.Status);
                }
                catch
                {
                    Leak._DisposeSafe();
                    throw;
                }
            }
        }


        public async Task StopAsync(bool silent = false)
        {
            await Task.Yield();

            using (await AsyncLock.LockWithAwait())
            {
                if (this.Status != DaemonStatus.Running)
                {
                    if (silent == false)
                        throw new ApplicationException($"The status of the daemon \"{Options.Name}\" ({Options.FriendlyName}) is '{this.Status}'.");
                    else
                        return;
                }

                this.Status = DaemonStatus.Stopping;
                this.StatusChangedEvent.Fire(this, this.Status);

                try
                {
                    Task stopTask = StopImplAsync(this.Param);

                    if (await TaskUtil.WaitObjectsAsync(tasks: stopTask._SingleArray(), timeout: this.Options.StopTimeout, exceptions: ExceptionWhen.None) == ExceptionWhen.TimeoutException)
                    {
                        // Timeouted
                        string msg = $"Error! The StopImplAsync() routine of the daemon \"{Options.Name}\" ({Options.FriendlyName}) has been timed out ({Options.StopTimeout} msecs). Terminating the process forcefully.";
                        Kernel.SelfKill(msg);
                    }

                    this.Param = null;
                }
                catch (Exception ex)
                {
                    Con.WriteLine($"Stopping the daemon \"{Options.Name}\" ({Options.FriendlyName}) failed.");
                    Con.WriteLine($"Error: {ex.ToString()}");

                    this.Status = DaemonStatus.Running;
                    this.StatusChangedEvent.Fire(this, this.Status);
                    throw;
                }

                if (SingleInstance != null)
                {
                    SingleInstance._DisposeSafe();
                    SingleInstance = null;
                }

                Leak._DisposeSafe();

                Con.WriteLine("Flushing local logs...");

                await LocalLogRouter.FlushAsync();

                Con.WriteLine("Flushing local logs completed.");

                Con.WriteLine($"The daemon \"{Options.Name}\" ({Options.FriendlyName}) is stopped successfully.");

                this.Status = DaemonStatus.Stopped;
                this.StatusChangedEvent.Fire(this, this.Status);
            }
        }

        public override string ToString() => $"\"{Options.Name}\" ({Options.FriendlyName})";

        public void Dispose() => Dispose(true);
        protected virtual void Dispose(bool disposing)
        {
            StopAsync(true)._TryGetResult();
        }
    }

    [Flags]
    enum DaemonMode
    {
        UserMode = 0,
        WindowsServiceMode = 1,
    }

    class DaemonHost
    {
        public Daemon Daemon { get; }
        public object Param { get; }

        public DaemonHost(Daemon daemon, object param = null)
        {
            this.Daemon = daemon;
            this.Param = param;
        }

        Once StartedOnce;

        public void TestRun()
        {
            if (StartedOnce.IsFirstCall() == false) throw new ApplicationException("DaemonHost is already started.");

            // Start the TelnetLogWatcher
            using (var telnetWatcher = new TelnetLocalLogWatcher(new TelnetStreamWatcherOptions((ip) => ip._GetIPAddressType().BitAny(IPAddressType.LocalUnicast | IPAddressType.Loopback), null,
                new IPEndPoint(IPAddress.Any, this.Daemon.Options.TelnetLogWatcherPort),
                new IPEndPoint(IPAddress.IPv6Any, this.Daemon.Options.TelnetLogWatcherPort))))
            {
                this.Daemon.Start(this.Param);

                Con.ReadLine($"Enter key to stop the {this.Daemon.Name} daemon >");

                this.Daemon.Stop(false);
            }
        }


        DaemonMode Mode;

        IService CreateService(DaemonMode mode)
        {
            IService service;

            if (this.Mode != DaemonMode.WindowsServiceMode)
            {
                // Usermode
                service = new UserModeService(
                    this.Daemon.Name,
                    () => this.Daemon.Start(this.Param),
                    () => this.Daemon.Stop(true),
                    this.Daemon.Options.TelnetLogWatcherPort);
            }
            else
            {
                // Windows service mode
                service = new WindowsService(
                    this.Daemon.Name,
                    () => this.Daemon.Start(this.Param),
                    () => this.Daemon.Stop(true),
                    this.Daemon.Options.TelnetLogWatcherPort);
            }

            return service;
        }

        public void ExecMain(DaemonMode mode)
        {
            if (Env.IsWindows == false && mode == DaemonMode.WindowsServiceMode)
                throw new ArgumentException("Env.IsWindows == false && mode == DaemonMode.WindowsServiceMode");

            if (StartedOnce.IsFirstCall() == false) throw new ApplicationException("DaemonHost is already started.");

            this.Mode = mode;

            IService service = CreateService(mode);

            service.ExecMain();
        }

        public void StopService(DaemonMode mode)
        {
            IService service = CreateService(mode);

            Con.WriteLine($"Stopping the daemon {Daemon.ToString()} ...");

            service.StopService(Daemon.Options.StopTimeout);

            Con.WriteLine();
            Con.WriteLine($"The daemon {Daemon.ToString()} is stopped successfully.");
        }

        public void Show(DaemonMode mode)
        {
            IService service = CreateService(mode);

            service.Show();
        }
    }

    [Flags]
    enum DaemonCmdType
    {
        Unknown = 0,
        Start,
        Stop,
        Test,
        Show,
        ExecMain,

        WinStart,
        WinStop,
        WinInstall,
        WinUninstall,
        WinExecSvc,
    }

    class DaemonCmdLineTool
    {
        public static int EntryPoint(ConsoleService c, string cmdName, string str, Daemon daemon)
        {
            ConsoleParam[] args =
            {
                new ConsoleParam("[command]"),
            };
            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            Con.WriteLine();
            Con.WriteLine($"Daemon {daemon.ToString()} control command");
            Con.WriteLine();

            string command = vl.DefaultParam.StrValue;

            if (command._IsEmpty())
            {
                Con.WriteError($"You must specify the [command] argument.\nFor details please enter \"{cmdName} /help\".");
                return 1;
            }

            DaemonCmdType cmdType = command._ParseEnum(DaemonCmdType.Unknown);

            if (cmdType == DaemonCmdType.Unknown)
            {
                Con.WriteError($"Invalid command \"{command}\".\nFor details please enter \"cmdName\" /help.");
                return 1;
            }

            Con.WriteLine($"Executing the {cmdType.ToString().ToLower()} command.");
            Con.WriteLine();

            DaemonHost host = new DaemonHost(daemon);

            switch (cmdType)
            {
                case DaemonCmdType.Start:
                    if (daemon.IsInstanceRunning())
                    {
                        Con.WriteError($"The {daemon.ToString()} is already running.");
                        return 1;
                    }
                    else
                    {
                        string exe;
                        string arguments;

                        if (Env.IsUnix)
                        {
                            exe = "nohup";
                            arguments = (Env.IsHostedByDotNetProcess ? Env.DotNetHostProcessExeName : $"\"{Env.ExeFileName}\"") + " " + (Env.IsHostedByDotNetProcess ? $"exec \"{Env.ExeFileName}\" /cmd:{cmdName} {DaemonCmdType.ExecMain}" : $"/cmd:{cmdName} {DaemonCmdType.ExecMain}");
                        }
                        else
                        {
                            exe = (Env.IsHostedByDotNetProcess ? Env.DotNetHostProcessExeName : $"\"{Env.ExeFileName}\"");
                            arguments = (Env.IsHostedByDotNetProcess ? $"exec \"{Env.ExeFileName}\" /cmd:{cmdName} {DaemonCmdType.ExecMain}" : $"/cmd:{cmdName} {DaemonCmdType.ExecMain}");
                        }

                        ProcessStartInfo info = new ProcessStartInfo()
                        {
                            FileName = exe,
                            Arguments = arguments,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            RedirectStandardInput = false,
                        };

                        try
                        {
                            using (Process p = Process.Start(info))
                            {
                                CancellationTokenSource cts = new CancellationTokenSource();

                                StringWriter stdOut = new StringWriter();
                                StringWriter stdErr = new StringWriter();

                                Task outputReaderTask = TaskUtil.StartAsyncTaskAsync(async () =>
                                {
                                    while (true)
                                    {
                                        cts.Token.ThrowIfCancellationRequested();

                                        string line = await p.StandardOutput.ReadLineAsync();
                                        if (line == null)
                                            throw new ApplicationException("StandardOutput is disconnected.");

                                        stdOut.WriteLine(line);

                                        if (line._InStr(UserModeService.ExecMainSignature, false))
                                        {
                                            return;
                                        }
                                    }
                                });

                                Task errorReaderTask = TaskUtil.StartAsyncTaskAsync(async () =>
                                {
                                    while (true)
                                    {
                                        cts.Token.ThrowIfCancellationRequested();

                                        string line = await p.StandardError.ReadLineAsync();

                                        stdErr.WriteLine(line);

                                        if (line == null)
                                            throw new ApplicationException("StandardError is disconnected.");
                                    }
                                }, leakCheck: false);

                                var result = TaskUtil.WaitObjectsAsync(new Task[] { outputReaderTask, errorReaderTask }, timeout: CoresConfig.DaemonSettings.StartExecTimeout)._GetResult();

                                cts.Cancel();

                                bool isError = false;

                                if (result == ExceptionWhen.TimeoutException)
                                {
                                    // Error
                                    Con.WriteError($"Failed to start the {daemon.ToString()}. Child process timed out.");
                                    isError = true;
                                }
                                else if (result == ExceptionWhen.TaskException)
                                {
                                    // Error
                                    Con.WriteError($"Failed to start the {daemon.ToString()}. Error occured in the child process.");
                                    isError = true;
                                }

                                if (isError)
                                {
                                    Con.WriteError();
                                    Con.WriteError("--- Standard output ---");
                                    Con.WriteError(stdOut.ToString());
                                    Con.WriteError();
                                    Con.WriteError("--- Standard error ---");
                                    Con.WriteError(stdErr.ToString());
                                    Con.WriteError();

                                    // Terminate the process
                                    try
                                    {
                                        p.Kill();
                                        p.WaitForExit();
                                    }
                                    catch { }
                                }
                                else
                                {
                                    // OK
                                    Con.WriteLine($"The {daemon.ToString()} is started successfully.");
                                }

                                outputReaderTask._TryWait(true);

                                if (isError)
                                {
                                    return 1;
                                }
                                else
                                {
                                    return 0;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Con.WriteError($"File name: '{info.FileName}'");
                            Con.WriteError($"Arguments: '{info.Arguments}'");
                            Con.WriteError(ex.Message);
                            return 1;
                        }
                    }

                case DaemonCmdType.Stop:
                    host.StopService(DaemonMode.UserMode);
                    break;

                case DaemonCmdType.Show:
                    host.Show(DaemonMode.UserMode);
                    break;

                case DaemonCmdType.ExecMain:
                    host.ExecMain(DaemonMode.UserMode);
                    break;

                case DaemonCmdType.Test:
                    host.TestRun();
                    break;

                case DaemonCmdType.WinExecSvc:
                    if (Env.IsWindows == false) throw new PlatformNotSupportedException();
                    host.ExecMain(DaemonMode.WindowsServiceMode);
                    break;

                case DaemonCmdType.WinInstall:
                    {
                        if (Env.IsWindows == false) throw new PlatformNotSupportedException();

                        string exe;
                        string arguments;

                        exe = (Env.IsHostedByDotNetProcess ? Env.DotNetHostProcessExeName : $"\"{Env.ExeFileName}\"");
                        arguments = (Env.IsHostedByDotNetProcess ? $"exec \"{Env.ExeFileName}\" /cmd:{cmdName} {DaemonCmdType.WinExecSvc}" : $"/cmd:{cmdName} {DaemonCmdType.WinExecSvc}");

                        string path = $"\"{exe}\" {arguments}";

                        if (Win32ApiUtil.IsServiceInstalled(daemon.Options.Name))
                        {
                            Con.WriteError($"The Windows service {daemon.ToString()} has already been installed.");
                            return -1;
                        }

                        Con.WriteLine($"Installing the Windows service {daemon.ToString()} ...");

                        Win32ApiUtil.InstallService(daemon.Options.Name, daemon.Options.FriendlyName, daemon.Options.FriendlyName, path);

                        Con.WriteLine($"The Windows service {daemon.ToString()} is successfully installed.");

                        Con.WriteLine();

                        Con.WriteLine($"Starting the Windows service {daemon.ToString()} ...");

                        Win32ApiUtil.StartService(daemon.Options.Name);

                        Con.WriteLine($"The Windows service {daemon.ToString()} is successfully started.");

                        return 0;
                    }

                case DaemonCmdType.WinUninstall:
                    if (Env.IsWindows == false) throw new PlatformNotSupportedException();

                    if (Win32ApiUtil.IsServiceInstalled(daemon.Options.Name) == false)
                    {
                        Con.WriteError($"The Windows service {daemon.ToString()} is not installed.");
                        return -1;
                    }

                    if (Win32ApiUtil.IsServiceRunning(daemon.Options.Name))
                    {
                        Con.WriteLine($"Stopping the Windows service {daemon.ToString()} ...");

                        Win32ApiUtil.StopService(daemon.Options.Name);

                        Con.WriteLine($"The Windows service {daemon.ToString()} is successfully stopped.");

                        Con.WriteLine();
                    }

                    Con.WriteLine($"Uninstalling the Windows service {daemon.ToString()} ...");

                    Win32ApiUtil.UninstallService(daemon.Options.Name);

                    Con.WriteLine($"The Windows service {daemon.ToString()} is successfully uninstalled.");

                    return 0;

                case DaemonCmdType.WinStart:

                    if (Env.IsWindows == false) throw new PlatformNotSupportedException();

                    if (Win32ApiUtil.IsServiceInstalled(daemon.Options.Name) == false)
                    {
                        Con.WriteError($"The Windows service {daemon.ToString()} is not installed.");
                        return -1;
                    }

                    if (Win32ApiUtil.IsServiceRunning(daemon.Options.Name))
                    {
                        Con.WriteError($"The Windows service {daemon.ToString()} is already running");
                        return -1;
                    }

                    Con.WriteLine($"Starting the Windows service {daemon.ToString()} ...");

                    Win32ApiUtil.StartService(daemon.Options.Name);

                    Con.WriteLine($"The Windows service {daemon.ToString()} is successfully started.");

                    return 0;

                case DaemonCmdType.WinStop:

                    if (Env.IsWindows == false) throw new PlatformNotSupportedException();

                    if (Win32ApiUtil.IsServiceInstalled(daemon.Options.Name) == false)
                    {
                        Con.WriteError($"The Windows service {daemon.ToString()} is not installed.");
                        return -1;
                    }

                    if (Win32ApiUtil.IsServiceRunning(daemon.Options.Name) == false)
                    {
                        Con.WriteError($"The Windows service {daemon.ToString()} is not started.");
                        return -1;
                    }

                    Con.WriteLine($"Stopping the Windows service {daemon.ToString()} ...");

                    Win32ApiUtil.StopService(daemon.Options.Name);

                    Con.WriteLine($"The Windows service {daemon.ToString()} is successfully stopped.");

                    return 0;
            }

            return 0;
        }
    }
}

#endif // CORES_BASIC_DAEMON
