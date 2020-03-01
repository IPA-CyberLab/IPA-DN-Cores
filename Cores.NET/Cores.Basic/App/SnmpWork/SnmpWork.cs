// IPA Cores.NET
// 
// Copyright (c) 2019- IPA CyberLab.
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

// Author: Daiyuu Nobori
// Snmp Work Daemon Util Main

#if true

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    public static class SnmpWorkConfig
    {
        public static readonly Copenhagen<int> DefaultPollingIntervalMsecs = 1000;
        public static readonly Copenhagen<int> TruncatedNameStrLen = 14;
    }

    // SNMP 用に、ある特定の値を取得するための抽象クラス
    public abstract class SnmpWorkFetcherBase : AsyncServiceWithMainLoop
    {
        public readonly int PollingIntervalMsecs = 0;

        protected abstract Task GetValueAsync(SortedDictionary<string, string> ret, RefInt nextPollingInterval, CancellationToken cancel = default);

        public IEnumerable<KeyValuePair<string, string>> CurrentValues { get; private set; }

        public SnmpWorkFetcherBase(int pollingInterval = 0)
        {
            try
            {
                this.CurrentValues = new SortedDictionary<string, string>();

                if (pollingInterval <= 0) pollingInterval = SnmpWorkConfig.DefaultPollingIntervalMsecs;

                this.PollingIntervalMsecs = pollingInterval;

                this.StartMainLoop(MainLoopAsync);
            }
            catch
            {
                this._DisposeSafe();
                throw;
            }
        }

        async Task MainLoopAsync(CancellationToken cancel)
        {
            while (cancel.IsCancellationRequested == false)
            {
                RefInt nextPollingInterval = this.PollingIntervalMsecs;

                try
                {
                    SortedDictionary<string, string> ret = new SortedDictionary<string, string>(StrComparer.IgnoreCaseTrimComparer);

                    await GetValueAsync(ret, nextPollingInterval, cancel);

                    this.CurrentValues = ret;
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }

                int interval = nextPollingInterval;
                if (interval <= 0) interval = this.PollingIntervalMsecs;

                await cancel._WaitUntilCanceledAsync(interval);
            }
        }

        // 値を正規化する (一旦 double に変換してから、小数点以下 3 桁の数値に変換する)
        public virtual string NormalizeValue(string src)
        {
            double d = double.Parse(src);

            return d.ToString("F3");
        }

        // 長すぎる名前を短くする
        public virtual string TruncateName(string src) => src._TruncStrMiddle(SnmpWorkConfig.TruncatedNameStrLen);
    }

    // SNMP 用に各種の値を常に取得し続けるプロセス
    public class SnmpWorkProcess : AsyncService
    {
        readonly CriticalSection LockList = new CriticalSection();

        readonly SortedDictionary<string, SnmpWorkFetcherBase> CurrentFetcherList = new SortedDictionary<string, SnmpWorkFetcherBase>(StrComparer.IgnoreCaseTrimComparer);

        public SnmpWorkProcess()
        {
            try
            {
            }
            catch
            {
                this._DisposeSafe();
                throw;
            }
        }

        public SortedDictionary<string, string> GetValues()
        {
            SortedDictionary<string, string> ret = new SortedDictionary<string, string>();

            lock (LockList)
            {
                foreach (string name in CurrentFetcherList.Keys)
                {
                    var fetcher = CurrentFetcherList[name];

                    IEnumerable<KeyValuePair<string, string>> values = fetcher.CurrentValues;

                    foreach (var kv in values)
                    {
                        if (kv.Value._IsFilled())
                        {
                            string name2 = $"{name} - {kv.Key}";
                            string value2 = kv.Value;

                            ret.Add(name2, value2);
                        }
                    }
                }
            }

            return ret;
        }

        public void Register(string name, SnmpWorkFetcherBase fetcher)
        {
            using (EnterCriticalCounter())
            {
                try
                {
                    name._NotEmptyCheck(nameof(name));

                    lock (LockList)
                    {
                        CurrentFetcherList.Add(name, fetcher);
                    }
                }
                catch
                {
                    fetcher._DisposeSafe();

                    throw;
                }
            }
        }

        protected override void DisposeImpl(Exception? ex)
        {
            try
            {
                List<SnmpWorkFetcherBase> o = new List<SnmpWorkFetcherBase>();

                lock (LockList)
                {
                    o = CurrentFetcherList.Values.ToList();
                    CurrentFetcherList.Clear();
                }

                foreach (var fetcher in o)
                {
                    fetcher._DisposeSafe();
                }
            }
            finally
            {
                base.DisposeImpl(ex);
            }
        }
    }
}

#endif

