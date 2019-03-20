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

using IPA.Cores.Helper.Basic;
using System.Runtime.CompilerServices;

namespace IPA.Cores.Basic
{
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

