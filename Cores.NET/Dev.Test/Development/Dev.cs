// IPA Cores.NET
// 
// Copyright (c) 2019- IPA CyberLab.
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

// Author: Daiyuu Nobori
// 開発中のクラスの一時置き場

#if true

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
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
using System.Diagnostics.CodeAnalysis;
using System.Net;

using IPA.Cores.Basic;
using IPA.Cores.Basic.Internal;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Microsoft.AspNetCore.Server.IIS.Core;
using Microsoft.EntityFrameworkCore.Query.Internal;
using System.Net.Sockets;

namespace IPA.Cores.Basic
{
    public class DialogSessionOptions
    {
        public Func<DialogSession, CancellationToken, Task> MainAsyncCallback { get; }
        public object? Param { get; }

        public DialogSessionOptions(Func<DialogSession, CancellationToken, Task> mainAsyncCallback, object? param = null)
        {
            MainAsyncCallback = mainAsyncCallback;
            this.Param = param;
        }
    }

    public interface IDialogRequestData { }

    public interface IDialogResponseData { }

    [Flags]
    public enum DialogRequestStatus
    {
        Running = 0,
        Ok,
        Error,
    }

    public class DialogRequest
    {
        public string RequestId { get; }
        public DialogRequestStatus Status { get; private set; } = DialogRequestStatus.Running;
        public DialogSession Session { get; }
        public int HardTimeout { get; }
        public int SoftTimeout { get; }
        public IDialogRequestData RequestData { get; }
        public IDialogResponseData? ResponseData { get; private set; }
        public Exception? Exception { get; private set; }
        readonly RefInt HeartBeatCounter = new RefInt();

        readonly AsyncManualResetEvent FinishedEvent = new AsyncManualResetEvent();

        internal DialogRequest(EnsureSpecial internalOnly, DialogSession session, IDialogRequestData requestData, int hardTimeout, int? softTimeout = null)
        {
            this.RequestId = Str.NewUid("REQUEST", '_');
            this.Session = session;
            this.RequestData = requestData;
            this.HardTimeout = hardTimeout;
            this.SoftTimeout = softTimeout ?? hardTimeout;
        }

        Once SetResponseOrErrorOnce;

        public void SetResponseData(IDialogResponseData response)
        {
            response._NullCheck();

            if (SetResponseOrErrorOnce.IsFirstCall() == false) return;

            this.ResponseData = response;
            this.Status = DialogRequestStatus.Ok;
            this.FinishedEvent.Set(true);
            this.Session.NoticeResponseFulfilledInternal(this);
        }

        public void SetResponseException(Exception exception)
        {
            exception._NullCheck();

            if (SetResponseOrErrorOnce.IsFirstCall() == false) return;

            this.Exception = exception;
            this.Status = DialogRequestStatus.Error;
            this.FinishedEvent.Set(true);
            this.Session.NoticeResponseFulfilledInternal(this);
        }

        public void SetResponseCancel()
        {
            SetResponseException(new OperationCanceledException());
        }

        public void HeartBeat()
        {
            this.HeartBeatCounter.Increment();
        }

        public async Task<IDialogResponseData> WaitForResponseAsync(CancellationToken cancel = default)
        {
            if (this.Status != DialogRequestStatus.Running)
            {
                if (this.Status == DialogRequestStatus.Error)
                {
                    throw this.Exception._NullCheck();
                }
                else
                {
                    return this.ResponseData._NullCheck();
                }
            }

            long now = TickNow;

            long hardTimeoutGiveupTime = this.HardTimeout >= 1 ? now + this.HardTimeout : long.MaxValue;
            int lastHeartBeatCounter = -1;

            try
            {
                LABEL_TIMEOUT_RETRY:
                now = TickNow;

                int timeout = int.MaxValue;

                if (hardTimeoutGiveupTime != long.MaxValue)
                {
                    int hardTimeoutInterval = (int)(hardTimeoutGiveupTime - now);
                    if (hardTimeoutInterval <= 0)
                    {
                        throw new TimeoutException();
                    }

                    timeout = Math.Min(timeout, hardTimeoutInterval);
                }

                if (this.SoftTimeout > 0)
                {
                    timeout = Math.Min(timeout, this.SoftTimeout);

                    if (lastHeartBeatCounter == this.HeartBeatCounter)
                    {
                        throw new TimeoutException();
                    }

                    lastHeartBeatCounter = this.HeartBeatCounter;
                }

                var result = await TaskUtil.WaitObjectsAsync(cancels: cancel._SingleArray(), manualEvents: FinishedEvent._SingleArray(), timeout: timeout, exceptions: ExceptionWhen.CancelException | ExceptionWhen.TaskException);

                if (result == ExceptionWhen.TimeoutException)
                {
                    goto LABEL_TIMEOUT_RETRY;
                }

                if (this.Status == DialogRequestStatus.Running)
                {
                    throw new CoresLibException("this.Status == DialogRequestStatus.Running");
                }

                if (this.Status == DialogRequestStatus.Error)
                {
                    throw this.Exception._NullCheck();
                }
                else
                {
                    return this.ResponseData._NullCheck();
                }
            }
            catch (Exception ex)
            {
                SetResponseException(ex);
                throw;
            }
        }
    }

