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
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.GlobalFunctions.Basic;

namespace IPA.Cores.Basic
{
    [Flags]
    enum DebugMode
    {
        Debug, // Default
        ReleaseWithLogs,
        ReleaseNoDebugLogs,
        ReleaseNoLogs,
    }

    static partial class AppConfig
    {
        public static partial class DebugSettings
        {
            public static readonly Copenhagen<LogPriority> LogMinimalDebugLevel = LogPriority.Minimal;
            public static readonly Copenhagen<LogPriority> LogMinimalInfoLevel = LogPriority.Info;

            public static readonly Copenhagen<LogPriority> LogMinimalDataLevel = LogPriority.Minimal;
            public static readonly Copenhagen<LogPriority> LogMinimalAccessLevel = LogPriority.Minimal;

            public static readonly Copenhagen<LogPriority> ConsoleMinimalLevel = LogPriority.Minimal;

            public static void SetDebugMode(DebugMode mode = DebugMode.Debug)
            {
                switch (mode)
                {
                    case DebugMode.Debug:
                        LogMinimalDebugLevel.Set(LogPriority.Minimal);
                        LogMinimalInfoLevel.Set(LogPriority.Info);

                        ConsoleMinimalLevel.Set(LogPriority.Minimal);
                        break;

                    case DebugMode.ReleaseWithLogs:
                        LogMinimalDebugLevel.Set(LogPriority.Minimal);
                        LogMinimalInfoLevel.Set(LogPriority.Info);

                        ConsoleMinimalLevel.Set(LogPriority.Info);
                        break;

                    case DebugMode.ReleaseNoDebugLogs:
                        LogMinimalDebugLevel.Set(LogPriority.None);
                        LogMinimalInfoLevel.Set(LogPriority.Info);

                        ConsoleMinimalLevel.Set(LogPriority.Info);
                        break;

                    case DebugMode.ReleaseNoLogs:
                        LogMinimalDebugLevel.Set(LogPriority.None);
                        LogMinimalInfoLevel.Set(LogPriority.None);

                        ConsoleMinimalLevel.Set(LogPriority.Info);
                        break;

                    default:
                        throw new ArgumentException("mode");
                }
            }

            public static bool IsDebugMode()
            {
                if (LogMinimalDebugLevel.Get() <= LogPriority.Debug ||
                    LogMinimalInfoLevel.Get() <= LogPriority.Debug ||
                    ConsoleMinimalLevel.Get() <= LogPriority.Debug)
                {
                    return true;
                }

                return false;
            }

            public static bool IsConsoleDebugMode()
            {
                if (ConsoleMinimalLevel.Get() <= LogPriority.Debug)
                {
                    return true;
                }

                return false;
            }
        }
    }

    class DebugWhereContainer
    {
        public object Msg;
        public string Where;
        public string Function;
        public int? ThreadID;

        public DebugWhereContainer(object message, string filename, int lineNumber, int threadId, string callerName)
        {
            this.Msg = message;
            if (filename.IsFilled())
                this.Where = filename + ":" + lineNumber;
            this.ThreadID = threadId == 0 ? (int ?)null : threadId;
            this.Function = callerName;
        }
    }

    static partial class Dbg
    {
        static Dbg()
        {
            bool isJsonSupported = false;
            InternalIsJsonSupported(ref isJsonSupported);
            Dbg.IsJsonSupported = isJsonSupported;
        }

        static GlobalInitializer gInit = new GlobalInitializer();

        public static void SetDebugMode(DebugMode mode = DebugMode.Debug) => AppConfig.DebugSettings.SetDebugMode(mode);

        public static bool IsDebugMode => AppConfig.DebugSettings.IsDebugMode();

        public static bool IsConsoleDebugMode => AppConfig.DebugSettings.IsConsoleDebugMode();

        public static void Report(string name, string value)
        {
            if (Dbg.IsDebugMode) GlobalIntervalReporter.Singleton.Report(name, value);
        }

        public static string WriteLine()
        {
            WriteLine("");
            return "";
        }
        public static object WriteLine(object obj)
        {
            if (obj == null) obj = "null";
            LocalLogRouter.PrintConsole(obj, priority: LogPriority.Debug);
            return obj;
        }
        public static void WriteLine(string str, params object[] args)
        {
            if (str == null) str = "null";
            LocalLogRouter.PrintConsole(string.Format(str, args), priority: LogPriority.Debug);
        }

