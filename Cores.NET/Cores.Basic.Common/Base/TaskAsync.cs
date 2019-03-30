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

using IPA.Cores.Helper.Basic;
using System.Runtime.CompilerServices;
using System.IO;

namespace IPA.Cores.Basic
{
    class AsyncLock : IDisposable
    {
        public class LockHolder : IDisposable
        {
            AsyncLock Parent;
            internal LockHolder(AsyncLock parent)
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

        public async Task<LockHolder> LockWithAwait()
        {
            await _LockAsync();

            return new LockHolder(this);
        }

        public LockHolder LockLegacy()
        {
            _Lock();
            return new LockHolder(this);
        }

        public Task _LockAsync() => Semaphone.WaitAsync();
        public void _Lock() => Semaphone.Wait();
        public void Unlock() => Semaphone.Release();

        public void Dispose()
        {
            if (DisposeFlag.IsFirstCall())
            {
                Semaphone.DisposeSafe();
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
        static GlobalInitializer gInit = new GlobalInitializer();

        public static async Task StartSyncTaskAsync(Action action, bool yieldOnStart = true, bool leakCheck = false)
        { if (yieldOnStart) await Task.Yield(); await Task.Factory.StartNew(action).LeakCheck(!leakCheck); }
        public static async Task<T> StartSyncTaskAsync<T>(Func<T> action, bool yieldOnStart = true, bool leakCheck = false)
        { if (yieldOnStart) await Task.Yield(); return await Task.Factory.StartNew(action).LeakCheck(!leakCheck); }

        public static async Task StartAsyncTaskAsync(Func<Task> action, bool yieldOnStart = true, bool leakCheck = false)
        { if (yieldOnStart) await Task.Yield(); await action().LeakCheck(!leakCheck); }
        public static async Task<T> StartAsyncTaskAsync<T>(Func<Task<T>> action, bool yieldOnStart = true, bool leakCheck = false)
        { if (yieldOnStart) await Task.Yield(); return await action().LeakCheck(!leakCheck); }

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
                    return procTask.Result;
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
                    return procTask.Result;
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
                    i.DisposeSafe();
                }
            }
        }

        public static int GetScheduledTimersCount()
        {
            try
            {
                int num = 0;
                object instance = Type.GetType("System.Threading.TimerQueue").GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).GetValue(null);

                lock (instance)
                {
                    object timer = instance.GetType().GetField("m_timers", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
                    Type timerType = timer.GetType();
                    FieldInfo nextField = timerType.GetField("m_next", BindingFlags.Instance | BindingFlags.NonPublic);

                    while (timer != null)
                    {
                        timer = nextField.GetValue(timer);
                        num++;
                    }

                    Util.DoNothing();
                }

                return num;
            }
            catch
            {
                return 0;
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
            ExceptionWhen exceptions = ExceptionWhen.None)
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
                        if (t.IsFaulted) t.Exception.ReThrow();
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
                        if (t.IsFaulted) t.Exception.ReThrow();
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
        {
            BackgroundWorker.Run(arg =>
            {
                cts.TryCancel();
            }, null);
        }

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
                    Dbg.WriteLine("Task exception: " + ex.GetSingleException().ToString());
            }
        }

        public static void TryWait(Task t, bool noDebugMessage = false)
        {
            if (t == null) return;
            try
            {
                t.Wait();
            }
            catch (Exception ex)
            {
                if (noDebugMessage == false)
                    Dbg.WriteLine("Task exception: " + ex.ToString());
            }
        }

        public static CancellationToken CurrentTaskVmGracefulCancel => (CancellationToken)ThreadData.CurrentThreadData["taskvm_current_graceful_cancel"];

        public static Holder<RefInt> EnterCriticalCounter(RefInt counter)
        {
            counter.Increment();
            return new Holder<RefInt>(c =>
            {
                counter.Decrement();
            },
            counter);
        }

