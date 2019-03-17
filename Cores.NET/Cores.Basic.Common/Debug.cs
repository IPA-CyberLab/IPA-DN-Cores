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
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Net;

using IPA.Cores.Helper.Basic;

namespace IPA.Cores.Basic
{
    static partial class Dbg
    {
        static bool is_debug_mode = false;
        public static bool IsDebugMode => is_debug_mode;

        public static void SetDebugMode(bool b = true) => is_debug_mode = b;

        public static void Report(string name, string value)
        {
            if (Dbg.IsDebugMode) GlobalIntervalReporter.Singleton.Report(name, value);
        }

        public static string WriteLine()
        {
            WriteLine("");
            return "";
        }
        public static string WriteLine(string str)
        {
            if (str == null) str = "null";
            if (Dbg.IsDebugMode)
            {
                Console.WriteLine(str);
            }
            Debug.WriteLine(str);
            return str;
        }
        public static void WriteLine(string str, params object[] args)
        {
            if (Dbg.IsDebugMode)
            {
                Console.WriteLine(str, args);
            }
            Debug.WriteLine(str);
        }

        public static long Where(string msg = "", [CallerFilePath] string filename = "", [CallerLineNumber] int line = 0, [CallerMemberName] string caller = null, long last_tick = 0)
        {
            if (Dbg.IsDebugMode)
            {
                long now = Time.Tick64;
                long diff = now - last_tick;
                WriteLine($"{Path.GetFileName(filename)}:{line} in {caller}()" + (last_tick == 0 ? "" : $" (took {diff} msecs) ") + (Str.IsFilledStr(msg) ? (": " + msg) : ""));
                return now;
            }
            else
            {
                return 0;
            }
        }

        public static long WhereThread(string msg = "", [CallerFilePath] string filename = "", [CallerLineNumber] int line = 0, [CallerMemberName] string caller = null, long last_tick = 0)
        {
            if (Dbg.IsDebugMode)
            {
                long now = Time.Tick64;
                long diff = now - last_tick;
                WriteLine($"Thread[{ThreadObj.CurrentThreadId}]: {Path.GetFileName(filename)}:{line} in {caller}()" + (last_tick == 0 ? "" : $" (took {diff} msecs) ") + (Str.IsFilledStr(msg) ? (": " + msg) : ""));
                return now;
            }
            else
            {
                return 0;
            }
        }

        public static string GetObjectInnerString(object obj, string instance_base_name = "")
        {
            return GetObjectInnerString(obj.GetType(), obj, instance_base_name);
        }
        public static string GetObjectInnerString(Type t)
        {
            return GetObjectInnerString(t, null, null);
        }
        public static string GetObjectInnerString(Type t, object obj, string instance_base_name)
        {
            DebugVars v = GetVarsFromClass(t, instance_base_name, obj);

            return v.ToString();
        }

        public static void WriteObject(object obj, string instance_base_name = "")
        {
            WriteObject(obj.GetType(), obj, instance_base_name);
        }
        public static void WriteObject(Type t)
        {
            WriteObject(t, null, null);
        }
        public static void WriteObject(Type t, object obj, string instance_base_name)
        {
            if (Dbg.IsDebugMode == false)
            {
                return;
            }

            DebugVars v = GetVarsFromClass(t, instance_base_name, obj);

            string str = v.ToString();

            Console.WriteLine(str);
        }

        public static void PrintObjectInnerString(object obj, string instance_base_name = "")
        {
            PrintObjectInnerString(obj.GetType(), obj, instance_base_name);
        }
        public static void PrintObjectInnerString(Type t)
        {
            PrintObjectInnerString(t, null, null);
        }
        public static void PrintObjectInnerString(Type t, object obj, string instance_base_name)
        {
            DebugVars v = GetVarsFromClass(t, instance_base_name, obj);

            string str = v.ToString();

            Console.WriteLine(str);
        }

