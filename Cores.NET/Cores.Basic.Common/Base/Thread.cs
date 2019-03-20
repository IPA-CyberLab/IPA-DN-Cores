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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

using IPA.Cores.Helper.Basic;

#pragma warning disable 0618

namespace IPA.Cores.Basic
{
    class BatchQueueItem<T>
    {
        internal BatchQueueItem(T item)
        {
            this.UserItem = item;
            this.IsCompleted = false;
            this.CompletedEvent = new AsyncManualResetEvent();
        }

        public T UserItem { get; }
        public bool IsCompleted { get; internal set; }
        public AsyncManualResetEvent CompletedEvent { get; }

        public void SetCompleted()
        {
            this.IsCompleted = true;
            this.CompletedEvent.Set();
        }
    }

    class BatchQueue<T>
    {
        object lockobj = new object();
        Queue<BatchQueueItem<T>> queue = new Queue<BatchQueueItem<T>>();
        AutoResetEvent new_event_signal = new AutoResetEvent(false);
        public int IdleThreadRemainTimeMsecs { get; }
        public const int DefaultIdleThreadRemainTimeMsecs = 1000;
        Action<BatchQueueItem<T>[]> process_items_proc;

        public BatchQueue(Action<BatchQueueItem<T>[]> process_items_proc, int idle_thread_remain_time_msecs = DefaultIdleThreadRemainTimeMsecs)
        {
            this.IdleThreadRemainTimeMsecs = idle_thread_remain_time_msecs;
            this.process_items_proc = process_items_proc;
        }

        void do_process_list(List<BatchQueueItem<T>> current)
        {
            try
            {
                this.process_items_proc(current.ToArray());
            }
            catch (Exception ex)
            {
                Dbg.WriteLine(ex.ToString());
            }

            foreach (var q in current)
            {
                q.SetCompleted();
            }
        }

        int thread_mode = 0;

        static Benchmark th = new Benchmark("num_thread_created", disabled: true);
        void bg_thread_proc(object param)
        {
            Thread.CurrentThread.IsBackground = true;
            long last_queue_proc_tick = 0;

            //Dbg.WhereThread($"BatchQueue<{typeof(T).Name}>: Start background thread.");
            th.IncrementMe++;

            while (true)
            {
                List<BatchQueueItem<T>> current = new List<BatchQueueItem<T>>();

                lock (lockobj)
                {
                    while (this.queue.Count >= 1)
                    {
                        BatchQueueItem<T> item = this.queue.Dequeue();
                        current.Add(item);
                    }
                }

                if (current.Count >= 1)
                {
                    do_process_list(current);

                    last_queue_proc_tick = Time.Tick64;
                }

                if (this.queue.Count >= 1)
                {
                    continue;
                }

                long now = Time.Tick64;
                long remain_time = last_queue_proc_tick + (long)this.IdleThreadRemainTimeMsecs - now;
                if (remain_time >= 1)
                {
                    new_event_signal.WaitOne((int)remain_time);
                }
                else
                {
                    lock (lockobj)
                    {
                        if (this.queue.Count >= 1)
                        {
                            continue;
                        }

                        thread_mode = 0;

                        //Dbg.WhereThread($"BatchQueue<{typeof(T).Name}>: Stop background thread.");

                        return;
                    }
                }
            }
        }

        public BatchQueueItem<T> Add(T item)
        {
            BatchQueueItem<T> q = new BatchQueueItem<T>(item);

            lock (lockobj)
            {
                this.queue.Enqueue(q);

                if (thread_mode == 0)
                {
                    thread_mode = 1;

                    ThreadObj t = new ThreadObj(bg_thread_proc);
                }

            }

            new_event_signal.Set();

            return q;
        }
    }

    class GlobalLockHandle : IDisposable
    {
        public GlobalLock GlobalLock { get; }
        Mutant mutant;

        internal GlobalLockHandle(GlobalLock g)
        {
            this.GlobalLock = g;

            mutant = Mutant.Create(this.GlobalLock.Name);
            mutant.Lock();
        }

        void unlock_main()
        {
            if (mutant != null)
            {
                mutant.Unlock();
                mutant = null;
            }
        }

        public void Unlock()
        {
            unlock_main();
        }

        public void Dispose()
        {
            unlock_main();
        }
    }

    class GlobalLock
    {
        public string Name { get; }

        public GlobalLock(string name)
        {
            this.Name = name;
        }

        public GlobalLockHandle Lock()
        {
            GlobalLockHandle h = new GlobalLockHandle(this);

            return h;
        }
    }

