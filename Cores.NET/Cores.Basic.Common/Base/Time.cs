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
using System.Diagnostics;
using System.Threading.Tasks;

namespace IPA.Cores.Basic
{
    class TimeHelper
    {
        internal Stopwatch Sw;
        internal long Freq;
        internal DateTimeOffset FirstDateTimeOffset;

        public TimeHelper()
        {
            FirstDateTimeOffset = DateTimeOffset.Now;
            //FirstDateTimeOffset = 
            Sw = new Stopwatch();
            Sw.Start();
            Freq = Stopwatch.Frequency;
        }

        public DateTimeOffset GetDateTimeOffset()
        {
            return FirstDateTimeOffset + this.Sw.Elapsed;
        }
    }

    abstract class TimeAdjustHistoryBase : IDisposable
    {
        class History
        {
            public long Tick, Time;
        }

        public const int DefaultInterval = 1000;
        public const int MaxAdjustTime = 1024;

        long Time64;
        long Tick64WithTime64;
        CriticalSection Lock = new CriticalSection();
        List<History> HistoryList = new List<History>();
        public int Interval { get; }
        AsyncManualResetEvent HaltEvent = new AsyncManualResetEvent();
        bool HaltFlag = false;
        Event InitCompletedEvent = new Event(true);

        Task MainTask;

        public TimeAdjustHistoryBase(int interval = DefaultInterval)
        {
            Init();
            this.Interval = interval;
            MainTask = TimeAdjustHistoryThreadAsync();
            InitCompletedEvent.Wait();
        }

        public void Dispose() => Dispose(true);
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;
            HaltFlag = true;
            HaltEvent.Set();
            MainTask.Wait();
        }

        protected abstract void Init();
        protected abstract long GetTick();

        async Task TimeAdjustHistoryThreadAsync()
        {
            int n = 0;
            bool first = false;
            bool createFirstEntry = true;
            int tickSpan = this.Interval;

            while (true)
            {
                long tick64 = GetTick();

                if (createFirstEntry)
                {
                    History t = new History() { Tick = tick64, Time = Time.SystemTime64 };
                    this.Tick64WithTime64 = tick64;
                    this.Time64 = t.Time;
                    HistoryList.Add(t);

                    InitCompletedEvent.Set();
                    createFirstEntry = false;
                }

                // Time correction
                n += tickSpan;
                if (n >= 1000 || first == false)
                {
                    long now = Time.SystemTime64;
                    long diff = Math.Abs(((now - this.Time64) + this.Tick64WithTime64) - tick64);

                    if (now < this.Time64 || diff >= 1000)
                    {
                        History t = new History();
                        lock (this.HistoryList)
                        {
                            t.Tick = tick64;
                            t.Time = now;
                            this.HistoryList.Add(t);

                            if (Dbg.IsDebugMode)
                                Console.WriteLine($"Adjust Time: Diff = {diff}, Tick = {t.Tick}, Time = {t.Time}, NUM_ADJUST TIME: {this.HistoryList.Count}");

                            // To prevent consuming memory infinite on a system that clock is skewd
                            if (this.HistoryList.Count >= MaxAdjustTime)
                            {
                                // Remove the second
                                this.HistoryList.RemoveAt(1);

                                if (Dbg.IsDebugMode)
                                    Console.WriteLine($"NUM_ADJUST TIME: {this.HistoryList.Count}");
                            }
                        }

                        this.Time64 = now;
                        this.Tick64WithTime64 = tick64;
                    }
                    first = true;
                    n = 0;
                }

                if (this.HaltFlag)
                    break;

                await this.HaltEvent.WaitAsync(tickSpan);
            }
        }

        public long Tick64ToTime64(long tick)
        {
            long ret = 0;
            if (tick == 0) return 0;
            lock (this.HistoryList)
            {
                for (int i = this.HistoryList.Count - 1; i >= 0; i--)
                {
                    History t = HistoryList[i];
                    if (t.Tick <= tick)
                    {
                        ret = t.Time + (tick - t.Tick);
                        break;
                    }
                }
            }
            if (ret == 0) ret = 1;
            return ret;
        }
    }

    static class FastTick64
    {
        static FastTick64()
        {
            Init();
        }

        class AdjustHistory : TimeAdjustHistoryBase
        {
            protected override void Init() => FastTick64.Init();
            protected override long GetTick() => FastTick64.Now;
        }

        static AdjustHistory History = new AdjustHistory();

        public static long Now { get => GetTick64() - Base; }

        static long Base;
        static void Init()
        {
            Base = GetTick64() - 1;
            state = 0;
        }

        static volatile uint state;

        static long GetTick64()
        {
            uint value = (uint)Environment.TickCount;
            uint value16bit = (value >> 16) & 0xFFFF;

            uint stateCopy = state;

            uint state16bit = (stateCopy >> 16) & 0xFFFF;
            uint rotate16bit = stateCopy & 0xFFFF;

            if (value16bit <= 0x1000 && state16bit >= 0xF000)
            {
                rotate16bit++;
            }

            uint stateNew = (value16bit << 16) & 0xFFFF0000 | rotate16bit & 0x0000FFFF;

            state = stateNew;

            return (long)value + 0x100000000L * (long)rotate16bit;
        }

        public static long Tick64ToTime64(long tick) => History.Tick64ToTime64(tick);
        public static DateTime Tick64ToDateTime(long tick) => Time.Time64ToDateTime(Tick64ToTime64(tick));
    }

    static class Time
    {
        static TimeHelper h = new TimeHelper();
        static TimeSpan baseTimeSpan = new TimeSpan(0, 0, 1);

        static public TimeSpan NowHighResTimeSpan
        {
            get
            {
                return h.Sw.Elapsed.Add(baseTimeSpan);
            }
        }

        static public long NowHighResLong100Usecs
        {
            get
            {
                return NowHighResTimeSpan.Ticks;
            }
        }

        static public long NowHighResLongMillisecs
        {
            get
            {
                return NowHighResLong100Usecs / 10000;
            }
        }

        static public long Tick64 => FastTick64.Now;

        static public long HighResTick64
        {
            get
            {
                return NowHighResLongMillisecs;
            }
        }

        static public double NowHighResDouble
        {
            get
            {
                return (double)NowHighResLong100Usecs / (double)10000000.0;
            }
        }

        static public DateTime NowHighResDateTimeLocal
        {
            get
            {
                return h.GetDateTimeOffset().LocalDateTime;
            }
        }

        static public DateTime NowHighResDateTimeUtc
        {
            get
            {
                return h.GetDateTimeOffset().UtcDateTime;
            }
        }

        static public DateTimeOffset NowHighResDateTimeOffset
        {
            get
            {
                return h.GetDateTimeOffset();
            }
        }

        public static long DateTimeToTime64(DateTime dt) => (long)Util.ConvertDateTime(dt);
        public static DateTime Time64ToDateTime(long time64) => Util.ConvertDateTime((ulong)time64);
        public static long SystemTime64 => DateTimeToTime64(DateTime.UtcNow);
        public static long LocalTime64 => DateTimeToTime64(DateTime.Now);

        public static long Tick64ToTime64(long tick) => FastTick64.Tick64ToTime64(tick);
        public static DateTime Tick64ToDateTime(long tick) => FastTick64.Tick64ToDateTime(tick);
    }
}
