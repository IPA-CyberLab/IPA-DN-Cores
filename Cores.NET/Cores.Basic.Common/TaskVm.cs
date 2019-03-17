using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Data;
using System.Data.Sql;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using System.Text;
using System.ComponentModel;
using System.Configuration;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Web;
using System.IO;
using System.Net;
using System.Net.Cache;
using System.Reflection;
using System.Drawing;
using System.Runtime.InteropServices;

using IPA.Cores.Helper.Basic;
using System.Runtime.CompilerServices;

namespace IPA.Cores.Basic
{
    class AsyncLock : IDisposable
    {
        public class LockHolder : IDisposable
        {
            AsyncLock parent;
            internal LockHolder(AsyncLock parent)
            {
                this.parent = parent;
            }

            Once dispose_flag;
            public void Dispose()
            {
                if (dispose_flag.IsFirstCall())
                {
                    this.parent.Unlock();
                }
            }
        }

        SemaphoreSlim semaphone = new SemaphoreSlim(1, 1);
        Once dispose_flag;

        public async Task<LockHolder> LockWithAwait()
        {
            await LockAsync();

            return new LockHolder(this);
        }

        public Task LockAsync() => semaphone.WaitAsync();
        public void Unlock() => semaphone.Release();

        public void Dispose()
        {
            if (dispose_flag.IsFirstCall())
            {
                semaphone.DisposeSafe();
                semaphone = null;
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
        internal static AsyncLocal<TaskVarObject> async_local_obj = new AsyncLocal<TaskVarObject>();

        public static T Get<T>()
        {
            var v = async_local_obj.Value;
            if (v == null) return default(T);

            T ret = (T)v.Get(typeof(T));
            return ret;
        }
        public static void Set<T>(T obj)
        {
            if (async_local_obj.Value == null) async_local_obj.Value = new TaskVarObject();
            async_local_obj.Value.Set(typeof(T), obj);
        }

        public static object Get(string name) => async_local_obj.Value.Get(name);
        public static void Set(string name, object obj) => async_local_obj.Value.Set(name, obj);
    }

    static class BackgroundWorker
    {
        static volatile int num_busy_worker_threads = 0;
        static volatile int num_worker_threads = 0;

        static Queue<Tuple<Action<object>, object>> queue = new Queue<Tuple<Action<object>, object>>();

        static AutoResetEvent signal = new AutoResetEvent(false);

        static void worker_thread_proc()
        {
            while (true)
            {
                Interlocked.Increment(ref num_busy_worker_threads);
                while (true)
                {
                    Tuple<Action<object>, object> work = null;
                    lock (queue)
                    {
                        if (queue.Count != 0)
                        {
                            work = queue.Dequeue();
                        }
                    }

                    if (work != null)
                    {
                        try
                        {
                            work.Item1(work.Item2);
                        }
                        catch (Exception ex)
                        {
                            Dbg.WriteLine(ex.ToString());
                        }
                    }
                    else
                    {
                        break;
                    }
                }
                Interlocked.Decrement(ref num_busy_worker_threads);

                signal.WaitOne();
            }
        }

        public static void Run(Action<object> action, object arg)
        {
            if (num_busy_worker_threads == num_worker_threads)
            {
                Interlocked.Increment(ref num_worker_threads);
                Thread t = new Thread(worker_thread_proc);
                t.IsBackground = true;
                t.Start();
            }

            lock (queue)
            {
                queue.Enqueue(new Tuple<Action<object>, object>(action, arg));
            }

            signal.Set();
        }

    }

    static class AsyncPreciseDelay
    {
        static SortedList<long, AsyncManualResetEvent> wait_list = new SortedList<long, AsyncManualResetEvent>();

        static Stopwatch w;

        static Thread background_thread;

        static AutoResetEvent ev = new AutoResetEvent(false);

        static List<Thread> worker_thread_list = new List<Thread>();

        static Queue<AsyncManualResetEvent> queued_tcs = new Queue<AsyncManualResetEvent>();

        static AutoResetEvent queued_tcs_signal = new AutoResetEvent(false);

        static AsyncPreciseDelay()
        {
            w = new Stopwatch();
            w.Start();

            background_thread = new Thread(background_thread_proc);
            try
            {
                background_thread.Priority = ThreadPriority.Highest;
            }
            catch { }
            background_thread.IsBackground = true;
            background_thread.Start();
        }

        static volatile int num_busy_worker_threads = 0;
        static volatile int num_worker_threads = 0;

        static void worker_thread_proc()
        {
            while (true)
            {
                Interlocked.Increment(ref num_busy_worker_threads);
                while (true)
                {
                    AsyncManualResetEvent tcs = null;
                    lock (queued_tcs)
                    {
                        if (queued_tcs.Count != 0)
                        {
                            tcs = queued_tcs.Dequeue();
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
                Interlocked.Decrement(ref num_busy_worker_threads);

                queued_tcs_signal.WaitOne();
            }
        }

        static void FireWorkerThread(AsyncManualResetEvent tc)
        {
            if (num_busy_worker_threads == num_worker_threads)
            {
                Interlocked.Increment(ref num_worker_threads);
                Thread t = new Thread(worker_thread_proc);
                t.IsBackground = true;
                t.Start();
                //Console.WriteLine($"num_worker_threads = {num_worker_threads}");
            }

            lock (queued_tcs)
            {
                queued_tcs.Enqueue(tc);
            }
            queued_tcs_signal.Set();
        }

        static void background_thread_proc()
        {
            //Benchmark b1 = new Benchmark("num_fired");
            //Benchmark b2 = new Benchmark("num_loop");
            //Benchmark b3 = new Benchmark("num_removed");
            while (true)
            {
                long now = Tick;
                long next_wait_target = -1;

                List<AsyncManualResetEvent> fire_event_list = new List<AsyncManualResetEvent>();

                lock (wait_list)
                {
                    List<long> past_target_list = new List<long>();
                    List<long> future_target_list = new List<long>();

                    foreach (long target in wait_list.Keys)
                    {
                        if (now >= target)
                        {
                            past_target_list.Add(target);
                            next_wait_target = 0;
                        }
                        else
                        {
                            future_target_list.Add(target);
                        }
                    }

                    foreach (long target in past_target_list)
                    {
                        AsyncManualResetEvent e = wait_list[target];

                        wait_list.Remove(target);

                        fire_event_list.Add(e);
                    }

                    if (next_wait_target == -1)
                    {
                        if (wait_list.Count >= 1)
                        {
                            next_wait_target = wait_list.Keys[0];
                        }
                    }
                }

                int n = 0;
                foreach (AsyncManualResetEvent tc in fire_event_list)
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
                long next_wait_tick = (Math.Max(next_wait_target - now, 0));
                if (next_wait_target == -1)
                {
                    next_wait_tick = -1;
                }
                if (next_wait_tick >= 1 || next_wait_tick == -1)
                {
                    if (next_wait_tick == -1 || next_wait_tick >= 100)
                    {
                        next_wait_tick = 100;
                    }
                    ev.WaitOne((int)next_wait_tick);
                }
            }
        }

        public static long Tick
        {
            get
            {
                lock (w)
                {
                    return w.ElapsedMilliseconds + 1L;
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

            long target_time = Tick + (long)msec;

            AsyncManualResetEvent tc = null;

            bool set_event = false;

            lock (wait_list)
            {
                long first_target_before = -1;
                long first_target_after = -1;

                if (wait_list.Count >= 1)
                {
                    first_target_before = wait_list.Keys[0];
                }

                if (wait_list.ContainsKey(target_time) == false)
                {
                    tc = new AsyncManualResetEvent();
                    wait_list.Add(target_time, tc);
                }
                else
                {
                    tc = wait_list[target_time];
                }

                first_target_after = wait_list.Keys[0];

                if (first_target_before != first_target_after)
                {
                    set_event = true;
                }
            }

            if (set_event)
            {
                ev.Set();
            }

            return tc.WaitAsync();
        }
    }
    /*
    public class AsyncAutoResetEvent : AsyncEvent
    {
        volatile Queue<TaskCompletionSource<object>> waiters = new Queue<TaskCompletionSource<object>>();
        volatile bool set;

        public override Task Wait()
        {
            lock (waiters)
            {
                var tcs = new TaskCompletionSource<object>();
                if (waiters.Count > 0 || !set)
                {
                    waiters.Enqueue(tcs);
                }
                else
                {
                    //tcs.SetCanceled();
                    tcs.SetResult(null);
                    set = false;
                }
                return tcs.Task;
            }
        }

        public override void Set()
        {
            TaskCompletionSource<object> toSet = null;
            lock (waiters)
            {
                if (waiters.Count > 0) toSet = waiters.Dequeue();
                else set = true;
            }
            if (toSet != null)
            {
                toSet.SetResult(null);
                //toSet.SetCanceled();
            }
        }
    }*/

    class AsyncAutoResetEvent
    {
        object lockobj = new object();
        List<AsyncManualResetEvent> event_queue = new List<AsyncManualResetEvent>();
        bool is_set = false;

        public Task WaitOneAsync(out Action cancel)
        {
            lock (lockobj)
            {
                if (is_set)
                {
                    is_set = false;
                    cancel = () => { };
                    return Task.CompletedTask;
                }

                AsyncManualResetEvent e = new AsyncManualResetEvent();

                Task ret = e.WaitAsync();

                event_queue.Add(e);

                cancel = () =>
                {
                    lock (lockobj)
                    {
                        event_queue.Remove(e);
                    }
                };

                return ret;
            }
        }

        volatile bool queued_set = false;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void QueueSet() => queued_set = true;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetIfQueued(bool softly = false)
        {
            if (queued_set) Set(softly);
        }

        public void Set(bool softly = false)
        {
            AsyncManualResetEvent ev = null;
            lock (lockobj)
            {
                if (event_queue.Count >= 1)
                {
                    ev = event_queue[event_queue.Count - 1];
                    event_queue.Remove(ev);
                }

                if (ev == null)
                {
                    is_set = true;
                }
            }

            if (ev != null)
            {
                ev.Set(softly);
            }
        }
    }

    class AsyncManualResetEvent
    {
        object lockobj = new object();
        volatile TaskCompletionSource<bool> tcs;
        bool is_set = false;

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
                    return this.is_set;
                }
            }
        }

        public Task WaitAsync()
        {
            lock (lockobj)
            {
                if (is_set)
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
                Task.Factory.StartNew(() => Set());
                return;
            }

            lock (lockobj)
            {
                if (is_set == false)
                {
                    is_set = true;
                    tcs.TrySetResult(true);
                }
            }
        }

        public void Reset()
        {
            lock (lockobj)
            {
                if (is_set)
                {
                    is_set = false;
                    init();
                }
            }
        }
    }

    static partial class TaskUtil
    {
        public static void Test()
        {
            //Dbg.WhereThread();
            ////Task<string> t = (Task<string>)ConvertTask(f1(), typeof(object), typeof(string));
            //Task<object> t = (Task<object>)ConvertTask(f2(), typeof(string), typeof(object));
            //Dbg.WhereThread();
            //t.Result.Print();
            //Dbg.WhereThread();
        }

        static async Task<object> f1()
        {
            Dbg.WhereThread();
            await Task.Delay(100);
            Dbg.WhereThread();
            return "Hello";
        }

        static async Task<string> f2()
        {
            Dbg.WhereThread();
            await Task.Delay(100);
            Dbg.WhereThread();
            return "Hello";
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
                    Type timer_type = timer.GetType();
                    FieldInfo next_field = timer_type.GetField("m_next", BindingFlags.Instance | BindingFlags.NonPublic);

                    while (timer != null)
                    {
                        timer = next_field.GetValue(timer);
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

        public static async Task WaitObjectsAsync(Task[] tasks = null, CancellationToken[] cancels = null, AsyncAutoResetEvent[] auto_events = null,
            AsyncManualResetEvent[] manual_events = null, int timeout = Timeout.Infinite)
        {
            if (tasks == null) tasks = new Task[0];
            if (cancels == null) cancels = new CancellationToken[0];
            if (auto_events == null) auto_events = new AsyncAutoResetEvent[0];
            if (manual_events == null) manual_events = new AsyncManualResetEvent[0];
            if (timeout == 0) return;

            List<Task> task_list = new List<Task>();
            List<CancellationTokenRegistration> reg_list = new List<CancellationTokenRegistration>();
            List<Action> undo_list = new List<Action>();

            foreach (Task t in tasks)
                task_list.Add(t);

            foreach (CancellationToken c in cancels)
            {
                task_list.Add(WhenCanceled(c, out CancellationTokenRegistration reg));
                reg_list.Add(reg);
            }

            foreach (AsyncAutoResetEvent ev in auto_events)
            {
                task_list.Add(ev.WaitOneAsync(out Action undo));
                undo_list.Add(undo);
            }

            foreach (AsyncManualResetEvent ev in manual_events)
            {
                task_list.Add(ev.WaitAsync());
            }

            if (timeout >= 1)
            {
                task_list.Add(Task.Delay(timeout));
            }

            try
            {
                await Task.WhenAny(task_list.ToArray());
            }
            catch { }
            finally
            {
                foreach (Action undo in undo_list)
                    undo();

                foreach (CancellationTokenRegistration reg in reg_list)
                    reg.Dispose();
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
        {
            BackgroundWorker.Run(arg =>
            {
                cts.TryCancel();
            }, null);
        }

        public static async Task TryWaitAsync(Task t)
        {
            try
            {
                await t;
            }
            catch { }
        }

        public static void TryWait(Task t)
        {
            try
            {
                t.Wait();
            }
            catch { }
        }

        // いずれかの CancellationToken がキャンセルされたときにキャンセルされる CancellationToken を作成する
        public static CancellationToken CombineCancellationTokens(bool no_wait, params CancellationToken[] tokens)
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            ChainCancellationTokensToCancellationTokenSource(cts, no_wait, tokens);
            return cts.Token;
        }

        // いずれかの CancellationToken がキャンセルされたときに CancellationTokenSource をキャンセルするように設定する
        public static void ChainCancellationTokensToCancellationTokenSource(CancellationTokenSource cts, bool no_wait, params CancellationToken[] tokens)
        {
            foreach (CancellationToken t in tokens)
            {
                t.Register(() =>
                {
                    if (no_wait == false)
                        cts.TryCancel();
                    else
                        cts.TryCancelNoBlock();
                });
            }
        }

        public static CancellationToken CurrentTaskVmGracefulCancel => (CancellationToken)ThreadData.CurrentThreadData["taskvm_current_graceful_cancel"];

        // 何らかのタスクをタイムアウトおよびキャンセル付きで実施する
        public static async Task<TResult> DoAsyncWithTimeout<TResult>(Func<CancellationToken, Task<TResult>> main_proc, Action cancel_proc = null, int timeout = Timeout.Infinite, CancellationToken cancel = default(CancellationToken), params CancellationToken[] cancel_tokens)
        {
            if (timeout < 0) timeout = Timeout.Infinite;
            if (timeout == 0) throw new TimeoutException("timeout == 0");

            List<Task> wait_tasks = new List<Task>();
            List<IDisposable> disposes = new List<IDisposable>();
            Task timeout_task = null;
            CancellationTokenSource timeout_cts = null;
            CancellationTokenSource cancel_for_proc = new CancellationTokenSource();

            if (timeout != Timeout.Infinite)
            {
                timeout_cts = new CancellationTokenSource();
                timeout_task = Task.Delay(timeout, timeout_cts.Token);
                disposes.Add(timeout_cts);

                wait_tasks.Add(timeout_task);
            }

            try
            {
                if (cancel.CanBeCanceled)
                {
                    cancel.ThrowIfCancellationRequested();

                    Task t = WhenCanceled(cancel, out CancellationTokenRegistration reg);
                    disposes.Add(reg);
                    wait_tasks.Add(t);
                }

                foreach (CancellationToken c in cancel_tokens)
                {
                    if (c.CanBeCanceled)
                    {
                        c.ThrowIfCancellationRequested();

                        Task t = WhenCanceled(c, out CancellationTokenRegistration reg);
                        disposes.Add(reg);
                        wait_tasks.Add(t);
                    }
                }

                Task<TResult> proc_task = main_proc(cancel_for_proc.Token);

                if (proc_task.IsCompleted)
                {
                    return proc_task.Result;
                }

                wait_tasks.Add(proc_task);

                await Task.WhenAny(wait_tasks.ToArray());

                foreach (CancellationToken c in cancel_tokens)
                {
                    c.ThrowIfCancellationRequested();
                }

                cancel.ThrowIfCancellationRequested();

                if (proc_task.IsCompleted)
                {
                    return proc_task.Result;
                }

                throw new TimeoutException();
            }
            catch
            {
                try
                {
                    cancel_for_proc.Cancel();
                }
                catch { }
                try
                {
                    if (cancel_proc != null) cancel_proc();
                }
                catch
                {
                }
                throw;
            }
            finally
            {
                if (timeout_cts != null)
                {
                    try
                    {
                        timeout_cts.Cancel();
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
    }

    class TimeoutDetector : IDisposable
    {
        Task main_loop;

        object LockObj = new object();

        Stopwatch sw = new Stopwatch();
        long tick { get => this.sw.ElapsedMilliseconds; }

        public long Timeout { get; }

        long next_timeout;

        AsyncAutoResetEvent ev = new AsyncAutoResetEvent();

        CancellationTokenSource halt = new CancellationTokenSource();

        CancelWatcher watcher;
        AutoResetEvent event_auto;
        ManualResetEvent event_manual;

        CancellationTokenSource cts = new CancellationTokenSource();
        public CancellationToken Cancel { get => cts.Token; }
        public Task TaskWaitMe { get => this.main_loop; }

        Action callme;

        public TimeoutDetector(int timeout, CancelWatcher watcher = null, AutoResetEvent event_auto = null, ManualResetEvent event_manual = null, Action callme = null)
        {
            this.Timeout = timeout;
            this.watcher = watcher;
            this.event_auto = event_auto;
            this.event_manual = event_manual;
            this.callme = callme;

            sw.Start();
            next_timeout = tick + this.Timeout;
            main_loop = timeout_detector_main_loop();
        }

        public void Keep()
        {
            lock (LockObj)
            {
                this.next_timeout = tick + this.Timeout;
            }
        }

        async Task timeout_detector_main_loop()
        {
            while (halt.IsCancellationRequested == false)
            {
                long now, remain_time;

                lock (LockObj)
                {
                    now = tick;
                    remain_time = next_timeout - now;
                }

                Dbg.Where($"remain_time = {remain_time}");

                if (remain_time < 0)
                {
                    break;
                }
                else
                {
                    await TaskUtil.WaitObjectsAsync(
                        auto_events: new AsyncAutoResetEvent[] { ev },
                        cancels: new CancellationToken[] { halt.Token },
                        timeout: (int)remain_time);
                }
            }

            cts.TryCancelAsync().LaissezFaire();
            if (this.watcher != null) this.watcher.Cancel();
            if (this.event_auto != null) this.event_auto.Set();
            if (this.event_manual != null) this.event_manual.Set();
            if (this.callme != null)
            {
                new Task(() =>
                {
                    try
                    {
                        this.callme();
                    }
                    catch { }
                }).Start();
            }
        }

        Once dispose_flag;
        public void Dispose()
        {
            if (dispose_flag.IsFirstCall())
            {
                halt.TryCancelAsync().LaissezFaire();
            }
        }
    }

    class CancelWatcher : IDisposable
    {
        CancellationTokenSource cts = new CancellationTokenSource();
        public CancellationToken CancelToken { get => cts.Token; }
        public Task TaskWaitMe { get; }
        public AsyncManualResetEvent EventWaitMe { get; } = new AsyncManualResetEvent();

        CancellationTokenSource canceller = new CancellationTokenSource();

        AsyncAutoResetEvent ev = new AsyncAutoResetEvent();
        volatile bool halt = false;

        HashSet<CancellationToken> target_list = new HashSet<CancellationToken>();
        List<Task> task_list = new List<Task>();

        object LockObj = new object();

        public CancelWatcher(params CancellationToken[] cancels)
        {
            AddWatch(canceller.Token);
            AddWatch(cancels);

            this.TaskWaitMe = cancel_watch_mainloop();
        }

        public void Cancel()
        {
            canceller.TryCancelAsync().LaissezFaire();
        }

        async Task cancel_watch_mainloop()
        {
            while (true)
            {
                List<CancellationToken> cancels = new List<CancellationToken>();

                lock (LockObj)
                {
                    foreach (CancellationToken c in target_list)
                        cancels.Add(c);
                }

                //await Task.WhenAny(ev.WaitOneAsync(), TaskUtil.WhenCanceled(cancels.ToArray()));
                await TaskUtil.WaitObjectsAsync(
                    cancels: cancels.ToArray(),
                    auto_events: new AsyncAutoResetEvent[] { ev });

                bool canceled = false;

                lock (LockObj)
                {
                    foreach (CancellationToken c in target_list)
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
                    this.EventWaitMe.Set();
                    //Dbg.Where();
                    break;
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
                    if (this.target_list.Contains(cancel) == false)
                    {
                        this.target_list.Add(cancel);
                        ret = true;
                    }
                }
            }

            if (ret)
            {
                this.ev.Set();
            }

            return ret;
        }

        Once dispose_flag;

        public void Dispose()
        {
            if (dispose_flag.IsFirstCall())
            {
                this.halt = true;
                this.ev.Set();
                this.TaskWaitMe.Wait();
            }
        }
    }

    class TaskVmAbortException : Exception
    {
        public TaskVmAbortException(string message) : base(message) { }
    }

    static class AbortedTaskExecuteThreadPrivate
    {
        static object LockObj = new object();
        static Dictionary<object, Queue<(SendOrPostCallback callback, object args)>> dispatch_queue_list = new Dictionary<object, Queue<(SendOrPostCallback, object)>>();
        static object dummy_orphants = new object();
        static AutoResetEvent ev = new AutoResetEvent(true);

        static AbortedTaskExecuteThreadPrivate()
        {
            Thread t = new Thread(thread_proc);
            t.IsBackground = true;
            t.Start();
        }

        static void thread_proc(object param)
        {
            SynchronizationContext.SetSynchronizationContext(new AbortedTaskExecuteThreadSynchronizationContext());

            while (true)
            {
                var actions = new List<(SendOrPostCallback callback, object args)>();

                lock (LockObj)
                {
                    foreach (object ctx in dispatch_queue_list.Keys)
                    {
                        var queue = dispatch_queue_list[ctx];

                        while (queue.Count >= 1)
                        {
                            actions.Add(queue.Dequeue());
                        }
                    }
                }

                foreach (var action in actions)
                {
                    try
                    {
                        //Dbg.WriteCurrentThreadId("aborted_call");
                        action.callback(action.args);
                    }
                    catch (Exception ex)
                    {
                        ex.ToString().Debug();
                    }
                }

                ev.WaitOne();
            }
        }

        public static void PostAction(object ctx, SendOrPostCallback callback, object arg)
        {
            lock (LockObj)
            {
                if (dispatch_queue_list.ContainsKey(ctx) == false)
                {
                    dispatch_queue_list.Add(ctx, new Queue<(SendOrPostCallback, object)>());
                }

                dispatch_queue_list[ctx].Enqueue((callback, arg));
            }

            ev.Set();
        }

        public static void RemoveContext(object ctx)
        {
            lock (LockObj)
            {
                if (dispatch_queue_list.ContainsKey(ctx))
                {
                    var queue = dispatch_queue_list[ctx];

                    while (queue.Count >= 1)
                    {
                        var q = queue.Dequeue();
                        PostAction(dummy_orphants, q.callback, q.args);
                    }

                    dispatch_queue_list.Remove(ctx);
                }
            }

            ev.Set();
        }

        class AbortedTaskExecuteThreadSynchronizationContext : SynchronizationContext
        {
            volatile int num_operations = 0;
            volatile int num_operations_total = 0;

            public bool IsAllOperationsCompleted => (num_operations_total >= 1 && num_operations == 0);

            public override void Post(SendOrPostCallback d, object state)
            {
                //Dbg.WriteCurrentThreadId("aborted_call_post");
                AbortedTaskExecuteThreadPrivate.PostAction(AbortedTaskExecuteThreadPrivate.dummy_orphants, d, state);
            }
        }
    }

    class TaskVm<TResult, TIn>
    {
        readonly ThreadObj thread;
        public ThreadObj ThreadObj => this.thread;

        Func<TIn, Task<TResult>> root_function;
        Task root_task;

        Queue<(SendOrPostCallback callback, object args)> dispatch_queue = new Queue<(SendOrPostCallback callback, object args)>();
        AutoResetEvent dispatch_queue_event = new AutoResetEvent(false);

        public TIn InputParameter { get; }

        CancellationToken GracefulCancel { get; }
        CancellationToken AbortCancel { get; }

        object ResultLock = new object();
        public Exception Error { get; private set; } = null;
        public TResult Result => this.GetResult(out _);
        TResult result = default(TResult);
        public bool IsCompleted { get; private set; } = false;
        public bool IsAborted { get; private set; } = false;
        public bool HasError => this.Error != null;
        public bool Ok => !HasError;

        public ManualResetEventSlim CompletedEvent { get; } = new ManualResetEventSlim();

        bool abort_flag = false;
        bool no_more_enqueue = false;

        TaskVmSynchronizationContext sync_ctx;

        public TaskVm(Func<TIn, Task<TResult>> root_action, TIn input_parameter = default(TIn), CancellationToken graceful_cancel = default(CancellationToken), CancellationToken abort_cancel = default(CancellationToken))
        {
            this.InputParameter = input_parameter;
            this.root_function = root_action;
            this.GracefulCancel = graceful_cancel;
            this.AbortCancel = abort_cancel;

            this.AbortCancel.Register(() =>
            {
                Abort(true);
            });

            this.thread = new ThreadObj(this.thread_proc);
            this.thread.WaitForInit();
        }

        public static Task<TResult> NewTaskVm(Func<TIn, Task<TResult>> root_action, TIn input_parameter = default(TIn), CancellationToken graceful_cancel = default(CancellationToken), CancellationToken abort_cancel = default(CancellationToken))
        {
            TaskVm<TResult, TIn> vm = new TaskVm<TResult, TIn>(root_action, input_parameter, graceful_cancel, abort_cancel);

            return Task<TResult>.Run(new Func<TResult>(vm.get_result_simple));
        }

        TResult get_result_simple()
        {
            return this.GetResult();
        }

        public bool Abort(bool no_wait = false)
        {
            this.abort_flag = true;

            this.dispatch_queue_event.Set();

            if (no_wait)
            {
                return this.IsAborted;
            }

            this.thread.WaitForEnd();

            return this.IsAborted;
        }

        public bool Wait(bool ignore_error = false, int timeout = Timeout.Infinite, CancellationToken cancel = default(CancellationToken))
        {
            TResult ret = GetResult(out bool timeouted, ignore_error, timeout, cancel);

            return !timeouted;
        }

        public TResult GetResult(bool ignore_error = false, int timeout = Timeout.Infinite, CancellationToken cancel = default(CancellationToken)) => GetResult(out _, ignore_error, timeout, cancel);
        public TResult GetResult(out bool timeouted, bool ignore_error = false, int timeout = Timeout.Infinite, CancellationToken cancel = default(CancellationToken))
        {
            CompletedEvent.Wait(timeout, cancel);

            if (this.IsCompleted == false)
            {
                timeouted = true;
                return default(TResult);
            }

            timeouted = false;

            if (this.Error != null)
            {
                if (ignore_error == false)
                {
                    throw this.Error;
                }
                else
                {
                    return default(TResult);
                }
            }

            return this.result;
        }

        void thread_proc(object param)
        {
            sync_ctx = new TaskVmSynchronizationContext(this);
            SynchronizationContext.SetSynchronizationContext(sync_ctx);

            ThreadData.CurrentThreadData["taskvm_current_graceful_cancel"] = this.GracefulCancel;

            //Dbg.WriteCurrentThreadId("before task_proc()");

            this.root_task = task_proc();

            dispatcher_loop();
        }

        void set_result(Exception ex = null, TResult result = default(TResult))
        {
            lock (this.ResultLock)
            {
                if (this.IsCompleted == false)
                {
                    this.IsCompleted = true;

                    if (ex == null)
                    {
                        this.result = result;
                    }
                    else
                    {
                        this.Error = ex;

                        if (ex is TaskVmAbortException)
                        {
                            this.IsAborted = true;
                        }
                    }

                    this.dispatch_queue_event.Set();
                    this.CompletedEvent.Set();
                }
            }
        }

        async Task task_proc()
        {
            //Dbg.WriteCurrentThreadId("task_proc: before yield");

            await Task.Yield();

            //Dbg.WriteCurrentThreadId("task_proc: before await");

            TResult ret = default(TResult);

            try
            {
                ret = await this.root_function(this.InputParameter);

                //Dbg.WriteCurrentThreadId("task_proc: after await");
                set_result(null, ret);
            }
            catch (Exception ex)
            {
                set_result(ex);
            }
        }

        void dispatcher_loop()
        {
            int num_executed_tasks = 0;

            while (this.IsCompleted == false)
            {
                this.dispatch_queue_event.WaitOne();

                if (this.abort_flag)
                {
                    set_result(new TaskVmAbortException("aborted."));
                }

                while (true)
                {
                    (SendOrPostCallback callback, object args) queued_item;

                    lock (this.dispatch_queue)
                    {
                        if (this.dispatch_queue.Count == 0)
                        {
                            break;
                        }
                        queued_item = this.dispatch_queue.Dequeue();
                    }

                    if (num_executed_tasks == 0)
                    {
                        ThreadObj.NoticeInited();
                    }
                    num_executed_tasks++;

                    if (this.abort_flag)
                    {
                        set_result(new TaskVmAbortException("aborted."));
                    }

                    try
                    {
                        queued_item.callback(queued_item.args);
                    }
                    catch (Exception ex)
                    {
                        ex.ToString().Debug();
                    }
                }
            }

            no_more_enqueue = true;

            List<(SendOrPostCallback callback, object args)> remaining_tasks = new List<(SendOrPostCallback callback, object args)>();
            lock (this.dispatch_queue)
            {
                while (true)
                {
                    if (this.dispatch_queue.Count == 0)
                    {
                        break;
                    }
                    remaining_tasks.Add(this.dispatch_queue.Dequeue());
                }
                foreach (var x in remaining_tasks)
                {
                    AbortedTaskExecuteThreadPrivate.PostAction(this.sync_ctx, x.callback, x.args);
                }
            }
        }

        public class TaskVmSynchronizationContext : SynchronizationContext
        {
            public readonly TaskVm<TResult, TIn> Vm;
            volatile int num_operations = 0;
            volatile int num_operations_total = 0;


            public bool IsAllOperationsCompleted => (num_operations_total >= 1 && num_operations == 0);

            public TaskVmSynchronizationContext(TaskVm<TResult, TIn> vm)
            {
                this.Vm = vm;
            }

            public override SynchronizationContext CreateCopy()
            {
                //Dbg.WriteCurrentThreadId("CreateCopy");
                return base.CreateCopy();
            }

            public override void OperationCompleted()
            {
                base.OperationCompleted();

                Interlocked.Decrement(ref num_operations);
                //Dbg.WriteCurrentThreadId("OperationCompleted. num_operations = " + num_operations);
                Vm.dispatch_queue_event.Set();
            }

            public override void OperationStarted()
            {
                base.OperationStarted();

                Interlocked.Increment(ref num_operations);
                Interlocked.Increment(ref num_operations_total);
                //Dbg.WriteCurrentThreadId("OperationStarted. num_operations = " + num_operations);
                Vm.dispatch_queue_event.Set();
            }

            public override void Post(SendOrPostCallback d, object state)
            {
                //Dbg.WriteCurrentThreadId("Post: " + this.Vm.halt);
                //base.Post(d, state);
                //d(state);

                bool ok = false;

                lock (Vm.dispatch_queue)
                {
                    if (Vm.no_more_enqueue == false)
                    {
                        Vm.dispatch_queue.Enqueue((d, state));
                        ok = true;
                    }
                }

                if (ok)
                {
                    Vm.dispatch_queue_event.Set();
                }
                else
                {
                    AbortedTaskExecuteThreadPrivate.PostAction(this, d, state);
                }
            }

            public override void Send(SendOrPostCallback d, object state)
            {
                //Dbg.WriteCurrentThreadId("Send");
                base.Send(d, state);
            }

            public override int Wait(IntPtr[] waitHandles, bool waitAll, int millisecondsTimeout)
            {
                //Dbg.WriteCurrentThreadId("Wait");
                return base.Wait(waitHandles, waitAll, millisecondsTimeout);
            }
        }
    }
}