    class MutantUnix : Mutant
    {

        string filename;
        int locked_count = 0;
        IntPtr fs;

        public MutantUnix(string name)
        {
            filename = Path.Combine(Env.UnixMutantDir, Mutant.GenerateInternalName(name) + ".lock");
            IO.MakeDirIfNotExists(Env.UnixMutantDir);
        }

        public override void Lock()
        {
            if (locked_count == 0)
            {
                IO.MakeDirIfNotExists(Env.UnixMutantDir);

                Unisys.Permissions perm = Unisys.Permissions.S_IRUSR | Unisys.Permissions.S_IWUSR | Unisys.Permissions.S_IRGRP | Unisys.Permissions.S_IWGRP | Unisys.Permissions.S_IROTH | Unisys.Permissions.S_IWOTH;

                IntPtr f = Unisys.Open(filename, Unisys.OpenFlags.O_CREAT, (int)perm);
                if (f.ToInt64() < 0)
                {
                    throw new IOException("Open failed.");
                }

                if (Unisys.FLock(f, Unisys.LockOperations.LOCK_EX) == -1)
                {
                    throw new IOException("FLock failed.");
                }

                this.fs = f;

                locked_count++;
            }
        }

        public override void Unlock()
        {
            if (locked_count <= 0) throw new ApplicationException("locked_count <= 0");
            if (locked_count == 1)
            {
                Unisys.Close(this.fs);

                this.fs = IntPtr.Zero;
            }
            locked_count--;
        }
    }

    class MutantWin32 : Mutant
    {
        Mutex mutex;
        int locked_count = 0;

        public MutantWin32(string name)
        {
            bool f;
            mutex = new Mutex(false, Mutant.GenerateInternalName(name), out f);
        }

        public override void Lock()
        {
            if (locked_count == 0)
            {
                int num_retry = 0;
                LABEL_RETRY:

                try
                {
                    mutex.WaitOne();
                }
                catch (AbandonedMutexException)
                {
                    if (num_retry >= 100) throw;
                    num_retry++;
                    goto LABEL_RETRY;
                }
            }
            locked_count++;
        }

        public override void Unlock()
        {
            if (locked_count <= 0) throw new ApplicationException("locked_count <= 0");
            if (locked_count == 1)
            {
                mutex.ReleaseMutex();
            }
            locked_count--;
        }
    }

    abstract class Mutant
    {
        public static string GenerateInternalName(string name)
        {
            name = name.Trim().ToUpperInvariant();
            return "dnmutant_" + Str.ByteToStr(Str.HashStr(name)).ToLowerInvariant();
        }

        public abstract void Lock();
        public abstract void Unlock();

        public static Mutant Create(string name)
        {
            if (Env.IsWindows)
            {
                return new MutantWin32(name);
            }
            else
            {
                return new MutantUnix(name);
            }
        }
    }

    class SemaphoneArrayItem
    {
        public Semaphore Semaphone;
        public int MaxCount;
        public int CurrentWaitThreads;

        public SemaphoneArrayItem(int max)
        {
            this.Semaphone = new Semaphore(max - 1, max);
            this.MaxCount = max;
        }
    }

    class SemaphoneArray
    {
        Dictionary<string, SemaphoneArrayItem> array = new Dictionary<string, SemaphoneArrayItem>();

        string normalize_name(string name)
        {
            Str.NormalizeString(ref name);
            name = name.ToUpperInvariant();
            return name;
        }

        public int GetNumSemaphone
        {
            get
            {
                lock (array)
                {
                    return array.Count;
                }
            }
        }

        public void Release(string name)
        {
            name = normalize_name(name);

            SemaphoneArrayItem sem;

            lock (array)
            {
                if (array.ContainsKey(name) == false)
                {
                    throw new ApplicationException("invalid semaphone name: " + name);
                }

                sem = array[name];

                int c = sem.Semaphone.Release();

                if ((c + 1) == sem.MaxCount)
                {
                    if (sem.CurrentWaitThreads == 0)
                    {
                        //sem.Semaphone.Dispose();

                        array.Remove(name);
                    }
                }
            }

        }