    public class DialogSession : AsyncService
    {
        public string SessionId { get; }
        public DialogSessionManager Manager { get; }
        public DialogSessionOptions Options { get; }
        public Task? MainTask { get; private set; } = null;

        public Exception? Exception { get; private set; } = null;
        public bool IsFinished { get; private set; } = false;

        public long Expires { get; private set; } = 0;

        readonly CriticalSection<DialogSession> RequestListLockObj = new CriticalSection<DialogSession>();
        readonly List<DialogRequest> RequestList = new List<DialogRequest>();

        internal DialogSession(EnsureSpecial internalOnly, DialogSessionManager manager, DialogSessionOptions options)
        {
            try
            {
                this.SessionId = Str.NewUid("SESSION", '_');
                this.Manager = manager;
                this.Options = options;
            }
            catch (Exception ex)
            {
                this._DisposeSafe(ex);
                throw;
            }
        }

        Once Started;

        internal void StartInternal()
        {
            if (Started.IsFirstCall())
            {
                this.MainTask = TaskUtil.StartAsyncTaskAsync(async () =>
                {
                    try
                    {
                        await Options.MainAsyncCallback(this, this.GrandCancel);
                    }
                    catch (Exception ex)
                    {
                        this.Exception = ex;
                        ex._Error();
                    }
                    finally
                    {
                        this.IsFinished = true;
                        if (this.Manager.Options.ExpiresAfterFinishedMsec >= 0)
                        {
                            this.Expires = TickNow + this.Manager.Options.ExpiresAfterFinishedMsec;
                        }
                    }
                });
            }
        }

        internal void NoticeResponseFulfilledInternal(DialogRequest request)
        {
            lock (this.RequestListLockObj)
            {
                this.RequestList.Remove(request);
            }

            Pulse.FirePulse(true);
        }

        public DialogRequest Request(IDialogRequestData requestData, int hardTimeout, int? softTimeout = null)
        {
            this.GrandCancel.ThrowIfCancellationRequested();

            DialogRequest request = new DialogRequest(EnsureSpecial.Yes, this, requestData, hardTimeout, softTimeout);

            lock (this.RequestListLockObj)
            {
                this.RequestList.Add(request);
            }

            Pulse.FirePulse(true);

            return request;
        }

