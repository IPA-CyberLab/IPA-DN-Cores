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

using IPA.Cores.Basic.Internal.WinSvc;

namespace IPA.Cores.Basic
{
    interface IService
    {
        string Name { get; }

        void ExecMain();
    }

    sealed class WindowsService : IService
    {
        public string Name { get; }

        public Action OnStart { get; }
        public Action OnStop { get; }

        readonly WindowsServiceObject WinSvcObj;

        public WindowsService(string name, Action onStart, Action onStop)
        {
            if (Env.IsWindows == false) throw new PlatformNotSupportedException();

            this.Name = name;

            this.OnStart = onStart;
            this.OnStop = onStop;

            this.WinSvcObj = new WindowsServiceObject(this);
        }

        Once Once;

        public void ExecMain()
        {
            if (Once.IsFirstCall() == false) throw new ApplicationException("StartMainLoop can be called only once.");

            ServiceBase.Run(this.WinSvcObj);
        }
    }

    namespace Internal.WinSvc
    {
        sealed class WindowsServiceObject : ServiceBase
        {
            readonly WindowsService Svc;
            readonly CriticalSection LockObj = new CriticalSection();

            public WindowsServiceObject(WindowsService service)
            {
                this.CanStop = true;
                this.CanShutdown = true;
                this.CanPauseAndContinue = false;

                this.AutoLog = true;
                this.ServiceName = service.Name;

                this.Svc = service;
            }

            Once StartOnce;

            void InternalStart()
            {
                lock (LockObj)
                {
                    if (StartOnce.IsFirstCall() == false) throw new ApplicationException($"WindowsServiceObject ({this.ServiceName}): You cannot try to start the same service twice.");

                    Svc.OnStart();
                }
            }

            Once StopOnce;

            void InternalStop()
            {
                lock (LockObj)
                {
                    if (StartOnce.IsSet == false) throw new ApplicationException($"WindowsServiceObject ({this.ServiceName}): The service is not started yet.");

                    if (StopOnce.IsFirstCall())
                    {
                        try
                        {
                            this.OnStop();
                        }
                        catch (Exception ex)
                        {
                            Kernel.SelfKill($"WindowsServiceObject ({this.ServiceName}): An error occured on the OnStop() routine. Terminating the process. Error: {ex.ToString()}", 0);
                        }
                    }
                }
            }

            protected override void OnStart(string[] args)
            {
                InternalStart();
            }

            protected override void OnStop()
            {
                InternalStop();
            }

            protected override void OnShutdown()
            {
                InternalStop();
            }
        }
    }
}

#endif // CORES_BASIC_DAEMON