        public static object WriteError(object obj)
        {
            if (obj == null) obj = "null";
            LocalLogRouter.PrintConsole(obj, priority: LogPriority.Error);
            return obj;
        }
        public static void WriteError(string str, params object[] args)
        {
            if (str == null) str = "null";
            LocalLogRouter.PrintConsole(string.Format(str, args), priority: LogPriority.Error);
        }

        public static void Where(object message = null, [CallerFilePath] string filename = "", [CallerLineNumber] int line = 0, [CallerMemberName] string caller = null, bool printThreadId = false)
        {
            if (Dbg.IsDebugMode)
            {
                filename = filename.GetFileName();

                DebugWhereContainer c = new DebugWhereContainer(message, filename, line, printThreadId ? Environment.CurrentManagedThreadId : 0, caller);
                DebugObject(c);
            }
        }

        public static bool IsJsonSupported { get; }

        static partial void InternalIsJsonSupported(ref bool ret);

        static partial void InternalConvertToJsonStringIfPossible(ref string ret, object obj, bool includeNull = false, bool escapeHtml = false,
            int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false);

        class ExceptionWrapper
        {
            public string ExceptionName { get; }
            public Exception Exception { get; }

            public ExceptionWrapper(Exception ex)
            {
                this.ExceptionName = ex.GetType().Name;
                this.Exception = ex;
            }
        }

        public static string GetObjectDump(object obj, string instanceBaseName, string separatorStr = ", ", bool hideEmpty = true, bool jsonIfPossible = false)
        {
            if (obj is Exception ex)
            {
                obj = new ExceptionWrapper(ex);
            }

            try
            {
                if (jsonIfPossible)
                {
                    string jsonStr = null;
                    try
                    {
                        InternalConvertToJsonStringIfPossible(ref jsonStr, obj, compact: true);
                        if (jsonStr != null)
                            return jsonStr;
                    }
                    catch
                    {
                    }
                }

                if (obj != null && obj.GetType().IsAnonymousType())
                    return obj.ToString();

                DebugVars v = GetVarsFromClass(obj.GetType(), separatorStr, hideEmpty, instanceBaseName, obj);

                return v.ToString();
            }
            catch// (Exception ex)
            {
                //Console.WriteLine($"GetObjectDump: {ex.ToString()}");
                if (obj == null) return "null";
                try
                {
                    return obj.ToString();
                }
                catch
                {
                    return "!!! GetObjectDump() error !!!";
                }
            }
        }

        public static void DebugObject(object obj)
        {
            if (Dbg.IsDebugMode == false) return;

            LocalLogRouter.PrintConsole(obj, priority: LogPriority.Debug);
        }

        public static void PrintObject(object obj)
        {
            LocalLogRouter.PrintConsole(obj, priority: LogPriority.Debug);
        }

