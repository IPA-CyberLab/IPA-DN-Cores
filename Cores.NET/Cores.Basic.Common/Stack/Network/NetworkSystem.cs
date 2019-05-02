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

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    class NetworkStackException : Exception
    {
        public NetworkStackException(string message) : base(message) { }
    }

    class NetworkStackShutdownException : DisconnectedException { }

    class NetworkSystemParam { }

    abstract class NetworkSystemBase : AsyncCleanupableCancellable
    {
        protected NetworkSystemParam Param;

        protected readonly CriticalSection LockObj = new CriticalSection();
        protected readonly HashSet<NetworkSock> OpenedSockList = new HashSet<NetworkSock>();

        public NetworkSystemBase(AsyncCleanuperLady lady, NetworkSystemParam param) : base(lady)
        {
            try
            {
                this.Param = param;
            }
            catch
            {
                Lady.DisposeSafe();
                throw;
            }
        }

        protected void CheckNotDisposed()
        {
            if (DisposeFlag.IsSet)
                throw new NetworkStackException("The network stack is already disposed.");
        }

        protected void AddToOpenedSockList(NetworkSock sock)
        {
            lock (LockObj)
            {
                OpenedSockList.Add(sock);
            }
        }

        protected void RemoveFromOpenedSockList(NetworkSock sock)
        {
            lock (LockObj)
            {
                OpenedSockList.Remove(sock);
            }
        }

        Once DisposeFlag;
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;
                this.CancelWatcher.Cancel();
            }
            finally { base.Dispose(disposing); }
        }

        public override async Task _CleanupInternalAsync()
        {
            try
            {
                while (CriticalCounter.Value >= 1)
                {
                    await Task.Delay(10);
                }

                NetworkSock[] openedSockets;

                lock (LockObj)
                {
                    openedSockets = OpenedSockList.ToArray();
                    OpenedSockList.Clear();
                }

                foreach (var s in openedSockets)
                {
                    s.Disconnect();
                    await s.Lady.CleanupAsync();
                }
            }
            finally { await base._CleanupInternalAsync(); }
        }
    }
}

