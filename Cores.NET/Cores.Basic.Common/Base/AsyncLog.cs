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

using IPA.Cores.Helper.Basic;

namespace IPA.Cores.Basic
{
    [Flags]
    enum AsyncLoggerSwitchType
    {
        None,
        Second,
        Minute,
        Hour,
        Day,
        Month,
    }

    [Flags]
    enum AsyncLoggerPendingTreatment
    {
        Discard,
        Ignore,
        Wait,
    }

    class AsyncLogRecord
    {
        public long Tick { get; }
        public object Data { get; }

        public AsyncLogRecord(object data) : this(0, data) { }
        public AsyncLogRecord(long tick, object data)
        {
            if (tick == 0) tick = FastTick64.Now;
            this.Tick = tick;
            this.Data = data;
        }

        public void WriteRecordToBuffer(MemoryBuffer<byte> b)
        {
            // Get the time
            DateTime dt = Time.Tick64ToDateTime(this.Tick).ToLocalTime();

            // Convert a time to a string
            string writeStr = dt.ToDtStr(true, DtstrOption.All, false) + " " + this.Data.ToString() + "\r\n";
            b.Write(writeStr.GetBytes_Ascii());
        }
    }

    class AsyncLogger : AsyncCleanupable
    {
        public const long DefaultMaxLogSize = 1073741823;
        public const string DefaultExtension = ".log";
        public const long BufferCacheMaxSize = 5 * 1024 * 1024;
        public const int DefaultMaxPendingRecords = 1000;

        readonly CriticalSection Lock = new CriticalSection();
        public string DirName { get; }
        public string Prefix { get; }
        public AsyncLoggerSwitchType SwitchType { get; set; }
        public long MaxLogSize { get; }
        readonly Queue<AsyncLogRecord> RecordQueue = new Queue<AsyncLogRecord>();
        readonly AsyncAutoResetEvent Event = new AsyncAutoResetEvent();
        readonly AsyncAutoResetEvent FlushEvent = new AsyncAutoResetEvent();
        readonly AsyncAutoResetEvent WaitPendingEvent = new AsyncAutoResetEvent();
        public string Extension { get; }
        public bool DiscardPendingDataOnDispose { get; set; }

        public int MaxPendingRecords { get; set; } = DefaultMaxPendingRecords;

        readonly CancellationTokenSource Cancel = new CancellationTokenSource();
        long LastTick = 0;
        AsyncLoggerSwitchType LastSwitchType = AsyncLoggerSwitchType.None;
        long CurrentFilePointer = 0;
        int CurrentLogNumber = 0;
        bool LogNumberIncremented = false;
        string LastCachedStr = null;

        public AsyncLogger(AsyncCleanuperLady lady, string dir, string prefix, AsyncLoggerSwitchType switchType = AsyncLoggerSwitchType.Day,
            long maxLogSize = DefaultMaxLogSize, string extension = DefaultExtension,
            long autoDeleteTotalMaxSize = 0)
            : base(lady)
        {
            try
            {
                this.DirName = dir.NonNullTrim();
                this.Prefix = prefix.NonNullTrim();
                this.SwitchType = switchType;
                this.Extension = extension.IsFilledOrDefault(DefaultExtension);
                this.MaxLogSize = Math.Max(maxLogSize, BufferCacheMaxSize * 10L);
                if (this.Extension.StartsWith(".") == false)
                    this.Extension = "." + this.Extension;

                if (autoDeleteTotalMaxSize != 0)
                {
                    OldFileEraser eraser = new OldFileEraser(this.Lady, autoDeleteTotalMaxSize, dir.SingleArray(), extension, 1000);
                }

                this.Lady.Add(LogThreadAsync());
            }
            catch
            {
                Lady.DisposeAllSafe();
                throw;
            }
        }

        public void Stop(bool abandonUnwritenData)
        {
            this.DiscardPendingDataOnDispose = abandonUnwritenData;
            this.Dispose();
        }

