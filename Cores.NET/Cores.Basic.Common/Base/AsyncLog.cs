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
    }

    class AsyncLog : AsyncCleanupable
    {
        public const long DefaultMaxLogSize = 4096;
        public const long BufferCacheMaxSize = 128; //10 * 1024 * 1024;

        readonly CriticalSection Lock = new CriticalSection();
        public string DirName { get; }
        public string Prefix { get; }
        public AsyncLogSwitchType SwitchType { get; set; }
        public long MaxLogSize { get; } = DefaultMaxLogSize;
        readonly Queue<AsyncLogRecord> RecordQueue = new Queue<AsyncLogRecord>();
        readonly AsyncAutoResetEvent Event = new AsyncAutoResetEvent();
        readonly AsyncManualResetEvent FlushEvent = new AsyncManualResetEvent();

        bool Halt = false;
        bool CacheFlag = false;
        long LastTick = 0;
        AsyncLogSwitchType LastSwitchType = AsyncLogSwitchType.None;
        long CurrentFilePointer = 0;
        int CurrentLogNumber = 0;
        bool LogNumberIncremented = false;

        public AsyncLog(AsyncCleanuperLady lady, string dir, string prefix, AsyncLogSwitchType switchType = AsyncLogSwitchType.Day, long maxLogSize = DefaultMaxLogSize)
            : base(lady)
        {
            this.DirName = dir.NonNullTrim();
            this.Prefix = prefix.NonNullTrim();
            this.SwitchType = this.SwitchType;

            this.Lady.Add(LogThread());
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

        async Task LogThread()
        {
            IO io = null;
            MemoryBuffer<byte> b = new MemoryBuffer<byte>();
            bool flag = false;
            string currentFileName = "";
            string currentLogFileDateName = "";
            bool logDateChanged = false;

            while (true)
            {
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
                        //logDateChanged = 
                    }
                }
            }
        }

        static bool MakeLogFileName(out string name, string dir, string prefix, long tick, AsyncLogSwitchType switchType, int num, string oldDateStr)
        {
            prefix = prefix.TrimNonNull();
            throw null;
        }

        static string MakeLogFileNameStringFromTick(long tick, AsyncLogSwitchType switchType)
        {
            throw null;
        }
    }
}
