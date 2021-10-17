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
// 開発中のクラスの一時置き場

#if true

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;

using IPA.Cores.Basic;
using IPA.Cores.Basic.DnsLib;
using IPA.Cores.Basic.Internal;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Microsoft.AspNetCore.Server.IIS.Core;
using Microsoft.EntityFrameworkCore.Query.Internal;
using System.Net.Sockets;

using Newtonsoft.Json;
using System.Data;

namespace IPA.Cores.Basic
{
    public class MikakaDDnsHost : HadbData
    {
        public string HostName = "";
        public string IPv4Address = "";
        public string IPv6Address = "";
        public int TestInt = 0;
    }

    public class MikakaDDnsDynamicConfig : HadbDynamicConfig
    {
        public int TestAbc;

        protected override void NormalizeImpl()
        {
            this.TestAbc = this.TestAbc._ZeroOrDefault(100, max: 1000);
        }
    }

    public class MikakaDDnsMem : HadbMemBase<MikakaDDnsDynamicConfig>
    {
        // --- Bulk 取得されるべきデータ群 (バックアップファイルとして保存されるデータ群) ---
        public StrDictionary<MikakaDDnsHost> HostList = new StrDictionary<MikakaDDnsHost>();

        // --- 上記データをもとにハッシュ化したメモリ上の高速アクセス可能なハッシュ化された中間データ ---
        // 以下はすべてのフィールドに [JsonIgnore] を付けること！！
    }

    public class MikakaDDnsHadb : HadbSqlBase<MikakaDDnsMem, MikakaDDnsDynamicConfig>
    {
        public MikakaDDnsHadb(HadbSqlSettings settings, MikakaDDnsDynamicConfig dynamicConfig) : base(settings, dynamicConfig)
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
    }

    public class HadbDynamicConfig : INormalizable
    {
        protected virtual void NormalizeImpl() { }

        public int HadbReloadIntervalMsecs;
        public int HadbLazyUpdateIntervalMsecs;
        public int HadbBackupFileWriteIntervalMsecs;
        public int HadbRecordStatIntervalMsecs;

        public HadbDynamicConfig()
        {
            this.Normalize();
        }

        public void Normalize()
        {
            this.HadbReloadIntervalMsecs = this.HadbReloadIntervalMsecs._ZeroOrDefault(Consts.HadbDynamicConfigDefaultValues.HadbReloadIntervalMsecs, max: Consts.HadbDynamicConfigMaxValues.HadbReloadIntervalMsecs);
            this.HadbLazyUpdateIntervalMsecs = this.HadbLazyUpdateIntervalMsecs._ZeroOrDefault(Consts.HadbDynamicConfigDefaultValues.HadbLazyUpdateIntervalMsecs, max: Consts.HadbDynamicConfigMaxValues.HadbLazyUpdateIntervalMsecs);
            this.HadbBackupFileWriteIntervalMsecs = this.HadbBackupFileWriteIntervalMsecs._ZeroOrDefault(Consts.HadbDynamicConfigDefaultValues.HadbBackupFileWriteIntervalMsecs, max: Consts.HadbDynamicConfigMaxValues.HadbBackupFileWriteIntervalMsecs);
            this.HadbRecordStatIntervalMsecs = this.HadbRecordStatIntervalMsecs._ZeroOrDefault(Consts.HadbDynamicConfigDefaultValues.HadbRecordStatIntervalMsecs, max: Consts.HadbDynamicConfigMaxValues.HadbRecordStatIntervalMsecs);

            this.NormalizeImpl();
        }
    }

    public class HadbSqlSettings : HadbSettingsBase
    {
        public string SqlConnectStringForRead { get; }
        public string SqlConnectStringForWrite { get; }

        public HadbSqlSettings(string sqlConnectStringForRead, string sqlConnectStringForWrite)
        {
            this.SqlConnectStringForRead = sqlConnectStringForRead;
            this.SqlConnectStringForWrite = sqlConnectStringForWrite;
        }
    }

    public sealed class HadbSqlConfigRow : INormalizable
    {
        [EasyKey]
        public long CONFIG_ID { get; set; } = 0;
        public string CONFIG_SYSTEMNAME { get; set; } = "";
        public string CONFIG_NAME { get; set; } = "";
        public string CONFIG_VALUE { get; set; } = "";
        public string CONFIG_EXT { get; set; } = "";

        public void Normalize()
        {
            this.CONFIG_SYSTEMNAME = this.CONFIG_SYSTEMNAME._NonNullTrim();
            this.CONFIG_NAME = this.CONFIG_NAME._NonNullTrim();
            this.CONFIG_VALUE = this.CONFIG_VALUE._NonNull();
            this.CONFIG_EXT = this.CONFIG_EXT._NonNull();
        }
    }

