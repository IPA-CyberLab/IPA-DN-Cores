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

    // センサー基本クラス
    public abstract class SensorBase : AsyncService
    {
        public SensorSettingsBase Settings { get; }
        public bool Connected { get; private set; }

        public SensorBase(SensorSettingsBase settings)
        {
            this.Settings = settings;
        }

        protected abstract Task ConnectImplAsync(CancellationToken cancel = default);
        protected abstract Task DisconnectImplAsync();
        readonly AsyncLock Lock = new AsyncLock();

        public async Task ConnectAsync(CancellationToken cancel = default)
        {
            await Task.Yield();

            using (await Lock.LockWithAwait(cancel))
            {
                if (this.Connected)
                    throw new CoresException("Already connected.");

                await ConnectImplAsync(cancel);

                this.Connected = true;
            }
        }

        protected override async Task CleanupImplAsync(Exception? ex)
        {
            try
            {
                using (await Lock.LockWithAwait())
                {
                    if (this.Connected)
                    {
                        this.Connected = false;

                        await DisconnectImplAsync();
                    }
                }
            }
            finally
            {
                await base.CleanupImplAsync(ex);
            }
        }
    }

    // COM ポートを用いて通信するセンサーの基本クラス
    public abstract class ComPortBasedSensorBase : SensorBase
    {
        public new ComPortBasedSensorSettings Settings => (ComPortBasedSensorSettings)base.Settings;

        protected ShellClientSock Sock { get; private set; } = null!;

        public ComPortBasedSensorBase(ComPortBasedSensorSettings settings) : base(settings)
        {
        }

        protected override async Task ConnectImplAsync(CancellationToken cancel = default)
        {
            ComPortClient com = new ComPortClient(this.Settings.ComSettings);

            try
            {
                var sock = await com.ConnectAndGetSockAsync(cancel);

                this.Sock = sock;
            }
            catch
            {
                await com._DisposeSafeAsync2();
                throw;
            }
        }

        protected override async Task DisconnectImplAsync()
        {
            await this.Sock._DisposeSafeAsync();

            this.Sock = null!;
        }
    }

    // eMeter 8870 電圧センサー
    public class VoltageSensor8870 : ComPortBasedSensorBase
    {
        public new ComPortBasedSensorSettings Settings => (ComPortBasedSensorSettings)base.Settings;

        public VoltageSensor8870(ComPortBasedSensorSettings settings) : base(settings)
        {
        }
    }


}

#endif

