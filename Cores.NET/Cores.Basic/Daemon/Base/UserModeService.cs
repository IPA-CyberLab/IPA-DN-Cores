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

namespace IPA.Cores.Basic
{
    static partial class CoresConfig
    {
        public static partial class UserModeServiceSettings
        {
            public static readonly Copenhagen<Func<string>> GetLocalHiveDirProc = new Func<string>(() => Env.LocalPathParser.Combine(Env.AppRootDir, "Local", "RunningService"));
        }
    }

    namespace Internal.UserModeService
    {
        [Serializable]
        [DataContract]
        class UserModeServicePidData
        {
            [DataMember]
            public long ProcessId;
        }
    }

    sealed class UserModeService : IService
    {
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
                if (Env.IsUnix)
                {
                    System.Runtime.Loader.AssemblyLoadContext.Default.Unloading += UnixSigTermHandler;
                }

                InternalStart();

                lock (HiveData.DataLock)
                {
                    HiveData.Data.ProcessId = Env.ProcessId;
                }

                try
                {
                    HiveData.SyncWithStorage(HiveSyncFlags.SaveToFile);
                }
                catch { }

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
                        Kernel.SelfKill($"UserModeService ({this.Name}): An error occured on the OnStop() routine. Terminating the process. Error: {ex.ToString()}", 0);
                    }

                    lock (HiveData.DataLock)
                    {
                        HiveData.Data.ProcessId = 0;
                    }

                    try
                    {
                        HiveData.SyncWithStorage(HiveSyncFlags.SaveToFile);
                    }
                    catch { }

                    // Break the freeze state of the ExecMain() function
                    StoppedEvent.Set();
                }
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

    }
}

#endif // CORES_BASIC_DAEMON