        public bool Wait(string name, int max_count, int timeout)
        {
            if (max_count <= 0 || (timeout < 0 && timeout != -1))
            {
                throw new ApplicationException("Invalid args.");
            }

            name = normalize_name(name);

            SemaphoneArrayItem sem = null;

            lock (array)
            {
                if (array.ContainsKey(name) == false)
                {
                    sem = new SemaphoneArrayItem(max_count);

                    array.Add(name, sem);

                    return true;
                }
                else
                {
                    sem = array[name];

                    if (sem.MaxCount != max_count)
                    {
                        throw new ApplicationException("max_count != current database value.");
                    }

                    if (sem.Semaphone.WaitOne(0))
                    {
                        return true;
                    }

                    Interlocked.Increment(ref sem.CurrentWaitThreads);
                }
            }

            bool ret = sem.Semaphone.WaitOne(timeout);

            Interlocked.Decrement(ref sem.CurrentWaitThreads);

            return ret;
        }
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

    class WorkerQueuePrivate
    {
        object lockObj = new object();

        List<ThreadObj> thread_list;
        ThreadProc thread_proc;
        int num_worker_threads;
        Queue<object> taskQueue = new Queue<object>();
        Exception raised_exception = null;

        void worker_thread(object param)
        {
            while (true)
            {
                object task = null;

                // キューから 1 個取得する
                lock (lockObj)
                {
                    if (taskQueue.Count == 0)
                    {
                        return;
                    }
                    task = taskQueue.Dequeue();
                }

                // タスクを処理する
                try
                {
                    this.thread_proc(task);
                }
                catch (Exception ex)
                {
                    if (raised_exception == null)
                    {
                        raised_exception = ex;
                    }

                    Dbg.WriteLine(ex.Message);
                }
            }
        }

        public WorkerQueuePrivate(ThreadProc thread_proc, int num_worker_threads, object[] tasks)
        {
            thread_list = new List<ThreadObj>();
            int i;

            this.thread_proc = thread_proc;
            this.num_worker_threads = num_worker_threads;

            foreach (object task in tasks)
            {
                taskQueue.Enqueue(task);
            }

            raised_exception = null;

            for (i = 0; i < num_worker_threads; i++)
            {
                ThreadObj t = new ThreadObj(worker_thread);

                thread_list.Add(t);
            }

            foreach (ThreadObj t in thread_list)
            {
                t.WaitForEnd();
            }

            if (raised_exception != null)
            {
                throw raised_exception;
            }
        }
    }

    static class Tick64
    {
        public static long Value => FastTick64.Now;

        public static uint ValueUInt32
        {
            get
            {
                unchecked
                {
                    return (uint)((ulong)Value);
                }
            }
        }
    }

    class Event
    {
        EventWaitHandle h;
        public const int Infinite = Timeout.Infinite;

        public Event()
        {
            init(false);
        }

        public Event(bool manualReset)
        {
            init(manualReset);
        }

        void init(bool manualReset)
        {
            h = new EventWaitHandle(false, (manualReset ? EventResetMode.ManualReset : EventResetMode.AutoReset));
        }

        public void Set()
        {
            h.Set();
        }

        public bool Wait()
        {
            return Wait(Infinite);
        }
        public bool Wait(int millisecs)
        {
            return h.WaitOne(millisecs, false);
        }

        public delegate bool WaitWithPollDelegate();

        public bool WaitWithPoll(int wait_millisecs, int poll_interval, WaitWithPollDelegate proc)
        {
            long end_tick = Time.Tick64 + (long)wait_millisecs;

            if (wait_millisecs == Infinite)
            {
                end_tick = long.MaxValue;
            }

            while (true)
            {
                long now = Time.Tick64;
                if (wait_millisecs != Infinite)
                {
                    if (now >= end_tick)
                    {
                        return false;
                    }
                }

                long next_wait = (end_tick - now);
                next_wait = Math.Min(next_wait, (long)poll_interval);
                next_wait = Math.Max(next_wait, 1);

                if (proc != null)
                {
                    if (proc())
                    {
                        return true;
                    }
                }

                if (Wait((int)next_wait))
                {
                    return true;
                }
            }
        }

        static EventWaitHandle[] toArray(Event[] events)
        {
            List<EventWaitHandle> list = new List<EventWaitHandle>();

            foreach (Event e in events)
            {
                list.Add(e.h);
            }

            return list.ToArray();
        }

        public static bool WaitAll(Event[] events)
        {
            return WaitAll(events, Infinite);
        }
        public static bool WaitAll(Event[] events, int millisecs)
        {
            if (events.Length <= 64)
            {
                return waitAllInner(events, millisecs);
            }
            else
            {
                return waitAllMulti(events, millisecs);
            }
        }