        public static DebugVars GetVarsFromClass(Type t, string name = null, object obj = null, ImmutableHashSet<object> duplicate_check = null)
        {
            if (duplicate_check == null) duplicate_check = ImmutableHashSet<object>.Empty;

            if (Str.IsEmptyStr(name)) name = t.Name;

            DebugVars ret = new DebugVars();

            var members_list = GetAllMembersFromType(t, obj != null, obj == null);

            foreach (MemberInfo info in members_list)
            {
                bool ok = false;
                if (info.MemberType == MemberTypes.Field)
                {
                    FieldInfo fi = info as FieldInfo;

                    ok = true;

                    if (fi.IsInitOnly)
                    {
                        ok = false;
                    }
                }
                else if (info.MemberType == MemberTypes.Property)
                {
                    PropertyInfo pi = info as PropertyInfo;

                    ok = true;
                }

                if (ok)
                {
                    //if (info.Name == "lockFile") Debugger.Break();

                    object data = GetValueOfFieldOrProperty(info, obj);
                    Type data_type = data?.GetType() ?? null;

                    if (IsPrimitiveType(data_type))
                    {
                        ret.Vars.Add((info, data));
                    }
                    else
                    {
                        if (data == null)
                        {
                            ret.Vars.Add((info, null));
                        }
                        else
                        {
                            if (data is IEnumerable)
                            {
                                int n = 0;
                                foreach (object item in (IEnumerable)data)
                                {
                                    if (duplicate_check.Contains(item) == false)
                                    {
                                        Type data_type2 = item?.GetType() ?? null;

                                        if (IsPrimitiveType(data_type2))
                                        {
                                            ret.Vars.Add((info, item));
                                        }
                                        else if (item == null)
                                        {
                                            ret.Vars.Add((info, null));
                                        }
                                        else
                                        {
                                            ret.Childlen.Add(GetVarsFromClass(data_type2, info.Name, item, duplicate_check.Add(data)));
                                        }
                                    }

                                    n++;
                                }
                            }
                            else
                            {
                                if (duplicate_check.Contains(data) == false)
                                {
                                    ret.Childlen.Add(GetVarsFromClass(data_type, info.Name, data, duplicate_check.Add(data)));
                                }
                            }
                        }
                    }
                }
            }

            ret.BaseName = name;

            return ret;
        }

        public static MemberInfo[] GetAllMembersFromType(Type t, bool hide_static, bool hide_instance)
        {
            HashSet<MemberInfo> a = new HashSet<MemberInfo>();

            if (hide_static == false)
            {
                a.UnionWith(t.GetMembers(BindingFlags.Static | BindingFlags.Public));
                a.UnionWith(t.GetMembers(BindingFlags.Static | BindingFlags.NonPublic));
            }

            if (hide_instance == false)
            {
                a.UnionWith(t.GetMembers(BindingFlags.Instance | BindingFlags.Public));
                a.UnionWith(t.GetMembers(BindingFlags.Instance | BindingFlags.NonPublic));
            }

            MemberInfo[] ret = new MemberInfo[a.Count];
            a.CopyTo(ret);
            return ret;
        }

        public static object GetValueOfFieldOrProperty(MemberInfo m, object obj)
        {
            switch (m)
            {
                case PropertyInfo p:
                    try
                    {
                        return p.GetValue(obj);
                    }
                    catch
                    {
                        return null;
                    }

                case FieldInfo f:
                    return f.GetValue(obj);
            }

            return null;
        }

        public static bool IsPrimitiveType(Type t)
        {
            if (t == null) return true;
            if (t.IsSubclassOf(typeof(System.Type))) return true;
            if (t.IsSubclassOf(typeof(System.Delegate))) return true;
            if (t.IsEnum) return true;
            if (t.IsPrimitive) return true;
            if (t == typeof(System.Delegate)) return true;
            if (t == typeof(System.MulticastDelegate)) return true;
            if (t == typeof(string)) return true;
            if (t == typeof(DateTime)) return true;
            if (t == typeof(TimeSpan)) return true;
            if (t == typeof(IPAddr)) return true;
            if (t == typeof(IPAddress)) return true;
            if (t == typeof(System.Numerics.BigInteger)) return true;

            return false;
        }

        public static void Suspend() => Kernel.SuspendForDebug();

        public static void Break() => Debugger.Break();
    }

    class DebugVars
    {
        public string BaseName = "";

        public List<(MemberInfo member_info, object data)> Vars = new List<(MemberInfo, object)>();
        public List<DebugVars> Childlen = new List<DebugVars>();