        public async Task<IDialogResponseData> RequestAndWaitForResponseAsync(IDialogRequestData requestData, int hardTimeout, int? softTimeout = null, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            this.GrandCancel.ThrowIfCancellationRequested();

            var req = this.Request(requestData, hardTimeout, softTimeout);

            using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken cancel2, cancel, this.GrandCancel))
            {
                return await req.WaitForResponseAsync(cancel2);
            }
        }

        readonly AsyncPulse Pulse = new AsyncPulse();

        public async Task<DialogRequest?> GetNextRequestAsync(int timeout = Timeout.Infinite, CancellationToken cancel = default)
        {
            var waiter = Pulse.GetPulseWaiter();

            long giveupTick = (timeout < 0) ? long.MaxValue : TickNow + timeout;

            while (this.GrandCancel.IsCancellationRequested == false)
            {
                lock (this.RequestListLockObj)
                {
                    if (this.RequestList.Count >= 1)
                    {
                        return this.RequestList[0];
                    }
                }

                int timeout2 = giveupTick == long.MaxValue ? Timeout.Infinite : (int)(giveupTick - TickNow);

                if (timeout2 <= 0) throw new TimeoutException();

                await waiter.WaitAsync(timeout2, this.GrandCancel);
            }

            if (this.IsFinished == false)
            {
                throw new OperationCanceledException();
            }
            else if (this.Exception != null)
            {
                throw this.Exception;
            }
            else
            {
                return null;
            }
        }

        public DialogRequest? GetRequestById(string requestId)
        {
            lock (this.RequestListLockObj)
            {
                return this.RequestList.Where(x => x.RequestId._IsSamei(requestId)).FirstOrDefault();
            }
        }

        public void SetResponseData(string requestId, IDialogResponseData response)
        {
            var request = GetRequestById(requestId);
            if (request != null) request.SetResponseData(response);
        }

        public void SetResponseException(string requestId, Exception exception)
        {
            var request = GetRequestById(requestId);
            if (request != null) request.SetResponseException(exception);
        }

        public void SetResponseCancel(string requestId)
        {
            var request = GetRequestById(requestId);
            if (request != null) request.SetResponseCancel();
        }

        public void SendHeartBeat(string requestId)
        {
            var request = GetRequestById(requestId);
            if (request != null) request.HeartBeat();
        }
    }

    public class DialogSessionManagerOptions
    {
        public int ExpiresAfterFinishedMsec { get; }
        public int GcIntervals { get; }

        public DialogSessionManagerOptions(int sessionExpiresAfterFinishedMsecs = Consts.Timeouts.DefaultDialogSessionExpiresAfterFinishedMsecs, int gcIntervals = Consts.Timeouts.DefaultDialogSessionGcIntervals)
        {
            ExpiresAfterFinishedMsec = sessionExpiresAfterFinishedMsecs;
            if (gcIntervals < 0) gcIntervals = Consts.Timeouts.DefaultDialogSessionGcIntervals;
            this.GcIntervals = gcIntervals;
        }
    }

    public class DialogSessionManager : AsyncService
    {
        public DialogSessionManagerOptions Options { get; }

        ImmutableDictionary<string, DialogSession> SessionList = ImmutableDictionary<string, DialogSession>.Empty.WithComparers(StrComparer.IgnoreCaseComparer);

        public DialogSessionManager(DialogSessionManagerOptions? options = null)
        {
            try
            {
                options ??= new DialogSessionManagerOptions();

                this.Options = options;
            }
            catch (Exception ex)
            {
                this._DisposeSafe(ex);
                throw;
            }
        }

        public DialogSession StartNewSession(DialogSessionOptions options)
        {
            this.CheckNotCanceled();

            DialogSession sess = new DialogSession(EnsureSpecial.Yes, this, options);

            if (ImmutableInterlocked.TryAdd(ref this.SessionList, sess.SessionId, sess) == false)
            {
                sess._DisposeSafe();
                throw new CoresLibException("ImmutableInterlocked.TryAdd failed. Unknown reason!");
            }

            try
            {
                sess.StartInternal();

                return sess;
            }
            catch
            {
                sess._DisposeSafe();
                ImmutableInterlocked.TryRemove(ref this.SessionList, sess.SessionId, out _);
                throw;
            }
        }

        public DialogSession? GetSessionById(string sessionId)
        {
            return this.SessionList.GetValueOrDefault(sessionId);
        }

        public void SetResponseData(string sessionId, string requestId, IDialogResponseData response)
        {
            var session = GetSessionById(sessionId);
            if (session != null) session.SetResponseData(requestId, response);
        }

        public void SetResponseException(string sessionId, string requestId, Exception exception)
        {
            var session = GetSessionById(sessionId);
            if (session != null) session.SetResponseException(requestId, exception);
        }

        public void SetResponseCancel(string sessionId, string requestId)
        {
            var session = GetSessionById(sessionId);
            if (session != null) session.SetResponseCancel(requestId);
        }

        public void SendHeartBeat(string sessionId, string requestId)
        {
            var session = GetSessionById(sessionId);
            if (session != null) session.SendHeartBeat(requestId);
        }

        long lastGcTick = 0;
        void Gc()
        {
            long now = TickNow;

            if (lastGcTick == 0 || now >= (lastGcTick + Options.GcIntervals))
            {
                lastGcTick = now;

                DeleteOldSessions();
            }
        }

        void DeleteOldSessions()
        {
            long now = TickNow;

            foreach (var session in this.SessionList.Values.ToList())
            {
                if (session.Expires != 0 && now >= session.Expires)
                {
                    session._DisposeSafe();
                    ImmutableInterlocked.TryRemove(ref this.SessionList, session.SessionId, out _);
                }
            }
        }

        protected override async Task CleanupImplAsync(Exception? ex)
        {
            try
            {
                foreach (var session in this.SessionList.Values.ToList())
                {
                    await session._DisposeSafeAsync();
                    ImmutableInterlocked.TryRemove(ref this.SessionList, session.SessionId, out _);
                }
            }
            finally
            {
                await base.CleanupImplAsync(ex);
            }
        }
    }
}

#endif