        static bool waitAllMulti(Event[] events, int millisecs)
        {
            int numBlocks = (events.Length + 63) / 64;
            List<Event>[] list = new List<Event>[numBlocks];
            int i;
            for (i = 0; i < numBlocks; i++)
            {
                list[i] = new List<Event>();
            }
            for (i = 0; i < events.Length; i++)
            {
                list[i / 64].Add(events[i]);
            }

            double start = Time.NowDouble;
            double giveup = start + (double)millisecs / 1000.0;
            foreach (List<Event> o in list)
            {
                double now = Time.NowDouble;
                if (now <= giveup || millisecs < 0)
                {
                    int waitmsecs;
                    if (millisecs >= 0)
                    {
                        waitmsecs = (int)((giveup - now) * 1000.0);
                    }
                    else
                    {
                        waitmsecs = Timeout.Infinite;
                    }

                    bool ret = waitAllInner(o.ToArray(), waitmsecs);
                    if (ret == false)
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        static bool waitAllInner(Event[] events, int millisecs)
        {
            if (events.Length == 1)
            {
                return events[0].Wait(millisecs);
            }
            return EventWaitHandle.WaitAll(toArray(events), millisecs, false);
        }

        public static bool WaitAny(Event[] events)
        {
            return WaitAny(events, Infinite);
        }
        public static bool WaitAny(Event[] events, int millisecs)
        {
            if (events.Length == 1)
            {
                return events[0].Wait(millisecs);
            }
            return ((WaitHandle.WaitTimeout == EventWaitHandle.WaitAny(toArray(events), millisecs, false)) ? false : true);
        }

        public IntPtr Handle
        {
            get
            {
                return h.Handle;
            }
        }
    }

    class ThreadData
    {
        static LocalDataStoreSlot slot = Thread.AllocateDataSlot();

        public static ConcurrentDictionary<string, object> CurrentThreadData
        {
            get
            {
                return GetCurrentThreadData();
            }
        }

        public static ConcurrentDictionary<string, object> GetCurrentThreadData()
        {
            ConcurrentDictionary<string, object> t;

            try
            {
                t = (ConcurrentDictionary<string, object>)Thread.GetData(slot);
            }
            catch
            {
                t = null;
            }

            if (t == null)
            {
                t = new ConcurrentDictionary<string, object>();

                Thread.SetData(slot, t);
            }

            return t;
        }
    }

    delegate void ThreadProc(object userObject);

    class ThreadObj
    {
        public readonly static RefInt NumCurrentThreads = new RefInt();

        static Once g_DebugReportNumCurrentThreads_flag;
        public static void DebugReportNumCurrentThreads()
        {
            if (g_DebugReportNumCurrentThreads_flag.IsFirstCall())
            {
                GlobalIntervalReporter.Singleton.ReportRefObject("NumThreads", NumCurrentThreads);
            }
        }

        static int defaultStackSize = 100000;

        static LocalDataStoreSlot currentObjSlot = Thread.AllocateDataSlot();

        public const int Infinite = Timeout.Infinite;

        bool stopped = false;

        public bool IsFinished
        {
            get
            {
                return this.stopped;
            }
        }

        ThreadProc proc;
        Thread thread;
        EventWaitHandle waitEnd;
        AsyncManualResetEvent waitEndAsync;
        EventWaitHandle waitInitForUser;
        AsyncManualResetEvent waitInitForUserAsync;
        public Thread Thread
        {
            get { return thread; }
        }
        object userObject;
        int index;
        public int Index { get => this.index; }
        public string Name { get; }

        public ThreadObj(ThreadProc threadProc, object userObject = null, int stacksize = 0, int index = 0, string name = null, bool is_background = false)
        {
            if (stacksize == 0)
            {
                stacksize = defaultStackSize;
            }

            if (name.IsEmpty())
            {
                try
                {
                    name = threadProc.Target.ToString() + "." + threadProc.Method.Name + "()";
                }
                catch
                {
                    try
                    {
                        name = threadProc.Method.DeclaringType + "." + threadProc.Method.Name + "()";
                    }
                    catch
                    {
                    }
                }
            }

            this.Name = name;

            this.proc = threadProc;
            this.userObject = userObject;
            this.index = index;
            waitEnd = new EventWaitHandle(false, EventResetMode.ManualReset);
            waitEndAsync = new AsyncManualResetEvent();
            waitInitForUser = new EventWaitHandle(false, EventResetMode.ManualReset);
            waitInitForUserAsync = new AsyncManualResetEvent();
            NumCurrentThreads.Increment();
            this.thread = new Thread(new ParameterizedThreadStart(commonThreadProc), stacksize);
            if (this.Name.IsFilled())
            {
                this.thread.Name = this.Name;
            }
            this.thread.IsBackground = is_background;
            this.thread.Start(this);
        }

        public static ThreadObj Start(ThreadProc proc, object param = null, bool is_background = false)
        {
            return new ThreadObj(proc, param, is_background: is_background);
        }

        public static ThreadObj[] StartMany(int num, ThreadProc proc, object param = null, bool is_background = false)
        {
            List<ThreadObj> ret = new List<ThreadObj>();
            for (int i = 0; i < num; i++)
            {
                ThreadObj t = new ThreadObj(proc, param, 0, i, is_background: is_background);
                ret.Add(t);
            }
            return ret.ToArray();
        }

        public static int DefaultStackSize
        {
            get
            {
                return defaultStackSize;
            }

            set
            {
                defaultStackSize = value;
            }
        }

        void commonThreadProc(object obj)
        {
            Thread.SetData(currentObjSlot, this);

            try
            {
                this.proc(this.userObject);
            }
            finally
            {
                stopped = true;
                NumCurrentThreads.Decrement();
                waitEnd.Set();
                waitEndAsync.Set();
            }
        }

        public static ThreadObj Current => GetCurrentThreadObj();
        public static int CurrentThreadId => Thread.CurrentThread.ManagedThreadId;

        public static ThreadObj GetCurrentThreadObj()
        {
            return (ThreadObj)Thread.GetData(currentObjSlot);
        }

        public static void NoticeInited()
        {
            GetCurrentThreadObj().waitInitForUser.Set();
            GetCurrentThreadObj().waitInitForUserAsync.Set();
        }

        public void WaitForInit()
        {
            waitInitForUser.WaitOne();
        }

        public Task WaitForInitAsync()
        {
            return waitInitForUserAsync.WaitAsync();
        }

        public void WaitForEnd(int timeout)
        {
            waitEnd.WaitOne(timeout, false);
        }
        public void WaitForEnd()
        {
            waitEnd.WaitOne();
        }

        public Task WaitForEndAsync()
        {
            return waitEndAsync.WaitAsync();
        }

        public static void Sleep(int millisec)
        {
            if (millisec == 0x7fffffff)
            {
                millisec = ThreadObj.Infinite;
            }

            Thread.Sleep(millisec);
        }

        public static void Yield()
        {
            Thread.Sleep(0);
        }

        public static void ProcessWorkQueue(ThreadProc thread_proc, int num_worker_threads, object[] tasks)
        {
            WorkerQueuePrivate q = new WorkerQueuePrivate(thread_proc, num_worker_threads, tasks);
        }
    }

    static class StillRunningThreadRegister
    {
        public static int RegularWatchInterval = 1 * 1000;

        static ConcurrentDictionary<string, Func<bool>> callbacks_list = new ConcurrentDictionary<string, Func<bool>>();

        static AutoResetEvent ev = new AutoResetEvent(true);

        static StillRunningThreadRegister()
        {
            ThreadObj t = new ThreadObj(thread_proc);
        }

        public static string RegisterRefInt(RefInt r)
        {
            return RegisterCallback(() =>
            {
                return (r.Value != 0);
            });
        }

        public static string RegisterRefBool(RefBool r)
        {
            return RegisterCallback(() =>
            {
                return r.Value;
            });
        }

        public static string RegisterCallback(Func<bool> proc)
        {
            string key = Str.NewGuid();

            ManualResetEventSlim ev = new ManualResetEventSlim(false);
            Once once_flag = new Once();

            callbacks_list[key] = new Func<bool>(() =>
            {
                if (once_flag.IsFirstCall())
                {
                    ev.Set();
                }
                return proc();
            });

            NotifyStatusChanges();

            ev.Wait();

            return key;
        }

        public static void Unregister(string key)
        {
            callbacks_list.TryRemove(key, out _);
        }

        public static void NotifyStatusChanges()
        {
            ev.Set();
        }

        static void thread_proc(object param)
        {
            Thread.CurrentThread.Priority = ThreadPriority.Highest;

            while (true)
            {
                var procs = callbacks_list.Values;

                bool prevent = false;

                foreach (var proc in procs)
                {
                    bool ret = false;
                    try
                    {
                        ret = proc();
                    }
                    catch
                    {
                    }

                    if (ret)
                    {
                        prevent = true;
                    }
                }

                Thread.CurrentThread.IsBackground = !prevent;

                ev.WaitOne(RegularWatchInterval);
            }
        }
    }
}