        public void WriteToString(StringWriter w, ImmutableList<string> parents)
        {
            this.Vars.Sort((a, b) => string.Compare(a.member_info.Name, b.member_info.Name));
            this.Childlen.Sort((a, b) => string.Compare(a.BaseName, b.BaseName));

            foreach (DebugVars var in Childlen)
            {
                var.WriteToString(w, parents.Add(var.BaseName));
            }

            foreach (var data in Vars)
            {
                MemberInfo p = data.member_info;
                object o = data.data;
                string print_str = "null";
                string closure = "'";
                if ((o?.GetType().IsPrimitive ?? true) || (o?.GetType().IsEnum ?? false)) closure = "";
                if (o != null)
                {
                    print_str = $"{closure}{o.ToString()}{closure}";
                }

                w.WriteLine($"{Str.CombineStringArray(ImmutableListToArray<string>(parents), ".")}.{p.Name} = {print_str}");
            }
        }

        public static T[] ImmutableListToArray<T>(ImmutableList<T> input)
        {
            T[] ret = new T[input.Count];
            input.CopyTo(ret);
            return ret;
        }

        public override string ToString()
        {
            ImmutableList<string> parents = ImmutableList.Create<string>(this.BaseName);
            StringWriter w = new StringWriter();

            WriteToString(w, parents);

            return w.ToString();
        }
    }

    class IntervalDebug
    {
        public string Name { get; }
        public IntervalDebug(string name = "Interval") => this.Name = name.NonNullTrim();
        long start_tick = 0;
        public void Start() => this.start_tick = Time.Tick64;
        public int Elapsed => (int)(Time.Tick64 - this.start_tick);
        public void PrintElapsed()
        {
            if (Dbg.IsDebugMode)
            {
                int value = this.Elapsed;
                Dbg.WriteLine($"{this.Name}: {value}");
            }
            Start();
        }
    }

    class Benchmark : IDisposable
    {
        public int Interval { get; }
        public long IncrementMe = 0;
        Once d;
        ThreadObj thread;
        ManualResetEventSlim halt_event = new ManualResetEventSlim();
        bool halt_flag = false;
        public string Name { get; }

        public Benchmark(string name = "Benchmark", int interval = 1000, bool disabled = false)
        {
            this.Interval = interval;
            this.Name = name;

            if (disabled == false)
            {
                this.thread = new ThreadObj(thread_proc);
            }
        }

        void thread_proc(object param)
        {
            Thread.CurrentThread.IsBackground = true;
            try
            {
                Thread.CurrentThread.Priority = ThreadPriority.Highest;
            }
            catch
            {
            }
            long last_value = 0;
            long last_tick = Time.Tick64;
            while (true)
            {
                int wait_interval = this.Interval;
                if (halt_flag) break;
                halt_event.Wait(wait_interval);
                if (halt_flag) break;

                long now_value = this.IncrementMe;
                long diff_value = now_value - last_value;

                last_value = now_value;
                long now_time = Time.Tick64;
                long span2 = now_time - last_tick;
                last_tick = now_time;
                span2 = Math.Max(span2, 1);

                double r = (double)diff_value * 1000.0 / (double)span2;

                string name = this.Name;
                string value = $"{Str.ToStr3((long)r)} / sec";

                GlobalIntervalReporter.Singleton.Report(name, value);
                //Dbg.WriteLine($"{name}: {value}");
            }

            GlobalIntervalReporter.Singleton.Report(this.Name, null);
        }

        public void Dispose()
        {
            if (d.IsFirstCall())
            {
                halt_flag = true;
                halt_event.Set();
                this.thread.WaitForEnd();
            }
        }
    }

    static class SingletonFactory
    {
        static Dictionary<string, object> table = new Dictionary<string, object>();

        public static T New<T>() where T : new()
        {
            Type t = typeof(T);
            string name = t.AssemblyQualifiedName;
            lock (table)
            {
                object ret = null;
                if (table.ContainsKey(name))
                    ret = table[name];
                else
                {
                    ret = new T();
                    table[name] = ret;
                }
                return (T)ret;
            }
        }
    }

    partial class GlobalIntervalReporter
    {
        public const int Interval = 1000;
        SortedList<string, Ref<(int ver, string value)>> table = new SortedList<string, Ref<(int ver, string value)>>();
        SortedList<string, object> table2 = new SortedList<string, object>();
        ThreadObj thread;

        public static GlobalIntervalReporter Singleton { get => SingletonFactory.New<GlobalIntervalReporter>(); }

        public GlobalIntervalReporter()
        {
            thread = new ThreadObj(main_thread);
        }

        public void ReportRefObject(string name, object ref_obj)
        {
            name = name.NonNullTrim();
            lock (table2)
            {
                if (table2.ContainsKey(name))
                {
                    if (ref_obj == null) table2.Remove(name);
                    table2[name] = ref_obj;
                }
                else
                {
                    if (ref_obj == null) return;
                    table2.Add(name, ref_obj);
                }
            }
        }