        public static async Task<int> DoMicroReadOperations(Func<Memory<byte>, long, bool, CancellationToken, Task<int>> microWriteOperation, Memory<byte> data, int maxSingleSize, long currentPosition, bool seekRequested, CancellationToken cancel = default)
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

                    int r = await microWriteOperation(target, currentPosition + totalSize, seekRequested, cancel);
                    seekRequested = false;

                    if (r <= 0)
                        break;

                    data = data.Slice(r);

                    totalSize += r;
                }

                return totalSize;
            }
        }

        public static async Task DoMicroWriteOperations(Func<ReadOnlyMemory<byte>, long, bool, CancellationToken, Task> microReadOperation, ReadOnlyMemory<byte> data, int maxSingleSize, long currentPosition, bool seekRequested, CancellationToken cancel = default)
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

                    await microReadOperation(target, currentPosition + totalSize, seekRequested, cancel);

                    totalSize += targetSize;

                    seekRequested = false;
                }
            }
        }

        class CombinedCancelContext
        {
            public CancellationTokenSource Cts = new CancellationTokenSource();
            public List<CancellationTokenRegistration> RegList = new List<CancellationTokenRegistration>();
        }

        public static Holder<object> CreateCombinedCancellationToken(out CancellationToken combinedToken, params CancellationToken[] cancels)
        {
            CombinedCancelContext ctx = new CombinedCancelContext();

            if (cancels != null)
            {
                foreach (CancellationToken c in cancels)
                {
                    if (c.IsCancellationRequested)
                    {
                        combinedToken = new CancellationToken(true);
                        return new Holder<object>(null);
                    }
                }

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
            }

            combinedToken = ctx.Cts.Token;

            return new Holder<object>(obj =>
            {
                CombinedCancelContext x = (CombinedCancelContext)obj;

                foreach (var reg in x.RegList)
                {
                    reg.DisposeSafe();
                }

                x.Cts.DisposeSafe();
            },
            ctx);
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

        public async Task<bool> WaitOneAsync(int timeout, CancellationToken cancel = default)
        {
            try
            {
                var reason = await TaskUtil.WaitObjectsAsync(cancels: cancel.SingleArray(),
                    events: this.SingleArray(),
                    timeout: timeout,
                    exceptions: ExceptionWhen.None);

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


        public void SetLazy() => Interlocked.Exchange(ref lazyQueuedSet, 1);


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

        public async Task<bool> WaitAsync(int timeout, CancellationToken cancel = default)
        {
            try
            {
                var reason = await TaskUtil.WaitObjectsAsync(cancels: cancel.SingleArray(),
                    manualEvents: this.SingleArray(),
                    timeout: timeout);

                return (reason != ExceptionWhen.None);
            }
            catch
            {
                return false;
            }
        }

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

    static class AsyncCleanuperLadyHelper
    {
        public static T AddToLady<T>(this T obj, AsyncCleanuperLady lady) where T : IDisposable
        {
            if (obj == null || lady == null) return obj;
            lady.Add(obj);
            return obj;
        }

        public static T AddToLady<T>(this T obj, AsyncCleanupable cleanupable) where T : IDisposable
        {
            if (obj == null || cleanupable == null) return obj;
            cleanupable.Lady.Add(obj);
            return obj;
        }
    }

    class AsyncCleanuperLady
    {
        static volatile int IdSeed;

        public int Id { get; }

        public AsyncCleanuperLady()
        {
            Id = Interlocked.Increment(ref IdSeed);
        }

        Queue<AsyncCleanuper> CleanuperQueue = new Queue<AsyncCleanuper>();
        Queue<Task> TaskQueue = new Queue<Task>();
        Queue<IDisposable> DisposableQueue = new Queue<IDisposable>();

        void InternalCollectMain(object obj)
        {
            IAsyncCleanupable cleanupable = obj as IAsyncCleanupable;
            AsyncCleanuper cleanuper = obj as AsyncCleanuper;
            Task task = obj as Task;
            IDisposable disposable = obj as IDisposable;

            lock (LockObj)
            {
                if (cleanupable != null) CleanuperQueue.Enqueue(cleanupable.AsyncCleanuper);
                if (cleanuper != null) CleanuperQueue.Enqueue(cleanuper);
                if (task != null) TaskQueue.Enqueue(task);
                if (disposable != null) DisposableQueue.Enqueue(disposable);
            }

            if (IsDisposed)
                DisposeAllSafe();
        }

        public void Add(IAsyncCleanupable cleanupable) => InternalCollectMain(cleanupable);
        public void Add(AsyncCleanuper cleanuper) => InternalCollectMain(cleanuper);
        public void Add(Task task) => InternalCollectMain(task);
        public void Add(IDisposable disposable) => InternalCollectMain(disposable);

        public void MergeFrom(AsyncCleanuperLady fromLady)
        {
            lock (LockObj)
            {
                lock (fromLady.LockObj)
                {
                    fromLady.CleanuperQueue.ToList().ForEach(x => this.CleanuperQueue.Enqueue(x));
                    fromLady.TaskQueue.ToList().ForEach(x => this.TaskQueue.Enqueue(x));
                    fromLady.DisposableQueue.ToList().ForEach(x => this.DisposableQueue.Enqueue(x));

                    fromLady.CleanuperQueue.Clear();
                    fromLady.TaskQueue.Clear();
                    fromLady.DisposableQueue.Clear();
                }
            }
        }

        public void MergeTo(AsyncCleanuperLady toLady)
            => toLady.MergeFrom(this);

        CriticalSection LockObj = new CriticalSection();
        volatile bool _disposed = false;
        public bool IsDisposed { get => _disposed; }

        public void DisposeAllSafe()
        {
            _disposed = true;

            IDisposable[] disposableList;
            lock (LockObj)
            {
                disposableList = DisposableQueue.Reverse().ToArray();
            }

            foreach (var disposable in disposableList)
                disposable.DisposeSafe();
        }

        volatile bool _cleanuped = false;
        public bool IsCleanuped { get => _cleanuped; }

        public async Task CleanupAsync()
        {
            _disposed = true;
            _cleanuped = true;

            AsyncCleanuper[] cleanuperList;
            Task[] taskList;
            IDisposable[] disposableList;

            lock (LockObj)
            {
                cleanuperList = CleanuperQueue.Reverse().ToArray();
                taskList = TaskQueue.Reverse().ToArray();
                disposableList = DisposableQueue.Reverse().ToArray();

                CleanuperQueue.Clear();
                TaskQueue.Clear();
                DisposableQueue.Clear();
            }

            foreach (var disposable in disposableList)
                disposable.DisposeSafe();

            foreach (var cleanuper in cleanuperList)
                await cleanuper;

            foreach (var task in taskList)
            {
                try
                {
                    await task;
                }
                catch { }
            }
        }

        public TaskAwaiter GetAwaiter()
            => CleanupAsync().GetAwaiter();
    }

    interface IAsyncCleanupable : IDisposable
    {
        AsyncCleanuper AsyncCleanuper { get; }
        Task _CleanupAsyncInternal();
    }

    abstract class AsyncCleanupableCancellable : AsyncCleanupable
    {
        public CancelWatcher CancelWatcher { get; }

        public CancellationToken GrandCancel { get => CancelWatcher.CancelToken; }

        public AsyncCleanupableCancellable(AsyncCleanuperLady lady, CancellationToken cancel = default) : base(lady)
        {
            try
            {
                CancelWatcher = new CancelWatcher(Lady, cancel);
            }
            catch
            {
                Lady.DisposeAllSafe();
                throw;
            }
        }
    }

    abstract class AsyncCleanupable : IAsyncCleanupable
    {
        static GlobalInitializer gInit = new GlobalInitializer();

        public AsyncCleanuper AsyncCleanuper { get; }
        protected internal AsyncCleanuperLady Lady;

        CriticalSection LockObj = new CriticalSection();

        Stack<Action> OnDisposeStack = new Stack<Action>();

        public AsyncCleanupable(AsyncCleanuperLady lady)
        {
            if (lady == null)
                throw new ArgumentNullException("lady == null");

            Lady = lady;
            this.AsyncCleanuper = new AsyncCleanuper(this);
            Lady.Add(this);
        }

        Once DisposeFlag;
        public void Dispose() => Dispose(true);
        protected virtual void Dispose(bool disposing)
        {
            if (disposing && DisposeFlag.IsFirstCall())
            {
                Action[] actions;

                lock (LockObj)
                {
                    actions = OnDisposeStack.ToArray();
                }

                foreach (var action in actions)
                {
                    try
                    {
                        action();
                    }
                    catch { }
                }

                Lady.DisposeAllSafe();
            }
        }

        public virtual async Task _CleanupAsyncInternal()
        {
            await Lady;
        }

        public void AddOnDispose(Action action)
        {
            if (action != null)
                lock (LockObj)
                    OnDisposeStack.Push(action);
        }
    }

    class AsyncCleanuper : IDisposable
    {
        IAsyncCleanupable Target { get; }

        public AsyncCleanuper(IAsyncCleanupable targetObject)
        {
            Target = targetObject;
        }

        Task internalCleanupTask = null;
        CriticalSection LockObj = new CriticalSection();

        public Task CleanupAsync()
        {
            Target.DisposeSafe();

            lock (LockObj)
            {
                if (internalCleanupTask == null)
                    internalCleanupTask = Target._CleanupAsyncInternal().TryWaitAsync(true);
            }

            return internalCleanupTask;
        }

        public TaskAwaiter GetAwaiter()
            => CleanupAsync().GetAwaiter();

        public void Dispose() { }
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

    class Holder<T> : IDisposable
    {
        public T Value { get; }
        Action<T> DisposeProc;
        LeakCheckerHolder Leak;

        public Holder(Action<T> disposeProc, T value = default(T))
        {
            this.Value = value;
            this.DisposeProc = disposeProc;

            Leak = LeakChecker.Enter();
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
                    Leak.DisposeSafe();
                }
            }
        }
    }

    class Holder : IDisposable
    {
        Action DisposeProc;
        LeakCheckerHolder Leak;

        public Holder(Action disposeProc)
        {
            this.DisposeProc = disposeProc;

            Leak = LeakChecker.Enter();
        }

        Once DisposeFlag;
        public void Dispose() => Dispose(true);
        protected virtual void Dispose(bool disposing)
        {
            if (disposing && DisposeFlag.IsFirstCall())
            {
                try
                {
                    DisposeProc();
                }
                finally
                {
                    Leak.DisposeSafe();
                }
            }
        }
    }

    class AsyncHolder<T> : IAsyncCleanupable
    {
        public AsyncCleanuper AsyncCleanuper { get; }

        public T UserData { get; }
        Action<T> DisposeProc;
        Func<T, Task> AsyncCleanupProc;
        LeakCheckerHolder Leak;
        LeakCheckerHolder Leak2;

        public AsyncHolder(Func<T, Task> asyncCleanupProc, Action<T> disposeProc = null, T userData = default(T))
        {
            this.UserData = userData;
            this.DisposeProc = disposeProc;
            this.AsyncCleanupProc = asyncCleanupProc;

            Leak = LeakChecker.Enter();
            Leak2 = LeakChecker.Enter();
            AsyncCleanuper = new AsyncCleanuper(this);
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
                        DisposeProc(UserData);
                }
                finally
                {
                    Leak.DisposeSafe();
                }
            }
        }

        public async Task _CleanupAsyncInternal()
        {
            try
            {
                await AsyncCleanupProc(UserData);
            }
            finally
            {
                Leak2.DisposeSafe();
            }
        }

        public TaskAwaiter GetAwaiter()
            => AsyncCleanuper.GetAwaiter();
    }

    class AsyncHolder : IAsyncCleanupable
    {
        public AsyncCleanuper AsyncCleanuper { get; }

        Action DisposeProc;
        Func<Task> AsyncCleanupProc;
        LeakCheckerHolder Leak;
        LeakCheckerHolder Leak2;

        public AsyncHolder(Func<Task> asyncCleanupProc, Action disposeProc = null)
        {
            this.DisposeProc = disposeProc;
            this.AsyncCleanupProc = asyncCleanupProc;

            Leak = LeakChecker.Enter();
            Leak2 = LeakChecker.Enter();
            AsyncCleanuper = new AsyncCleanuper(this);
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
                        DisposeProc();
                }
                finally
                {
                    Leak.DisposeSafe();
                }
            }
        }

        public async Task _CleanupAsyncInternal()
        {
            try
            {
                await AsyncCleanupProc();
            }
            finally
            {
                Leak2.DisposeSafe();
            }
        }

        public TaskAwaiter GetAwaiter()
            => AsyncCleanuper.GetAwaiter();
    }


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

        public Holder<int> RegisterCallbackWithUsing(FastEventCallback<TCaller, TEventType> proc, object userState = null)
            => new Holder<int>(id => UnregisterCallback(id), RegisterCallback(proc, userState));

        public int RegisterAsyncEvent(AsyncAutoResetEvent ev)
        {
            if (ev == null) return 0;
            return AsyncEventList.Add(ev);
        }

        public bool UnregisterAsyncEvent(int id)
        {
            return AsyncEventList.Delete(id);
        }

        public Holder<int> RegisterAsyncEventWithUsing(AsyncAutoResetEvent ev)
            => new Holder<int>(id => UnregisterAsyncEvent(id), RegisterAsyncEvent(ev));

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

    enum CancelWatcherCallbackEventType
    {
        Canceled,
    }

    class CancelWatcher : AsyncCleanupable
    {
        static GlobalInitializer gInit = new GlobalInitializer();

        CancellationTokenSource cts = new CancellationTokenSource();
        public CancellationToken CancelToken { get => cts.Token; }
        public AsyncManualResetEvent EventWaitMe { get; } = new AsyncManualResetEvent();
        public bool Canceled { get; private set; } = false;

        public FastEventListenerList<CancelWatcher, CancelWatcherCallbackEventType> EventList { get; } = new FastEventListenerList<CancelWatcher, CancelWatcherCallbackEventType>();

        Task mainLoop;

        CancellationTokenSource canceller = new CancellationTokenSource();

        AsyncAutoResetEvent ev = new AsyncAutoResetEvent();
        volatile bool halt = false;

        HashSet<CancellationToken> targetList = new HashSet<CancellationToken>();
        List<Task> taskList = new List<Task>();

        object LockObj = new object();

        public CancelWatcher(params CancellationToken[] cancels) : this(null, cancels) { }

        public CancelWatcher(AsyncCleanuperLady parentLady, params CancellationToken[] cancels)
            : base(new AsyncCleanuperLady())
        {
            AddWatch(canceller.Token);
            AddWatch(cancels);

            if (parentLady != null)
                parentLady.Add(this);

            this.mainLoop = CancelWatcherMainLoop().AddToLady(this);
        }

        public void Cancel()
        {
            canceller.TryCancelAsync().LaissezFaire();
            this.Canceled = true;
        }

        async Task CancelWatcherMainLoop()
        {
            using (LeakChecker.Enter())
            {
                while (true)
                {
                    List<CancellationToken> cancels = new List<CancellationToken>();

                    lock (LockObj)
                    {
                        foreach (CancellationToken c in targetList)
                            cancels.Add(c);
                    }

                    await TaskUtil.WaitObjectsAsync(
                        cancels: cancels.ToArray(),
                        events: new AsyncAutoResetEvent[] { ev });

                    bool canceled = false;

                    lock (LockObj)
                    {
                        foreach (CancellationToken c in targetList)
                        {
                            if (c.IsCancellationRequested)
                            {
                                canceled = true;
                                break;
                            }
                        }
                    }

                    if (halt)
                    {
                        canceled = true;
                    }

                    if (canceled)
                    {
                        this.cts.TryCancelAsync().LaissezFaire();
                        this.EventWaitMe.Set(true);
                        this.Canceled = true;
                        EventList.Fire(this, CancelWatcherCallbackEventType.Canceled);
                        break;
                    }
                }
            }
        }

        public bool AddWatch(params CancellationToken[] cancels)
        {
            bool ret = false;

            lock (LockObj)
            {
                foreach (CancellationToken cancel in cancels)
                {
                    if (cancel != CancellationToken.None)
                    {
                        if (this.targetList.Contains(cancel) == false)
                        {
                            this.targetList.Add(cancel);
                            ret = true;
                        }
                    }
                }
            }

            if (ret)
            {
                this.ev.Set();
            }

            return ret;
        }

        Once DisposeFlag;
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;
                this.halt = true;
                this.Canceled = true;
                this.ev.Set();
                this.cts.TryCancelAsync().LaissezFaire();
                this.EventWaitMe.Set(true);
            }
            finally { base.Dispose(disposing); }
        }
    }

    delegate bool TimeoutDetectorCallback(TimeoutDetector detector);

    class TimeoutDetector : AsyncCleanupable
    {
        Task mainLoop;

        CriticalSection LockObj = new CriticalSection();

        public long Timeout { get; }

        long NextTimeout;

        AsyncAutoResetEvent ev = new AsyncAutoResetEvent();

        AsyncAutoResetEvent eventAuto;
        AsyncManualResetEvent eventManual;

        CancellationTokenSource halt = new CancellationTokenSource();

        CancelWatcher cancelWatcher;

        CancellationTokenSource cts = new CancellationTokenSource();
        public CancellationToken Cancel { get => cts.Token; }
        public Task TaskWaitMe { get => this.mainLoop; }

        public object UserState { get; }

        public bool TimedOut { get; private set; } = false;

        TimeoutDetectorCallback Callback;

        public TimeoutDetector(AsyncCleanuperLady lady, int timeout, CancelWatcher watcher = null, AsyncAutoResetEvent eventAuto = null, AsyncManualResetEvent eventManual = null,
            TimeoutDetectorCallback callback = null, object userState = null)
            : base(lady)
        {
            if (timeout == System.Threading.Timeout.Infinite || timeout == int.MaxValue)
            {
                return;
            }

            this.Timeout = timeout;
            this.cancelWatcher = watcher.AddToLady(this);
            this.eventAuto = eventAuto;
            this.eventManual = eventManual;
            this.Callback = callback;
            this.UserState = userState;

            NextTimeout = FastTick64.Now + this.Timeout;
            mainLoop = TimeoutDetectorMainLoop().AddToLady(this);
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

                    if (this.TimedOut || halt.IsCancellationRequested)
                    {
                        cts.TryCancelAsync().LaissezFaire();

                        if (this.cancelWatcher != null) this.cancelWatcher.Cancel();
                        if (this.eventAuto != null) this.eventAuto.Set();
                        if (this.eventManual != null) this.eventManual.Set();

                        return;
                    }
                    else
                    {
                        await TaskUtil.WaitObjectsAsync(
                            events: new AsyncAutoResetEvent[] { ev },
                            cancels: new CancellationToken[] { halt.Token },
                            timeout: (int)remainTime);
                    }
                }
            }
        }

        Once DisposeFlag;
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;
                cancelWatcher.DisposeSafe();
                halt.TryCancelAsync().LaissezFaire();
            }
            finally { base.Dispose(disposing); }
        }
    }

    static class LeakChecker
    {
        static GlobalInitializer gInit = new GlobalInitializer();

        internal static Dictionary<long, LeakCheckerHolder> _InternalList = new Dictionary<long, LeakCheckerHolder>();
        internal static long _InternalCurrentId = 0;

        static public AsyncCleanuperLady SuperGrandLady { get; } = new AsyncCleanuperLady();

        public static LeakCheckerHolder Enter([CallerFilePath] string filename = "", [CallerLineNumber] int line = 0, [CallerMemberName] string caller = null)
            => new LeakCheckerHolder($"{caller}() - {Path.GetFileName(filename)}:{line}", Environment.StackTrace);

        public static int Count
        {
            get
            {
                lock (_InternalList)
                    return _InternalList.Count;
            }
        }

        public static void Print()
        {
            SuperGrandLady.CleanupAsync().TryWait();

            lock (_InternalList)
            {
                if (Dbg.IsConsoleDebugMode)
                {
                    if (Count == 0)
                    {
                        Console.WriteLine("@@@ No leaks @@@");
                    }
                    else
                    {
                        Console.WriteLine($"*** Leaked !!! count = {Count} ***");
                        Console.WriteLine($"--- Leaked list  count = {Count} ---");
                        Console.Write(GetString());
                        Console.WriteLine($"--- End of leaked list  count = {Count} --");
                    }
                }
            }
        }

        public static string GetString()
        {
            StringWriter w = new StringWriter();
            int num = 0;
            lock (_InternalList)
            {
                foreach (var v in _InternalList.OrderBy(x => x.Key))
                {
                    num++;
                    w.WriteLine($"#{num}: {v.Key}: {v.Value.Name}");
                    if (string.IsNullOrEmpty(v.Value.StackTrace) == false)
                    {
                        w.WriteLine(v.Value.StackTrace);
                        w.WriteLine("---");
                    }
                }
            }
            return w.ToString();
        }
    }

    class LeakCheckerHolder : IDisposable
    {
        long Id;
        public string Name { get; }
        public string StackTrace { get; }

        internal LeakCheckerHolder(string name, string stackTrace)
        {
            stackTrace = "";

            if (string.IsNullOrEmpty(name)) name = "<untitled>";

            Id = Interlocked.Increment(ref LeakChecker._InternalCurrentId);
            Name = name;
            StackTrace = string.IsNullOrEmpty(stackTrace) ? "" : stackTrace;

            lock (LeakChecker._InternalList)
                LeakChecker._InternalList.Add(Id, this);
        }

        public void Dispose() => Dispose(true);
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;
            lock (LeakChecker._InternalList)
            {
                Debug.Assert(LeakChecker._InternalList.ContainsKey(Id));
                LeakChecker._InternalList.Remove(Id);
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

        public WhenAll(Task t, bool throwException = false) : this(throwException, t.SingleArray()) { }

        public static Task Await(IEnumerable<Task> tasks, bool throwException = false)
            => Await(throwException, tasks.ToArray());

        public static Task Await(Task t, bool throwException = false)
            => Await(throwException, t.SingleArray());

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
                                t.Exception.ReThrow();
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

    class DelayAction : AsyncCleanupable
    {
        public Action<object> Action { get; }
        public object UserState { get; }
        public int Timeout { get; }

        Task MainTask;

        public bool IsCompleted = false;
        public bool IsCompletedSuccessfully = false;
        public bool IsCanceled = false;

        public Exception Exception { get; private set; } = null;

        CancellationTokenSource CancelSource = new CancellationTokenSource();

        public DelayAction(AsyncCleanuperLady lady, int timeout, Action<object> action, object userState = null)
            : base(lady)
        {
            if (timeout < 0 || timeout == int.MaxValue) timeout = System.Threading.Timeout.Infinite;

            this.Timeout = timeout;
            this.Action = action;
            this.UserState = userState;

            this.MainTask = MainTaskProc().AddToLady(this);
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
                        cancels: CancelSource.Token.SingleArray(),
                        exceptions: ExceptionWhen.CancelException);

                    InternalInvokeAction();
                }
                catch
                {
                    IsCompleted = true;
                    IsCanceled = true;
                    IsCompletedSuccessfully = false;
                }
            }
        }

        public void Cancel() => Dispose();

        Once DisposeFlag;
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;
                CancelSource.Cancel();
            }
            finally { base.Dispose(disposing); }
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
        public delegate Task<ValueOrClosed<TUserReturnElement>> AsyncReceiveCallback(TUserState state);

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
                    userTask = AsyncReceiveProc(state);
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

                        if (userTask.Result.IsOpen)
                        {
                            ret.Add(userTask.Result.Value);
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
                    if (userTask.Result.IsOpen)
                    {
                        ret.Add(userTask.Result.Value);
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
                throwingException.ReThrow();
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
                ex.ReThrow();
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

    public readonly struct BackgroundStateDataUpdatePolicy
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

        public abstract void RegisterSystemStateChangeNotificationCallbackOnlyOnce(Action callMe);
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
        static Thread thread = null;
        static AutoResetEvent threadSignal = new AutoResetEvent(false);
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
                        ret.RegisterSystemStateChangeNotificationCallbackOnlyOnce(() =>
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
                if (thread == null)
                {
                    EnsureStartThreadIfStopped(CacheData.DataUpdatePolicy);
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

                EnsureStartThreadIfStopped(updatePolicy);

                return CacheData;
            }
        }

        static void EnsureStartThreadIfStopped(BackgroundStateDataUpdatePolicy updatePolicy)
        {
            lock (LockObj)
            {
                if (thread == null)
                {
                    thread = new Thread(MaintainThread);
                    thread.IsBackground = true;
                    thread.Priority = ThreadPriority.BelowNormal;
                    thread.Name = $"MaintainThread for BackgroundState<{typeof(TData).ToString()}>";
                    thread.Start(updatePolicy);
                }
            }
        }

        static int nextInterval = 0;

        static void MaintainThread(object param)
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
                        thread = null;
                        return;
                    }
                }

                int i = tm.GetNextInterval();

                i = Math.Max(i, 100);

                threadSignal.WaitOne(i);
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

    class AsyncOneShotTester : AsyncCleanupable
    {
        Task t = null;
        public AsyncOneShotTester(AsyncCleanuperLady lady, Func<Task> Proc) : base(lady)
        {
            t = Proc();
            t.TryWaitAsync(false).LaissezFaire();
        }

        public override async Task _CleanupAsyncInternal()
        {
            try
            {
                await t;
            }
            finally { await base._CleanupAsyncInternal(); }
        }
    }

    class AsyncTester : IDisposable
    {
        List<AsyncCleanuperLady> LadyList = new List<AsyncCleanuperLady>();

        public AsyncCleanuperLady SingleLady { get; } = null;

        CancellationTokenSource CancelSource = new CancellationTokenSource();
        public CancellationToken Cancelled => CancelSource.Token;

        public AsyncTester(bool createSingleLady) : this()
        {
            if (createSingleLady)
            {
                AddLady(SingleLady = new AsyncCleanuperLady());
            }
        }
        public AsyncTester(params AsyncCleanuperLady[] ladyList)
        {
            foreach (AsyncCleanuperLady lady in ladyList)
                AddLady(lady);
        }

        public void AddLady(params AsyncCleanuperLady[] ladyList)
        {
            if (OnceFlag.IsSet || DisposeFlag.IsSet)
                throw new ApplicationException("Already exiting.");

            lock (LadyList)
            {
                foreach (AsyncCleanuperLady lady in ladyList)
                    LadyList.Add(lady);
            }
        }

        Once OnceFlag;

        public void EnterKeyPrompt(string message = "Enter to quit :")
        {
            if (OnceFlag.IsFirstCall())
            {
                Console.Write(message);
                Console.ReadLine();
                Console.WriteLine();

                Dispose();
            }
        }

        public void Dispose() => Dispose(true);
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;

            CancelSource.Cancel();

            AsyncCleanuperLady[] ladyListCopy;

            lock (LadyList)
            {
                ladyListCopy = LadyList.ToArray();
            }

            foreach (var lady in ladyListCopy)
            {
                lady.CleanupAsync().TryWait();
            }
        }
    }
}
