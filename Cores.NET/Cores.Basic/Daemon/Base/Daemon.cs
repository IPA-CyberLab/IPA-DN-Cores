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
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Diagnostics;
using System.ServiceProcess;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Helper.Basic
{
    static class DaemonHelper
    {
        public static void Start(this IDaemon daemon, object param = null) => daemon.StartAsync(param)._GetResult();
        public static void Stop(this IDaemon daemon, bool silent = false) => daemon.StopAsync(silent)._GetResult();
    }
}

namespace IPA.Cores.Basic
{
    static partial class CoresConfig
    {
        public static partial class DaemonDefaultSettings
        {
            public static readonly Copenhagen<int> DefaultStopTimeout = 5 * 1000;
        }
    }

    interface IDaemon : IDisposable
    {
        Task StartAsync(object param = null);
        Task StopAsync(bool silent = false);
        string Name { get; }
    }

    abstract class Daemon : IDaemon
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

        public async Task StartAsync(object param = null)
        {
            await Task.Yield();
            using (await AsyncLock.LockWithAwait())
            {
                Leak = LeakChecker.Enter(LeakCounterKind.StartDaemon);

                try
                {
                    if (this.Status != DaemonStatus.Stopped)
                        throw new ApplicationException($"The status of the daemon \"{Options.Name}\" (\"{Options.FriendlyName}\") is '{this.Status}'.");

                    if (this.Options.SingleInstance)
                    {
                        try
                        {
                            SingleInstance = new SingleInstance($"svc_instance_{Options.Name}", true);
                        }
                        catch
                        {
                            throw new ApplicationException($"Another instance of the daemon \"{Options.Name}\" (\"{Options.FriendlyName}\") has been already running.");
                        }
                    }

                    Con.WriteDebug($"Starting the daemon \"{Options.Name}\" (\"{Options.FriendlyName}\") ...");

                    this.Status = DaemonStatus.Starting;
                    this.StatusChangedEvent.Fire(this, this.Status);

                    try
                    {
                        await StartImplAsync(param);
                    }
                    catch (Exception ex)
                    {
                        Con.WriteError($"Starting the daemon \"{Options.Name}\" (\"{Options.FriendlyName}\") failed.");
                        Con.WriteError($"Error: {ex.ToString()}");

                        this.Status = DaemonStatus.Stopped;
                        this.StatusChangedEvent.Fire(this, this.Status);
                        throw;
                    }

                    Con.WriteDebug($"The daemon \"{Options.Name}\" (\"{Options.FriendlyName}\") is started successfully.");

                    this.Param = param;

                    this.Status = DaemonStatus.Running;
                    this.StatusChangedEvent.Fire(this, this.Status);

                    if (Env.IsUnix)
                    {
                        // Register the SIGTERM handler
                        SigTermOnce = new Once();
                        System.Runtime.Loader.AssemblyLoadContext.Default.Unloading += UnixSigTermHandler;
                    }
                }
                catch
                {
                    Leak._DisposeSafe();
                    throw;
                }
            }
        }

        // SIGTERM handler
        Once SigTermOnce;
        private void UnixSigTermHandler(System.Runtime.Loader.AssemblyLoadContext obj)
        {
            if (SigTermOnce.IsFirstCall())
            {
                // Stop the service
                Con.WriteDebug($"The daemon \"{Options.Name}\" (\"{Options.FriendlyName}\") received the SIGTERM signal. Shutting down the daemon...");
                this.StopAsync(true)._GetResult();
                Con.WriteDebug($"The daemon \"{Options.Name}\" (\"{Options.FriendlyName}\") completed the SIGTERM handler.");
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
                        throw new ApplicationException($"The status of the daemon \"{Options.Name}\" (\"{Options.FriendlyName}\") is '{this.Status}'.");
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
                        string msg = $"Error! The StopImplAsync() routine of the daemon \"{Options.Name}\" (\"{Options.FriendlyName}\") has been timed out ({Options.StopTimeout} msecs). Terminating the process forcefully.";
                        Kernel.SelfKill(msg);
                    }

                    this.Param = null;
                }
                catch (Exception ex)
                {
                    Con.WriteError($"Stopping the daemon \"{Options.Name}\" (\"{Options.FriendlyName}\") failed.");
                    Con.WriteError($"Error: {ex.ToString()}");

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

                Con.WriteDebug("Flushing local logs...");

                await LocalLogRouter.FlushAsync();

                Con.WriteDebug("Flushing local logs completed.");

                Con.WriteDebug($"The daemon \"{Options.Name}\" (\"{Options.FriendlyName}\") is stopped successfully.");

                this.Status = DaemonStatus.Stopped;
                this.StatusChangedEvent.Fire(this, this.Status);

                if (Env.IsUnix)
                {
                    // Unregister the SIGTERM handler
                    System.Runtime.Loader.AssemblyLoadContext.Default.Unloading -= UnixSigTermHandler;
                }
            }
        }

        public override string ToString() => $"\"{Options.Name}\" (\"{Options.FriendlyName}\")";

        public void Dispose() => Dispose(true);
        protected virtual void Dispose(bool disposing)
        {
            StopAsync(true)._TryGetResult();
        }
    }

    class DaemonHost
    {
        public IDaemon Daemon { get; }
        public object Param { get; }

        public DaemonHost(IDaemon daemon, object param = null)
        {
            this.Daemon = daemon;
            this.Param = param;
        }

        Once StartedOnce;

        public void TestRun()
        {
            if (StartedOnce.IsFirstCall() == false) throw new ApplicationException("DaemonHost is already started.");

            this.Daemon.Start(this.Param);

            Con.ReadLine($"Enter key to stop the {this.Daemon.Name} daemon >");

            this.Daemon.Stop(false);
        }

        public void ExecServiceMainRoutine()
        {
            if (StartedOnce.IsFirstCall() == false) throw new ApplicationException("DaemonHost is already started.");

            this.Daemon.Start(this.Param);
        }

        public void ShutdownService()
        {
            this.Daemon.Stop(false);
        }

        public void StopService()
        {
        }
    }
}

#endif // CORES_BASIC_DAEMON
