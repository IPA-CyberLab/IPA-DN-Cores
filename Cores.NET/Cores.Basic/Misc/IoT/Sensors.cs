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
// Description

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
    public static partial class CoresConfig
    {
        public static partial class SensorConfig
        {
            public static readonly Copenhagen<int> RetryIntervalBaseMsecs = 1 * 1000;
            public static readonly Copenhagen<int> RetryIntervalMaxMsecs = 15 * 1000;
        }
    }

    // センサー設定
    public abstract class SensorSettingsBase
    {
        public SensorSettingsBase() { }
    }

    // COM ポートを用いて通信をするセンサー設定
    public class ComPortBasedSensorSettings : SensorSettingsBase
    {
        public ComPortSettings ComSettings { get; }

        public ComPortBasedSensorSettings(ComPortSettings comSettings)
        {
            ComSettings = comSettings;
        }
    }

    // センサーで取得された値データ
    public class SensorData
    {
        public SortedDictionary<string, string> ValueList { get; } = new SortedDictionary<string, string>(StrComparer.IgnoreCaseComparer);

        public string PrimaryValue
        {
            get
            {
                if (this.ValueList.TryGetValue("", out string? ret))
                    return ret._NonNull();

                return "";
            }
        }

        public void SetPrimaryValue(string primaryValue)
        {
            this.ValueList[""] = primaryValue._NonNullTrim();
        }

        public void AddValue(string name, string value)
        {
            this.ValueList[name._NonNullTrim()] = value._NonNullTrim();
        }

        public SensorData() { }

        public SensorData(string primaryValue)
        {
            SetPrimaryValue(primaryValue);
        }
    }

    // センサー基本クラス
    public abstract class SensorBase : AsyncServiceWithMainLoop
    {
        public SensorSettingsBase Settings { get; }
        public bool Started { get; private set; }

        public SensorData CurrentData { get; private set; } = new SensorData();

        public SensorBase(SensorSettingsBase settings)
        {
            this.Settings = settings;
        }

        readonly AsyncLock Lock = new AsyncLock();

        protected abstract Task ConnectAndGetValueImplAsync(CancellationToken cancel = default);

        public async Task StartAsync(CancellationToken cancel = default)
        {
            await Task.Yield();

            using (await Lock.LockWithAwait(cancel))
            {
                if (this.Started)
                    throw new CoresException("Already connected.");

                this.Started = true;

                Task t = this.StartMainLoop(MainLoopAsync);
            }
        }

        // 最新データのセット
        protected void UpdateCurrentData(SensorData data)
        {
            this.CurrentData = data;
        }

        // 接続試行 / 値取得を繰り返すループ
        async Task MainLoopAsync(CancellationToken cancel = default)
        {
            int nextWaitTime = 0;

            while (true)
            {
                cancel.ThrowIfCancellationRequested();

                await Task.Yield();

                try
                {
                    await ConnectAndGetValueImplAsync(cancel);
                }
                catch (Exception ex)
                {
                    ex.ToString()._DebugFunc();
                }

                cancel.ThrowIfCancellationRequested();

                nextWaitTime = nextWaitTime + CoresConfig.SensorConfig.RetryIntervalBaseMsecs;
                nextWaitTime = Math.Min(nextWaitTime, CoresConfig.SensorConfig.RetryIntervalMaxMsecs);

                nextWaitTime = Util.GenRandInterval(nextWaitTime);

                $"Waiting for {nextWaitTime} msecs to next retry"._DebugFunc();

                await cancel._WaitUntilCanceledAsync(nextWaitTime);
            }
        }
    }

    // COM ポートを用いて通信するセンサーの基本クラス
    public abstract class ComPortBasedSensorBase : SensorBase
    {
        public new ComPortBasedSensorSettings Settings => (ComPortBasedSensorSettings)base.Settings;

        protected abstract Task GetValueFromComPortImplAsync(ShellClientSock sock, CancellationToken cancel = default);

        public ComPortBasedSensorBase(ComPortBasedSensorSettings settings) : base(settings)
        {
        }

        protected override async Task ConnectAndGetValueImplAsync(CancellationToken cancel = default)
        {
            await Task.Yield();

            ComPortClient? client = null;
            try
            {
                client = new ComPortClient(this.Settings.ComSettings);

                using ShellClientSock sock = await client.ConnectAndGetSockAsync(cancel);

                await TaskUtil.DoAsyncWithTimeout(async (c) =>
                {
                    await Task.Yield();

                    await GetValueFromComPortImplAsync(sock, c);

                    return 0;
                },
                cancelProc: () =>
                {
                    sock._DisposeSafe(new OperationCanceledException());
                },
                cancel: cancel);
            }
            catch
            {
                await client._DisposeSafeAsync();
                throw;
            }
        }
    }

    // eMeter 8870 電圧センサー
    public class VoltageSensor8870 : ComPortBasedSensorBase
    {
        public new ComPortBasedSensorSettings Settings => (ComPortBasedSensorSettings)base.Settings;

        public VoltageSensor8870(ComPortBasedSensorSettings settings) : base(settings)
        {
        }

        protected override async Task GetValueFromComPortImplAsync(ShellClientSock sock, CancellationToken cancel = default)
        {
            var reader = new BinaryLineReader(sock.Stream);

            while (true)
            {
                string? line = await reader.ReadSingleLineStringAsync(cancel: cancel);
                if (line == null)
                {
                    break;
                }

                // ここで line には "0123.4A" のようなアンペア文字列が入っているはずである。
                if (line.EndsWith("A"))
                {
                    string numstr = line._SliceHead(line.Length - 1);

                    if (double.TryParse(numstr, out double value))
                    {
                        SensorData data = new SensorData(value.ToString("F2"));

                        this.UpdateCurrentData(data);
                    }
                }
            }
        }
    }


}

#endif

