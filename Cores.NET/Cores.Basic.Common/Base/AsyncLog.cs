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
using System.Threading.Tasks;
using System.IO;

using IPA.Cores.Helper.Basic;

namespace IPA.Cores.Basic
{
    [Flags]
    enum AsyncLogSwitchType
    {
        None,
        Second,
        Minute,
        Hour,
        Day,
        Month,
    }

    class AsyncLogRecord
    {
        public long Tick { get; }
        public object Data { get; }

        public AsyncLogRecord(long tick, object data)
        {
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

    class AsyncLog : AsyncCleanupable
    {
        public const long DefaultMaxLogSize = 4096;
        public const string DefaultExtension = ".log";
        public const long BufferCacheMaxSize = 128; //10 * 1024 * 1024;

        readonly CriticalSection Lock = new CriticalSection();
        public string DirName { get; }
        public string Prefix { get; }
        public AsyncLogSwitchType SwitchType { get; set; }
        public long MaxLogSize { get; } = DefaultMaxLogSize;
        readonly Queue<AsyncLogRecord> RecordQueue = new Queue<AsyncLogRecord>();
        readonly AsyncAutoResetEvent Event = new AsyncAutoResetEvent();
        readonly AsyncManualResetEvent FlushEvent = new AsyncManualResetEvent();
        public string Extension { get; }

        bool Halt = false;
        long LastTick = 0;
        AsyncLogSwitchType LastSwitchType = AsyncLogSwitchType.None;
        long CurrentFilePointer = 0;
        int CurrentLogNumber = 0;
        bool LogNumberIncremented = false;
        string LastCachedStr = null;

        public AsyncLog(AsyncCleanuperLady lady, string dir, string prefix, AsyncLogSwitchType switchType = AsyncLogSwitchType.Day, long maxLogSize = DefaultMaxLogSize, string extension = DefaultExtension)
            : base(lady)
        {
            try
            {
                this.DirName = dir.NonNullTrim();
                this.Prefix = prefix.NonNullTrim();
                this.SwitchType = this.SwitchType;
                this.Extension = extension.IsFilledOrDefault(DefaultExtension);
                if (this.Extension.StartsWith(".") == false)
                    this.Extension = "." + this.Extension;

                this.Lady.Add(LogThreadAsync());
            }
            catch
            {
                Lady.DisposeAllSafe();
                throw;
            }
        }

        Once DisposeFlag;
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;
                Halt = true;
                Event.Set();
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

                    if (b.Length > this.MaxLogSize)
                    {
                        // Erase if the size of the buffer is larger than the maximum log file size
                        b.Clear();
                    }

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
                }

                if (this.Halt)
                {
                    // Break after finishing to save all records
                    // when the stop flag stood
                    int num;
                    lock (this.RecordQueue)
                    {
                        num = this.RecordQueue.Count;
                    }

                    if (num == 0 || io == null)
                    {
                        break;
                    }
                }
                else
                {
                    await this.Event.WaitOneAsync(9821);
                }
            }

            if (io != null)
            {
                io.Close(true);
            }

            b.Clear();
        }

        public void Add(AsyncLogRecord r)
        {
            lock (this.RecordQueue)
            {
                this.RecordQueue.Enqueue(r);
            }

            this.Event.Set();
        }

        bool MakeLogFileName(out string name, string dir, string prefix, long tick, AsyncLogSwitchType switchType, int num, ref string oldDateStr)
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

        string MakeLogFileNameStringFromTick(long tick, AsyncLogSwitchType switchType)
        {
            if (this.LastCachedStr != null)
                if (this.LastTick == tick && this.LastSwitchType == switchType)
                    return this.LastCachedStr;

            DateTime dt = Time.Tick64ToDateTime(tick).ToLocalTime();
            string str = "";

            switch (switchType)
            {
                case AsyncLogSwitchType.Second:
                    str = dt.ToString("_yyyyMMdd_HHmmss");
                    break;

                case AsyncLogSwitchType.Minute:
                    str = dt.ToString("_yyyyMMdd_HHmm");
                    break;

                case AsyncLogSwitchType.Hour:
                    str = dt.ToString("_yyyyMMdd_HH");
                    break;

                case AsyncLogSwitchType.Day:
                    str = dt.ToString("_yyyyMMdd");
                    break;

                case AsyncLogSwitchType.Month:
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