        public static DebugVars GetVarsFromClass(Type t, string separatorStr, bool hideEmpty, string instanceBaseName, object obj, ImmutableHashSet<object> duplicateCheck = null)
        {
            if (obj == null || IsPrimitiveType(t) || t.IsArray)
            {
                ObjectContainerForDebugVars container = new ObjectContainerForDebugVars(obj);
                return GetVarsFromClass(container.GetType(), separatorStr, hideEmpty, instanceBaseName, container, duplicateCheck);
            }

            if (duplicateCheck == null) duplicateCheck = ImmutableHashSet<object>.Empty;

            if (instanceBaseName == null) instanceBaseName = t?.Name ?? "null";

            DebugVars ret = new DebugVars(separatorStr, hideEmpty, obj is ObjectContainerForDebugVars);

            var membersList = GetAllMembersFromType(t, obj != null, obj == null, true);

            int order = 0;

            foreach (MemberInfo info in membersList)
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

                    if (!(data is MethodBase) && (data_type == null || data_type.MemberType.Bit(MemberTypes.TypeInfo) || data_type.MemberType.Bit(MemberTypes.NestedType)))
                    {
                        if (IsPrimitiveType(data_type))
                        {
                            ret.Vars.Add((info, data, ++order));
                        }
                        else
                        {
                            if (data == null)
                            {
                                ret.Vars.Add((info, null, ++order));
                            }
                            else
                            {
                                if (data is IEnumerable)
                                {
                                    if (data is byte[] byteArray)
                                    {
                                        ret.Vars.Add((info, data, ++order));
                                    }
                                    else if (data is Memory<byte> byteMemory)
                                    {
                                        ret.Vars.Add((info, byteMemory.Span.ToArray(), ++order));
                                    }
                                    else
                                    {
                                        int n = 0;
                                        foreach (object item in (IEnumerable)data)
                                        {
                                            if (duplicateCheck.Contains(item) == false)
                                            {
                                                Type data_type2 = item?.GetType() ?? null;

                                                if (IsPrimitiveType(data_type2))
                                                {
                                                    ret.Vars.Add((info, item, ++order));
                                                }
                                                else if (item == null)
                                                {
                                                    ret.Vars.Add((info, null, ++order));
                                                }
                                                else
                                                {
                                                    ret.Childlen.Add((GetVarsFromClass(data_type2, separatorStr, hideEmpty, info.Name, item, duplicateCheck.Add(data)), ++order));
                                                }
                                            }

                                            n++;
                                        }
                                    }
                                }
                                else
                                {
                                    if (duplicateCheck.Contains(data) == false)
                                    {
                                        ret.Childlen.Add((GetVarsFromClass(data_type, separatorStr, hideEmpty, info.Name, data, duplicateCheck.Add(data)), ++order));
                                    }
                                }
                            }
                        }
                    }
                }
            }

            ret.BaseName = instanceBaseName;

