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
using System.Runtime.Serialization;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Basic.Internal.UserModeService;
using System.IO.Pipes;

namespace IPA.Cores.Basic
{
    static partial class CoresConfig
    {
        public static partial class UserModeServiceSettings
        {
            public static readonly Copenhagen<Func<string>> GetLocalHiveDirProc = new Func<string>(() => Env.LocalPathParser.Combine(Env.AppRootDir, "Local", "DaemonPid"));
        }
    }

    namespace Internal.UserModeService
    {
        [Serializable]
        [DataContract]
        class UserModeServicePidData
        {
            [DataMember]
            public long Pid;

            [DataMember]
            public string EventName;
        }
    }

    sealed class UserModeService : IService
    {
        public const string ExecMainSignature = "--- User mode service started ---";

        public string Name { get; }

        public Action OnStart { get; }
        public Action OnStop { get; }

        readonly CriticalSection LockObj = new CriticalSection();

        readonly Hive Hive;
        readonly HiveData<UserModeServicePidData> HiveData;
        readonly Event StoppedEvent = new Event(true);

        SingleInstance SingleInstance;

        public UserModeService(string name, Action onStart, Action onStop)
        {
            this.Name = name;

            this.OnStart = onStart;
            this.OnStop = onStop;

            this.Hive = new Hive(new HiveOptions(CoresConfig.UserModeServiceSettings.GetLocalHiveDirProc.Value()));
            this.HiveData = new HiveData<UserModeServicePidData>(this.Hive, this.Name, () => new UserModeServicePidData());
        }

        Once Once;

        public void ExecMain()
        {
            if (Once.IsFirstCall() == false) throw new ApplicationException("StartMainLoop can be called only once.");

            this.SingleInstance = new SingleInstance($"UserModeService_{this.Name}");

            try
            {
                string eventName = null;
                EventWaitHandle eventHandle = null;

                if (Env.IsUnix)
                {
                    System.Runtime.Loader.AssemblyLoadContext.Default.Unloading += UnixSigTermHandler;
                }
                else
                {
                    eventName = @"Global\usermodesvc_" + Util.Rand(16)._GetHexString().ToLower();

                    eventHandle = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);

                    TaskUtil.StartSyncTaskAsync(() =>
                    {
                        eventHandle.WaitOne();

                        Win32PipeRecvHandler();
                    }, leakCheck: false)._LaissezFaire(true);
                }

                InternalStart();

                lock (HiveData.DataLock)
                {
                    HiveData.Data.Pid = Env.ProcessId;
                    HiveData.Data.EventName = eventName;
                }

                HiveData.SyncWithStorage(HiveSyncFlags.SaveToFile, true);

                Console.WriteLine(ExecMainSignature);

                // The daemon routine is now started. Wait here until InternalStop() is called.
                StoppedEvent.Wait();
            }
            catch
            {
                this.SingleInstance._DisposeSafe();
                this.SingleInstance = null;
                throw;
            }
        }


        Once StartOnce;

        void InternalStart()
        {
            lock (LockObj)
            {
                if (StartOnce.IsFirstCall() == false) throw new ApplicationException($"UserModeService ({this.Name}): You cannot try to start the same service twice.");

                this.OnStart();
            }
        }

        Once StopOnce;

        void InternalStop()
        {
            this.SingleInstance._DisposeSafe();
            this.SingleInstance = null;

            lock (LockObj)
            {
                if (StartOnce.IsSet == false) throw new ApplicationException($"UserModeService ({this.Name}): The service is not started yet.");

                if (StopOnce.IsFirstCall())
                {
                    try
                    {
                        this.OnStop();
                    }
                    catch (Exception ex)
                    {
                        Kernel.SelfKill($"UserModeService ({this.Name}): An error occured on the OnStop() routine. Terminating the process. Error: {ex.ToString()}");
                    }

                    lock (HiveData.DataLock)
                    {
                        HiveData.Data.Pid = 0;
                    }

                    HiveData.SyncWithStorage(HiveSyncFlags.SaveToFile, true);

                    // Break the freeze state of the ExecMain() function
                    StoppedEvent.Set();
                }
            }
        }

        // Win32 Pipe handler
        Once PipeRecvOnce;
        void Win32PipeRecvHandler()
        {
            if (PipeRecvOnce.IsFirstCall())
            {
                // Stop the service
                Con.WriteLine($"The daemon \"{Name}\" received the shutdown command. Shutting down the daemon...");

                InternalStop();

                Con.WriteLine($"The daemon \"{Name}\" completed the shutdown command handler.");
            }
        }

        // SIGTERM handler
        Once SigTermOnce;
        void UnixSigTermHandler(System.Runtime.Loader.AssemblyLoadContext obj)
        {
            if (SigTermOnce.IsFirstCall())
            {
                // Stop the service
                Con.WriteLine($"The daemon \"{Name}\" received the SIGTERM signal. Shutting down the daemon...");

                InternalStop();

                Con.WriteLine($"The daemon \"{Name}\" completed the SIGTERM handler.");
            }
        }

        public void StopService(int stopTimeout)
        {
            HiveData.SyncWithStorage(HiveSyncFlags.LoadFromFile, true);

            long pid = HiveData.Data.Pid;

            if (pid != 0)
            {
                if (Env.IsWindows == false)
                {
                    if (UnixApi.Kill((int)pid, UnixApi.Signals.SIGTERM) == 0)
                    {
                        Con.WriteLine($"Shutting down the daemon \"{Name}\" (pid = {pid}) ...");

                        if (UnixApi.WaitProcessExit((int)pid, stopTimeout) == false)
                        {
                            Con.WriteLine($"Shutting down the daemon \"{Name}\" (pid = {pid}) timed out.");

                            throw new ApplicationException($"Shutting down the daemon \"{Name}\" (pid = {pid}) timed out.");
                        }
                    }
                    else
                    {
                        Con.WriteLine($"The daemon \"{Name}\" is not running.");
                    }
                }
                else
                {
                    if (ManualResetEvent.TryOpenExisting(HiveData.Data.EventName, out EventWaitHandle eventHandle))
                    {
                        try
                        {
                            Con.WriteLine($"Stopping the daemon \"{Name}\" (pid = {pid}) ...");

                            Dbg.Where();

                            eventHandle.Set();

                            Dbg.Where();

                            if (Win32ApiUtil.WaitProcessExit((int)pid, stopTimeout) == false)
                            {
                                Con.WriteLine($"Stopping the daemon \"{Name}\" (pid = {pid}) timed out.");

                                throw new ApplicationException($"Stopping the daemon \"{Name}\" (pid = {pid}) timed out.");
                            }

                            Dbg.Where();
                        }
                        finally
                        {
                            eventHandle._DisposeSafe();
                        }
                    }
                    else
                    {
                        Con.WriteLine($"The daemon \"{Name}\" is not running.");
                    }
                }
            }
            else
            {
                Con.WriteLine($"The daemon \"{Name}\" is not running.");
            }
        }
    }
}

#endif // CORES_BASIC_DAEMON
