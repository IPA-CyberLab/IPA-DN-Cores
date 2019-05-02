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
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    static partial class CoresConfig
    {
        public static partial class Logger
        {
            public static readonly Copenhagen<long> DefaultAutoDeleteTotalMinSize = 4000000000L; // 4GB
            public static readonly Copenhagen<long> DefaultMaxLogSize = 1073741823L; // 1GB
            public static readonly Copenhagen<int> DefaultMaxPendingRecords = 10000;
            public static readonly Copenhagen<int> EraserIntervalMsecs = 10 * 60 * 1000; // 10 mins
        }
    }

    [Flags]
    enum LogSwitchType
    {
        None,
        Second,
        Minute,
        Hour,
        Day,
        Month,
    }

    [Flags]
    enum LogPendingTreatment
    {
        Discard,
        Wait,
        WriteForcefully,
    }

    [Flags]
    enum LogPriority
    {
        Trace = 0,
        Debug = 1,
        Info = 2,
        Error = 4,
        None = 0x7FFFFFFF,
    }

    [Serializable]
    class LogInfoOptions
    {
        public bool WithTimeStamp = true;
        public bool WithMachineName = false;
        public bool WithAppName = false;
        public bool WithKind = false;
        public bool WithPriority = false;
        public bool WithTag = false;
        public bool WithTypeName = false;

        public string NewLineForString = "\r\n";
        public string ObjectPrintSeparator = ", ";

        public string MachineName = Str.GetSimpleHostnameFromFqdn(Env.MachineName);
        public string AppName = Env.ExeAssemblySimpleName;
        public string Kind = "";

        public bool WriteAsJsonFormat = false;

        public void Normalize(string logKind)
        {
            this.MachineName = this.MachineName.NonNullTrim().NoSpace();
            this.AppName = this.AppName.NonNullTrim().NoSpace();
            this.Kind = this.Kind.NonNullTrim().FilledOrDefault(logKind).NoSpace();
        }
    }

    [Flags]
    enum LogFlags
    {
        None = 0,
        NoOutputToConsole = 1,
    }

    interface ILogRecordTimeStamp
    {
        DateTimeOffset TimeStamp { get; }
    }

    class LogRecord
    {
        public static readonly byte[] CrLfByte = "\r\n".GetBytes_Ascii();

        public DateTimeOffset TimeStamp { get; }
        public object Data { get; }
        public LogPriority Priority { get; }
        public LogFlags Flags { get; }
        public string Tag { get; }

        public LogRecord(object data, LogPriority priority = LogPriority.Debug, LogFlags flags = LogFlags.None, string tag = null) : this(0, data, priority, flags, tag) { }

        public LogRecord(long tick, object data, LogPriority priority = LogPriority.Debug, LogFlags flags = LogFlags.None, string tag = null)
            : this(tick == 0 ? ((data as ILogRecordTimeStamp)?.TimeStamp ?? DateTimeOffset.Now) : Time.Tick64ToDateTimeOffsetLocal(tick), data, priority, flags, tag) { }

        public LogRecord(DateTimeOffset dateTime, object data, LogPriority priority = LogPriority.Debug, LogFlags flags = LogFlags.None, string tag = null)
        {
            this.TimeStamp = dateTime;
            this.Data = data;
            this.Priority = priority;
            this.Flags = flags;
            this.Tag = tag;
        }

        LogInfoOptions ConsolePrintOptions = new LogInfoOptions() { };

        public string ConsolePrintableString => GetTextFromData(this.Data, ConsolePrintOptions);

        public static string GetTextFromData(object data, LogInfoOptions opt)
        {
            if (data == null) return "null";
            if (data is string str) return str;

            return data.GetObjectDump("", opt.ObjectPrintSeparator, true);
        }

        public static string GetMultilineText(string src, LogInfoOptions opt, int paddingLenForNextLines = 1)
        {
            if (opt.NewLineForString.IndexOf("\n") == -1)
                paddingLenForNextLines = 0;

            string pad = Str.MakeCharArray(' ', paddingLenForNextLines);
            string[] lines = src.GetLines();
            StringBuilder sb = new StringBuilder();

            int num = 0;
            foreach (string line in lines)
            {
                if (line.IsFilled())
                {
                    string line2 = line.TrimEnd();
                    if (num >= 1)
                    {
                        sb.Append(opt.NewLineForString);
                        sb.Append(pad);
                    }
                    sb.Append(line2);
                    num++;
                }
            }

            return sb.ToString();
        }

        class JsonContainer
        {
            public DateTimeOffset? TimeStamp;
            public string MachineName;
            public string AppName;
            public string Kind;
            public string Priority;
            public string Tag;
            public string TypeName;
            public object Data;
        }

        public void WriteRecordToBuffer(Logger g, LogInfoOptions opt, MemoryBuffer<byte> b)
        {
            if (opt.WriteAsJsonFormat && Dbg.IsJsonSupported)
            {
                // JSON text
                JsonContainer jc = new JsonContainer();

                if (opt.WithTimeStamp)
                    jc.TimeStamp = this.TimeStamp;

                if (opt.WithMachineName)
                    jc.MachineName = opt.MachineName;

                if (opt.WithAppName)
                    jc.AppName = opt.AppName;

                if (opt.WithKind)
                    jc.Kind = opt.Kind;

                if (opt.WithPriority)
                    jc.Priority = this.Priority.ToString();

                if (opt.WithTag)
                    jc.Tag = this.Tag.FilledOrDefault(LogTag.NoTag);

                if (opt.WithTypeName)
                    jc.TypeName = this.Data?.GetType().Name ?? "null";

                jc.Data = this.Data;

                string jsonText = jc.GetObjectDump(jsonIfPossible: true);

                b.Write(jsonText.GetBytes_UTF8());
                b.Write(CrLfByte);
            }
            else
            {
                // Normal text
                StringBuilder sb = new StringBuilder();

                // Timestamp
                if (opt.WithTimeStamp)
                {
                    sb.Append(this.TimeStamp.ToDtStr(true, DtstrOption.All, false));
                    sb.Append(" ");
                }

                // Additional strings
                List<string> additionalList = new List<string>();

                if (opt.WithMachineName)
                    additionalList.Add(opt.MachineName);

                if (opt.WithAppName)
                    additionalList.Add(opt.AppName);

                if (opt.WithKind)
                    additionalList.Add(opt.Kind);

                if (opt.WithPriority)
                    additionalList.Add(this.Priority.ToString());

                if (opt.WithTag)
                    additionalList.Add(this.Tag.FilledOrDefault(LogTag.NoTag));

                if (opt.WithTypeName)
                    additionalList.Add(this.Data?.GetType().Name ?? "null");

                string additionalStr = Str.CombineStringArray(" ", additionalList.ToArray());
                if (additionalStr.IsFilled())
                {
                    sb.Append("[");
                    sb.Append(additionalStr);
                    sb.Append("] ");
                }

                // Log text
                string logText = GetMultilineText(GetTextFromData(this.Data, opt), opt);
                sb.Append(logText);
                sb.Append("\r\n");

                b.Write(sb.ToString().GetBytes_UTF8());
            }
        }
    }

    class Logger : AsyncService
    {
        public const string DefaultExtension = ".log";
        public const long BufferCacheMaxSize = 5 * 1024 * 1024;

        public readonly byte[] NewFilePreamble = Str.BomUtf8;

        readonly CriticalSection Lock = new CriticalSection();
        public string DirName { get; }
        public string Kind { get; }
        public string Prefix { get; }
        public LogSwitchType SwitchType { get; set; }
        public long MaxLogSize { get; }
        readonly Queue<LogRecord> RecordQueue = new Queue<LogRecord>();
        readonly AsyncAutoResetEvent Event = new AsyncAutoResetEvent();
        readonly AsyncAutoResetEvent FlushEvent = new AsyncAutoResetEvent();
        readonly AsyncAutoResetEvent WaitPendingEvent = new AsyncAutoResetEvent();
        public string Extension { get; }
        public bool DiscardPendingDataOnDispose { get; set; } = false;

        public int MaxPendingRecords { get; set; } = CoresConfig.Logger.DefaultMaxPendingRecords;

        LogInfoOptions InfoOptions { get; }

        long LastTick = 0;
        LogSwitchType LastSwitchType = LogSwitchType.None;
        long CurrentFilePointer = 0;
        int CurrentLogNumber = 0;
        bool LogNumberIncremented = false;
        string LastCachedStr = null;

        public bool KeepFileHandleWhenIdle { get; set; }

        public bool NoFlush { get; set; } = false;

        Task LogTask = null;

        public Logger(string dir, string kind, string prefix, LogSwitchType switchType = LogSwitchType.Day,
            LogInfoOptions infoOptions = default,
            long maxLogSize = 0, string extension = DefaultExtension,
            long? autoDeleteTotalMinSize = null,
            bool keepFileHandleWhenIdle = true)
            : base()
        {
            this.DirName = dir.NonNullTrim();
            this.Kind = kind.NonNullTrim().FilledOrDefault(LogKind.Default);
            this.Prefix = prefix.NonNullTrim().FilledOrDefault("log").ReplaceStr("\\", "_").Replace("/", "_");
            this.SwitchType = switchType;
            this.Extension = extension.FilledOrDefault(DefaultExtension);
            this.KeepFileHandleWhenIdle = keepFileHandleWhenIdle;
            if (this.MaxLogSize <= 0)
                this.MaxLogSize = CoresConfig.Logger.DefaultMaxLogSize;
            this.MaxLogSize = Math.Max(maxLogSize, BufferCacheMaxSize * 10L);
            if (this.Extension.StartsWith(".") == false)
                this.Extension = "." + this.Extension;

            this.InfoOptions = infoOptions.CloneDeep();
            this.InfoOptions.Normalize(this.Kind);


            if (autoDeleteTotalMinSize != null)
            {
                autoDeleteTotalMinSize = autoDeleteTotalMinSize.FilledOrDefault(CoresConfig.Logger.DefaultAutoDeleteTotalMinSize.Value);
                OldFileEraser eraser = new OldFileEraser(autoDeleteTotalMinSize ?? 0, dir.SingleArray(), extension, CoresConfig.Logger.EraserIntervalMsecs);
            }

            LogTask = LogThreadAsync().LeakCheck();
        }

        protected override void CancelImpl(Exception ex) { }

        protected override async Task CleanupImplAsync()
        {
            await LogTask;
        }

        protected override void DisposeImpl() { }

        public void Stop(bool abandonUnwritenData)
        {
            this.DiscardPendingDataOnDispose = abandonUnwritenData;

            this.Cancel();
        }

        async Task LogThreadAsync()
        {
            IO io = null;
            MemoryBuffer<byte> b = new MemoryBuffer<byte>();
            string currentFileName = "";
            string currentLogFileDateName = "";
            bool logDateChanged = false;
            long lastIdleFlushTick = 0;

            while (true)
            {
                await Task.Yield();

                LogRecord rec = null;
                long s = FastTick64.Now;

                while (true)
                {
                    string fileName;
                    int num;

                    lock (this.RecordQueue)
                    {
                        rec = RecordQueue.DequeueOrNull();
                        num = RecordQueue.Count;
                    }

                    if (MaxPendingRecords >= 1)
                    {
                        if (num == (this.MaxPendingRecords - 1))
                        {
                            this.WaitPendingEvent.Set();
                        }
                    }

                    if (b.Length > Math.Max(this.MaxLogSize, BufferCacheMaxSize))
                    {
                        // Erase if the size of the buffer is larger than the maximum log file size
                        b.Clear();
                    }

                    await Task.Yield();

                    if (b.Length >= BufferCacheMaxSize)
                    {
                        // Write the contents of the buffer to the file
                        if (io != null)
                        {
                            if ((CurrentFilePointer + b.Length) > MaxLogSize)
                            {
                                if (LogNumberIncremented == false)
                                {
                                    CurrentLogNumber++;
                                    LogNumberIncremented = true;
                                }
                            }
                            else
                            {
                                if (await io.WriteAsync(b.Memory) == false)
                                {
                                    io.Close(this.NoFlush);
                                    io = null;
                                    b.Clear();
                                }
                                else
                                {
                                    CurrentFilePointer += b.Length;
                                    b.Clear();
                                }
                            }
                        }
                    }

                    if (rec == null)
                    {
                        if (b.Length != 0)
                        {
                            // Write the contents of the buffer to the file
                            if (io != null)
                            {
                                if ((CurrentFilePointer + b.Length) > MaxLogSize)
                                {
                                    if (LogNumberIncremented == false)
                                    {
                                        CurrentLogNumber++;
                                        LogNumberIncremented = true;
                                    }
                                }
                                else
                                {
                                    if (await io.WriteAsync(b.Memory) == false)
                                    {
                                        io.Close(this.NoFlush);
                                        io = null;
                                        b.Clear();
                                    }
                                    else
                                    {
                                        CurrentFilePointer += b.Length;
                                        b.Clear();
                                    }
                                }
                            }
                        }

                        FlushEvent.Set();

                        break;
                    }

                    // Generate a log file name
                    lock (this.Lock)
                    {
                        logDateChanged = MakeLogFileName(out fileName, this.DirName, this.Prefix,
                            rec.TimeStamp, this.SwitchType, this.CurrentLogNumber, ref currentLogFileDateName);

                        if (logDateChanged)
                        {
                            int existingMaxLogNumber = 0;

                            try
                            {
                                string[] existingFiles = Directory.GetFiles(IO.InnerFilePath(this.DirName), "*" + this.Extension, SearchOption.TopDirectoryOnly);

                                string candidateFileNameStartStr = Path.GetFileNameWithoutExtension(fileName).Split("~").FirstOrDefault();

                                if (candidateFileNameStartStr.IsFilled())
                                {
                                    string maxFileName = existingFiles.Select(x => x.GetFileName()).OrderByDescending(x => x).Where(x => x.InStr("~") && x.StartsWith(candidateFileNameStartStr, StringComparison.OrdinalIgnoreCase)).Select(x => Path.GetFileNameWithoutExtension(x)).FirstOrDefault();

                                    if (maxFileName.IsFilled())
                                    {
                                        string existingFileNumberStr = maxFileName.Split("~").LastOrDefault();
                                        if (existingFileNumberStr.IsFilled())
                                        {
                                            existingMaxLogNumber = existingFileNumberStr.ToInt();
                                        }
                                    }
                                }
                            }
                            catch { }

                            if (existingMaxLogNumber != 0)
                            {
                                this.CurrentLogNumber = existingMaxLogNumber;
                                MakeLogFileName(out fileName, this.DirName, this.Prefix,
                                    rec.TimeStamp, this.SwitchType, this.CurrentLogNumber, ref currentLogFileDateName);
                            }
                            else
                            {
                                this.CurrentLogNumber = 0;
                                MakeLogFileName(out fileName, this.DirName, this.Prefix,
                                    rec.TimeStamp, this.SwitchType, 0, ref currentLogFileDateName);
                                for (int i = 0; ; i++)
                                {
                                    MakeLogFileName(out string tmp, this.DirName, this.Prefix,
                                        rec.TimeStamp, this.SwitchType, i, ref currentLogFileDateName);

                                    if (IO.IsFileExists(tmp) == false)
                                        break;

                                    this.CurrentLogNumber = i;
                                }
                            }
                        }
                    }

                    if (io != null)
                    {
                        if (currentFileName != fileName)
                        {
                            // If a log file is currently opened and writing to another log
                            // file is needed for this time, write the contents of the 
                            // buffer and close the log file. Write the contents of the buffer
                            if (io != null)
                            {
                                if (logDateChanged)
                                {
                                    if ((this.CurrentFilePointer + b.Length) <= this.MaxLogSize)
                                    {
                                        if (await io.WriteAsync(b.Memory) == false)
                                        {
                                            io.Close(this.NoFlush);
                                            b.Clear();
                                            io = null;
                                        }
                                        else
                                        {
                                            this.CurrentFilePointer += b.Length;
                                            b.Clear();
                                        }
                                    }
                                }
                                // Close the file
                                if (io != null)
                                {
                                    io.Close(this.NoFlush);
                                    io = null;
                                }
                            }

                            this.LogNumberIncremented = false;

                            // Open or create a new log file
                            currentFileName = fileName;
                            try
                            {
                                io = IO.FileOpen(fileName, writeMode: true, useAsync: true);
                                this.CurrentFilePointer = io.FileSize64;
                                io.Seek(SeekOrigin.End, 0);
                            }
                            catch
                            {
                                // Create a log file
                                lock (this.Lock)
                                {
                                    try
                                    {
                                        IO.MakeDirIfNotExists(this.DirName);
                                        Win32FolderCompression.SetFolderCompression(this.DirName, true);
                                    }
                                    catch { }
                                }
                                try
                                {
                                    io = IO.FileCreate(fileName, useAsync: true);
                                    await io.WriteAsync(NewFilePreamble);
                                }
                                catch (Exception ex)
                                {
                                    if (Dbg.IsConsoleDebugMode)
                                        Console.WriteLine($"IO.FileCreate('{fileName}') failed. {ex.Message}");
                                }
                                this.CurrentFilePointer = 0;
                            }
                        }
                    }
                    else
                    {
                        // Open or create a new log file
                        currentFileName = fileName;
                        try
                        {
                            io = IO.FileOpen(fileName, writeMode: true, useAsync: true);
                            this.CurrentFilePointer = io.FileSize64;
                            io.Seek(SeekOrigin.End, 0);
                        }
                        catch
                        {
                            // Create a log file
                            lock (this.Lock)
                            {
                                try
                                {
                                    IO.MakeDirIfNotExists(this.DirName);
                                    Win32FolderCompression.SetFolderCompression(this.DirName, true);
                                }
                                catch { }
                            }
                            this.CurrentFilePointer = 0;
                            try
                            {
                                io = IO.FileCreate(fileName, useAsync: true);
                                await io.WriteAsync(NewFilePreamble);
                            }
                            catch (Exception ex)
                            {
                                if (Dbg.IsConsoleDebugMode)
                                    Console.WriteLine($"IO.FileCreate('{fileName}') failed. {ex.Message}");
                                await Task.Delay(30);
                            }
                        }

                        this.LogNumberIncremented = false;
                    }

                    // Write the contents of the log to the buffer
                    rec.WriteRecordToBuffer(this, this.InfoOptions, b);

                    if (io == null)
                        break;

                    if (this.GrandCancel.IsCancellationRequested)
                        break;
                }

                // Break after finishing to save all records
                // when the stop flag stood
                int num2;
                lock (this.RecordQueue)
                {
                    num2 = this.RecordQueue.Count;
                }

                if (this.GrandCancel.IsCancellationRequested)
                {
                    if ((num2 == 0 && b.Length == 0) || io == null || DiscardPendingDataOnDispose)
                    {
                        break;
                    }
                }
                else
                {
                    if (num2 == 0)
                    {
                        int nextWaitInterval = Util.RandSInt31() % 10000;

                        if (this.KeepFileHandleWhenIdle == false)
                        {
                            if (io != null)
                            {
                                io.Close(this.NoFlush);
                                io = null;
                            }
                        }
                        else
                        {
                            long now = Tick64.Now;

                            if (lastIdleFlushTick == 0 || (lastIdleFlushTick + 1000) <= now)
                            {
                                lastIdleFlushTick = now;

                                if (io != null)
                                {
                                    io.Flush();
                                }
                            }
                        }

                        await this.Event.WaitOneAsync(nextWaitInterval, this.GrandCancel);
                    }
                }
            }

            if (io != null)
            {
                io.Close(true);
            }

            b.Clear();
        }

        public async Task<bool> AddAsync(LogRecord r, LogPendingTreatment pendingTreatment = LogPendingTreatment.Discard, CancellationToken pendingWaitCancel = default)
        {
            if (r.Priority == LogPriority.None)
            {
                return true;
            }

            if (this.GrandCancel.IsCancellationRequested)
            {
                return false;
            }

            while (MaxPendingRecords >= 1 && this.RecordQueue.Count >= this.MaxPendingRecords)
            {
                if (pendingTreatment == LogPendingTreatment.WriteForcefully)
                {
                    break;
                }
                else if (pendingTreatment == LogPendingTreatment.Wait)
                {
                    await TaskUtil.WaitObjectsAsync(cancels: new CancellationToken[] { pendingWaitCancel, this.GrandCancel }, events: this.WaitPendingEvent.SingleArray());
                }
                else
                {
                    return false;
                }
            }

            lock (this.RecordQueue)
            {
                this.RecordQueue.Enqueue(r);
            }

            this.Event.Set();

            return true;
        }

        public bool Add(LogRecord r)
        {
            if (r.Priority == LogPriority.None)
            {
                return true;
            }

            if (this.GrandCancel.IsCancellationRequested)
            {
                return false;
            }

            if (MaxPendingRecords >= 1 && this.RecordQueue.Count >= this.MaxPendingRecords)
            {
                return false;
            }

            lock (this.RecordQueue)
            {
                this.RecordQueue.Enqueue(r);
            }

            this.Event.Set();

            return true;
        }

        public async Task<bool> WaitAllLogFlush()
        {
            while (this.GrandCancel.IsCancellationRequested == false)
            {
                int num;
                lock (this.RecordQueue)
                    num = this.RecordQueue.Count;

                if (num == 0) return true;
                if (this.GrandCancel.IsCancellationRequested) return false;

                await this.FlushEvent.WaitOneAsync(100, this.GrandCancel);
            }
            return false;
        }

        bool MakeLogFileName(out string name, string dir, string prefix, DateTimeOffset dateTime, LogSwitchType switchType, int num, ref string oldDateStr)
        {
            prefix = prefix.TrimNonNull();
            string tmp = MakeLogFileNameStringFromTick(dateTime, switchType);
            string tmp2 = "";
            bool ret = false;

            if (num != 0)
            {
                long maxLogSize = this.MaxLogSize;
                int digits = 9;
                if (maxLogSize >= 1000000000L)
                    digits = 3;
                else if (maxLogSize >= 100000000L)
                    digits = 4;
                else if (maxLogSize >= 10000000L)
                    digits = 5;
                else if (maxLogSize >= 1000000L)
                    digits = 6;
                else if (maxLogSize >= 100000L)
                    digits = 7;
                else if (maxLogSize >= 10000L)
                    digits = 8;
                tmp2 = "~" + num.ToString($"D" + digits);
            }

            if (oldDateStr != tmp)
            {
                ret = true;
                oldDateStr = tmp;
            }

            name = Path.Combine(dir, prefix + tmp + tmp2 + this.Extension);

            return ret;
        }

        string MakeLogFileNameStringFromTick(DateTimeOffset dateTime, LogSwitchType switchType)
        {
            if (this.LastCachedStr != null)
                if (this.LastTick == dateTime.Ticks && this.LastSwitchType == switchType)
                    return this.LastCachedStr;

            string str = "";

            switch (switchType)
            {
                case LogSwitchType.Second:
                    str = dateTime.ToString("_yyyyMMdd_HHmmss");
                    break;

                case LogSwitchType.Minute:
                    str = dateTime.ToString("_yyyyMMdd_HHmm");
                    break;

                case LogSwitchType.Hour:
                    str = dateTime.ToString("_yyyyMMdd_HH");
                    break;

                case LogSwitchType.Day:
                    str = dateTime.ToString("_yyyyMMdd");
                    break;

                case LogSwitchType.Month:
                    str = dateTime.ToString("_yyyyMM");
                    break;
            }

            this.LastCachedStr = str;
            this.LastTick = dateTime.Ticks;
            this.LastSwitchType = SwitchType;

            return str;
        }
    }
}