        public void Report(string name, string value)
        {
            if (Dbg.IsDebugMode == false) return;
            name = name.NonNullTrim();
            lock (table)
            {
                Ref<(int ver, string value)> r;
                if (table.ContainsKey(name))
                {
                    if (value.IsEmpty()) table.Remove(name);
                    r = table[name];
                }
                else
                {
                    if (value.IsEmpty()) return;
                    r = new Ref<(int ver, string value)>();
                    table.Add(name, r);
                }
                r.Set((r.Value.ver + 1, value));
            }
        }

        void print()
        {
            List<string> o = new List<string>();
            lock (table)
            {
                foreach (string name in table.Keys)
                {
                    var r = table[name];
                    o.Add($"{name}: {r.Value.value}");
                }
                foreach (string name in table2.Keys)
                {
                    object r = table2[name];
                    try
                    {
                        o.Add($"{name}: {r.ToString()}");
                    }
                    catch
                    {
                    }
                }
            }
            string s = Str.CombineStringArray2(", ", o.ToArray());

            Dbg.WriteLine(s);
        }

        void main_thread(object param)
        {
            Thread.CurrentThread.Priority = ThreadPriority.Highest;
            Thread.CurrentThread.IsBackground = true;
            IntervalManager m = new IntervalManager(Interval);
            while (true)
            {
                print();
                Kernel.SleepThread(m.GetNextInterval());
            }
        }
    }


    class IntervalReporter : IDisposable
    {
        public int Interval { get; }
        Once d;
        ThreadObj thread;
        ManualResetEventSlim halt_event = new ManualResetEventSlim();
        bool halt_flag = false;
        public string Name { get; }
        public object SetMeToPrint = null;
        public const int DefaultInterval = 1000;
        public Func<string> PrintProc { get; }

        public IntervalReporter(string name = "Reporter", int interval = DefaultInterval, Func<string> print_proc = null)
        {
            if (interval == 0) interval = DefaultInterval;
            this.Interval = interval;
            this.Name = name;
            this.PrintProc = print_proc;

            if (Dbg.IsDebugMode)
            {
                this.thread = new ThreadObj(thread_proc);
            }
        }

        void thread_proc(object param)
        {
            Thread.CurrentThread.IsBackground = true;
            IntervalManager m = new IntervalManager(this.Interval);
            while (true)
            {
                int wait_interval = m.GetNextInterval(out int span);
                if (halt_flag) break;
                halt_event.Wait(wait_interval);
                if (halt_flag) break;

                //try
                {
                    Print();
                }
                //catch
                {
                }
            }
        }

        public virtual void Print()
        {
            string str = "";
            object obj = this.SetMeToPrint;
            if (obj != null) str = obj.ToString();
            if (this.PrintProc != null)
            {
                str = this.PrintProc();
            }
            if (str.IsFilled())
            {
                Dbg.WriteLine($"{this.Name}: {str}");
            }
        }

        public void Dispose()
        {
            if (d.IsFirstCall())
            {
                halt_flag = true;
                halt_event.Set();
                this.thread.WaitForEnd();
            }
        }

        static Singleton<IntervalReporter> thread_pool_stat_reporter;
        public static IntervalReporter StartThreadPoolStatReporter()
        {
            return thread_pool_stat_reporter.CreateOrGet(() =>
            {
                return new IntervalReporter("<Stat>", print_proc: () =>
                {
                    List<string> o = new List<string>();

                    ThreadPool.GetAvailableThreads(out int avail_workers, out int avail_ports);
                    ThreadPool.GetMaxThreads(out int max_workers, out int max_ports);
                    ThreadPool.GetMinThreads(out int min_workers, out int min_ports);
                    long mem = GC.GetTotalMemory(true);

                    int num_queued = TaskUtil.GetQueuedTasksCount();

                    int num_timered = TaskUtil.GetScheduledTimersCount();

                    o.Add($"Work: {max_workers - avail_workers}");

                    o.Add($"Pend: {num_queued}");

                    o.Add($"Delay: {num_timered}");

                    o.Add($"I/O: {max_ports - avail_ports}");

                    o.Add($"Mem: {(mem / 1024).ToStr3()} kb");

                    return Str.CombineStringArray(o.ToArray(), ", ");
                });
            });
        }
    }
}

