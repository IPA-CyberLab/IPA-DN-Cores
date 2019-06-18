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
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using System.IO;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    static partial class CoresConfig
    {
        public static partial class TaskAsyncSettings
        {
            public static readonly Copenhagen<int> WaitTimeoutUntilPendingTaskFinish = 1 * 1000;
        }
    }

    class AsyncLock : IDisposable
    {
        public class LockHolder : IDisposable
        {
            AsyncLock Parent;
            public LockHolder(AsyncLock parent)
            {
                this.Parent = parent;
            }

            Once DisposeFlag;
            public void Dispose()
            {
                if (DisposeFlag.IsFirstCall())
                {
                    this.Parent.Unlock();
                }
            }
        }

        SemaphoreSlim Semaphone = new SemaphoreSlim(1, 1);
        Once DisposeFlag;

        public async Task<LockHolder> LockWithAwait(CancellationToken cancel = default)
        {
            await _LockAsync(cancel);

            return new LockHolder(this);
        }

        public LockHolder LockLegacy(CancellationToken cancel = default)
        {
            _Lock(cancel);
            return new LockHolder(this);
        }

        public Task _LockAsync(CancellationToken cancel = default) => Semaphone.WaitAsync(cancel);
        public void _Lock(CancellationToken cancel = default) => Semaphone.Wait(cancel);
        public void Unlock() => Semaphone.Release();

        public void Dispose()
        {
            if (DisposeFlag.IsFirstCall())
            {
                Semaphone._DisposeSafe();
                Semaphone = null;
            }
        }
    }

    static class AsyncPreciseDelay
    {
        static SortedList<long, AsyncManualResetEvent> WaitList = new SortedList<long, AsyncManualResetEvent>();

        static Stopwatch stopWatch;

        static Thread BackgroundThread;

        static AutoResetEvent ev = new AutoResetEvent(false);

        static List<Thread> WorkerThreadList = new List<Thread>();

        static Queue<AsyncManualResetEvent> QueuedManualResetEvents = new Queue<AsyncManualResetEvent>();

        static AutoResetEvent QueuedAutoResetEvents = new AutoResetEvent(false);

        static AsyncPreciseDelay()
        {
            stopWatch = new Stopwatch();
            stopWatch.Start();

            BackgroundThread = new Thread(BackgroundThreadProc);
            try
            {
                BackgroundThread.Priority = ThreadPriority.Highest;
            }
            catch { }
            BackgroundThread.IsBackground = true;
            BackgroundThread.Start();
        }

        static volatile int NumBusyWorkerThreads = 0;
        static volatile int NumWorkerThreads = 0;

        static void WorkerThreadProc()
        {
            while (true)
            {
                Interlocked.Increment(ref NumBusyWorkerThreads);
                while (true)
                {
                    AsyncManualResetEvent tcs = null;
                    lock (QueuedManualResetEvents)
                    {
                        if (QueuedManualResetEvents.Count != 0)
                        {
                            tcs = QueuedManualResetEvents.Dequeue();
                        }
                    }

                    if (tcs != null)
                    {
                        tcs.Set();
                    }
                    else
                    {
                        break;
                    }
                }
                Interlocked.Decrement(ref NumBusyWorkerThreads);

                QueuedAutoResetEvents.WaitOne();
            }
        }

        static void FireWorkerThread(AsyncManualResetEvent tc)
        {
            if (NumBusyWorkerThreads == NumWorkerThreads)
            {
                Interlocked.Increment(ref NumWorkerThreads);
                Thread t = new Thread(WorkerThreadProc);
                t.IsBackground = true;
                t.Start();
                //Console.WriteLine($"num_worker_threads = {num_worker_threads}");
            }

            lock (QueuedManualResetEvents)
            {
                QueuedManualResetEvents.Enqueue(tc);
            }
            QueuedAutoResetEvents.Set();
        }

        static void BackgroundThreadProc()
        {
            //Benchmark b1 = new Benchmark("num_fired");
            //Benchmark b2 = new Benchmark("num_loop");
            //Benchmark b3 = new Benchmark("num_removed");
            while (true)
            {
                long now = Tick;
                long nextWaitTarget = -1;

                List<AsyncManualResetEvent> fireEventList = new List<AsyncManualResetEvent>();

                lock (WaitList)
                {
                    List<long> pastTargetList = new List<long>();
                    List<long> futureTargetList = new List<long>();

                    foreach (long target in WaitList.Keys)
                    {
                        if (now >= target)
                        {
                            pastTargetList.Add(target);
                            nextWaitTarget = 0;
                        }
                        else
                        {
                            futureTargetList.Add(target);
                        }
                    }

                    foreach (long target in pastTargetList)
                    {
                        AsyncManualResetEvent e = WaitList[target];

                        WaitList.Remove(target);

                        fireEventList.Add(e);
                    }

                    if (nextWaitTarget == -1)
                    {
                        if (WaitList.Count >= 1)
                        {
                            nextWaitTarget = WaitList.Keys[0];
                        }
                    }
                }

                int n = 0;
                foreach (AsyncManualResetEvent tc in fireEventList)
                {
                    //tc.TrySetResult(0);
                    //Task.Factory.StartNew(() => tc.TrySetResult(0));
                    FireWorkerThread(tc);
                    n++;
                    //b1.IncrementMe++;
                }
                //n.Print();
                //b2.IncrementMe++;

                now = Tick;
                long nextWaitTick = (Math.Max(nextWaitTarget - now, 0));
                if (nextWaitTarget == -1)
                {
                    nextWaitTick = -1;
                }
                if (nextWaitTick >= 1 || nextWaitTick == -1)
                {
                    if (nextWaitTick == -1 || nextWaitTick >= 100)
                    {
                        nextWaitTick = 100;
                    }
                    ev.WaitOne((int)nextWaitTick);
                }
            }
        }

        public static long Tick
        {
            get
            {
                lock (stopWatch)
                {
                    return stopWatch.ElapsedMilliseconds + 1L;
                }
            }
        }

        public static Task PreciseDelay(int msec)
        {
            if (msec == Timeout.Infinite)
            {
                return Task.Delay(Timeout.Infinite);
            }
            if (msec <= 0)
            {
                return Task.CompletedTask;
            }

            long targetTime = Tick + (long)msec;

            AsyncManualResetEvent tc = null;

            bool setEvent = false;

            lock (WaitList)
            {
                long firstTargetBefore = -1;
                long firstTargetAfter = -1;

                if (WaitList.Count >= 1)
                {
                    firstTargetBefore = WaitList.Keys[0];
                }

                if (WaitList.ContainsKey(targetTime) == false)
                {
                    tc = new AsyncManualResetEvent();
                    WaitList.Add(targetTime, tc);
                }
                else
                {
                    tc = WaitList[targetTime];
                }

                firstTargetAfter = WaitList.Keys[0];

                if (firstTargetBefore != firstTargetAfter)
                {
                    setEvent = true;
                }
            }

            if (setEvent)
            {
                ev.Set();
            }

            return tc.WaitAsync();
        }
    }

    [Flags]
    enum ExceptionWhen
    {
        None = 0,
        TaskException = 1,
        CancelException = 2,
        TimeoutException = 4,
        All = 0x7FFFFFFF,
    }

    static partial class TaskUtil
    {


        static int NumPendingAsyncTasks = 0;

        public static int GetNumPendingAsyncTasks() => NumPendingAsyncTasks;

        public static async Task StartSyncTaskAsync(Action action, bool yieldOnStart = true, bool leakCheck = true)
        { if (leakCheck) Interlocked.Increment(ref NumPendingAsyncTasks); try { if (yieldOnStart) await Task.Yield(); await Task.Factory.StartNew(action)._LeakCheck(!leakCheck); } finally { if (leakCheck) Interlocked.Decrement(ref NumPendingAsyncTasks); } }
        public static async Task<T> StartSyncTaskAsync<T>(Func<T> action, bool yieldOnStart = true, bool leakCheck = true)
        { if (leakCheck) Interlocked.Increment(ref NumPendingAsyncTasks); try { if (yieldOnStart) await Task.Yield(); return await Task.Factory.StartNew(action)._LeakCheck(!leakCheck); } finally { if (leakCheck) Interlocked.Decrement(ref NumPendingAsyncTasks); } }

        public static async Task StartAsyncTaskAsync(Func<Task> action, bool yieldOnStart = true, bool leakCheck = true)
        { if (leakCheck) Interlocked.Increment(ref NumPendingAsyncTasks); try { if (yieldOnStart) await Task.Yield(); await action()._LeakCheck(!leakCheck); } finally { if (leakCheck) Interlocked.Decrement(ref NumPendingAsyncTasks); } }
        public static async Task<T> StartAsyncTaskAsync<T>(Func<Task<T>> action, bool yieldOnStart = true, bool leakCheck = true)
        { if (leakCheck) Interlocked.Increment(ref NumPendingAsyncTasks); try { if (yieldOnStart) await Task.Yield(); return await action()._LeakCheck(!leakCheck); } finally { if (leakCheck) Interlocked.Decrement(ref NumPendingAsyncTasks); } }



        public static async Task StartSyncTaskAsync(Action<object> action, object param, bool yieldOnStart = true, bool leakCheck = true)
        { if (leakCheck) Interlocked.Increment(ref NumPendingAsyncTasks); try { if (yieldOnStart) await Task.Yield(); await Task.Factory.StartNew(action, param)._LeakCheck(!leakCheck); } finally { if (leakCheck) Interlocked.Decrement(ref NumPendingAsyncTasks); } }
        public static async Task<T> StartSyncTaskAsync<T>(Func<object, T> action, object param, bool yieldOnStart = true, bool leakCheck = true)
        { if (leakCheck) Interlocked.Increment(ref NumPendingAsyncTasks); try { if (yieldOnStart) await Task.Yield(); return await Task.Factory.StartNew(action, param)._LeakCheck(!leakCheck); } finally { if (leakCheck) Interlocked.Decrement(ref NumPendingAsyncTasks); } }

        public static async Task StartAsyncTaskAsync(Func<object, Task> action, object param, bool yieldOnStart = true, bool leakCheck = true)
        { if (leakCheck) Interlocked.Increment(ref NumPendingAsyncTasks); try { if (yieldOnStart) await Task.Yield(); await action(param)._LeakCheck(!leakCheck); } finally { if (leakCheck) Interlocked.Decrement(ref NumPendingAsyncTasks); } }
        public static async Task<T> StartAsyncTaskAsync<T>(Func<object, Task<T>> action, object param, bool yieldOnStart = true, bool leakCheck = true)
        { if (leakCheck) Interlocked.Increment(ref NumPendingAsyncTasks); try { if (yieldOnStart) await Task.Yield(); return await action(param)._LeakCheck(!leakCheck); } finally { if (leakCheck) Interlocked.Decrement(ref NumPendingAsyncTasks); } }

        public static int WaitUntilAllPendingAsyncTasksFinish(int? timeout = null, CancellationToken cancel = default, int targetCount = 0)
        {
            int timeout2 = timeout ?? CoresConfig.TaskAsyncSettings.WaitTimeoutUntilPendingTaskFinish;

            WaitWithPoll(timeout2, 10, () => GetNumPendingAsyncTasks() <= targetCount, cancel);

            return GetNumPendingAsyncTasks();
        }

        public static bool WaitWithPoll(int timeout, int pollInterval, Func<bool> pollProc, CancellationToken cancel = default)
        {
            return WaitWithPoll(timeout, pollInterval, pollProc,
                interval => cancel.WaitHandle.WaitOne(interval));
        }

        public static bool WaitWithPoll(int timeout, int pollInterval, Func<bool> pollProc, Func<int, bool> waitProc)
        {
            long end_tick = Time.Tick64 + (long)timeout;

            if (timeout == Timeout.Infinite)
            {
                end_tick = long.MaxValue;
            }

            while (true)
            {
                long now = Time.Tick64;
                if (timeout != Timeout.Infinite)
                {
                    if (now >= end_tick)
                    {
                        return false;
                    }
                }

                long next_wait = (end_tick - now);
                next_wait = Math.Min(next_wait, (long)pollInterval);
                next_wait = Math.Max(next_wait, 1);

                if (pollProc != null)
                {
                    if (pollProc())
                    {
                        return true;
                    }
                }

                if (waitProc((int)next_wait))
                {
                    return true;
                }
            }
        }

        public static async Task<bool> AwaitWithPollAsync(int timeout, int pollInterval, Func<bool> pollProc, CancellationToken cancel = default)
        {
            long end_tick = Time.Tick64 + (long)timeout;

            if (timeout == Timeout.Infinite)
            {
                end_tick = long.MaxValue;
            }

            while (true)
            {
                long now = Time.Tick64;
                if (timeout != Timeout.Infinite)
                {
                    if (now >= end_tick)
                    {
                        return false;
                    }
                }

                long next_wait = (end_tick - now);
                next_wait = Math.Min(next_wait, (long)pollInterval);
                next_wait = Math.Max(next_wait, 1);

                if (pollProc != null)
                {
                    if (pollProc())
                    {
                        return true;
                    }
                }

                if (await cancel._WaitUntilCanceledAsync((int)next_wait))
                {
                    // Canceled
                    return false;
                }
            }
        }

        public static int GetMinTimeout(params int[] values)
        {
            long minValue = long.MaxValue;
            foreach (int v in values)
            {
                long vv;
                if (v < 0)
                    vv = long.MaxValue;
                else
                    vv = v;
                minValue = Math.Min(minValue, vv);
            }
            if (minValue == long.MaxValue)
                return Timeout.Infinite;
            else
                return (int)minValue;
        }

        public static async Task<TResult> DoAsyncWithTimeout<TResult>(Func<CancellationToken, Task<TResult>> mainProc, Action cancelProc = null, int timeout = Timeout.Infinite, CancellationToken cancel = default, params CancellationToken[] cancelTokens)
        {
            if (timeout < 0) timeout = Timeout.Infinite;
            if (timeout == 0) throw new TimeoutException("timeout == 0");

            if (timeout == Timeout.Infinite && cancel == null && (cancelTokens == null || cancelTokens.Length == 0))
                return await mainProc(default);

            List<Task> waitTasks = new List<Task>();
            List<IDisposable> disposes = new List<IDisposable>();
            Task timeoutTask = null;
            CancellationTokenSource timeoutCancelSources = null;
            CancellationTokenSource cancelLocal = new CancellationTokenSource();

            if (timeout != Timeout.Infinite)
            {
                timeoutCancelSources = new CancellationTokenSource();
                timeoutTask = Task.Delay(timeout, timeoutCancelSources.Token);
                disposes.Add(timeoutCancelSources);

                waitTasks.Add(timeoutTask);
            }

            try
            {
                if (cancel.CanBeCanceled)
                {
                    cancel.ThrowIfCancellationRequested();

                    Task t = WhenCanceled(cancel, out CancellationTokenRegistration reg);
                    disposes.Add(reg);
                    waitTasks.Add(t);
                }

                foreach (CancellationToken c in cancelTokens)
                {
                    if (c.CanBeCanceled)
                    {
                        c.ThrowIfCancellationRequested();

                        Task t = WhenCanceled(c, out CancellationTokenRegistration reg);
                        disposes.Add(reg);
                        waitTasks.Add(t);
                    }
                }

                Task<TResult> procTask = mainProc(cancelLocal.Token);

                if (procTask.IsCompleted)
                {
                    return procTask._GetResult();
                }

                waitTasks.Add(procTask);

                await Task.WhenAny(waitTasks.ToArray());

                foreach (CancellationToken c in cancelTokens)
                {
                    c.ThrowIfCancellationRequested();
                }

                cancel.ThrowIfCancellationRequested();

                if (procTask.IsCompleted)
                {
                    return procTask._GetResult();
                }

                throw new TimeoutException();
            }
            catch
            {
                try
                {
                    cancelLocal.Cancel();
                }
                catch { }
                try
                {
                    if (cancelProc != null) cancelProc();
                }
                catch
                {
                }
                throw;
            }
            finally
            {
                if (timeoutCancelSources != null)
                {
                    try
                    {
                        timeoutCancelSources.Cancel();
                    }
                    catch
                    {
                    }
                }
                foreach (IDisposable i in disposes)
                {
                    i._DisposeSafe();
                }
            }
        }

        static readonly Type TimerQueueType = Type.GetType("System.Threading.TimerQueue");
        static readonly FieldReaderWriter TimerQueueReaderWriter = new FieldReaderWriter(TimerQueueType, true);

        static readonly Type TimerQueueTimerType = Type.GetType("System.Threading.TimerQueueTimer");
        static readonly FieldInfo[] TimerQueueTimersFieldList = TimerQueueReaderWriter.MetadataTable.Values.OfType<FieldInfo>().Where(x => x.FieldType == TimerQueueTimerType).ToArray();

        static bool FailedFlag_GetScheduledTimersCount = false;

        static readonly FieldInfo TimerQueueTimer_mNext_FieldInfo = TimerQueueTimerType.GetField("m_next", BindingFlags.Instance | BindingFlags.NonPublic);

        public static int GetScheduledTimersCount()
        {
            if (FailedFlag_GetScheduledTimersCount) return -1;

            try
            {
                int num = 0;

                Array timerQueueInstanceList = (Array)TimerQueueType.GetProperty("Instances", BindingFlags.Static | BindingFlags.Public).GetValue(null);

                foreach (object timerQueueInstance in timerQueueInstanceList)
                {
                    foreach (FieldInfo timerField in TimerQueueTimersFieldList)
                    {
                        object timer = timerField.GetValue(timerQueueInstance);

                        if (timer != null)
                        {
                            lock (timerQueueInstance)
                            {
                                while (timer != null)
                                {
                                    timer = TimerQueueTimer_mNext_FieldInfo.GetValue(timer);
                                    num++;
                                }
                            }
                        }
                    }
                }

                return num;
            }
            catch
            {
                FailedFlag_GetScheduledTimersCount = true;
                return -1;
            }
        }

        public static int GetQueuedTasksCount()
        {
            try
            {
                Type t1 = Type.GetType("System.Threading.ThreadPoolGlobals");
                FieldInfo f1 = t1.GetField("workQueue");
                object o = f1.GetValue(null);
                Type t2 = o.GetType();
                FieldInfo f2 = t2.GetField("workItems", BindingFlags.Instance | BindingFlags.NonPublic);
                object o2 = f2.GetValue(o);
                Type t3 = o2.GetType();
                PropertyInfo f3 = t3.GetProperty("Count");
                int ret = (int)f3.GetValue(o2);

                return ret;
            }
            catch
            {
                return 0;
            }
        }

        public static Task PreciseDelay(int msec)
        {
            return AsyncPreciseDelay.PreciseDelay(msec);
        }

        public static async Task<ExceptionWhen> WaitObjectsAsync(Task[] tasks = null, CancellationToken[] cancels = null, AsyncAutoResetEvent[] events = null,
            AsyncManualResetEvent[] manualEvents = null, int timeout = Timeout.Infinite,
            ExceptionWhen exceptions = ExceptionWhen.None, LeakCounterKind leakCounterKind = LeakCounterKind.WaitObjectsAsync)
        {
            LeakChecker.IncrementLeakCounter(leakCounterKind);
            try
            {
                if (tasks == null) tasks = new Task[0];
                if (cancels == null) cancels = new CancellationToken[0];
                if (events == null) events = new AsyncAutoResetEvent[0];
                if (manualEvents == null) manualEvents = new AsyncManualResetEvent[0];
                if (timeout == 0)
                {
                    if (exceptions.Bit(ExceptionWhen.TimeoutException))
                        throw new TimeoutException();

                    return ExceptionWhen.TimeoutException;
                }

                if (exceptions.Bit(ExceptionWhen.TaskException))
                {
                    foreach (Task t in tasks)
                    {
                        if (t != null)
                        {
                            if (t.IsFaulted) t.Exception._ReThrow();
                            if (t.IsCanceled) throw new TaskCanceledException();
                        }
                    }
                }
                else
                {
                    foreach (Task t in tasks)
                    {
                        if (t != null)
                        {
                            if (t.IsFaulted) return ExceptionWhen.TaskException;
                            if (t.IsCanceled) return ExceptionWhen.TaskException;
                        }
                    }
                }

                if (exceptions.Bit(ExceptionWhen.CancelException))
                {
                    foreach (CancellationToken c in cancels)
                        c.ThrowIfCancellationRequested();
                }
                else
                {
                    foreach (CancellationToken c in cancels)
                        if (c.IsCancellationRequested)
                            return ExceptionWhen.CancelException;
                }

                List<Task> taskList = new List<Task>();
                List<CancellationTokenRegistration> regList = new List<CancellationTokenRegistration>();
                List<Action> undoList = new List<Action>();

                foreach (Task t in tasks)
                {
                    if (t != null)
                    {
                        taskList.Add(t);
                    }
                }

                foreach (CancellationToken c in cancels)
                {
                    taskList.Add(WhenCanceled(c, out CancellationTokenRegistration reg));
                    regList.Add(reg);
                }

                foreach (AsyncAutoResetEvent ev in events)
                {
                    if (ev != null)
                    {
                        taskList.Add(ev.WaitOneAsync(out Action undo));
                        undoList.Add(undo);
                    }
                }

                foreach (AsyncManualResetEvent ev in manualEvents)
                {
                    if (ev != null)
                    {
                        taskList.Add(ev.WaitAsync());
                    }
                }

                CancellationTokenSource delayCancel = new CancellationTokenSource();

                Task timeoutTask = null;
                bool timedOut = false;

                if (timeout >= 1)
                {
                    timeoutTask = Task.Delay(timeout, delayCancel.Token);
                    taskList.Add(timeoutTask);
                }

                try
                {
                    Task r = await Task.WhenAny(taskList.ToArray());
                    if (r == timeoutTask) timedOut = true;
                }
                catch { }

                foreach (Action undo in undoList)
                    undo();

                foreach (CancellationTokenRegistration reg in regList)
                {
                    reg.Dispose();
                }

                if (delayCancel != null)
                {
                    delayCancel.Cancel();
                    delayCancel.Dispose();
                }

                if (exceptions.Bit(ExceptionWhen.TimeoutException))
                {
                    if (timedOut)
                        throw new TimeoutException();
                }
                else
                {
                    if (timedOut)
                        return ExceptionWhen.TimeoutException;
                }

                if (exceptions.Bit(ExceptionWhen.TaskException))
                {
                    foreach (Task t in tasks)
                    {
                        if (t != null)
                        {
                            if (t.IsFaulted) t.Exception._ReThrow();
                            if (t.IsCanceled) throw new TaskCanceledException();
                        }
                    }
                }
                else
                {
                    foreach (Task t in tasks)
                    {
                        if (t != null)
                        {
                            if (t.IsFaulted) return ExceptionWhen.TaskException;
                            if (t.IsCanceled) return ExceptionWhen.TaskException;
                        }
                    }
                }

                if (exceptions.Bit(ExceptionWhen.CancelException))
                {
                    foreach (CancellationToken c in cancels)
                        c.ThrowIfCancellationRequested();
                }
                else
                {
                    foreach (CancellationToken c in cancels)
                        if (c.IsCancellationRequested)
                            return ExceptionWhen.CancelException;
                }

                return ExceptionWhen.None;
            }
            finally
            {
                LeakChecker.DecrementLeakCounter(leakCounterKind);
            }
        }

        public static Task WhenCanceled(CancellationToken cancel, out CancellationTokenRegistration registration)
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

            registration = cancel.Register(() =>
            {
                tcs.SetResult(true);
            });

            return tcs.Task;
        }

        public static TaskVm<TResult, TIn> GetCurrentTaskVm<TResult, TIn>()
        {
            TaskVm<TResult, TIn>.TaskVmSynchronizationContext ctx = (TaskVm<TResult, TIn>.TaskVmSynchronizationContext)SynchronizationContext.Current;

            return ctx.Vm;
        }

        public static async Task CancelAsync(CancellationTokenSource cts, bool throwOnFirstException = false)
        {
            await Task.Run(() => cts.Cancel(throwOnFirstException));
        }

        public static async Task TryCancelAsync(CancellationTokenSource cts)
        {
            await Task.Run(() => TryCancel(cts));
        }

        public static void TryCancel(CancellationTokenSource cts)
        {
            try
            {
                cts.Cancel();
            }
            catch
            {
            }
        }

        public static void TryCancelNoBlock(CancellationTokenSource cts)
            => cts._TryCancelAsync()._LaissezFaire(true);

        public static async Task TryWaitAsync(Task t, bool noDebugMessage = false)
        {
            if (t == null) return;
            try
            {
                await t;
            }
            catch (Exception ex)
            {
                if (noDebugMessage == false)
                    Dbg.WriteLine("Task exception: " + ex._GetSingleException().ToString());
            }
        }

        public static void TryWait(Task t, bool noDebugMessage = false)
        {
            if (t == null) return;
            try
            {
                t._GetResult();
            }
            catch (Exception ex)
            {
                if (noDebugMessage == false)
                    Dbg.WriteLine("Task exception: " + ex.ToString());
            }
        }

        public static CancellationToken CurrentTaskVmGracefulCancel => (CancellationToken)ThreadLocalStorage.CurrentThreadData["taskvm_current_graceful_cancel"];

        public static ValueHolder EnterCriticalCounter(RefInt counter)
        {
            counter.Increment();
            return new ValueHolder(() =>
            {
                counter.Decrement();
            },
            LeakCounterKind.EnterCriticalCounter);
        }

        public static async Task<int> DoMicroReadOperations(Func<Memory<byte>, long, CancellationToken, Task<int>> microWriteOperation, Memory<byte> data, int maxSingleSize, long currentPosition, CancellationToken cancel = default)
        {
            checked
            {
                if (data.Length == 0) return 0;
                maxSingleSize = Math.Max(maxSingleSize, 1);
                int totalSize = 0;

                while (data.Length >= 1)
                {
                    cancel.ThrowIfCancellationRequested();

                    int targetSize = Math.Min(maxSingleSize, data.Length);
                    Memory<byte> target = data.Slice(0, targetSize);

                    int r = await microWriteOperation(target, currentPosition + totalSize, cancel);

                    if (r < 0)
                        throw new ApplicationException($"microWriteOperation returned '{r}'.");

                    if (r == 0)
                        break;

                    data = data.Slice(r);

                    totalSize += r;
                }

                return totalSize;
            }
        }

        public static async Task DoMicroWriteOperations(Func<ReadOnlyMemory<byte>, long, CancellationToken, Task> microReadOperation, ReadOnlyMemory<byte> data, int maxSingleSize, long currentPosition, CancellationToken cancel = default)
        {
            checked
            {
                if (data.Length == 0) return;
                maxSingleSize = Math.Max(maxSingleSize, 1);
                int totalSize = 0;

                while (data.Length >= 1)
                {
                    cancel.ThrowIfCancellationRequested();

                    int targetSize = Math.Min(maxSingleSize, data.Length);
                    ReadOnlyMemory<byte> target = data.Slice(0, targetSize);
                    data = data.Slice(targetSize);

                    await microReadOperation(target, currentPosition + totalSize, cancel);

                    totalSize += targetSize;
                }
            }
        }

        class CombinedCancelContext
        {
            public CancellationTokenSource Cts = new CancellationTokenSource();
            public List<CancellationTokenRegistration> RegList = new List<CancellationTokenRegistration>();
        }

        public static ValueHolder<object> CreateCombinedCancellationToken(out CancellationToken combinedToken, params CancellationToken[] cancels)
        {
            if (cancels == null)
            {
                combinedToken = default;
                return new ValueHolder<object>(null, null, LeakCounterKind.CreateCombinedCancellationToken);
            }

            cancels = cancels.Where(x => x.CanBeCanceled).ToArray();

            if (cancels.Length == 0)
            {
                combinedToken = default;
                return new ValueHolder<object>(null, null, LeakCounterKind.CreateCombinedCancellationToken);
            }

            if (cancels.Length == 1)
            {
                combinedToken = cancels[0];
                return new ValueHolder<object>(null, null, LeakCounterKind.CreateCombinedCancellationToken);
            }

            foreach (CancellationToken c in cancels)
            {
                if (c.IsCancellationRequested)
                {
                    combinedToken = new CancellationToken(true);
                    return new ValueHolder<object>(null, null, LeakCounterKind.CreateCombinedCancellationToken);
                }
            }

            CombinedCancelContext ctx = new CombinedCancelContext();

            foreach (CancellationToken c in cancels)
            {
                if (c != default)
                {
                    var reg = c.Register(() =>
                    {
                        ctx.Cts.Cancel();
                    });

                    ctx.RegList.Add(reg);
                }
            }

            combinedToken = ctx.Cts.Token;

            return new ValueHolder<object>(obj =>
            {
                CombinedCancelContext x = (CombinedCancelContext)obj;

                foreach (var reg in x.RegList)
                {
                    reg._DisposeSafe();
                }
            },
            ctx, LeakCounterKind.CreateCombinedCancellationToken);
        }
    }

    class AsyncCallbackList
    {
        List<(Action<object> action, object state)> HardCallbackList = new List<(Action<object> action, object state)>();
        List<(Action<object> action, object state)> SoftCallbackList = new List<(Action<object> action, object state)>();

        public void AddHardCallback(Action<object> action, object state = null)
        {
            lock (HardCallbackList)
                HardCallbackList.Add((action, state));
        }

        public void AddSoftCallback(Action<object> action, object state = null)
        {
            lock (SoftCallbackList)
                SoftCallbackList.Add((action, state));
        }

        public void Invoke()
        {
            (Action<object> action, object state)[] arrayCopy;

            if (HardCallbackList.Count >= 1)
            {
                lock (HardCallbackList)
                {
                    arrayCopy = HardCallbackList.ToArray();
                }
                foreach (var v in arrayCopy)
                {
                    try
                    {
                        v.action(v.state);
                    }
                    catch { }
                }
            }

            if (SoftCallbackList.Count >= 1)
            {
                lock (SoftCallbackList)
                {
                    arrayCopy = SoftCallbackList.ToArray();
                }
                foreach (var v in arrayCopy)
                {
                    try
                    {
                        Task.Factory.StartNew(() =>
                        {
                            try
                            {
                                v.action(v.state);
                            }
                            catch { }
                        });
                    }
                    catch { }
                }
            }
        }
    }


    class AsyncAutoResetEvent
    {
        object lockobj = new object();
        List<AsyncManualResetEvent> eventQueue = new List<AsyncManualResetEvent>();
        bool isSet = false;

        public AsyncCallbackList CallbackList { get; } = new AsyncCallbackList();

        public async Task<bool> WaitOneAsync(int timeout, CancellationToken cancel = default, LeakCounterKind leakCounterKind = LeakCounterKind.WaitObjectsAsync)
        {
            try
            {
                var reason = await TaskUtil.WaitObjectsAsync(cancels: cancel._SingleArray(),
                    events: this._SingleArray(),
                    timeout: timeout,
                    exceptions: ExceptionWhen.None,
                    leakCounterKind: leakCounterKind);

                return (reason != ExceptionWhen.None);
            }
            catch
            {
                return false;
            }
        }

        public Task WaitOneAsync(out Action cancel)
        {
            lock (lockobj)
            {
                if (isSet)
                {
                    isSet = false;
                    cancel = () => { };
                    return Task.CompletedTask;
                }

                AsyncManualResetEvent e = new AsyncManualResetEvent();

                Task ret = e.WaitAsync();

                eventQueue.Add(e);

                cancel = () =>
                {
                    lock (lockobj)
                    {
                        eventQueue.Remove(e);
                    }
                };

                return ret;
            }
        }

        volatile int lazyQueuedSet = 0;

        public void SetLazyEnqueue() => Interlocked.Exchange(ref lazyQueuedSet, 1);

        public void SetIfLazyQueued(bool softly = false)
        {
            if (Interlocked.CompareExchange(ref lazyQueuedSet, 0, 1) == 1)
            {
                Set(softly);
            }
        }

        public void Set(bool softly = false)
        {
            AsyncManualResetEvent ev = null;
            lock (lockobj)
            {
                if (eventQueue.Count >= 1)
                {
                    ev = eventQueue[eventQueue.Count - 1];
                    eventQueue.Remove(ev);
                }

                if (ev == null)
                {
                    isSet = true;
                }
            }

            if (ev != null)
            {
                ev.Set(softly);
            }

            CallbackList.Invoke();
        }
    }

    class AsyncManualResetEvent
    {
        object lockobj = new object();
        volatile TaskCompletionSource<bool> tcs;
        bool isSet = false;

        public AsyncCallbackList CallbackList { get; } = new AsyncCallbackList();

        public AsyncManualResetEvent()
        {
            init();
        }

        void init()
        {
            this.tcs = new TaskCompletionSource<bool>();
        }

        public bool IsSet
        {
            get
            {
                lock (lockobj)
                {
                    return this.isSet;
                }
            }
        }

        public async Task<bool> WaitAsync(int timeout, CancellationToken cancel = default, LeakCounterKind leakCounterKind = LeakCounterKind.WaitObjectsAsync)
        {
            try
            {
                var reason = await TaskUtil.WaitObjectsAsync(cancels: cancel._SingleArray(),
                    manualEvents: this._SingleArray(),
                    timeout: timeout,
                    leakCounterKind: leakCounterKind);

                return (reason == ExceptionWhen.None);
            }
            catch
            {
                return false;
            }
        }
        public void Wait(int timeout, CancellationToken cancel = default, LeakCounterKind leakCounterKind = LeakCounterKind.WaitObjectsAsync)
            => WaitAsync(timeout, cancel, leakCounterKind)._GetResult();

        public Task WaitAsync()
        {
            lock (lockobj)
            {
                if (isSet)
                {
                    return Task.CompletedTask;
                }
                else
                {
                    return tcs.Task;
                }
            }
        }
        public void Wait() => WaitAsync()._GetResult();

        public void Set(bool softly = false)
        {
            if (softly)
            {
                if (isSet == false)
                {
                    Task.Factory.StartNew(() => Set(false));
                }
            }
            else
            {
                lock (lockobj)
                {
                    if (isSet == false)
                    {
                        isSet = true;
                        tcs.TrySetResult(true);

                        this.CallbackList.Invoke();
                    }
                }
            }
        }

        public void Reset()
        {
            lock (lockobj)
            {
                if (isSet)
                {
                    isSet = false;
                    init();
                }
            }
        }
    }

    interface IAsyncService : IDisposable
    {
        Task CleanupAsync(Exception ex = null);
        Task DisposeWithCleanupAsync(Exception ex = null);
        void Cancel(Exception ex = null);
        void Dispose(Exception ex = null);
    }

    abstract class AsyncService : IAsyncService
    {
        static long IdSeed = 0;


        public CancelWatcher CancelWatcher { get; }
        public CancellationToken GrandCancel { get => CancelWatcher.CancelToken; }

        readonly RefInt CriticalCounter = new RefInt();

        readonly List<Action> OnDisposeList = new List<Action>();
        readonly List<Action> OnCancelList = new List<Action>();

        readonly List<IAsyncService> DirectDisposeList = new List<IAsyncService>();
        readonly List<IAsyncService> IndirectDisposeList = new List<IAsyncService>();

        CriticalSection LockObj = new CriticalSection();

        public long Id { get; }
        public string ObjectName { get; }

        public AsyncService(CancellationToken cancel = default)
        {
            this.Id = Interlocked.Increment(ref IdSeed);
            this.ObjectName = this.ToString();

            this.CancelWatcher = new CancelWatcher(cancel);

            this.CancelWatcher.EventList.RegisterCallback(CanceledCallback);
        }

        public void AddOnCancelAction(Action proc)
        {
            if (proc != null)
            {
                lock (LockObj)
                    this.OnCancelList.Add(proc);
            }
        }

        public void AddOnDisposeAction(Action proc)
        {
            if (proc != null)
            {
                lock (LockObj)
                    this.OnDisposeList.Add(proc);
            }
        }

        public T AddDirectDisposeLink<T>(T service) where T : IAsyncService
        {
            if (service != default)
            {
                lock (DirectDisposeList)
                    DirectDisposeList.Add(service);
            }
            return service;
        }

        public T AddIndirectDisposeLink<T>(T service) where T : IAsyncService
        {
            if (service != default)
            {
                lock (IndirectDisposeList)
                    IndirectDisposeList.Add(service);
            }
            return service;
        }

        IAsyncService[] GetDirectDisposeLinkList()
        {
            lock (DirectDisposeList)
                return DirectDisposeList.ToArray();
        }

        IAsyncService[] GetIndirectDisposeLinkList()
        {
            lock (IndirectDisposeList)
                return IndirectDisposeList.ToArray();
        }

        void CancelDisposeLinks(Exception ex)
        {
            // Direct
            foreach (var obj in GetDirectDisposeLinkList())
                obj._CancelSafe(ex);

            // Indirect
            foreach (var obj in GetIndirectDisposeLinkList())
                TaskUtil.StartSyncTaskAsync(() => obj._CancelSafe(ex))._LaissezFaire();
        }

        async Task CleanupDisposeLinksAsync(Exception ex)
        {
            // Direct
            foreach (var obj in GetDirectDisposeLinkList())
                await obj._CleanupSafeAsync(ex);

            // Indirect
            foreach (var obj in GetIndirectDisposeLinkList())
                TaskUtil.StartAsyncTaskAsync(() => obj._CleanupSafeAsync(ex))._LaissezFaire();
        }

        void DisposeLinks(Exception ex)
        {
            // Direct
            foreach (var obj in GetDirectDisposeLinkList())
                obj._DisposeSafe(ex);

            // Indirect
            foreach (var obj in GetIndirectDisposeLinkList())
                TaskUtil.StartSyncTaskAsync(() => obj._DisposeSafe(ex))._LaissezFaire();
        }

        readonly AsyncLock CriticalProcessAsyncLock = new AsyncLock();
        protected async Task<TResult> RunCriticalProcessAsync<TResult>(bool obtainLock, CancellationToken cancel, Func<CancellationToken, Task<TResult>> func)
        {
            using (EnterCriticalCounter())
            {
                AsyncLock.LockHolder lockHolder = null;

                if (obtainLock)
                    lockHolder = await CriticalProcessAsyncLock.LockWithAwait(cancel);

                try
                {
                    using (CreatePerTaskCancellationToken(out CancellationToken opCancel, cancel))
                    {
                        opCancel.ThrowIfCancellationRequested();

                        return await func(opCancel);
                    }
                }
                finally
                {
                    if (lockHolder != null)
                        lockHolder.Dispose();
                }
            }
        }
        protected async Task RunCriticalProcessAsync(bool obtainLock, CancellationToken cancel, Func<CancellationToken, Task> func)
        {
            using (EnterCriticalCounter())
            {
                AsyncLock.LockHolder lockHolder = null;

                if (obtainLock)
                    lockHolder = await CriticalProcessAsyncLock.LockWithAwait(cancel);

                try
                {
                    using (CreatePerTaskCancellationToken(out CancellationToken opCancel, cancel))
                    {
                        opCancel.ThrowIfCancellationRequested();

                        await func(opCancel);
                    }
                }
                finally
                {
                    if (lockHolder != null)
                        lockHolder.Dispose();
                }
            }
        }

        protected Task<AsyncLock.LockHolder> CriticalProcessLockWithAwait(CancellationToken cancel = default)
            => CriticalProcessAsyncLock.LockWithAwait(cancel);

        protected ValueHolder<object> CreatePerTaskCancellationToken(out CancellationToken combinedToken, params CancellationToken[] cancels)
            => TaskUtil.CreateCombinedCancellationToken(out combinedToken, this.GrandCancel._SingleArray().Concat(cancels).ToArray());

        protected ValueHolder EnterCriticalCounter()
        {
            ValueHolder ret = TaskUtil.EnterCriticalCounter(CriticalCounter);
            try
            {
                CheckNotCanceled();

                return ret;
            }
            catch
            {
                ret._DisposeSafe();
                throw;
            }
        }

        bool IsCanceledPrivateFlag = false;
        public bool IsCanceled => (IsCanceledPrivateFlag || this.GrandCancel.IsCancellationRequested);

        protected void CheckNotCanceled()
        {
            if (this.IsCanceled)
                throw CancelReason;
        }

        Exception CancelReason = new OperationCanceledException();

        Once Canceled;
        public void Cancel(Exception ex = null)
        {
            if (Canceled.IsFirstCall() == false) return;

            if (ex != null)
                CancelReason = ex;

            this.CancelWatcher.Cancel();

            CancelInternalMain();
        }

        protected virtual void CancelImpl(Exception ex) { }
        protected virtual Task CleanupImplAsync(Exception ex) => Task.CompletedTask;
        protected virtual void DisposeImpl(Exception ex) { }

        void CanceledCallback(CancelWatcher caller, NonsenseEventType type, object userState)
            => CancelInternalMain();

        Once CanceledInternal;
        void CancelInternalMain()
        {
            IsCanceledPrivateFlag = true;

            if (CanceledInternal.IsFirstCall())
            {
                CancelDisposeLinks(CancelReason);

                try
                {
                    CancelImpl(CancelReason);
                }
                catch (Exception ex)
                {
                    Dbg.WriteLine("CancelInternal exception: " + ex._GetSingleException().ToString());
                }

                Action[] procList = null;

                lock (LockObj)
                {
                    procList = OnCancelList.ToArray();
                    OnCancelList.Clear();
                }

                foreach (Action proc in procList)
                {
                    try
                    {
                        proc();
                    }
                    catch { }
                }
            }
        }

        Once Cleanuped;
        public async Task CleanupAsync(Exception ex = null)
        {
            IsCanceledPrivateFlag = true;

            if (Cleanuped.IsFirstCall())
            {
                Cancel(ex);

                while (CriticalCounter.Value >= 1)
                    await Task.Delay(10);

                await CleanupDisposeLinksAsync(ex);

                await CleanupImplAsync(ex)._TryWaitAsync();
            }
        }

        Once Disposed;
        public void Dispose() => Dispose(null);
        public void Dispose(Exception ex)
        {
            IsCanceledPrivateFlag = true;

            if (Disposed.IsFirstCall())
            {
                CleanupAsync(ex)._TryGetResult();

                DisposeLinks(ex);

                try
                {
                    DisposeImpl(ex);
                }
                catch (Exception ex2)
                {
                    Dbg.WriteLine("Dispose exception: " + ex2._GetSingleException().ToString());
                }

                Action[] procList = null;

                lock (LockObj)
                {
                    procList = OnDisposeList.ToArray();
                    OnDisposeList.Clear();
                }

                foreach (Action proc in procList)
                {
                    try
                    {
                        proc();
                    }
                    catch { }
                }

                this.CancelWatcher._DisposeSafe();
            }
        }

        public async Task DisposeWithCleanupAsync(Exception ex = null)
        {
            this._CancelSafe(ex);
            await this._CleanupSafeAsync(ex);
            this._DisposeSafe(ex);
        }
    }

    abstract class AsyncServiceWithMainLoop : AsyncService
    {
        IHolder Leak = null;

        public AsyncServiceWithMainLoop(CancellationToken cancel = default) : base(cancel)
        {
        }

        Task MainLoopTask = null;

        public Task MainLoopToWaitComplete { get; private set; } = null;

        Once once;

        protected Task StartMainLoop(Func<Task> mainLoopProc, bool noLeakCheck = false)
            => StartMainLoop((c) => mainLoopProc(), noLeakCheck);

        protected Task StartMainLoop(Func<CancellationToken, Task> mainLoopProc, bool noLeakCheck = false)
        {
            if (once.IsFirstCall() == false)
                throw new Exception("StartMainLoop is already called.");

            MainLoopTask = TaskUtil.StartAsyncTaskAsync(o => mainLoopProc((CancellationToken)o), this.GrandCancel, leakCheck: !noLeakCheck);

            if (noLeakCheck == false)
                Leak = LeakChecker.Enter(LeakCounterKind.AsyncServiceWithMainLoop);

            this.MainLoopToWaitComplete = MainLoopTask;

            return MainLoopTask; // For reference. Not needed for most of all applications.
        }

        protected override async Task CleanupImplAsync(Exception ex)
        {
            try
            {
                try
                {
                    if (MainLoopTask != null)
                    {
                        try
                        {
                            await MainLoopTask;
                        }
                        finally
                        {
                            MainLoopTask = null;
                        }
                    }
                }
                catch (TaskCanceledException) { }
                catch (OperationCanceledException) { }
            }
            finally
            {
                Leak._DisposeSafe();
            }
        }
    }

    struct FastReadList<T>
    {
        static CriticalSection GlobalWriteLock = new CriticalSection();
        static volatile int IdSeed = 0;

        SortedDictionary<int, T> Hash;

        volatile T[] InternalFastList;

        public T[] GetListFast() => InternalFastList;

        public int Add(T value)
        {
            lock (GlobalWriteLock)
            {
                if (Hash == null)
                    Hash = new SortedDictionary<int, T>();

                int id = ++IdSeed;
                Hash.Add(id, value);
                Update();
                return id;
            }
        }

        public bool Delete(int id)
        {
            lock (GlobalWriteLock)
            {
                if (Hash == null)
                    return false;

                bool ret = Hash.Remove(id);
                if (ret)
                {
                    Update();
                }
                return ret;
            }
        }

        void Update()
        {
            if (Hash.Count == 0)
                InternalFastList = null;
            else
                InternalFastList = Hash.Values.ToArray();
        }
    }

    struct ValueHolder : IHolder
    {
        Action DisposeProc;
        IHolder Leak;
        LeakCounterKind LeakKind;

        static readonly bool FullStackTrace = CoresConfig.DebugSettings.LeakCheckerFullStackLog;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueHolder(Action disposeProc, LeakCounterKind leakCheckKind = LeakCounterKind.OthersCounter)
        {
            this.DisposeProc = disposeProc;

            this.LeakKind = leakCheckKind;

            if (FullStackTrace && leakCheckKind != LeakCounterKind.DoNotTrack)
            {
                Leak = LeakChecker.Enter(leakCheckKind);
            }
            else
            {
                LeakChecker.IncrementLeakCounter(this.LeakKind);
                Leak = null;
            }

            DisposeFlag = new Once();
        }

        Once DisposeFlag;
        public void Dispose()
        {
            if (DisposeFlag.IsFirstCall())
            {
                try
                {
                    if (DisposeProc != null)
                        DisposeProc();
                }
                finally
                {
                    if (Leak != null)
                        Leak._DisposeSafe();
                    else
                        LeakChecker.DecrementLeakCounter(this.LeakKind);
                }
            }
        }
    }

    class Holder : Holder<int>
    {
        public Holder(Action disposeProc, LeakCounterKind leakCheckKind = LeakCounterKind.OthersCounter)
            : base(_ => disposeProc(), default, leakCheckKind) { }
    }

    class Holder<T> : IHolder
    {
        public T Value { get; }
        Action<T> DisposeProc;
        IHolder Leak = null;
        LeakCounterKind LeakKind;

        static readonly bool FullStackTrace = CoresConfig.DebugSettings.LeakCheckerFullStackLog;

        public Holder(Action<T> disposeProc, T value = default(T), LeakCounterKind leakCheckKind = LeakCounterKind.OthersCounter)
        {
            this.Value = value;
            this.DisposeProc = disposeProc;

            this.LeakKind = leakCheckKind;

            if (FullStackTrace && leakCheckKind != LeakCounterKind.DoNotTrack)
            {
                Leak = LeakChecker.Enter(leakCheckKind);
            }
            else
            {
                LeakChecker.IncrementLeakCounter(this.LeakKind);
            }
        }

        Once DisposeFlag;
        public void Dispose() => Dispose(true);
        protected virtual void Dispose(bool disposing)
        {
            if (disposing && DisposeFlag.IsFirstCall())
            {
                try
                {
                    if (DisposeProc != null)
                        DisposeProc(Value);
                }
                finally
                {
                    if (Leak != null)
                        Leak._DisposeSafe();
                    else
                        LeakChecker.DecrementLeakCounter(this.LeakKind);
                }
            }
        }
    }

    struct ValueHolder<T> : IHolder
    {
        public T Value { get; }
        Action<T> DisposeProc;
        IHolder Leak;
        LeakCounterKind LeakKind;

        static readonly bool FullStackTrace = CoresConfig.DebugSettings.LeakCheckerFullStackLog;

        public ValueHolder(Action<T> disposeProc, T value = default(T), LeakCounterKind leakCheckKind = LeakCounterKind.OthersCounter)
        {
            this.Value = value;
            this.DisposeProc = disposeProc;

            this.LeakKind = leakCheckKind;

            if (FullStackTrace && leakCheckKind != LeakCounterKind.DoNotTrack)
            {
                Leak = LeakChecker.Enter(leakCheckKind);
            }
            else
            {
                LeakChecker.IncrementLeakCounter(this.LeakKind);
                Leak = null;
            }

            DisposeFlag = new Once();
        }

        Once DisposeFlag;
        public void Dispose()
        {
            if (DisposeFlag.IsFirstCall())
            {
                try
                {
                    if (DisposeProc != null)
                        DisposeProc(Value);
                }
                finally
                {
                    if (Leak != null)
                        Leak._DisposeSafe();
                    else
                        LeakChecker.DecrementLeakCounter(this.LeakKind);
                }
            }
        }
    }

    interface IHolder : IDisposable { }

    delegate void FastEventCallback<TCaller, TEventType>(TCaller caller, TEventType type, object userState);

    class FastEvent<TCaller, TEventType>
    {
        public FastEventCallback<TCaller, TEventType> Proc { get; }
        public object UserState { get; }

        public FastEvent(FastEventCallback<TCaller, TEventType> proc, object userState)
        {
            this.Proc = proc;
            this.UserState = userState;
        }

        public void CallSafe(TCaller buffer, TEventType type)
        {
            try
            {
                this.Proc(buffer, type, UserState);
            }
            catch { }
        }
    }

    [Flags]
    enum NonsenseEventType
    {
        Nonsense,
    }

    class FastEventListenerList<TCaller, TEventType>
    {
        FastReadList<FastEvent<TCaller, TEventType>> ListenerList;
        FastReadList<AsyncAutoResetEvent> AsyncEventList;

        public int RegisterCallback(FastEventCallback<TCaller, TEventType> proc, object userState = null)
        {
            if (proc == null) return 0;
            return ListenerList.Add(new FastEvent<TCaller, TEventType>(proc, userState));
        }

        public bool UnregisterCallback(int id)
        {
            return ListenerList.Delete(id);
        }

        public ValueHolder<int> RegisterCallbackWithUsing(FastEventCallback<TCaller, TEventType> proc, object userState = null)
            => new ValueHolder<int>(id => UnregisterCallback(id), RegisterCallback(proc, userState));

        public int RegisterAsyncEvent(AsyncAutoResetEvent ev)
        {
            if (ev == null) return 0;
            return AsyncEventList.Add(ev);
        }

        public bool UnregisterAsyncEvent(int id)
        {
            return AsyncEventList.Delete(id);
        }

        public ValueHolder<int> RegisterAsyncEventWithUsing(AsyncAutoResetEvent ev)
            => new ValueHolder<int>(id => UnregisterAsyncEvent(id), RegisterAsyncEvent(ev));

        public void Fire(TCaller caller, TEventType type)
        {
            var listenerList = ListenerList.GetListFast();
            if (listenerList != null)
                foreach (var e in listenerList)
                    e.CallSafe(caller, type);

            var asyncEventList = AsyncEventList.GetListFast();
            if (asyncEventList != null)
                foreach (var e in asyncEventList)
                    e.Set();
        }
    }


    delegate Task AsyncEventCallback<TCaller, TEventType>(TCaller caller, TEventType type, object userState);

    class AsyncEvent<TCaller, TEventType>
    {
        public AsyncEventCallback<TCaller, TEventType> Proc { get; }
        public object UserState { get; }

        public AsyncEvent(AsyncEventCallback<TCaller, TEventType> proc, object userState)
        {
            this.Proc = proc;
            this.UserState = userState;
        }

        public async Task CallSafeAsync(TCaller buffer, TEventType type)
        {
            try
            {
                await this.Proc(buffer, type, UserState);
            }
            catch { }
        }
    }

    class AsyncEventListenerList<TCaller, TEventType>
    {
        FastReadList<AsyncEvent<TCaller, TEventType>> ListenerList;
        FastReadList<AsyncAutoResetEvent> AsyncEventList;

        public int RegisterCallback(AsyncEventCallback<TCaller, TEventType> proc, object userState = null)
        {
            if (proc == null) return 0;
            return ListenerList.Add(new AsyncEvent<TCaller, TEventType>(proc, userState));
        }

        public bool UnregisterCallback(int id)
        {
            return ListenerList.Delete(id);
        }

        public ValueHolder<int> RegisterCallbackWithUsing(AsyncEventCallback<TCaller, TEventType> proc, object userState = null)
            => new ValueHolder<int>(id => UnregisterCallback(id), RegisterCallback(proc, userState));

        public int RegisterAsyncEvent(AsyncAutoResetEvent ev)
        {
            if (ev == null) return 0;
            return AsyncEventList.Add(ev);
        }

        public bool UnregisterAsyncEvent(int id)
        {
            return AsyncEventList.Delete(id);
        }

        public ValueHolder<int> RegisterAsyncEventWithUsing(AsyncAutoResetEvent ev)
            => new ValueHolder<int>(id => UnregisterAsyncEvent(id), RegisterAsyncEvent(ev));

        public async Task FireAsync(TCaller caller, TEventType type)
        {
            var listenerList = ListenerList.GetListFast();
            if (listenerList != null)
                foreach (var e in listenerList)
                    await e.CallSafeAsync(caller, type);

            var asyncEventList = AsyncEventList.GetListFast();
            if (asyncEventList != null)
                foreach (var e in asyncEventList)
                    e.Set();
        }
    }

    class CancelWatcher : IDisposable
    {
        readonly CancellationTokenSource GrandCancelTokenSource;
        readonly IDisposable LeakHolder;
        readonly IDisposable ObjectHolder;
        readonly IDisposable RegisterHolder;

        public FastEventListenerList<CancelWatcher, NonsenseEventType> EventList { get; } = new FastEventListenerList<CancelWatcher, NonsenseEventType>();

        public CancellationToken CancelToken { get; }

        public bool Canceled => this.CancelToken.IsCancellationRequested;

        public CancelWatcher(params CancellationToken[] cancels)
        {
            this.GrandCancelTokenSource = new CancellationTokenSource();

            CancellationToken[] tokens = this.GrandCancelTokenSource.Token._SingleArray().Concat(cancels).ToArray();

            this.ObjectHolder = TaskUtil.CreateCombinedCancellationToken(out CancellationToken cancelToken, tokens);
            this.RegisterHolder = cancelToken.Register(() => this.EventList.Fire(this, NonsenseEventType.Nonsense));
            this.CancelToken = cancelToken;

            this.LeakHolder = LeakChecker.Enter(LeakCounterKind.CancelWatcher);
        }

        Once CancelFlag;
        public void Cancel()
        {
            if (CancelFlag.IsFirstCall())
                this.GrandCancelTokenSource._TryCancel();
        }

        public void Dispose() => Dispose(true);
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;

            Cancel();

            this.RegisterHolder._DisposeSafe();
            this.ObjectHolder._DisposeSafe();
            this.LeakHolder._DisposeSafe();
        }
    }

    delegate bool TimeoutDetectorCallback(TimeoutDetector detector);

    class TimeoutDetector : AsyncService
    {
        Task MainTask;

        CriticalSection LockObj = new CriticalSection();

        public long Timeout { get; }

        long NextTimeout;

        AsyncAutoResetEvent ev = new AsyncAutoResetEvent();

        AsyncAutoResetEvent eventAuto;
        AsyncManualResetEvent eventManual;

        public object UserState { get; }

        public bool TimedOut { get; private set; } = false;

        CancelWatcher cancelWatcherToCancel;

        TimeoutDetectorCallback Callback;

        public TimeoutDetector(int timeout, CancelWatcher watcher = null, AsyncAutoResetEvent eventAuto = null, AsyncManualResetEvent eventManual = null,
            TimeoutDetectorCallback callback = null, object userState = null)
        {
            if (timeout == System.Threading.Timeout.Infinite || timeout == int.MaxValue)
            {
                return;
            }

            this.cancelWatcherToCancel = watcher;
            this.Timeout = timeout;
            this.eventAuto = eventAuto;
            this.eventManual = eventManual;
            this.Callback = callback;
            this.UserState = userState;

            NextTimeout = FastTick64.Now + this.Timeout;
            MainTask = TimeoutDetectorMainLoop();
        }

        public void Keep()
        {
            Interlocked.Exchange(ref this.NextTimeout, FastTick64.Now + this.Timeout);
        }

        async Task TimeoutDetectorMainLoop()
        {
            using (LeakChecker.Enter())
            {
                while (true)
                {
                    long nextTimeout = Interlocked.Read(ref this.NextTimeout);

                    long now = FastTick64.Now;

                    long remainTime = nextTimeout - now;

                    if (remainTime <= 0)
                    {
                        if (Callback != null && Callback(this))
                        {
                            Keep();
                        }
                        else
                        {
                            this.TimedOut = true;
                        }
                    }

                    if (this.TimedOut || this.GrandCancel.IsCancellationRequested)
                    {
                        if (this.cancelWatcherToCancel != null) this.cancelWatcherToCancel.Cancel();
                        if (this.eventAuto != null) this.eventAuto.Set();
                        if (this.eventManual != null) this.eventManual.Set();

                        return;
                    }
                    else
                    {
                        await TaskUtil.WaitObjectsAsync(
                            events: new AsyncAutoResetEvent[] { ev },
                            cancels: new CancellationToken[] { this.GrandCancel },
                            timeout: (int)remainTime);
                    }
                }
            }
        }

        protected override async Task CleanupImplAsync(Exception ex)
        {
            await MainTask;
        }
    }

    struct LazyCriticalSection
    {
        CriticalSection _LockObj;

        public void EnsureCreated()
        {
            Limbo.ObjectVolatileSlow = this.LockObj;
        }

        public CriticalSection LockObj
        {
            get
            {
                if (_LockObj == null)
                {
                    _LockObj = new CriticalSection();
                }
                return _LockObj;
            }
        }
    }

    class CriticalSection { }

    class WhenAll : IDisposable
    {
        public Task WaitMe { get; }
        public bool AllOk { get; private set; } = false;
        public bool HasError { get => !AllOk; }

        CancellationTokenSource CancelSource = new CancellationTokenSource();

        public WhenAll(IEnumerable<Task> tasks, bool throwException = false) : this(throwException, tasks.ToArray()) { }

        public WhenAll(Task t, bool throwException = false) : this(throwException, t._SingleArray()) { }

        public static Task Await(IEnumerable<Task> tasks, bool throwException = false)
            => Await(throwException, tasks.ToArray());

        public static Task Await(Task t, bool throwException = false)
            => Await(throwException, t._SingleArray());

        public static async Task Await(bool throwException = false, params Task[] tasks)
        {
            using (var w = new WhenAll(throwException, tasks))
                await w.WaitMe;
        }

        public WhenAll(bool throwException = false, params Task[] tasks)
        {
            this.WaitMe = WaitMain(tasks, throwException);
        }

        async Task WaitMain(Task[] tasks, bool throwException)
        {
            Task cancelTask = TaskUtil.WhenCanceled(CancelSource.Token, out CancellationTokenRegistration reg);
            using (reg)
            {
                bool allOk = true;
                foreach (Task t in tasks)
                {
                    if (t != null)
                    {
                        try
                        {
                            await Task.WhenAny(t, cancelTask);
                        }
                        catch { }

                        if (throwException)
                        {
                            if (t.IsFaulted)
                                t.Exception._ReThrow();
                            if (t.IsCanceled)
                                throw new TaskCanceledException();
                        }

                        if (t.IsCompletedSuccessfully == false)
                            allOk = false;

                        if (CancelSource.Token.IsCancellationRequested)
                        {
                            allOk = false;
                            return;
                        }
                    }
                }

                AllOk = allOk;
            }
        }

        public void Dispose() => Dispose(true);
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;
            CancelSource.Cancel();
        }
    }

    class GroupManager<TKey, TGroupContext> : IDisposable
    {
        public class GroupHandle : Holder<GroupInstance>
        {
            public TKey Key { get; }
            public TGroupContext Context { get; }
            public GroupInstance Instance { get; }

            internal GroupHandle(Action<GroupInstance> disposeProc, GroupInstance groupInstance, TKey key) : base(disposeProc, groupInstance)
            {
                this.Instance = groupInstance;
                this.Context = this.Instance.Context;
                this.Key = key;
            }
        }

        public class GroupInstance
        {
            public TKey Key;
            public TGroupContext Context;
            public int Num;
        }

        public delegate TGroupContext NewGroupContextCallback(TKey key, object userState);
        public delegate void DeleteGroupContextCallback(TKey key, TGroupContext groupContext, object userState);

        public object UserState { get; }

        NewGroupContextCallback NewGroupContextProc;
        DeleteGroupContextCallback DeleteGroupContextProc;

        Dictionary<TKey, GroupInstance> Hash = new Dictionary<TKey, GroupInstance>();

        CriticalSection LockObj = new CriticalSection();

        public GroupManager(NewGroupContextCallback onNewGroup, DeleteGroupContextCallback onDeleteGroup, object userState = null)
        {
            NewGroupContextProc = onNewGroup;
            DeleteGroupContextProc = onDeleteGroup;
            UserState = userState;
        }

        public GroupHandle Enter(TKey key)
        {
            lock (LockObj)
            {
                GroupInstance g = null;
                if (Hash.TryGetValue(key, out g) == false)
                {
                    var context = NewGroupContextProc(key, UserState);
                    g = new GroupInstance()
                    {
                        Key = key,
                        Context = context,
                        Num = 0,
                    };
                    Hash.Add(key, g);
                }

                Debug.Assert(g.Num >= 0);
                g.Num++;

                return new GroupHandle(x =>
                {
                    lock (LockObj)
                    {
                        x.Num--;
                        Debug.Assert(x.Num >= 0);

                        if (x.Num == 0)
                        {
                            Hash.Remove(x.Key);

                            DeleteGroupContextProc(x.Key, x.Context, this.UserState);
                        }
                    }
                }, g, key);
            }
        }

        public void Dispose() => Dispose(true);
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;
            lock (LockObj)
            {
                foreach (var v in Hash.Values)
                {
                    try
                    {
                        DeleteGroupContextProc(v.Key, v.Context, this.UserState);
                    }
                    catch { }
                }

                Hash.Clear();
            }
        }
    }

    class DelayAction : AsyncService
    {
        public Action<object> Action { get; }
        public object UserState { get; }
        public int Timeout { get; }

        Task MainTask;

        public bool IsCompleted = false;
        public bool IsCompletedSuccessfully = false;

        public Exception Exception { get; private set; } = null;

        public DelayAction(int timeout, Action<object> action, object userState = null)
        {
            if (timeout < 0 || timeout == int.MaxValue) timeout = System.Threading.Timeout.Infinite;

            this.Timeout = timeout;
            this.Action = action;
            this.UserState = userState;

            this.MainTask = MainTaskProc();
        }

        void InternalInvokeAction()
        {
            try
            {
                this.Action(this.UserState);

                IsCompleted = true;
                IsCompletedSuccessfully = true;
            }
            catch (Exception ex)
            {
                IsCompleted = true;
                IsCompletedSuccessfully = false;

                Exception = ex;
            }
        }

        async Task MainTaskProc()
        {
            using (LeakChecker.Enter())
            {
                try
                {
                    await TaskUtil.WaitObjectsAsync(timeout: this.Timeout,
                        cancels: this.GrandCancel._SingleArray(),
                        exceptions: ExceptionWhen.CancelException);

                    InternalInvokeAction();
                }
                catch
                {
                    IsCompleted = true;
                    IsCompletedSuccessfully = false;
                }
            }
        }

        protected override async Task CleanupImplAsync(Exception ex)
        {
            await this.MainTask;
        }
    }

    struct ValueOrClosed<T>
    {
        bool InternalIsOpen;
        public T Value;

        public bool IsOpen { get => InternalIsOpen; }
        public bool IsClosed { get => !InternalIsOpen; }

        public ValueOrClosed(T value)
        {
            InternalIsOpen = true;
            Value = value;
        }
    }

    class AsyncBulkReceiver<TUserReturnElement, TUserState>
    {
        public delegate Task<ValueOrClosed<TUserReturnElement>> AsyncReceiveCallback(TUserState state, CancellationToken cancel);

        public int DefaultMaxCount { get; } = 1024;

        AsyncReceiveCallback AsyncReceiveProc;

        public AsyncBulkReceiver(AsyncReceiveCallback asyncReceiveProc, int defaultMaxCount = 1024)
        {
            DefaultMaxCount = defaultMaxCount;
            AsyncReceiveProc = asyncReceiveProc;
        }

        Task<ValueOrClosed<TUserReturnElement>> pushedUserTask = null;

        public async Task<TUserReturnElement[]> Recv(CancellationToken cancel, TUserState state = default(TUserState), int? maxCount = null)
        {
            if (maxCount == null) maxCount = DefaultMaxCount;
            if (maxCount <= 0) maxCount = int.MaxValue;
            List<TUserReturnElement> ret = new List<TUserReturnElement>();

            while (true)
            {
                cancel.ThrowIfCancellationRequested();

                Task<ValueOrClosed<TUserReturnElement>> userTask;
                if (pushedUserTask != null)
                {
                    userTask = pushedUserTask;
                    pushedUserTask = null;
                }
                else
                {
                    userTask = AsyncReceiveProc(state, cancel);
                }
                if (userTask.IsCompleted == false)
                {
                    if (ret.Count >= 1)
                    {
                        pushedUserTask = userTask;
                        break;
                    }
                    else
                    {
                        await TaskUtil.WaitObjectsAsync(
                            tasks: new Task[] { userTask },
                            cancels: new CancellationToken[] { cancel });

                        cancel.ThrowIfCancellationRequested();

                        var result = userTask._GetResult();

                        if (result.IsOpen)
                        {
                            ret.Add(result.Value);
                        }
                        else
                        {
                            pushedUserTask = userTask;
                            break;
                        }
                    }
                }
                else
                {
                    var result = userTask._GetResult();

                    if (result.IsOpen)
                    {
                        ret.Add(result.Value);
                    }
                    else
                    {
                        break;
                    }
                }
                if (ret.Count >= maxCount) break;
            }

            if (ret.Count >= 1)
                return ret.ToArray();
            else
                return null; // Disconnected
        }
    }

    class SharedQueue<T>
    {
        class QueueBody
        {
            static long globalTimestamp;

            public QueueBody Next;

            public SortedList<long, T> _InternalList = new SortedList<long, T>();
            public readonly int MaxItems;

            public QueueBody(int maxItems)
            {
                if (maxItems <= 0) maxItems = int.MaxValue;
                MaxItems = maxItems;
            }

            public void Enqueue(T item, bool distinct = false)
            {
                lock (_InternalList)
                {
                    if (_InternalList.Count > MaxItems) return;
                    if (distinct && _InternalList.ContainsValue(item)) return;
                    long ts = Interlocked.Increment(ref globalTimestamp);
                    _InternalList.Add(ts, item);
                }
            }

            public T Dequeue()
            {
                lock (_InternalList)
                {
                    if (_InternalList.Count == 0) return default(T);
                    long ts = _InternalList.Keys[0];
                    T ret = _InternalList[ts];
                    _InternalList.Remove(ts);
                    return ret;
                }
            }

            public T[] ToArray()
            {
                lock (_InternalList)
                {
                    return _InternalList.Values.ToArray();
                }
            }

            public static void Merge(QueueBody q1, QueueBody q2)
            {
                if (q1 == q2) return;
                Debug.Assert(q1._InternalList != null);
                Debug.Assert(q2._InternalList != null);

                lock (q1._InternalList)
                {
                    lock (q2._InternalList)
                    {
                        QueueBody q3 = new QueueBody(Math.Max(q1.MaxItems, q2.MaxItems));
                        foreach (long ts in q1._InternalList.Keys)
                            q3._InternalList.Add(ts, q1._InternalList[ts]);
                        foreach (long ts in q2._InternalList.Keys)
                            q3._InternalList.Add(ts, q2._InternalList[ts]);
                        if (q3._InternalList.Count > q3.MaxItems)
                        {
                            int num = 0;
                            List<long> removeList = new List<long>();
                            foreach (long ts in q3._InternalList.Keys)
                            {
                                num++;
                                if (num > q3.MaxItems)
                                    removeList.Add(ts);
                            }
                            foreach (long ts in removeList)
                                q3._InternalList.Remove(ts);
                        }
                        q1._InternalList = null;
                        q2._InternalList = null;
                        Debug.Assert(q1.Next == null);
                        Debug.Assert(q2.Next == null);
                        q1.Next = q3;
                        q2.Next = q3;
                    }
                }
            }

            public QueueBody GetLast()
            {
                if (Next == null)
                    return this;
                else
                    return Next.GetLast();
            }
        }

        QueueBody First;

        public static readonly CriticalSection GlobalLock = new CriticalSection();

        public bool Distinct { get; }

        public SharedQueue(int maxItems = 0, bool distinct = false)
        {
            Distinct = distinct;
            First = new QueueBody(maxItems);
        }

        public void Encounter(SharedQueue<T> other)
        {
            if (this == other) return;

            lock (GlobalLock)
            {
                QueueBody last1 = this.First.GetLast();
                QueueBody last2 = other.First.GetLast();
                if (last1 == last2) return;

                QueueBody.Merge(last1, last2);
            }
        }

        public void Enqueue(T value)
        {
            lock (GlobalLock)
                this.First.GetLast().Enqueue(value, Distinct);
        }

        public T Dequeue()
        {
            lock (GlobalLock)
                return this.First.GetLast().Dequeue();
        }

        public T[] ToArray()
        {
            lock (GlobalLock)
                return this.First.GetLast().ToArray();
        }

        public int CountFast
        {
            get
            {
                var q = this.First.GetLast();
                var list = q._InternalList;
                if (list == null) return 0;
                lock (list)
                    return list.Count;
            }
        }

        public T[] ItemsReadOnly { get => ToArray(); }
    }

    class ExceptionQueue
    {
        public const int MaxItems = 128;
        SharedQueue<Exception> Queue = new SharedQueue<Exception>(MaxItems, true);
        public AsyncManualResetEvent WhenExceptionAdded { get; } = new AsyncManualResetEvent();

        HashSet<Task> WatchedTasks = new HashSet<Task>();

        public void Raise(Exception ex) => Add(ex, true);

        public void Add(Exception ex, bool raiseFirstException = false, bool doNotCheckWatchedTasks = false)
        {
            if (ex == null)
                ex = new Exception("null exception");

            if (doNotCheckWatchedTasks == false)
                CheckWatchedTasks();

            Exception throwingException = null;

            AggregateException aex = ex as AggregateException;

            if (aex != null)
            {
                var exp = aex.Flatten().InnerExceptions;

                lock (SharedQueue<Exception>.GlobalLock)
                {
                    foreach (var expi in exp)
                        Queue.Enqueue(expi);

                    if (raiseFirstException)
                        throwingException = Queue.ItemsReadOnly[0];
                }
            }
            else
            {
                lock (SharedQueue<Exception>.GlobalLock)
                {
                    Queue.Enqueue(ex);
                    if (raiseFirstException)
                        throwingException = Queue.ItemsReadOnly[0];
                }
            }

            WhenExceptionAdded.Set(true);

            if (throwingException != null)
                throwingException._ReThrow();
        }

        public void Encounter(ExceptionQueue other) => this.Queue.Encounter(other.Queue);

        public Exception[] GetExceptions()
        {
            CheckWatchedTasks();
            return this.Queue.ItemsReadOnly;
        }
        public Exception[] Exceptions => GetExceptions();
        public Exception FirstException => Exceptions.FirstOrDefault();

        public void ThrowFirstExceptionIfExists()
        {
            Exception ex = null;
            lock (SharedQueue<Exception>.GlobalLock)
            {
                if (HasError)
                    ex = FirstException;
            }

            if (ex != null)
                ex._ReThrow();
        }

        public bool HasError => Exceptions.Length != 0;
        public bool IsOk => !HasError;

        public bool RegisterWatchedTask(Task t)
        {
            if (t.IsCompleted)
            {
                if (t.IsFaulted)
                    Add(t.Exception);
                else if (t.IsCanceled)
                    Add(new TaskCanceledException());

                return true;
            }

            bool ret;

            lock (SharedQueue<Exception>.GlobalLock)
            {
                ret = WatchedTasks.Add(t);
            }

            t.ContinueWith(x =>
            {
                CheckWatchedTasks();
            });

            return ret;
        }

        public bool UnregisterWatchedTask(Task t)
        {
            lock (SharedQueue<Exception>.GlobalLock)
                return WatchedTasks.Remove(t);
        }

        void CheckWatchedTasks()
        {
            List<Task> o = new List<Task>();

            List<Exception> expList = new List<Exception>();

            lock (SharedQueue<Exception>.GlobalLock)
            {
                foreach (Task t in WatchedTasks)
                {
                    if (t.IsCompleted)
                    {
                        if (t.IsFaulted)
                            expList.Add(t.Exception);
                        else if (t.IsCanceled)
                            expList.Add(new TaskCanceledException());

                        o.Add(t);
                    }
                }

                foreach (Task t in o)
                    WatchedTasks.Remove(t);
            }

            foreach (Exception ex in expList)
                Add(ex, doNotCheckWatchedTasks: true);
        }
    }

    class HierarchyPosition : RefInt
    {
        public HierarchyPosition() : base(-1) { }
        public bool IsInstalled { get => (this.Value != -1); }
    }

    class SharedHierarchy<T>
    {
        public class HierarchyBodyItem : IComparable<HierarchyBodyItem>, IEquatable<HierarchyBodyItem>
        {
            public HierarchyPosition Position;
            public T Value;
            public HierarchyBodyItem(HierarchyPosition position, T value)
            {
                this.Position = position;
                this.Value = value;
            }

            public int CompareTo(HierarchyBodyItem other) => this.Position.CompareTo(other.Position);
            public bool Equals(HierarchyBodyItem other) => this.Position.Equals(other.Position);
            public override bool Equals(object obj) => (obj is HierarchyBodyItem) ? this.Position.Equals(obj as HierarchyBodyItem) : false;
            public override int GetHashCode() => this.Position.GetHashCode();
        }

        class HierarchyBody
        {
            public List<HierarchyBodyItem> _InternalList = new List<HierarchyBodyItem>();

            public HierarchyBody Next = null;

            public HierarchyBody() { }

            public HierarchyBodyItem[] ToArray()
            {
                lock (_InternalList)
                    return _InternalList.ToArray();
            }

            public HierarchyBodyItem Join(HierarchyPosition targetPosition, bool joinAsSuperior, T value, HierarchyPosition myPosition)
            {
                lock (_InternalList)
                {
                    if (targetPosition == null)
                    {
                        var me = new HierarchyBodyItem(myPosition, value);
                        var current = _InternalList;

                        current = current.Append(me).ToList();

                        int positionIncrement = 0;
                        current.ForEach(x => x.Position.Set(++positionIncrement));

                        _InternalList.Clear();
                        _InternalList.AddRange(current);

                        return me;
                    }
                    else
                    {
                        var current = _InternalList;

                        var inferiors = current.Where(x => joinAsSuperior ? (x.Position <= targetPosition) : (x.Position < targetPosition));
                        var me = new HierarchyBodyItem(myPosition, value);
                        var superiors = current.Where(x => joinAsSuperior ? (x.Position > targetPosition) : (x.Position >= targetPosition));

                        current = inferiors.Append(me).Concat(superiors).ToList();

                        int positionIncrement = 0;
                        current.ForEach(x => x.Position.Set(++positionIncrement));

                        _InternalList.Clear();
                        _InternalList.AddRange(current);

                        return me;
                    }
                }
            }

            public void Resign(HierarchyBodyItem me)
            {
                lock (_InternalList)
                {
                    var current = _InternalList;

                    if (current.Contains(me))
                    {
                        current.Remove(me);

                        int positionIncrement = 0;
                        current.ForEach(x => x.Position.Set(++positionIncrement));

                        Debug.Assert(me.Position.IsInstalled);

                        me.Position.Set(-1);
                    }
                    else
                    {
                        Debug.Assert(false);
                    }
                }
            }

            public static void Merge(HierarchyBody inferiors, HierarchyBody superiors)
            {
                if (inferiors == superiors) return;

                Debug.Assert(inferiors._InternalList != null);
                Debug.Assert(superiors._InternalList != null);

                lock (inferiors._InternalList)
                {
                    lock (superiors._InternalList)
                    {
                        HierarchyBody merged = new HierarchyBody();
                        merged._InternalList.AddRange(inferiors._InternalList.Concat(superiors._InternalList));

                        int positionIncrement = 0;
                        merged._InternalList.ForEach(x => x.Position.Set(++positionIncrement));

                        inferiors._InternalList = superiors._InternalList = null;
                        Debug.Assert(inferiors.Next == null); Debug.Assert(superiors.Next == null);
                        inferiors.Next = superiors.Next = merged;
                    }
                }
            }

            public HierarchyBody GetLast()
            {
                if (Next == null)
                    return this;
                else
                    return Next.GetLast();
            }
        }

        HierarchyBody First;

        public static readonly CriticalSection GlobalLock = new CriticalSection();

        public SharedHierarchy()
        {
            First = new HierarchyBody();
        }

        public void Encounter(SharedHierarchy<T> inferiors)
        {
            if (this == inferiors) return;

            lock (GlobalLock)
            {
                HierarchyBody inferiorsBody = inferiors.First.GetLast();
                HierarchyBody superiorsBody = this.First.GetLast();
                if (inferiorsBody == superiorsBody) return;

                HierarchyBody.Merge(inferiorsBody, superiorsBody);
            }
        }

        public HierarchyBodyItem Join(HierarchyPosition targetPosition, bool joinAsSuperior, T value, HierarchyPosition myPosition)
        {
            Debug.Assert(myPosition.IsInstalled == false);

            lock (GlobalLock)
                return this.First.GetLast().Join(targetPosition, joinAsSuperior, value, myPosition);
        }

        public void Resign(HierarchyBodyItem me)
        {
            Debug.Assert(me.Position.IsInstalled);

            lock (GlobalLock)
                this.First.GetLast().Resign(me);
        }

        public HierarchyBodyItem[] ToArrayWithPositions()
        {
            lock (GlobalLock)
                return this.First.GetLast().ToArray();
        }

        public HierarchyBodyItem[] ItemsWithPositionsReadOnly { get => ToArrayWithPositions(); }

        public T[] ToArray() => ToArrayWithPositions().Select(x => x.Value).ToArray();
        public T[] ItemsReadOnly { get => ToArray(); }
    }

    class LocalTimer
    {
        SortedSet<long> List = new SortedSet<long>();
        HashSet<long> Hash = new HashSet<long>();
        public long Now { get; private set; } = FastTick64.Now;
        public bool AutomaticUpdateNow { get; }

        public LocalTimer(bool automaticUpdateNow = true)
        {
            AutomaticUpdateNow = automaticUpdateNow;
        }

        public void UpdateNow() => Now = FastTick64.Now;
        public void UpdateNow(long nowTick) => Now = nowTick;

        public long AddTick(long tick)
        {
            if (Hash.Add(tick))
                List.Add(tick);

            return tick;
        }

        public long AddTimeout(int interval)
        {
            if (interval == Timeout.Infinite) return long.MaxValue;
            interval = Math.Max(interval, 0);
            if (AutomaticUpdateNow) UpdateNow();
            long v = Now + interval;
            AddTick(v);
            return v;
        }

        public int GetNextInterval()
        {
            int ret = Timeout.Infinite;
            if (AutomaticUpdateNow) UpdateNow();
            long now = Now;
            List<long> deleteList = null;

            foreach (long v in List)
            {
                if (now >= v)
                {
                    ret = 0;
                    if (deleteList == null) deleteList = new List<long>();
                    deleteList.Add(v);
                }
                else
                {
                    break;
                }
            }

            if (deleteList != null)
            {
                foreach (long v in deleteList)
                {
                    List.Remove(v);
                    Hash.Remove(v);
                }
            }

            if (ret == Timeout.Infinite)
            {
                if (List.Count >= 1)
                {
                    long v = List.First();
                    ret = (int)(v - now);
                    Debug.Assert(ret > 0);
                    if (ret <= 0) ret = 0;
                }
            }

            return ret;
        }
    }

    class AsyncLocalTimer
    {
        LocalTimer Timer = new LocalTimer(true);
        CriticalSection LockObj = new CriticalSection();
        public long Now => Timer.Now;

        AsyncAutoResetEvent TimerChangedEvent = new AsyncAutoResetEvent();

        public long AddTick(long tick)
        {
            long ret;

            lock (LockObj)
                ret = Timer.AddTick(tick);

            TimerChangedEvent.Set();

            return ret;
        }

        public long AddTimeout(int interval)
        {
            long ret;

            lock (LockObj)
                ret = Timer.AddTimeout(interval);

            TimerChangedEvent.Set();

            return ret;
        }

        public bool RepeatIntervalTimer(int interval, ref long nextFireTick, bool beginNow = true)
        {
            long now = Now;

            if (nextFireTick == 0 || now >= nextFireTick)
            {
                if (nextFireTick == 0)
                    nextFireTick = now;

                if (beginNow)
                    nextFireTick = now + interval;
                else
                    nextFireTick = Math.Max(nextFireTick + interval, now);

                if (interval < 0)
                    nextFireTick = long.MaxValue;

                if (nextFireTick != long.MaxValue)
                {
                    AddTick(nextFireTick);
                }

                return true;
            }

            return false;
        }

        public int GetNextInterval()
        {
            lock (LockObj)
                return Timer.GetNextInterval();
        }

        public async Task<bool> WaitUntilNextTickAsync(CancellationToken cancel = default)
        {
            while (true)
            {
                int nextInterval = GetNextInterval();

                if (nextInterval == 0)
                    return true;

                var reason = await TaskUtil.WaitObjectsAsync(cancels: cancel._SingleArray(), events: this.TimerChangedEvent._SingleArray(), timeout: nextInterval);

                if (reason == ExceptionWhen.CancelException)
                    return false;

                if (reason == ExceptionWhen.TimeoutException)
                    return true;
            }
        }
    }

    readonly struct BackgroundStateDataUpdatePolicy
    {
        public readonly int InitialPollingInterval;
        public readonly int MaxPollingInterval;
        public readonly int IdleTimeoutToFreeThreadInterval;

        public const int DefaultInitialPollingInterval = 1 * 1000;
        public const int DefaultMaxPollingInterval = 60 * 1000;
        public const int DefaultIdleTimeoutToFreeThreadInterval = 180 * 1000;

        public BackgroundStateDataUpdatePolicy(int initialPollingInterval = DefaultInitialPollingInterval,
            int maxPollingInterval = DefaultMaxPollingInterval,
            int timeoutToStopThread = DefaultIdleTimeoutToFreeThreadInterval)
        {
            InitialPollingInterval = initialPollingInterval;
            MaxPollingInterval = maxPollingInterval;
            IdleTimeoutToFreeThreadInterval = timeoutToStopThread;
        }

        public static BackgroundStateDataUpdatePolicy Default { get; }
            = new BackgroundStateDataUpdatePolicy(1 * 1000, 60 * 1000, 30 * 1000);

        public BackgroundStateDataUpdatePolicy SafeValue
        {
            get
            {
                return new BackgroundStateDataUpdatePolicy(
                    Math.Max(this.InitialPollingInterval, 1 * 100),
                    Math.Max(this.MaxPollingInterval, 1 * 500),
                    Math.Max(Math.Max(this.IdleTimeoutToFreeThreadInterval, 1 * 500), this.MaxPollingInterval)
                    );
            }
        }
    }

    abstract class BackgroundStateDataBase : IEquatable<BackgroundStateDataBase>
    {
        public DateTimeOffset TimeStamp { get; } = DateTimeOffset.Now;
        public long TickTimeStamp { get; } = FastTick64.Now;

        public abstract BackgroundStateDataUpdatePolicy DataUpdatePolicy { get; }

        public abstract bool Equals(BackgroundStateDataBase other);

        public abstract void RegisterSystemStateChangeNotificationCallbackOnlyOnceImpl(Action callMe);
    }

    static class BackgroundState<TData>
        where TData : BackgroundStateDataBase, new()
    {
        public struct CurrentData
        {
            public int Version;
            public TData Data;
        }

        public static CurrentData Current
        {
            get
            {
                CurrentData d = new CurrentData();
                d.Data = GetState();
                d.Version = InternalVersion;
                return d;
            }
        }

        static volatile TData CacheData = null;

        static volatile int NumRead = 0;

        static volatile int InternalVersion = 0;

        static bool CallbackIsRegistered = false;

        static CriticalSection LockObj = new CriticalSection();
        static Task task = null;
        static AsyncAutoResetEvent threadSignal = new AsyncAutoResetEvent();
        static bool callbackIsCalled = false;

        public static FastEventListenerList<TData, int> EventListener { get; } = new FastEventListenerList<TData, int>();

        static TData TryGetTData()
        {
            try
            {
                TData ret = new TData();

                if (CallbackIsRegistered == false)
                {
                    try
                    {
                        ret.RegisterSystemStateChangeNotificationCallbackOnlyOnceImpl(() =>
                        {
                            callbackIsCalled = true;
                            GetState();
                            threadSignal.Set();
                        });

                        CallbackIsRegistered = true;
                    }
                    catch { }
                }

                return ret;
            }
            catch
            {
                return null;
            }
        }

        static TData GetState()
        {
            NumRead++;

            if (CacheData != null)
            {
                if (task == null)
                {
                    EnsureStartTaskIfStopped(CacheData.DataUpdatePolicy);
                }

                return CacheData;
            }
            else
            {
                BackgroundStateDataUpdatePolicy updatePolicy = BackgroundStateDataUpdatePolicy.Default;
                TData data = TryGetTData();
                if (data != null)
                {
                    updatePolicy = data.DataUpdatePolicy;

                    bool inc = false;
                    if (CacheData == null)
                    {
                        inc = true;
                    }
                    else
                    {
                        if (CacheData.Equals(data) == false)
                            inc = true;
                    }
                    CacheData = data;

                    if (inc)
                    {
                        InternalVersion++;
                        EventListener.Fire(CacheData, 0);
                    }
                }

                EnsureStartTaskIfStopped(updatePolicy);

                return CacheData;
            }
        }

        static void EnsureStartTaskIfStopped(BackgroundStateDataUpdatePolicy updatePolicy)
        {
            lock (LockObj)
            {
                if (task == null)
                {
                    task = TaskUtil.StartAsyncTaskAsync(MaintainTaskProcAsync, updatePolicy, leakCheck: false);
                }
            }
        }

        static int nextInterval = 0;

        static async Task MaintainTaskProcAsync(object param)
        {
            BackgroundStateDataUpdatePolicy policy = (BackgroundStateDataUpdatePolicy)param;
            policy = policy.SafeValue;

            LocalTimer tm = new LocalTimer();

            if (nextInterval == 0)
            {
                nextInterval = policy.InitialPollingInterval;
            }

            long nextGetDataTick = tm.AddTimeout(nextInterval);

            long nextIdleDetectTick = tm.AddTimeout(policy.IdleTimeoutToFreeThreadInterval);

            int lastNumRead = NumRead;

            while (true)
            {
                if (FastTick64.Now >= nextGetDataTick || callbackIsCalled)
                {
                    TData data = TryGetTData();

                    nextInterval = Math.Min(nextInterval + policy.InitialPollingInterval, policy.MaxPollingInterval);
                    bool inc = false;

                    if (data != null)
                    {
                        if (data.Equals(CacheData) == false)
                        {
                            nextInterval = policy.InitialPollingInterval;
                            inc = true;
                        }
                        CacheData = data;
                    }
                    else
                    {
                        nextInterval = policy.InitialPollingInterval;
                    }

                    if (callbackIsCalled)
                    {
                        nextInterval = policy.InitialPollingInterval;
                    }

                    if (inc)
                    {
                        InternalVersion++;
                        EventListener.Fire(CacheData, 0);
                    }

                    nextGetDataTick = tm.AddTimeout(nextInterval);

                    callbackIsCalled = false;
                }

                if (FastTick64.Now >= nextIdleDetectTick)
                {
                    int numread = NumRead;
                    if (lastNumRead != numread)
                    {
                        lastNumRead = numread;
                        nextIdleDetectTick = tm.AddTimeout(policy.IdleTimeoutToFreeThreadInterval);
                    }
                    else
                    {
                        task = null;
                        return;
                    }
                }

                int i = tm.GetNextInterval();

                i = Math.Max(i, 100);

                await threadSignal.WaitOneAsync(i, leakCounterKind: LeakCounterKind.DoNotTrack);
            }
        }
    }
    class TaskVarObject
    {
        Dictionary<string, object> data = new Dictionary<string, object>();

        public object Get(Type type) => Get(type.AssemblyQualifiedName);
        public void Set(Type type, object obj) => Set(type.AssemblyQualifiedName, obj);

        public object Get(string key)
        {
            lock (data)
            {
                if (data.ContainsKey(key))
                    return data[key];
                else
                    return null;
            }
        }
        public void Set(string key, object obj)
        {
            lock (data)
            {
                if (obj != null)
                {
                    if (data.ContainsKey(key) == false)
                        data.Add(key, obj);
                    else
                        data[key] = obj;
                }
                else
                {
                    if (data.ContainsKey(key))
                        data.Remove(key);
                }
            }
        }
    }

    static class TaskVar<T>
    {
        public static T Value { get => TaskVar.Get<T>(); set => TaskVar.Set<T>(value); }
    }

    static class TaskVar
    {
        internal static AsyncLocal<TaskVarObject> AsyncLocalObj = new AsyncLocal<TaskVarObject>();

        public static T Get<T>()
        {
            var v = AsyncLocalObj.Value;
            if (v == null) return default(T);

            T ret = (T)v.Get(typeof(T));
            return ret;
        }
        public static void Set<T>(T obj)
        {
            if (AsyncLocalObj.Value == null) AsyncLocalObj.Value = new TaskVarObject();
            AsyncLocalObj.Value.Set(typeof(T), obj);
        }

        public static object Get(string name) => AsyncLocalObj.Value.Get(name);
        public static void Set(string name, object obj) => AsyncLocalObj.Value.Set(name, obj);
    }

    class AsyncOneShotTester : AsyncService
    {
        Task t = null;
        public AsyncOneShotTester(Func<Task> Proc)
        {
            t = Proc();
            t._TryWaitAsync(false)._LaissezFaire();
        }

        protected override async Task CleanupImplAsync(Exception ex)
        {
            await t;
        }
    }

    class RefCounterHolder : IDisposable
    {
        RefCounter Counter;
        public RefCounterHolder(RefCounter counter)
        {
            Counter = counter;

            Counter.InternalIncrement();
        }

        public void Dispose() => Dispose(true);
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;

            Counter.InternalDecrement();
        }
    }

    interface IAsyncClosable : IDisposable
    {
        Task CloseAsync();
        Exception LastError { get; }
    }

    enum RefCounterEventType
    {
        Increment,
        Decrement,
        AllReleased,
    }

    class RefCounter
    {
        int counter = 0;

        public int Counter => counter;
        public bool IsZero => (counter == 0);

        CriticalSection LockObj = new CriticalSection();

        public FastEventListenerList<RefCounter, RefCounterEventType> EventListener = new FastEventListenerList<RefCounter, RefCounterEventType>();

        internal void InternalIncrement()
        {
            lock (LockObj)
            {
                Interlocked.Increment(ref counter);
                EventListener.Fire(this, RefCounterEventType.Increment);
            }
        }

        internal void InternalDecrement()
        {
            lock (LockObj)
            {
                int r = Interlocked.Decrement(ref counter);
                Debug.Assert(r >= 0);
                EventListener.Fire(this, RefCounterEventType.Decrement);

                if (r == 0)
                    EventListener.Fire(this, RefCounterEventType.AllReleased);
            }
        }

        public RefCounterHolder AddRef() => new RefCounterHolder(this);
    }

    class RefCounterObjectHandle<T> : IDisposable
    {
        public T Object { get; }
        readonly RefCounterHolder CounterHolder;

        public RefCounterObjectHandle(T obj, Action onDispose)
        {
            this.Object = obj;
            RefCounter counter = new RefCounter();
            counter.EventListener.RegisterCallback((caller, eventType, state) =>
            {
                switch (eventType)
                {
                    case RefCounterEventType.AllReleased:
                        onDispose();
                        break;
                }
            });
            this.CounterHolder = counter.AddRef();
        }

        public RefCounterObjectHandle(RefCounter counter, T obj)
        {
            this.Object = obj;
            this.CounterHolder = counter.AddRef();
        }

        public void Dispose() => Dispose(true);
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;
            this.CounterHolder.Dispose();
        }
    }

    abstract class ObjectPoolBase<TObject, TParam> : IDisposable
        where TObject : IAsyncClosable
    {
        long AccessCounter = 0;

        public class ObjectEntry : IDisposable
        {
            public RefCounter Counter { get; } = new RefCounter();
            public TObject Object { get; }
            public string Key { get; }
            public long LastReleasedTick { get; private set; } = 0;
            public long LastAccessCounterValue = 0;

            readonly CriticalSection LockObj = new CriticalSection();

            readonly ObjectPoolBase<TObject, TParam> PoolBase;

            public ObjectEntry(ObjectPoolBase<TObject, TParam> poolBase, TObject targetObject, string key)
            {
                this.Object = targetObject;
                this.PoolBase = poolBase;
                this.Key = key;

                this.Counter.EventListener.RegisterCallback((caller, eventType, state) =>
                {
                    lock (LockObj)
                    {
                        switch (eventType)
                        {
                            case RefCounterEventType.AllReleased:
                                this.LastReleasedTick = Tick64.Now;
                                break;
                        }
                    }
                });
            }

            public RefCounterObjectHandle<TObject> TryGetIfAlive()
            {
                try
                {
                    long now = Tick64.Now;

                    lock (LockObj)
                    {
                        if (this.Object.LastError == null)
                        {
                            this.LastReleasedTick = 0;

                            return new RefCounterObjectHandle<TObject>(this.Counter, this.Object);
                        }
                    }
                }
                catch { }

                return null;
            }

            public Task CloseAsync() => this.Object.CloseAsync();

            public void Dispose() => Dispose(true);
            Once DisposeFlag;
            protected virtual void Dispose(bool disposing)
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;
                this.Object._DisposeSafe();
            }
        }

        readonly Dictionary<string, ObjectEntry> ObjectList;
        readonly AsyncLock LockAsyncObj = new AsyncLock();
        readonly CancellationTokenSource CancelSource = new CancellationTokenSource();

        public int LifeTime { get; }
        public int MaxObjects { get; }

        readonly AsyncAutoResetEvent FireGcNowEvent = new AsyncAutoResetEvent();

        Task GcTask;

        public ObjectPoolBase(int lifeTime, int maxObjects, IEqualityComparer<string> comparer)
        {
            this.LifeTime = Math.Max(lifeTime, 0);
            this.MaxObjects = maxObjects;

            ObjectList = new Dictionary<string, ObjectEntry>(comparer);
            GcTask = GcTaskProc();
        }

        protected abstract Task<TObject> OpenImplAsync(string key, TParam param, CancellationToken cancel);

        public async Task<RefCounterObjectHandle<TObject>> OpenOrGetAsync(string key, TParam param, CancellationToken cancel = default)
        {
            using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken cancelOp, CancelSource.Token, cancel))
            {
                int numRetry = 0;
                if (this.DisposeFlag.IsSet)
                    throw new ObjectDisposedException($"ObjectPoolBase<T>");

                L_RETRY:

                if (numRetry >= 1)
                    await Task.Yield();

                RefCounterObjectHandle<TObject> ret;

                using (await LockAsyncObj.LockWithAwait(cancelOp))
                {
                    cancelOp.ThrowIfCancellationRequested();

                    if (this.DisposeFlag.IsSet)
                        throw new ObjectDisposedException($"ObjectPoolBase<T>");

                    if (ObjectList.TryGetValue(key, out ObjectEntry entry) == false)
                    {
                        await Task.Yield();
                        TObject t = await OpenImplAsync(key, param, cancelOp);
                        entry = new ObjectEntry(this, t, key);
                        ObjectList.Add(key, entry);
                    }

                    entry.LastAccessCounterValue = Interlocked.Increment(ref this.AccessCounter);

                    ret = entry.TryGetIfAlive();
                    if (ret == null)
                    {
                        ObjectList.Remove(key);
                        await entry.CloseAsync()._TryWaitAsync();
                        numRetry++;
                        if (numRetry >= 100)
                            throw new ApplicationException("numRetry >= 100");
                        goto L_RETRY;
                    }
                }

                if (ObjectList.Count > this.MaxObjects)
                {
                    FireGcNowEvent.Set(true);
                }

                return ret;
            }
        }

        public async Task EnumAndCloseHandlesAsync(Func<string, TObject, bool> enumProc, Action afterClosedProc = null, Comparison<TObject> sortProc = null, CancellationToken cancel = default)
        {
            using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken cancelOp, CancelSource.Token, cancel))
            {
                using (await LockAsyncObj.LockWithAwait(cancelOp))
                {
                    cancelOp.ThrowIfCancellationRequested();

                    if (this.DisposeFlag.IsSet)
                        throw new ObjectDisposedException($"ObjectPoolBase<T>");

                    List<ObjectEntry> closeList = new List<ObjectEntry>();

                    foreach (ObjectEntry entry in this.ObjectList.Values)
                    {
                        cancelOp.ThrowIfCancellationRequested();

                        try
                        {
                            if (enumProc(entry.Key, entry.Object) == true)
                                closeList.Add(entry);
                        }
                        catch { }
                    }

                    if (sortProc != null)
                        closeList.Sort((x, y) => sortProc(x.Object, y.Object));

                    foreach (ObjectEntry entry in closeList)
                    {
                        cancelOp.ThrowIfCancellationRequested();

                        try
                        {
                            await entry.CloseAsync();
                        }
                        catch { }

                        ObjectList.Remove(entry.Key);
                    }

                    if (afterClosedProc != null)
                    {
                        afterClosedProc();
                    }
                }
            }
        }

        async Task GcTaskProc()
        {
            CancellationToken cancel = this.CancelSource.Token;

            while (true)
            {
                try
                {
                    cancel.ThrowIfCancellationRequested();

                    await TaskUtil.WaitObjectsAsync(cancels: cancel._SingleArray(), events: FireGcNowEvent._SingleArray(), timeout: Math.Max(LifeTime / 2, 100));

                    cancel.ThrowIfCancellationRequested();


                    long now = Tick64.Now;

                    using (await LockAsyncObj.LockWithAwait(cancel))
                    {
                        HashSet<ObjectEntry> gcTargetList = new HashSet<ObjectEntry>();

                        foreach (ObjectEntry entry in this.ObjectList.Values)
                        {
                            if (entry.LastReleasedTick != 0)
                            {
                                if ((entry.LastReleasedTick + LifeTime) < now)
                                {
                                    gcTargetList.Add(entry);
                                }
                                else if (entry.Object.LastError != null)
                                {
                                    gcTargetList.Add(entry);
                                }
                            }
                        }

                        int numRemains = this.ObjectList.Count - gcTargetList.Count;
                        if (numRemains > this.MaxObjects)
                        {
                            int numToDelete = numRemains - this.MaxObjects;

                            Con.WriteLine(numToDelete);

                            this.ObjectList.Values.Where(x => gcTargetList.Contains(x) == false).OrderBy(x => x.LastAccessCounterValue).Take(numToDelete)._DoForEach(x => gcTargetList.Add(x));
                        }

                        foreach (ObjectEntry deleteTargetEntry in gcTargetList)
                        {
                            cancel.ThrowIfCancellationRequested();

                            this.ObjectList.Remove(deleteTargetEntry.Key);

                            try
                            {
                                await deleteTargetEntry.CloseAsync();
                            }
                            catch (Exception ex)
                            {
                                Con.WriteDebug(ex.ToString());
                            }
                        }
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException || ex is TaskCanceledException))
                {
                    Con.WriteDebug("GcTaskProc: " + ex.ToString());
                    await Task.Delay(10);
                }
            }
        }

        public void Dispose() => Dispose(true);
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;

            this.CancelSource.Cancel();

            GcTask._TryGetResult(true);

            using (LockAsyncObj.LockLegacy())
            {
                foreach (ObjectEntry entry in this.ObjectList.Values)
                {
                    entry._DisposeSafe();
                }
                this.ObjectList.Clear();
            }
        }
    }

    class SingleThreadWorker : IDisposable
    {
        class Job
        {
            public Func<object, object> Proc;
            public object Param;
            public AsyncManualResetEvent Completed = new AsyncManualResetEvent();
            public Exception Error = null;
            public object Result = null;

            public Job(Func<object, object> proc, object param)
            {
                this.Proc = proc;
                this.Param = param;
            }
        }

        readonly ThreadObj Thread;
        readonly CriticalSection LockObj = new CriticalSection();
        readonly Queue<Job> Queue = new Queue<Job>();
        readonly Event QueueInsertedEvent = new Event(false);
        readonly CancellationTokenSource CancalSource = new CancellationTokenSource();

        readonly IHolder Leak;

        public bool SelfThread { get; }
        public int SelfThreadId { get; }

        public SingleThreadWorker(string name = "", bool selfThread = false)
        {
            this.SelfThread = selfThread;

            if (this.SelfThread == false)
            {
                Thread = new ThreadObj(ThreadProc, name: name, isBackground: true);
            }
            else
            {
                if (System.Threading.Thread.CurrentThread.IsThreadPoolThread)
                {
                    throw new ArgumentException("selfThread == true while CurrentThread.IsThreadPoolThread");
                }

                this.SelfThreadId = Environment.CurrentManagedThreadId;
            }

            Leak = LeakChecker.Enter(LeakCounterKind.SingleThreadWorker);
        }

        public async Task<TResult> ExecAsync<TResult, TParam>(Func<TParam, TResult> proc, TParam param, int timeout = Timeout.Infinite, CancellationToken cancel = default)
        {
            if (DisposeFlag.IsSet) throw new ObjectDisposedException("SingleWorkerThread");

            if (this.SelfThread)
            {
                if (timeout != Timeout.Infinite) throw new ArgumentException("this.SelfThread == true && timeout != Timeout.Infinite");
                if (cancel.CanBeCanceled) throw new ArgumentException("this.SelfThread == true && cancel.CanBeCanceled == true");
                if (this.SelfThreadId != Environment.CurrentManagedThreadId) throw new ApplicationException("this.SelfThreadId != Environment.CurrentManagedThreadId");

                return proc(param);
            }

            Job job = new Job((p) => proc((TParam)p), param);

            lock (this.LockObj)
            {
                this.Queue.Enqueue(job);
            }

            this.QueueInsertedEvent.Set();

            await TaskUtil.WaitObjectsAsync(cancels: new CancellationToken[] { cancel, this.CancalSource.Token },
                    manualEvents: job.Completed._SingleArray(),
                    timeout: timeout, exceptions: ExceptionWhen.All);

            if (job.Error != null) throw job.Error;

            return (TResult)job.Result;
        }

        public Task ExecAsync<TParam>(Action<TParam> proc, TParam param, int timeout = Timeout.Infinite, CancellationToken cancel = default)
            => ExecAsync<int, TParam>(p => { proc(p); return 0; }, param, timeout, cancel);

        void ThreadProc(object param)
        {
            while (CancalSource.IsCancellationRequested == false)
            {
                if (CancalSource.IsCancellationRequested)
                {
                    return;
                }

                Job nextJob = null;
                bool wait = false;

                lock (this.LockObj)
                {
                    if (this.Queue.TryDequeue(out nextJob) == false)
                    {
                        wait = true;
                    }
                }

                if (nextJob != null)
                {
                    try
                    {
                        object ret = nextJob.Proc(nextJob.Param);

                        nextJob.Result = ret;
                    }
                    catch (Exception ex)
                    {
                        nextJob.Error = ex;
                    }

                    nextJob.Completed.Set(true);
                }

                if (wait)
                {
                    QueueInsertedEvent.Wait();
                }
            }
        }

        public void Dispose() => Dispose(true);
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;

            if (this.SelfThread == false)
            {
                CancalSource.Cancel();
                this.QueueInsertedEvent.Set();
                this.Thread.WaitForEnd();
            }

            Leak._DisposeSafe();
        }
    }
}