    public sealed class HadbSqlDataRow : INormalizable
    {
        [EasyManualKey]
        public string DATA_UID { get; set; } = "";
        public string DATA_SYSTEMNAME { get; set; } = "";
        public string DATA_TYPE { get; set; } = "";
        public long DATA_VER { get; set; } = 0;
        public bool DATA_DELETED { get; set; } = false;
        public DateTimeOffset DATA_CREATE_DT { get; set; } = Util.ZeroDateTimeOffsetValue;
        public DateTimeOffset DATA_UPDATE_DT { get; set; } = Util.ZeroDateTimeOffsetValue;
        public DateTimeOffset DATA_DELETE_DT { get; set; } = Util.ZeroDateTimeOffsetValue;
        public string DATA_KEY1 { get; set; } = "";
        public string DATA_KEY2 { get; set; } = "";
        public string DATA_KEY3 { get; set; } = "";
        public string DATA_KEY4 { get; set; } = "";
        public string DATA_VALUE { get; set; } = "";
        public string DATA_EXT { get; set; } = "";

        public void Normalize()
        {
            this.DATA_UID = this.DATA_UID._NonNullTrim().ToUpper();
            this.DATA_SYSTEMNAME = this.DATA_SYSTEMNAME._NonNullTrim();
            this.DATA_TYPE = this.DATA_TYPE._NonNullTrim();
            this.DATA_CREATE_DT = this.DATA_CREATE_DT._NormalizeDateTimeOffset();
            this.DATA_UPDATE_DT = this.DATA_UPDATE_DT._NormalizeDateTimeOffset();
            this.DATA_DELETE_DT = this.DATA_DELETE_DT._NormalizeDateTimeOffset();
            this.DATA_KEY1 = this.DATA_KEY1._NonNullTrim().ToUpper();
            this.DATA_KEY2 = this.DATA_KEY2._NonNullTrim().ToUpper();
            this.DATA_KEY3 = this.DATA_KEY3._NonNullTrim().ToUpper();
            this.DATA_KEY4 = this.DATA_KEY4._NonNullTrim().ToUpper();
            this.DATA_VALUE = this.DATA_VALUE._NonNull();
            this.DATA_EXT = this.DATA_EXT._NonNull();
        }
    }

    public abstract class HadbSqlBase<TMem, TDynamicConfig> : HadbBase<TMem, TDynamicConfig>
        where TMem : HadbMemBase<TDynamicConfig>
        where TDynamicConfig : HadbDynamicConfig, new()
    {
        public new HadbSqlSettings Settings => (HadbSqlSettings)base.Settings;

        public HadbSqlBase(HadbSqlSettings settings, TDynamicConfig dynamicConfig) : base(settings, dynamicConfig)
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

        public async Task<Database> OpenSqlDatabaseForReadAsync(CancellationToken cancel = default)
        {
            Database db = new Database(this.Settings.SqlConnectStringForRead, defaultIsolationLevel: IsolationLevel.Snapshot);

            try
            {
                await db.EnsureOpenAsync(cancel);

                return db;
            }
            catch
            {
                await db._DisposeSafeAsync();
                throw;
            }
        }

        public async Task<Database> OpenSqlDatabaseForWriteAsync(CancellationToken cancel = default)
        {
            Database db = new Database(this.Settings.SqlConnectStringForWrite, defaultIsolationLevel: IsolationLevel.Serializable);

            try
            {
                await db.EnsureOpenAsync(cancel);

                return db;
            }
            catch
            {
                await db._DisposeSafeAsync();
                throw;
            }
        }

        protected override async Task<TMem> ReloadImplAsync(TMem? current, CancellationToken cancel)
        {
            try
            {
                IEnumerable<HadbSqlConfigRow> configList = null!;
                IEnumerable<HadbSqlDataRow> dataList = null!;

                await using var db = await this.OpenSqlDatabaseForReadAsync(cancel);

                await db.TranReadSnapshotIfNecessaryAsync(async () =>
                {
                    configList = await db.EasySelectAsync<HadbSqlConfigRow>("select * from CONFIG where CONFIG_SYSTEMNAME = @CONFIG_SYSTEMNAME", new { CONFIG_SYSTEMNAME = "abc" });
                    dataList = await db.EasySelectAsync<HadbSqlDataRow>("select * from DATA where DATA_SYSTEMNAME = @DATA_SYSTEMNAME", new { CONFIG_SYSTEMNAME = "abc" }); // TODO
                });
            }
            catch (Exception ex)
            {
                Debug($"ReloadImplAsync: Database Connect Error: {ex.ToString()}");
                throw;
            }
        }
    }

    public abstract class HadbSettingsBase
    {
    }

    public abstract class HadbData : INormalizable
    {
        public string Uid = "";
        public long Ver = 0;
        public bool Deleted = false;
        public DateTimeOffset CreateDt = Util.ZeroDateTimeOffsetValue;
        public DateTimeOffset UpdateDt = Util.ZeroDateTimeOffsetValue;
        public DateTimeOffset DeleteDt = Util.ZeroDateTimeOffsetValue;

        public string GetTypeName() => this.GetType().Name;
        public string GetUidPrefix() => this.GetTypeName().ToUpper();