            return ret;
        }

        public static MemberInfo[] GetAllMembersFromType(Type t, bool hideStatic, bool hideInstance, bool hideNonPublic)
        {
            HashSet<MemberInfo> a = new HashSet<MemberInfo>();

            if (hideStatic == false)
            {
                a.UnionWith(t.GetMembers(BindingFlags.Static | BindingFlags.Public));
                if (hideNonPublic == false)
                    a.UnionWith(t.GetMembers(BindingFlags.Static | BindingFlags.NonPublic));
            }

            if (hideInstance == false)
            {
                a.UnionWith(t.GetMembers(BindingFlags.Instance | BindingFlags.Public));
                if (hideNonPublic == false)
                    a.UnionWith(t.GetMembers(BindingFlags.Instance | BindingFlags.NonPublic));
            }

            return a.Where(x => x.MemberType == MemberTypes.Field || x.MemberType == MemberTypes.Property).ToArrayList();
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
            if (t == typeof(DateTimeOffset)) return true;
            if (t == typeof(TimeSpan)) return true;
            if (t == typeof(IPAddr)) return true;
            if (t == typeof(IPAddress)) return true;
            if (t == typeof(BigNumber)) return true;
            if (t == typeof(System.Numerics.BigInteger)) return true;

            return false;
        }

        public static void Suspend() => Kernel.SuspendForDebug();

        public static void Break() => Debugger.Break();
    }

    class ObjectContainerForDebugVars
    {
        public object Object;
        public ObjectContainerForDebugVars(object obj)
        {
            this.Object = obj;
        }
    }

    class DebugVars
    {
        public string BaseName = "";

        public string SeparatorStr { get; }
        public bool HideEmpty { get; }
        public bool IsSimplePrimitive { get; }

        public DebugVars(string separatorStr, bool hideEmpty, bool isSimplePrimitive)
        {
            this.SeparatorStr = separatorStr;
            this.HideEmpty = hideEmpty;
            this.IsSimplePrimitive = isSimplePrimitive;
        }

        public List<(MemberInfo memberInfo, object data, int order)> Vars = new List<(MemberInfo, object, int)>();
        public List<(DebugVars child, int order)> Childlen = new List<(DebugVars, int)>();

        public void WriteToString(List<string> currentList, ImmutableList<string> parents)
        {
            SortedList<int, object> localList = new SortedList<int, object>();

            foreach (var data in Vars)
            {
                bool hide = false;

                MemberInfo p = data.memberInfo;
                object o = data.data;
                string printStr = "null";
                string closure = "\"";
                if ((o?.GetType().IsPrimitive ?? true) || (o?.GetType().IsEnum ?? false)) closure = "";
                if (o != null)
                {
                    if (o is byte[] byteArray)
                    {
                        printStr = byteArray.GetHexString();
                    }
                    else
                    {
                        printStr = $"{closure}{o.ToString().Unescape()}{closure}";
                    }
                }

                if (o.IsEmpty(false))
                    hide = true;

                if (HideEmpty == false || hide == false)
                {
                    string leftStr = "";
                    if (this.IsSimplePrimitive == false)
                    {
                        leftStr = $"{Str.CombineStringArray(ImmutableListToArray<string>(parents), ".")}.{p.Name} = ";
                    }

                    localList.Add(data.order, $"{leftStr}{printStr}");
                }
            }

            foreach (var v in Childlen)
            {
                List<string> tmpList = new List<string>();
                v.child.WriteToString(tmpList, parents.Add(v.child.BaseName));

                localList.Add(v.order, tmpList);
            }

            foreach (var v in localList.Values)
            {
                if (v is string s)
                    currentList.Add(s);
                else if (v is List<string> list)
                    foreach (string s2 in list)
                        currentList.Add(s2);
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
            List<string> strList = new List<string>();

            WriteToString(strList, parents);

            for (int i = 0; i < strList.Count; i++)
            {
                string str = strList[i];
                if (str.StartsWith("."))
                    str = str.Substring(1);
                strList[i] = str;
            }

            return strList.ToArray().Combine(this.SeparatorStr);
        }
    }

    class IntervalDebug
    {
        public string Name { get; }
        public IntervalDebug(string name = "Interval") => this.Name = name.NonNullTrim();
        long StartTick = 0;
        public void Start() => this.StartTick = Time.Tick64;
        public int Elapsed => (int)(Time.Tick64 - this.StartTick);
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
        Once DisposeFlag;
        ThreadObj Thread;
        ManualResetEventSlim HaltEvent = new ManualResetEventSlim();
        bool HaltFlag = false;
        public string Name { get; }

        public Benchmark(string name = "Benchmark", int interval = 1000, bool disabled = false)
        {
            this.Interval = interval;
            this.Name = name;

            if (disabled == false)
            {
                this.Thread = new ThreadObj(thread_proc);
            }
        }

        void thread_proc(object param)
        {
            System.Threading.Thread.CurrentThread.IsBackground = true;
            try
            {
                System.Threading.Thread.CurrentThread.Priority = ThreadPriority.Highest;
            }
            catch
            {
            }
            long last_value = 0;
            long last_tick = Time.Tick64;
            while (true)
            {
                int wait_interval = this.Interval;
                if (HaltFlag) break;
                HaltEvent.Wait(wait_interval);
                if (HaltFlag) break;

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
            if (DisposeFlag.IsFirstCall())
            {
                HaltFlag = true;
                HaltEvent.Set();
                this.Thread.WaitForEnd();
            }
        }
    }

    static class SingletonFactory
    {
        static Dictionary<string, object> Table = new Dictionary<string, object>();

        public static T New<T>() where T : new()
        {
            Type t = typeof(T);
            string name = t.AssemblyQualifiedName;
            lock (Table)
            {
                object ret = null;
                if (Table.ContainsKey(name))
                    ret = Table[name];
                else
                {
                    ret = new T();
                    Table[name] = ret;
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

        public void ReportRefObject(string name, object refObj)
        {
            name = name.NonNullTrim();
            lock (table2)
            {
                if (table2.ContainsKey(name))
                {
                    if (refObj == null) table2.Remove(name);
                    table2[name] = refObj;
                }
                else
                {
                    if (refObj == null) return;
                    table2.Add(name, refObj);
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

        public IntervalReporter(string name = "Reporter", int interval = DefaultInterval, Func<string> printProc = null)
        {
            if (interval == 0) interval = DefaultInterval;
            this.Interval = interval;
            this.Name = name;
            this.PrintProc = printProc;

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
                return new IntervalReporter("<Stat>", printProc: () =>
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