        Once DisposeFlag;
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;
                Cancel.Cancel();
            }
            finally { base.Dispose(disposing); }
        }

        async Task LogThreadAsync()
        {
            IO io = null;
            MemoryBuffer<byte> b = new MemoryBuffer<byte>();
            string currentFileName = "";
            string currentLogFileDateName = "";
            bool logDateChanged = false;

            while (true)
            {
                await Task.Yield();

                AsyncLogRecord rec = null;
                long s = FastTick64.Now;

                while (true)
                {
                    string fileName;
                    int num;

                    lock(this.RecordQueue)
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
                                    io.Close(true);
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
                                        io.Close(true);
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
                            rec.Tick, this.SwitchType, this.CurrentLogNumber, ref currentLogFileDateName);

                        if (logDateChanged)
                        {
                            this.CurrentLogNumber = 0;
                            MakeLogFileName(out fileName, this.DirName, this.Prefix,
                                rec.Tick, this.SwitchType, 0, ref currentLogFileDateName);
                            for (int i = 0; ; i++)
                            {
                                MakeLogFileName(out string tmp, this.DirName, this.Prefix,
                                    rec.Tick, this.SwitchType, i, ref currentLogFileDateName);

                                if (IO.IsFileExists(tmp) == false)
                                    break;


                                this.CurrentLogNumber = i;
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
                                            io.Close(true);
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
                                    io.Close(true);
                                    io = null;
                                }
                            }

                            this.LogNumberIncremented = false;

                            // Open or create a new log file
                            currentFileName = fileName;
                            try
                            {
                                io = IO.FileOpen(fileName, true);
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
                                    }
                                    catch { }
                                }
                                try
                                {
                                    io = IO.FileCreate(fileName);
                                }
                                catch { }
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
                            io = IO.FileOpen(fileName, true);
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
                                }
                                catch { }
                            }
                            this.CurrentFilePointer = 0;
                            try
                            {
                                io = IO.FileCreate(fileName);
                            }
                            catch
                            {
                                await Task.Delay(30);
                            }
                        }

                        this.LogNumberIncremented = false;
                    }

                    // Write the contents of the log to the buffer
                    rec.WriteRecordToBuffer(b);

                    if (io == null)
                        break;

                    if (this.Cancel.IsCancellationRequested)
                        break;
                }

                // Break after finishing to save all records
                // when the stop flag stood
                int num2;
                lock (this.RecordQueue)
                {
                    num2 = this.RecordQueue.Count;
                }

                if (this.Cancel.IsCancellationRequested)
                {
                    if (num2 == 0 || io == null || DiscardPendingDataOnDispose)
                    {
                        break;
                    }
                }
                else
                {
                    if (num2 == 0)
                    {
                        await this.Event.WaitOneAsync(9821, this.Cancel.Token);
                    }
                }
            }

            if (io != null)
            {
                io.Close(true);
            }

            b.Clear();
        }

        public async Task<bool> AddAsync(AsyncLogRecord r, AsyncLoggerPendingTreatment pendingTreatment = AsyncLoggerPendingTreatment.Discard, CancellationToken pendingWaitCancel = default)
        {
            if (this.Cancel.IsCancellationRequested)
            {
                return false;
            }

            while (MaxPendingRecords >= 1 && this.RecordQueue.Count >= this.MaxPendingRecords)
            {
                if (pendingTreatment == AsyncLoggerPendingTreatment.Ignore)
                {
                    break;
                }
                else if (pendingTreatment == AsyncLoggerPendingTreatment.Wait)
                {
                    await TaskUtil.WaitObjectsAsync(cancels: new CancellationToken[] { pendingWaitCancel, this.Cancel.Token }, events: this.WaitPendingEvent.SingleArray());
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

        public bool Add(AsyncLogRecord r)
        {
            if (this.Cancel.IsCancellationRequested)
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
            while (this.Cancel.IsCancellationRequested == false)
            {
                int num;
                lock (this.RecordQueue)
                    num = this.RecordQueue.Count;

                if (num == 0) return true;
                if (this.Cancel.IsCancellationRequested) return false;

                await this.FlushEvent.WaitOneAsync(100, this.Cancel.Token);
            }
            return false;
        }

        bool MakeLogFileName(out string name, string dir, string prefix, long tick, AsyncLoggerSwitchType switchType, int num, ref string oldDateStr)
        {
            prefix = prefix.TrimNonNull();
            string tmp = MakeLogFileNameStringFromTick(tick, switchType);
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

        string MakeLogFileNameStringFromTick(long tick, AsyncLoggerSwitchType switchType)
        {
            if (this.LastCachedStr != null)
                if (this.LastTick == tick && this.LastSwitchType == switchType)
                    return this.LastCachedStr;

            DateTime dt = Time.Tick64ToDateTime(tick).ToLocalTime();
            string str = "";

            switch (switchType)
            {
                case AsyncLoggerSwitchType.Second:
                    str = dt.ToString("_yyyyMMdd_HHmmss");
                    break;

                case AsyncLoggerSwitchType.Minute:
                    str = dt.ToString("_yyyyMMdd_HHmm");
                    break;

                case AsyncLoggerSwitchType.Hour:
                    str = dt.ToString("_yyyyMMdd_HH");
                    break;

                case AsyncLoggerSwitchType.Day:
                    str = dt.ToString("_yyyyMMdd");
                    break;

                case AsyncLoggerSwitchType.Month:
                    str = dt.ToString("_yyyyMM");
                    break;
            }

            this.LastCachedStr = str;
            this.LastTick = tick;
            this.LastSwitchType = SwitchType;

            return str;
        }
    }
}