        public void Normalize()
        {
            if (this.Uid._IsEmpty()) this.Uid = Str.NewUid(this.GetUidPrefix(), '_');
            this.Uid = this.Uid._NonNullTrim().ToUpper();
            this.CreateDt = this.CreateDt._NormalizeDateTimeOffset();
            this.UpdateDt = this.UpdateDt._NormalizeDateTimeOffset();
            this.DeleteDt = this.DeleteDt._NormalizeDateTimeOffset();
        }
    }

    public abstract class HadbMemBase<TDynamicConfig> // 注意! ファイル保存するため、むやみに [JsonIgnore] を書かないこと！！
        where TDynamicConfig : HadbDynamicConfig, new()
    {
        // --- Bulk 取得されるべきデータ群 (バックアップファイルとして保存されるデータ群) ---
        public TDynamicConfig DynamicConfig = new TDynamicConfig();

        // --- 上記データをもとにハッシュ化したメモリ上の高速アクセス可能なハッシュ化された中間データ ---
        // 以下はすべてのフィールドに [JsonIgnore] を付けること！！
    }

    public abstract class HadbBase<TMem, TDynamicConfig> : AsyncService
        where TMem : HadbMemBase<TDynamicConfig>
        where TDynamicConfig : HadbDynamicConfig, new()
    {
        Task ReloadMainLoopTask;
        Task LazyUpdateMainLoopTask;

        public bool IsLoopStarted { get; private set; } = false;
        public int LastReloadTookMsecs { get; private set; } = 0;
        public bool IsDatabaseConnectedForReload { get; private set; } = false;
        public bool IsDatabaseConnectedForLazyWrite { get; private set; } = false;

        public HadbSettingsBase Settings { get; }
        public TDynamicConfig CurrentDynamicConfig { get; private set; }

        public TMem? MemDb { get; private set; } = null;

        protected abstract Task<TMem> ReloadImplAsync(TMem? current, CancellationToken cancel);

        public HadbBase(HadbSettingsBase settings, TDynamicConfig dynamicConfig)
        {
            try
            {
                settings._NullCheck(nameof(settings));
                this.Settings = settings;

                dynamicConfig._NullCheck(nameof(dynamicConfig));
                this.CurrentDynamicConfig = dynamicConfig;

                this.ReloadMainLoopTask = ReloadMainLoopAsync(this.GrandCancel)._LeakCheck();
                this.LazyUpdateMainLoopTask = LazyUpdateMainLoopAsync(this.GrandCancel)._LeakCheck();
            }
            catch
            {
                this._DisposeSafe();
                throw;
            }
        }

        public void StartLoop()
        {
            this.IsLoopStarted = true;
        }

        async Task ReloadCoreAsync(CancellationToken cancel)
        {
            try
            {
                TMem? currentMemDb = this.MemDb;

                TMem newMemDb = await this.ReloadImplAsync(currentMemDb, cancel);

                this.IsDatabaseConnectedForReload = true;

                this.MemDb = newMemDb;
            }
            catch
            {
                this.IsDatabaseConnectedForReload = false;

                // データベースからもバックアップファイルからもまだデータが読み込まれていない場合は、バックアップファイルから読み込む

                // バックアップファイルの読み込みを行なった上で、DB 例外はちゃんと throw する
                throw;
            }
        }

        async Task ReloadMainLoopAsync(CancellationToken cancel)
        {
            int numCycle = 0;
            int numError = 0;

            await Task.Yield();
            Debug($"ReloadMainLoopAsync: Waiting for start.");
            await TaskUtil.AwaitWithPollAsync(Timeout.Infinite, 100, () => this.IsLoopStarted, cancel);
            Debug($"ReloadMainLoopAsync: Started.");

            while (cancel.IsCancellationRequested == false)
            {
                numCycle++;
                Debug($"ReloadMainLoopAsync: numCycle={numCycle}, numError={numError} Start.");

                long startTick = Time.HighResTick64;
                bool ok = false;

                try
                {
                    await ReloadCoreAsync(cancel);
                    ok = true;
                }
                catch (Exception ex)
                {
                    ex._Error();
                }

                long endTick = Time.HighResTick64;
                if (ok)
                {
                    LastReloadTookMsecs = (int)(endTick - startTick);
                }
                else
                {
                    LastReloadTookMsecs = 0;
                }

                Debug($"ReloadMainLoopAsync: numCycle={numCycle}, numError={numError} End. Took time: {endTick - startTick} msecs.");
            }

            Debug($"ReloadMainLoopAsync: Finished.");
        }

        async Task LazyUpdateMainLoopAsync(CancellationToken cancel)
        {
            await Task.Yield();
            await TaskUtil.AwaitWithPollAsync(Timeout.Infinite, 100, () => this.IsLoopStarted, cancel);
        }

        public void Debug(string str)
        {
            $"{this.GetType().Name}: {str}"._Debug();
        }
    }
}

#endif

