﻿// IPA Cores.NET
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
// Hadb Library

// メモ HADB 物理 DB 消費領域
//      30 万 DDNS 模擬レコード時  データ領域 300MB、インデックス領域 600 MB
//      つまり 1 レコードあたり デーサイズ 1.0KB、インデックスサイズ 2.0 KB (だいたいの目安)
//      SQL Server Express の DB は 10GB までなので 300 万レコードくらいが限度か?
//      .NET オンメモリ上で MemDb に展開すると 10GB くらいのメモリを消費した。数分かかった。

#if CORES_BASIC_DATABASE

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Buffers;
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
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Reflection;
using System.Data;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace IPA.Cores.Basic;

public class HadbTestData : HadbData
{
    public string HostName = "";
    public string IPv4Address = "";
    public string IPv6Address = "";
    public int TestInt = 0;

    public override HadbKeys GetKeys() => new HadbKeys(this.HostName);

    public override HadbLabels GetLabels() => new HadbLabels(this.IPv4Address, this.IPv6Address);

    public override void Normalize()
    {
        this.HostName = this.HostName._NonNullTrim().ToLower();
        this.IPv4Address = this.IPv4Address._NormalizeIp();
        this.IPv6Address = this.IPv6Address._NormalizeIp();
    }

}

public class HadbTestDynamicConfig : HadbDynamicConfig
{
    public int TestAbc;
    public string[] TestDef = EmptyOf<string>();
    public string TestStr1 = "";
    public string TestStr2 = "";

    protected override void NormalizeImpl()
    {
        this.TestAbc = this.TestAbc._ZeroToDefault(100, max: 1000);
    }
}

public class HadbTestMem : HadbMemDataBase
{
    protected override List<Type> GetDefinedUserDataTypesImpl()
    {
        List<Type> ret = new List<Type>();
        ret.Add(typeof(HadbTestData));
        return ret;
    }

    protected override List<Type> GetDefinedUserLogTypesImpl()
    {
        throw new NotImplementedException();
    }
}

public class HadbTest : HadbSqlBase<HadbTestMem, HadbTestDynamicConfig>
{
    public HadbTest(HadbSqlSettings settings, HadbTestDynamicConfig dynamicConfig) : base(settings, dynamicConfig) { }
}

public class HadbDynamicConfig : INormalizable
{
    protected virtual void NormalizeImpl() { }

    public int HadbReloadIntervalMsecsLastOk = Consts.HadbDynamicConfigDefaultValues.HadbReloadIntervalMsecsLastOk;
    public int HadbReloadIntervalMsecsLastError = Consts.HadbDynamicConfigDefaultValues.HadbReloadIntervalMsecsLastError;
    public int HadbReloadTimeShiftMarginMsecs = Consts.HadbDynamicConfigDefaultValues.HadbReloadTimeShiftMarginMsecs;
    public int HadbFullReloadIntervalMsecs = Consts.HadbDynamicConfigDefaultValues.HadbFullReloadIntervalMsecs;
    public int HadbLazyUpdateIntervalMsecs = Consts.HadbDynamicConfigDefaultValues.HadbLazyUpdateIntervalMsecs;

    public int HadbAutomaticSnapshotIntervalMsecs = Consts.HadbDynamicConfigDefaultValues.HadbAutomaticSnapshotIntervalMsecs;
    public int HadbAutomaticStatIntervalMsecs = Consts.HadbDynamicConfigDefaultValues.HadbAutomaticStatIntervalMsecs;

    public int HadbMaxDbConcurrentWriteTransactionsTotal = Consts.HadbDynamicConfigDefaultValues.HadbMaxDbConcurrentWriteTransactionsTotal;
    public int HadbMaxDbConcurrentReadTransactionsTotal = Consts.HadbDynamicConfigDefaultValues.HadbMaxDbConcurrentReadTransactionsTotal;
    public int HadbMaxDbConcurrentWriteTransactionsPerClient = Consts.HadbDynamicConfigDefaultValues.HadbMaxDbConcurrentWriteTransactionsPerClient;
    public int HadbMaxDbConcurrentReadTransactionsPerClient = Consts.HadbDynamicConfigDefaultValues.HadbMaxDbConcurrentReadTransactionsPerClient;

    public HadbDynamicConfig()
    {
        this.Normalize();
    }

    public void Normalize()
    {
        this.HadbReloadIntervalMsecsLastOk = this.HadbReloadIntervalMsecsLastOk._ZeroToDefault(Consts.HadbDynamicConfigDefaultValues.HadbReloadIntervalMsecsLastOk, max: Consts.HadbDynamicConfigMaxValues.HadbReloadIntervalMsecsLastOk);
        this.HadbReloadIntervalMsecsLastError = this.HadbReloadIntervalMsecsLastError._ZeroToDefault(Consts.HadbDynamicConfigDefaultValues.HadbReloadIntervalMsecsLastError, max: Consts.HadbDynamicConfigMaxValues.HadbReloadIntervalMsecsLastError);
        this.HadbReloadTimeShiftMarginMsecs = this.HadbReloadTimeShiftMarginMsecs._ZeroToDefault(Consts.HadbDynamicConfigDefaultValues.HadbReloadTimeShiftMarginMsecs, max: Consts.HadbDynamicConfigMaxValues.HadbReloadTimeShiftMarginMsecs);
        this.HadbFullReloadIntervalMsecs = this.HadbFullReloadIntervalMsecs._ZeroToDefault(Consts.HadbDynamicConfigDefaultValues.HadbFullReloadIntervalMsecs, max: Consts.HadbDynamicConfigMaxValues.HadbFullReloadIntervalMsecs);
        this.HadbLazyUpdateIntervalMsecs = this.HadbLazyUpdateIntervalMsecs._ZeroToDefault(Consts.HadbDynamicConfigDefaultValues.HadbLazyUpdateIntervalMsecs, max: Consts.HadbDynamicConfigMaxValues.HadbLazyUpdateIntervalMsecs);

        this.HadbAutomaticSnapshotIntervalMsecs = Math.Max(this.HadbAutomaticSnapshotIntervalMsecs, 0);
        this.HadbAutomaticStatIntervalMsecs = Math.Max(this.HadbAutomaticStatIntervalMsecs, 0);

        this.HadbMaxDbConcurrentWriteTransactionsTotal = this.HadbMaxDbConcurrentWriteTransactionsTotal._ZeroToDefault(Consts.HadbDynamicConfigDefaultValues.HadbMaxDbConcurrentWriteTransactionsTotal);
        this.HadbMaxDbConcurrentReadTransactionsTotal = this.HadbMaxDbConcurrentReadTransactionsTotal._ZeroToDefault(Consts.HadbDynamicConfigDefaultValues.HadbMaxDbConcurrentReadTransactionsTotal);
        this.HadbMaxDbConcurrentWriteTransactionsPerClient = this.HadbMaxDbConcurrentWriteTransactionsPerClient._ZeroToDefault(Consts.HadbDynamicConfigDefaultValues.HadbMaxDbConcurrentWriteTransactionsPerClient);
        this.HadbMaxDbConcurrentReadTransactionsPerClient = this.HadbMaxDbConcurrentReadTransactionsPerClient._ZeroToDefault(Consts.HadbDynamicConfigDefaultValues.HadbMaxDbConcurrentReadTransactionsPerClient);

        this.NormalizeImpl();
    }

    public KeyValueList<string, string> UpdateFromDatabaseAndReturnMissingValues(KeyValueList<string, string> dataListFromDb)
    {
        var rw = this.GetType()._GetFieldReaderWriter();

        var fields = rw.MetadataTable.Where(x => x.Value.MemberType.IsAnyOfThem(MemberTypes.Field, MemberTypes.Property));

        HashSet<string> suppliedList = new HashSet<string>(StrComparer.IgnoreCaseTrimComparer);

        foreach (var field in fields)
        {
            string name = field.Key;
            var metaInfo = field.Value;
            Type? type = metaInfo.GetFieldOrPropertyInfo();

            bool updated = false;

            if (type == typeof(int))
            {
                if (dataListFromDb._TryGetFirstValue(name, out string valueStr, StrComparer.IgnoreCaseTrimComparer))
                {
                    rw.SetValue(this, name, valueStr._ToInt());
                    updated = true;
                }
            }
            else if (type == typeof(string))
            {
                if (dataListFromDb._TryGetFirstValue(name, out string valueStr, StrComparer.IgnoreCaseTrimComparer))
                {
                    rw.SetValue(this, name, valueStr._NonNullTrim());
                    updated = true;
                }
            }
            else if (type == typeof(string[]))
            {
                string[] newArray = dataListFromDb.Where(x => x.Key._IsSameTrimi(name) && x.Value._IsFilled()).Select(x => x.Value._NonNullTrim()).ToArray();
                if (newArray.Any())
                {
                    rw.SetValue(this, name, newArray);
                    updated = true;
                }
            }
            else
            {
                continue;
            }

            if (updated)
            {
                suppliedList.Add(name);
            }
        }

        this.Normalize();

        KeyValueList<string, string> ret = new KeyValueList<string, string>();

        foreach (var field in fields)
        {
            string name = field.Key;
            var metaInfo = field.Value;
            Type? type = metaInfo.GetFieldOrPropertyInfo();

            if (suppliedList.Contains(name) == false)
            {
                if (type == typeof(int))
                {
                    ret.Add(name, ((int)rw.GetValue(this, name)!).ToString());
                }
                else if (type == typeof(string))
                {
                    ret.Add(name, ((string?)rw.GetValue(this, name))._NonNullTrim());
                }
                else if (type == typeof(string[]))
                {
                    string[]? currentArray = (string[]?)rw.GetValue(this, name);
                    if (currentArray == null)
                    {
                        currentArray = EmptyOf<string>();
                    }

                    var tmp = currentArray.Where(x => x._IsFilled()).Select(x => x._NonNullTrim());

                    if (tmp.Any())
                    {
                        foreach (var a in currentArray)
                        {
                            ret.Add(name, a);
                        }
                    }
                    else
                    {
                        ret.Add(name, "");
                    }
                }
            }
        }

        return ret;
    }
}

public class HadbSqlSettings : HadbSettingsBase
{
    public string SqlConnectStringForRead { get; }
    public string SqlConnectStringForWrite { get; }

    public IsolationLevel IsolationLevelForRead { get; }
    public IsolationLevel IsolationLevelForWrite { get; }

    public HadbSqlSettings(string systemName, string sqlConnectStringForRead, string sqlConnectStringForWrite, IsolationLevel isoLevelForRead = IsolationLevel.Snapshot, IsolationLevel isoLevelForWrite = IsolationLevel.Serializable, HadbOptionFlags optionFlags = HadbOptionFlags.None) : base(systemName, optionFlags)
    {
        this.SqlConnectStringForRead = sqlConnectStringForRead;
        this.SqlConnectStringForWrite = sqlConnectStringForWrite;

        this.IsolationLevelForRead = isoLevelForRead;
        this.IsolationLevelForWrite = isoLevelForWrite;
    }
}

[EasyTable("HADB_STAT")]
public sealed class HadbSqlStatRow : INormalizable
{
    [EasyManualKey]
    public string STAT_UID { get; set; } = "";
    public string STAT_SYSTEMNAME { get; set; } = "";
    public long STAT_SNAPSHOT_NO { get; set; } = 0;
    public DateTimeOffset STAT_DT { get; set; } = Util.ZeroDateTimeOffsetValue;
    public string STAT_GENERATOR { get; set; } = "";
    public string STAT_VALUE { get; set; } = "";
    public string STAT_EXT1 { get; set; } = "";
    public string STAT_EXT2 { get; set; } = "";

    public void Normalize()
    {
        this.STAT_UID = this.STAT_UID._NormalizeKey(true);
        this.STAT_SYSTEMNAME = this.STAT_SYSTEMNAME._NormalizeKey(true);
        this.STAT_DT = this.STAT_DT._NormalizeDateTimeOffset();
        this.STAT_GENERATOR = this.STAT_GENERATOR._NonNullTrim().ToLower();
        this.STAT_VALUE = this.STAT_VALUE._NonNull();
        this.STAT_EXT1 = this.STAT_EXT1._NonNull();
        this.STAT_EXT2 = this.STAT_EXT2._NonNull();
    }
}

[EasyTable("HADB_SNAPSHOT")]
public sealed class HadbSqlSnapshotRow : INormalizable
{
    [EasyManualKey]
    public string SNAPSHOT_UID { get; set; } = "";
    public string SNAPSHOT_SYSTEM_NAME { get; set; } = "";
    public long SNAPSHOT_NO { get; set; } = 0;
    public DateTimeOffset SNAPSHOT_DT { get; set; } = Util.ZeroDateTimeOffsetValue;
    public string SNAPSHOT_DESCRIPTION { get; set; } = "";
    public string SNAPSHOT_EXT1 { get; set; } = "";
    public string SNAPSHOT_EXT2 { get; set; } = "";

    public void Normalize()
    {
        this.SNAPSHOT_UID = this.SNAPSHOT_UID._NormalizeKey(true);
        this.SNAPSHOT_SYSTEM_NAME = this.SNAPSHOT_SYSTEM_NAME._NormalizeKey(true);
        this.SNAPSHOT_DT = this.SNAPSHOT_DT._NormalizeDateTimeOffset();
        this.SNAPSHOT_DESCRIPTION = this.SNAPSHOT_DESCRIPTION._NonNull();
        this.SNAPSHOT_EXT1 = this.SNAPSHOT_EXT1._NonNull();
        this.SNAPSHOT_EXT2 = this.SNAPSHOT_EXT2._NonNull();
    }
}

[EasyTable("HADB_KV")]
public sealed class HadbSqlKvRow : INormalizable
{
    [EasyKey]
    public long KV_ID { get; set; } = 0;
    public string KV_SYSTEM_NAME { get; set; } = "";
    public string KV_KEY { get; set; } = "";
    public string KV_VALUE { get; set; } = "";
    public bool KV_DELETED { get; set; } = false;
    public DateTimeOffset KV_CREATE_DT { get; set; } = Util.ZeroDateTimeOffsetValue;
    public DateTimeOffset KV_UPDATE_DT { get; set; } = Util.ZeroDateTimeOffsetValue;

    public void Normalize()
    {
        this.KV_SYSTEM_NAME = this.KV_SYSTEM_NAME._NormalizeKey(true);
        this.KV_KEY = this.KV_KEY._NormalizeKey(true, Consts.Numbers.SqlMaxSafeStrLengthActual);
        this.KV_VALUE = this.KV_VALUE._NonNull();
        this.KV_CREATE_DT = this.KV_CREATE_DT._NormalizeDateTimeOffset();
        this.KV_UPDATE_DT = this.KV_UPDATE_DT._NormalizeDateTimeOffset();
    }
}

[EasyTable("HADB_LOG")]
public sealed class HadbSqlLogRow : INormalizable
{
    [EasyKey]
    public long LOG_ID { get; set; } = 0;
    public string LOG_UID { get; set; } = "";
    public string LOG_SYSTEM_NAME { get; set; } = "";
    public string LOG_TYPE { get; set; } = "";
    public string LOG_NAMESPACE { get; set; } = "";
    public DateTimeOffset LOG_DT { get; set; } = Util.ZeroDateTimeOffsetValue;
    public long LOG_SNAP_NO { get; set; } = 0;
    public bool LOG_DELETED { get; set; } = false;
    public string LOG_LABEL1 { get; set; } = "";
    public string LOG_LABEL2 { get; set; } = "";
    public string LOG_LABEL3 { get; set; } = "";
    public string LOG_LABEL4 { get; set; } = "";
    public string LOG_VALUE { get; set; } = "";
    public string LOG_EXT1 { get; set; } = "";
    public string LOG_EXT2 { get; set; } = "";

    public void Normalize()
    {
        this.LOG_UID = this.LOG_UID._NormalizeKey(true);
        this.LOG_SYSTEM_NAME = this.LOG_SYSTEM_NAME._NormalizeKey(true);
        this.LOG_TYPE = this.LOG_TYPE._NonNullTrim();
        this.LOG_NAMESPACE = this.LOG_NAMESPACE._NormalizeKey(true);
        this.LOG_DT = this.LOG_DT._NormalizeDateTimeOffset();
        this.LOG_LABEL1 = this.LOG_LABEL1._NormalizeKey(true);
        this.LOG_LABEL2 = this.LOG_LABEL2._NormalizeKey(true);
        this.LOG_LABEL3 = this.LOG_LABEL3._NormalizeKey(true);
        this.LOG_LABEL4 = this.LOG_LABEL4._NormalizeKey(true);
        this.LOG_VALUE = this.LOG_VALUE._NonNull();
        this.LOG_EXT1 = this.LOG_EXT1._NonNull();
        this.LOG_EXT2 = this.LOG_EXT2._NonNull();
    }
}

[EasyTable("HADB_CONFIG")]
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
        this.CONFIG_SYSTEMNAME = this.CONFIG_SYSTEMNAME._NormalizeKey(true);
        this.CONFIG_NAME = this.CONFIG_NAME._NonNullTrim();
        this.CONFIG_VALUE = this.CONFIG_VALUE._NonNull();
        this.CONFIG_EXT = this.CONFIG_EXT._NonNull();
    }
}

[EasyTable("HADB_DATA")]
public sealed class HadbSqlDataRow : INormalizable
{
    [EasyManualKey]
    public string DATA_UID { get; set; } = "";
    public string DATA_SYSTEMNAME { get; set; } = "";
    public string DATA_TYPE { get; set; } = "";
    public string DATA_NAMESPACE { get; set; } = "";
    public long DATA_VER { get; set; } = 0;
    public bool DATA_DELETED { get; set; } = false;
    public bool DATA_ARCHIVE { get; set; } = false;
    public long DATA_SNAPSHOT_NO { get; set; } = 0;
    public DateTimeOffset DATA_CREATE_DT { get; set; } = Util.ZeroDateTimeOffsetValue;
    public DateTimeOffset DATA_UPDATE_DT { get; set; } = Util.ZeroDateTimeOffsetValue;
    public DateTimeOffset DATA_DELETE_DT { get; set; } = Util.ZeroDateTimeOffsetValue;
    public string DATA_KEY1 { get; set; } = "";
    public string DATA_KEY2 { get; set; } = "";
    public string DATA_KEY3 { get; set; } = "";
    public string DATA_KEY4 { get; set; } = "";
    public string DATA_LABEL1 { get; set; } = "";
    public string DATA_LABEL2 { get; set; } = "";
    public string DATA_LABEL3 { get; set; } = "";
    public string DATA_LABEL4 { get; set; } = "";
    public string DATA_VALUE { get; set; } = "";
    public string DATA_EXT1 { get; set; } = "";
    public string DATA_EXT2 { get; set; } = "";
    public long DATA_LAZY_COUNT1 { get; set; } = 0;
    public long DATA_LAZY_COUNT2 { get; set; } = 0;
    public string DATA_UID_ORIGINAL { get; set; } = "";

    public void Normalize()
    {
        this.DATA_UID = this.DATA_UID._NormalizeUid(true);
        this.DATA_SYSTEMNAME = this.DATA_SYSTEMNAME._NormalizeKey(true);
        this.DATA_TYPE = this.DATA_TYPE._NonNullTrim();
        this.DATA_NAMESPACE = this.DATA_NAMESPACE._NormalizeKey(true);
        this.DATA_CREATE_DT = this.DATA_CREATE_DT._NormalizeDateTimeOffset();
        this.DATA_UPDATE_DT = this.DATA_UPDATE_DT._NormalizeDateTimeOffset();
        this.DATA_DELETE_DT = this.DATA_DELETE_DT._NormalizeDateTimeOffset();
        this.DATA_KEY1 = this.DATA_KEY1._NormalizeKey(true);
        this.DATA_KEY2 = this.DATA_KEY2._NormalizeKey(true);
        this.DATA_KEY3 = this.DATA_KEY3._NormalizeKey(true);
        this.DATA_KEY4 = this.DATA_KEY4._NormalizeKey(true);
        this.DATA_LABEL1 = this.DATA_LABEL1._NormalizeKey(true);
        this.DATA_LABEL2 = this.DATA_LABEL2._NormalizeKey(true);
        this.DATA_LABEL3 = this.DATA_LABEL3._NormalizeKey(true);
        this.DATA_LABEL4 = this.DATA_LABEL4._NormalizeKey(true);
        this.DATA_VALUE = this.DATA_VALUE._NonNull();
        this.DATA_EXT1 = this.DATA_EXT1._NonNull();
        this.DATA_EXT2 = this.DATA_EXT2._NonNull();
        this.DATA_UID_ORIGINAL = this.DATA_UID_ORIGINAL._NormalizeUid(true);
    }
}

public abstract class HadbSqlBase<TMem, TDynamicConfig> : HadbBase<TMem, TDynamicConfig>
    where TMem : HadbMemDataBase, new()
    where TDynamicConfig : HadbDynamicConfig
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

    protected override bool IsDeadlockExceptionImpl(Exception ex)
        => Database.IsDeadlockException(ex);

    public async Task<Database> OpenSqlDatabaseAsync(bool writeMode, CancellationToken cancel = default)
    {
        Database db = new Database(
            writeMode ? this.Settings.SqlConnectStringForWrite : this.Settings.SqlConnectStringForRead,
            defaultIsolationLevel: writeMode ? this.Settings.IsolationLevelForWrite : this.Settings.IsolationLevelForRead);

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

    protected override async Task<KeyValueList<string, string>> LoadDynamicConfigFromDatabaseImplAsync(CancellationToken cancel = default)
    {
        IEnumerable<HadbSqlConfigRow> configList = null!;

        try
        {
            await using var dbReader = await this.OpenSqlDatabaseAsync(false, cancel);

            await dbReader.TranReadSnapshotIfNecessaryAsync(async () =>
            {
                configList = await dbReader.EasySelectAsync<HadbSqlConfigRow>("select * from HADB_CONFIG where CONFIG_SYSTEMNAME = @CONFIG_SYSTEMNAME", new { CONFIG_SYSTEMNAME = this.SystemName });
            });
        }
        catch (Exception ex)
        {
            Debug($"LoadDynamicConfigFromDatabaseImplAsync: Database Connect Error for Read: {ex.ToString()}");
            throw;
        }

        configList._NormalizeAll();

        KeyValueList<string, string> ret = new KeyValueList<string, string>();
        foreach (var configRow in configList)
        {
            ret.Add(configRow.CONFIG_NAME, configRow.CONFIG_VALUE);
        }

        return ret;
    }

    protected override async Task WriteStatImplAsync(HadbTran tran, DateTimeOffset dt, string generator, string value, string ext1, string ext2, CancellationToken cancel = default)
    {
        var dbWriter = ((HadbSqlTran)tran).Db;
        tran.CheckIsWriteMode();

        // 現在の Snapshot No を取得
        long snapNo = (await this.AtomicGetKvImplAsync(tran, "_HADB_SYS_CURRENT_SNAPSHOT_NO", cancel))._ToLong();

        // STAT を書き込み
        HadbSqlStatRow r = new HadbSqlStatRow
        {
            STAT_UID = Str.NewUid("STAT", '_'),
            STAT_SYSTEMNAME = this.SystemName,
            STAT_SNAPSHOT_NO = snapNo,
            STAT_DT = dt,
            STAT_GENERATOR = generator._NonNullTrim()._NormalizeFqdn().ToLower(),
            STAT_VALUE = value._NonNull(),
            STAT_EXT1 = ext1._NonNull(),
            STAT_EXT2 = ext2._NonNull(),
        };

        await dbWriter.EasyInsertAsync(r, cancel);

        // 最後に STAT を書き込んだ日時を書き込み
        await this.AtomicSetKvImplAsync(tran, "_HADB_SYS_LAST_STAT_WRITE_DT", dt._ToDtStr(withMSsecs: true, withNanoSecs: true));
    }

    protected override async Task AppendMissingDynamicConfigToDatabaseImplAsync(KeyValueList<string, string> missingValues, CancellationToken cancel = default)
    {
        if (missingValues.Any() == false) return;

        // DB にまだ存在しないが定義されるべき TDynamicConfig のフィールドがある場合は DB に初期値を書き込む
        await using var dbWriter = await this.OpenSqlDatabaseAsync(true, cancel);

        foreach (var missingValueName in missingValues.Select(x => x.Key._NonNullTrim()).Distinct(StrComparer.IgnoreCaseComparer))
        {
            await dbWriter.TranAsync(async () =>
            {
                // DB にまだ値がない場合のみ書き込む。
                // すでにある場合は書き込みしない。

                var tmp = await dbWriter.EasySelectAsync<HadbSqlConfigRow>("select * from HADB_CONFIG where CONFIG_SYSTEMNAME = @CONFIG_SYSTEMNAME and CONFIG_NAME = @CONFIG_NAME",
                new
                {
                    CONFIG_SYSTEMNAME = this.SystemName,
                    CONFIG_NAME = missingValueName,
                },
                cancel: cancel);

                if (tmp.Any())
                {
                    return false;
                }

                var valuesList = missingValues.Where(x => x.Key._IsSameiTrim(missingValueName)).Select(x => x.Value);

                foreach (var value in valuesList)
                {
                    await dbWriter.EasyInsertAsync(new HadbSqlConfigRow
                    {
                        CONFIG_SYSTEMNAME = this.SystemName,
                        CONFIG_NAME = missingValueName,
                        CONFIG_VALUE = value,
                        CONFIG_EXT = "",
                    }, cancel);
                }

                return true;
            });
        }
    }

    protected override async Task<List<HadbObject>> ReloadDataFromDatabaseImplAsync(bool fullReloadMode, DateTimeOffset partialReloadMinUpdateTime, Ref<DateTimeOffset>? lastStatTimestamp, CancellationToken cancel = default)
    {
        IEnumerable<HadbSqlDataRow> rows = null!;


        //Dbg.Where();
        try
        {
            await using var dbReader = await this.OpenSqlDatabaseAsync(false, cancel);

            await dbReader.TranReadSnapshotIfNecessaryAsync(async () =>
            {
                string sql = "select * from HADB_DATA where DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_ARCHIVE = 0";

                if (fullReloadMode == false)
                {
                    sql += " and DATA_UPDATE_DT >= @DT_MIN";
                }

                rows = await dbReader.EasySelectAsync<HadbSqlDataRow>("select * from HADB_DATA where DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_ARCHIVE = 0", new { DATA_SYSTEMNAME = this.SystemName, DT_MIN = partialReloadMinUpdateTime });

                HadbSqlKvRow? kv = await dbReader.EasySelectSingleAsync<HadbSqlKvRow>("select top 1 * from HADB_KV where KV_SYSTEM_NAME = @KV_SYSTEM_NAME and KV_KEY = @KV_KEY and KV_DELETED = 0 order by KV_ID desc",
                    new
                    {
                        KV_SYSTEM_NAME = this.SystemName,
                        KV_KEY = "_HADB_SYS_LAST_STAT_WRITE_DT",
                    });

                DateTimeOffset lastStat = Util.ZeroDateTimeOffsetValue;

                if (kv != null) lastStat = Str.DtstrToDateTimeOffset(kv.KV_VALUE);

                lastStatTimestamp?.Set(lastStat);
            });
        }
        catch (Exception ex)
        {
            Debug($"ReloadDataFromDatabaseImplAsync: Database Connect Error for Read: {ex.ToString()}");
            throw;
        }

        //Dbg.Where();

        var rowsList = rows.ToList();

        //rowsList.Count._Print();

        await rowsList._NormalizeAllParallelAsync(cancel: cancel);

        //Dbg.Where();

        List<HadbObject> ret = await rowsList._ProcessParallelAndAggregateAsync(srcList =>
        {
            List<HadbObject> tmp = new List<HadbObject>();
            foreach (var row in srcList)
            {
                Type? type = this.GetDataTypeByTypeName(row.DATA_TYPE);
                if (type != null)
                {
                    HadbData? data = (HadbData?)row.DATA_VALUE._JsonToObject(type);
                    if (data != null)
                    {
                        HadbObject obj = new HadbObject(data, row.DATA_EXT1, row.DATA_EXT2, row.DATA_UID, row.DATA_VER, false, row.DATA_SNAPSHOT_NO, row.DATA_NAMESPACE, row.DATA_DELETED, row.DATA_CREATE_DT, row.DATA_UPDATE_DT, row.DATA_DELETE_DT);

                        tmp.Add(obj);
                    }
                }
            }
            return TR(tmp);
        }, cancel: cancel);

        //Dbg.Where();

        //ret.Count._Print();

        return ret;
    }

    protected override async Task<HadbTran> BeginDatabaseTransactionImplAsync(bool writeMode, bool isTransaction, CancellationToken cancel = default)
    {
        Database db = await this.OpenSqlDatabaseAsync(writeMode, cancel);

        try
        {
            if (isTransaction)
            {
                await db.BeginAsync(cancel: cancel);
            }

            bool isDbSnapshotReadMode = (writeMode == false) && db.DefaultIsolationLevel == IsolationLevel.Snapshot;

            HadbSqlTran ret = new HadbSqlTran(writeMode, isTransaction, this, db, isDbSnapshotReadMode);

            return ret;
        }
        catch (Exception ex)
        {
            await db._DisposeSafeAsync(ex);
            throw;
        }
    }

    protected async Task<HadbSqlDataRow?> GetRowByKeyAsync(Database db, string typeName, string nameSpace, HadbKeys key, bool lightLock, bool and, CancellationToken cancel = default)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();

        List<string> conditions = new List<string>();

        if (key.Key1._IsFilled()) conditions.Add("DATA_ARCHIVE = 0 and DATA_DELETED = 0 and DATA_KEY1 != '' and DATA_KEY1 = @DATA_KEY1 and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_NAMESPACE = @DATA_NAMESPACE and DATA_TYPE = @DATA_TYPE");
        if (key.Key2._IsFilled()) conditions.Add("DATA_ARCHIVE = 0 and DATA_DELETED = 0 and DATA_KEY2 != '' and DATA_KEY2 = @DATA_KEY2 and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_NAMESPACE = @DATA_NAMESPACE and DATA_TYPE = @DATA_TYPE");
        if (key.Key3._IsFilled()) conditions.Add("DATA_ARCHIVE = 0 and DATA_DELETED = 0 and DATA_KEY3 != '' and DATA_KEY3 = @DATA_KEY3 and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_NAMESPACE = @DATA_NAMESPACE and DATA_TYPE = @DATA_TYPE");
        if (key.Key4._IsFilled()) conditions.Add("DATA_ARCHIVE = 0 and DATA_DELETED = 0 and DATA_KEY4 != '' and DATA_KEY4 = @DATA_KEY4 and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_NAMESPACE = @DATA_NAMESPACE and DATA_TYPE = @DATA_TYPE");

        if (conditions.Count == 0)
        {
            return null;
        }

        // READCOMMITTEDLOCK は、「トランザクション分離レベルが Snapshot かつ読み取り専用の場合」以外に付ける。
        string where = $"select * from HADB_DATA  { (lightLock ? "with (READCOMMITTEDLOCK)" : "") } where {conditions.Select(x => $" ( {x} )")._Combine(and ? " and " : " or ")}";

        return await db.EasySelectSingleAsync<HadbSqlDataRow>(where,
            new
            {
                DATA_KEY1 = key.Key1,
                DATA_KEY2 = key.Key2,
                DATA_KEY3 = key.Key3,
                DATA_KEY4 = key.Key4,
                DATA_SYSTEMNAME = this.SystemName,
                DATA_TYPE = typeName,
                DATA_NAMESPACE = nameSpace,
            },
            cancel: cancel,
            throwErrorIfMultipleFound: true);
    }

    protected async Task<IEnumerable<HadbSqlDataRow>> GetRowsByLabelsAsync(Database db, string typeName, string nameSpace, HadbLabels labels, bool lightLock, CancellationToken cancel = default)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        List<string> conditions = new List<string>();

        if (labels.Label1._IsFilled()) conditions.Add("DATA_LABEL1 != '' and DATA_DELETED = 0 and DATA_ARCHIVE = 0 and DATA_LABEL1 = @DATA_LABEL1");
        if (labels.Label2._IsFilled()) conditions.Add("DATA_LABEL2 != '' and DATA_DELETED = 0 and DATA_ARCHIVE = 0 and DATA_LABEL2 = @DATA_LABEL2");
        if (labels.Label3._IsFilled()) conditions.Add("DATA_LABEL3 != '' and DATA_DELETED = 0 and DATA_ARCHIVE = 0 and DATA_LABEL3 = @DATA_LABEL3");
        if (labels.Label4._IsFilled()) conditions.Add("DATA_LABEL4 != '' and DATA_DELETED = 0 and DATA_ARCHIVE = 0 and DATA_LABEL4 = @DATA_LABEL4");

        if (conditions.Count == 0)
        {
            return EmptyOf<HadbSqlDataRow>();
        }

        // READCOMMITTEDLOCK は、「トランザクション分離レベルが Snapshot かつ読み取り専用の場合」以外に付ける。
        return await db.EasySelectAsync<HadbSqlDataRow>($"select * from HADB_DATA { (lightLock ? "with (READCOMMITTEDLOCK)" : "") } where ({conditions._Combine(" and ")}) and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_TYPE = @DATA_TYPE and DATA_NAMESPACE = @DATA_NAMESPACE",
            new
            {
                DATA_LABEL1 = labels.Label1,
                DATA_LABEL2 = labels.Label2,
                DATA_LABEL3 = labels.Label3,
                DATA_LABEL4 = labels.Label4,
                DATA_SYSTEMNAME = this.SystemName,
                DATA_TYPE = typeName,
                DATA_NAMESPACE = nameSpace,
            },
            cancel: cancel);
    }

    protected async Task<HadbSqlDataRow?> GetRowByUidAsync(Database db, string typeName, string nameSpace, string uid, CancellationToken cancel = default)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();

        uid = uid._NormalizeUid(true);

        if (uid._IsEmpty()) return null;

        return await db.EasySelectSingleAsync<HadbSqlDataRow>("select * from HADB_DATA where DATA_UID = @DATA_UID and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_DELETED = 0 and DATA_ARCHIVE = 0 and DATA_TYPE = @DATA_TYPE and DATA_NAMESPACE = @DATA_NAMESPACE",
            new
            {
                DATA_UID = uid,
                DATA_SYSTEMNAME = this.SystemName,
                DATA_TYPE = typeName,
                DATA_NAMESPACE = nameSpace,
            },
            cancel: cancel,
            throwErrorIfMultipleFound: true);
    }

    protected internal override async Task<string> AtomicGetKvImplAsync(HadbTran tran, string key, CancellationToken cancel = default)
    {
        key = key._NormalizeKey(true);

        Database? dbReader = ((HadbSqlTran)tran).Db;

        var existingRow = await dbReader.EasySelectSingleAsync<HadbSqlKvRow>("select * from HADB_KV where KV_KEY = @KV_KEY and KV_SYSTEM_NAME = @KV_SYSTEM_NAME and KV_DELETED = 0",
            new
            {
                KV_KEY = key,
                KV_SYSTEM_NAME = this.SystemName,
            },
            cancel: cancel,
            throwErrorIfMultipleFound: true);

        if (existingRow != null)
        {
            return existingRow.KV_VALUE._NonNull();
        }

        return "";
    }

    protected internal override async Task<StrDictionary<string>> AtomicGetKvListImplAsync(HadbTran tran, CancellationToken cancel = default)
    {
        Database? dbReader = ((HadbSqlTran)tran).Db;

        var rows = await dbReader.EasySelectAsync<HadbSqlKvRow>("select * from HADB_KV where KV_SYSTEM_NAME = @KV_SYSTEM_NAME and KV_DELETED = 0",
            new
            {
                KV_SYSTEM_NAME = this.SystemName,
            },
            cancel: cancel);

        StrDictionary<string> ret = new StrDictionary<string>(StrCmpi);

        foreach (var row in rows)
        {
            ret.Add(row.KV_KEY, row.KV_VALUE);
        }

        return ret;
    }

    protected internal override async Task AtomicSetKvImplAsync(HadbTran tran, string key, string value, CancellationToken cancel = default)
    {
        key = key._NormalizeKey(true);
        value = value._NonNull();

        Database? dbWriter = ((HadbSqlTran)tran).Db;
        tran.CheckIsWriteMode();

        var now = DtOffsetNow;

        var row = await dbWriter.EasyFindOrInsertAsync<HadbSqlKvRow>("select * from HADB_KV where KV_KEY = @KV_KEY and KV_SYSTEM_NAME = @KV_SYSTEM_NAME and KV_DELETED = 0",
            new
            {
                KV_KEY = key,
                KV_SYSTEM_NAME = this.SystemName,
            },
            new HadbSqlKvRow
            {
                KV_SYSTEM_NAME = this.SystemName,
                KV_KEY = key,
                KV_VALUE = value,
                KV_DELETED = false,
                KV_CREATE_DT = now,
                KV_UPDATE_DT = now,
            }
        );

        if (row.KV_VALUE != value)
        {
            row.KV_UPDATE_DT = now;
            row.KV_VALUE = value;

            await dbWriter.EasyUpdateAsync(row, true, cancel);
        }
    }

    protected internal override async Task AtomicAddSnapImplAsync(HadbTran tran, HadbSnapshot snap, CancellationToken cancel = default)
    {
        tran.CheckIsWriteMode();

        var dbWriter = ((HadbSqlTran)tran).Db;

        HadbSqlSnapshotRow r = new HadbSqlSnapshotRow
        {
            SNAPSHOT_UID = snap.Uid,
            SNAPSHOT_SYSTEM_NAME = snap.SystemName,
            SNAPSHOT_NO = snap.Number,
            SNAPSHOT_DT = snap.TimeStamp,
            SNAPSHOT_DESCRIPTION = snap.Description,
            SNAPSHOT_EXT1 = snap.Ext1,
            SNAPSHOT_EXT2 = snap.Ext2,
        };

        await dbWriter.EasyInsertAsync(r, cancel);
    }

    protected internal override async Task AtomicAddDataListToDatabaseImplAsync(HadbTran tran, IEnumerable<HadbObject> dataList, CancellationToken cancel = default)
    {
        tran.CheckIsWriteMode();
        var dbWriter = ((HadbSqlTran)tran).Db;

        foreach (HadbObject data in dataList)
        {
            if (data.Deleted) throw new CoresLibException("data.Deleted == true");

            var keys = data.GetKeys();

            var labels = data.GetLabels();

            HadbSqlDataRow row = new HadbSqlDataRow
            {
                DATA_UID = data.Uid,
                DATA_SYSTEMNAME = this.SystemName,
                DATA_TYPE = data.GetUserDataTypeName(),
                DATA_VER = data.Ver,
                DATA_DELETED = false,
                DATA_ARCHIVE = false,
                DATA_SNAPSHOT_NO = data.SnapshotNo,
                DATA_NAMESPACE = data.NameSpace,
                DATA_CREATE_DT = data.CreateDt,
                DATA_UPDATE_DT = data.UpdateDt,
                DATA_DELETE_DT = data.DeleteDt,
                DATA_KEY1 = keys.Key1,
                DATA_KEY2 = keys.Key2,
                DATA_KEY3 = keys.Key3,
                DATA_KEY4 = keys.Key4,
                DATA_LABEL1 = labels.Label1,
                DATA_LABEL2 = labels.Label2,
                DATA_LABEL3 = labels.Label3,
                DATA_LABEL4 = labels.Label4,
                DATA_VALUE = data.GetUserDataJsonString(),
                DATA_EXT1 = data.Ext1,
                DATA_EXT2 = data.Ext2,
                DATA_LAZY_COUNT1 = 0,
                DATA_LAZY_COUNT2 = 0,
                DATA_UID_ORIGINAL = data.Uid,
            };

            //// DB に書き込む前に DB 上で KEY1 ～ KEY4 の重複を検査する
            //var existingRow = await GetRowByKeyAsync(dbWriter, row.DATA_TYPE, row.DATA_NAMESPACE, keys, cancel);

            //if (existingRow != null)
            //{
            //    throw new CoresLibException($"Duplicated key in the physical database. Namespace = {data.NameSpace}, Keys = {keys._ObjectToJson(compact: true)}");
            //}

            // DB に書き込む
            await dbWriter.EasyInsertAsync(row, cancel);
        }
    }

    protected internal override async Task AtomicAddLogImplAsync(HadbTran tran, HadbLog log, string nameSpace, string ext1, string ext2, CancellationToken cancel = default)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        tran.CheckIsWriteMode();
        var dbWriter = ((HadbSqlTran)tran).Db;

        string typeName = log.GetLogDataTypeName();
        string uid = Str.NewUid("LOG_" + typeName, '_');
        uid = uid._NormalizeUid(true);

        var label = log.GetLabels();

        HadbSqlLogRow row = new HadbSqlLogRow
        {
            LOG_UID = uid,
            LOG_SYSTEM_NAME = this.SystemName,
            LOG_TYPE = typeName,
            LOG_NAMESPACE = nameSpace,
            LOG_DT = DtOffsetNow,
            LOG_SNAP_NO = tran.CurrentSnapNoForWriteMode,
            LOG_DELETED = false,
            LOG_LABEL1 = label.Label1,
            LOG_LABEL2 = label.Label2,
            LOG_LABEL3 = label.Label3,
            LOG_LABEL4 = label.Label4,
            LOG_VALUE = log.GetLogDataJsonString(),
            LOG_EXT1 = ext1._NonNull(),
            LOG_EXT2 = ext2._NonNull(),
        };

        row.Normalize();

        await dbWriter.EasyInsertAsync(row, cancel);
    }

    protected internal override async Task<IEnumerable<HadbLog>> AtomicSearchLogImplAsync(HadbTran tran, string typeName, HadbLogQuery query, string nameSpace, CancellationToken cancel = default)
    {
        typeName = typeName._NonNullTrim();
        query.Normalize();
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        var db = ((HadbSqlTran)tran).Db;

        List<string> conditions = new List<string>();

        if (query.Uid._IsFilled()) conditions.Add("LOG_UID = @LOG_UID");
        if (query.TimeStart._IsZeroDateTime() == false) conditions.Add("LOG_DT >= @DT_START");
        if (query.TimeEnd._IsZeroDateTime() == false) conditions.Add("LOG_DT <= @DT_END");
        if (query.SnapshotNoStart > 0) conditions.Add("LOG_SNAP_NO >= @SNAP_START");
        if (query.SnapshotNoEnd > 0) conditions.Add("LOG_SNAP_NO <= @SNAP_END");

        var labels = query.Labels;
        if (labels.Label1._IsFilled()) conditions.Add("LOG_LABEL1 = @LOG_LABEL1");
        if (labels.Label2._IsFilled()) conditions.Add("LOG_LABEL2 = @LOG_LABEL2");
        if (labels.Label3._IsFilled()) conditions.Add("LOG_LABEL3 = @LOG_LABEL3");
        if (labels.Label4._IsFilled()) conditions.Add("LOG_LABEL4 = @LOG_LABEL4");

        var labels2 = query.SearchTemplate?.GetLabels() ?? new HadbLabels("");
        if (labels2.Label1._IsFilled()) conditions.Add("LOG_LABEL1 = @LOG_LABEL1_02");
        if (labels2.Label2._IsFilled()) conditions.Add("LOG_LABEL2 = @LOG_LABEL2_02");
        if (labels2.Label3._IsFilled()) conditions.Add("LOG_LABEL3 = @LOG_LABEL3_02");
        if (labels2.Label4._IsFilled()) conditions.Add("LOG_LABEL4 = @LOG_LABEL4_02");

        if (conditions.Count == 0) conditions.Add("1 = 1");

        string qstr = $"select {(query.MaxReturmItems >= 1 ? $"top {query.MaxReturmItems}" : "")} * from HADB_LOG where {conditions._Combine(" and ")} and LOG_SYSTEM_NAME = @LOG_SYSTEM_NAME and LOG_TYPE = @LOG_TYPE and LOG_NAMESPACE = @LOG_NAMESPACE and LOG_DELETED = 0 order by LOG_ID desc";

        var rows = await db.EasySelectAsync<HadbSqlLogRow>(qstr,
            new
            {
                LOG_UID = query.Uid,
                DT_START = query.TimeStart,
                DT_END = query.TimeEnd,
                SNAP_START = query.SnapshotNoStart,
                SNAP_END = query.SnapshotNoEnd,

                LOG_LABEL1 = labels.Label1._NonNull(),
                LOG_LABEL2 = labels.Label2._NonNull(),
                LOG_LABEL3 = labels.Label3._NonNull(),
                LOG_LABEL4 = labels.Label4._NonNull(),

                LOG_LABEL1_02 = labels2.Label1._NonNull(),
                LOG_LABEL2_02 = labels2.Label2._NonNull(),
                LOG_LABEL3_02 = labels2.Label3._NonNull(),
                LOG_LABEL4_02 = labels2.Label4._NonNull(),

                LOG_SYSTEM_NAME = this.SystemName,
                LOG_TYPE = typeName,
                LOG_NAMESPACE = nameSpace,
            },
            cancel: cancel);

        List<HadbLog> ret = new List<HadbLog>();

        foreach (var row in rows)
        {
            ret.Add(this.JsonToHadbLog(row.LOG_VALUE, typeName));
        }

        return ret;
    }

    protected internal override async Task<bool> LazyUpdateImplAsync(HadbTran tran, HadbObject data, CancellationToken cancel = default)
    {
        data.CheckIsNotMemoryDbObject();
        tran.CheckIsWriteMode();
        var dbWriter = ((HadbSqlTran)tran).Db;

        string typeName = data.GetUserDataTypeName();

        var keys = data.GetKeys();
        var labels = data.GetLabels();
        if (data.Deleted) throw new CoresLibException("data.Deleted == true");

        string query = "update HADB_DATA set DATA_VALUE = @DATA_VALUE, DATA_UPDATE_DT = @DATA_UPDATE_DT, DATA_LAZY_COUNT1 = DATA_LAZY_COUNT1 + 1, DATA_LAZY_COUNT2 = DATA_LAZY_COUNT2 + 1 " +
            "where DATA_UID = @DATA_UID and DATA_VER = @DATA_VER and DATA_UPDATE_DT < @DATA_UPDATE_DT and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_TYPE = @DATA_TYPE and DATA_ARCHIVE = 0 and DATA_DELETED = 0 and " +
            "DATA_KEY1 = @DATA_KEY1 and DATA_KEY2 = @DATA_KEY2 and DATA_KEY3 = @DATA_KEY3 and DATA_KEY4 = @DATA_KEY4 and " +
            "DATA_LABEL1 = @DATA_LABEL1 and DATA_LABEL2 = @DATA_LABEL2 and DATA_LABEL3 = @DATA_LABEL3 and DATA_LABEL4 = DATA_LABEL4";

        int ret = await dbWriter.EasyExecuteAsync(query,
            new
            {
                DATA_VALUE = data.GetUserDataJsonString(),
                DATA_UPDATE_DT = DtOffsetNow,
                DATA_UID = data.Uid,
                DATA_VER = data.Ver,
                DATA_SYSTEMNAME = this.SystemName,
                DATA_TYPE = typeName,
                DATA_KEY1 = keys.Key1,
                DATA_KEY2 = keys.Key2,
                DATA_KEY3 = keys.Key3,
                DATA_KEY4 = keys.Key4,
                DATA_LABEL1 = labels.Label1,
                DATA_LABEL2 = labels.Label2,
                DATA_LABEL3 = labels.Label3,
                DATA_LABEL4 = labels.Label4,
            });

        return ret >= 1;
    }

    protected internal override async Task<HadbObject> AtomicUpdateDataOnDatabaseImplAsync(HadbTran tran, HadbObject data, CancellationToken cancel = default)
    {
        int maxArchive = Math.Max(data.GetData<HadbData>().GetMaxArchivedCount(), 0);

        data.CheckIsNotMemoryDbObject();
        tran.CheckIsWriteMode();
        var dbWriter = ((HadbSqlTran)tran).Db;

        string typeName = data.GetUserDataTypeName();

        var keys = data.GetKeys();

        var labels = data.GetLabels();

        if (data.Deleted) throw new CoresLibException("data.Deleted == true");

        // 現在のデータを取得
        var row = await this.GetRowByUidAsync(dbWriter, typeName, data.NameSpace, data.Uid, cancel);
        if (row == null)
        {
            // 現在のデータがない
            throw new CoresLibException($"No data existing in the physical database. Uid = {data.Uid}, TypeName = {typeName}");
        }

        long newVer = row.DATA_VER + 1;

        if (true)
        {
            //// 現在のアーカイブを一段繰り上げる
            //await dbWriter.EasyExecuteAsync("update HADB_DATA set DATA_ARCHIVE = DATA_ARCHIVE + 1 where DATA_UID like @DATA_UID and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_TYPE = @DATA_TYPE and DATA_ARCHIVE >= 1 and DATA_NAMESPACE = @DATA_NAMESPACE",
            //    new
            //    {
            //        DATA_UID = data.Uid + ":%",
            //        DATA_SYSTEMNAME = this.SystemName,
            //        DATA_TYPE = typeName,
            //        DATA_NAMESPACE = data.NameSpace,
            //    });

            if (maxArchive >= 1)
            {
                // 現在のデータをアーカイブ化する
                HadbSqlDataRow rowOld = row._CloneDeep();

                rowOld.DATA_UID_ORIGINAL = rowOld.DATA_UID;
                rowOld.DATA_UID += ":" + rowOld.DATA_VER.ToString("D20");
                rowOld.DATA_ARCHIVE = true;
                rowOld.Normalize();

                await dbWriter.EasyInsertAsync(rowOld, cancel);

                // 古いアーカイブを物理的に消す
                if (maxArchive != int.MaxValue)
                {
                    long threshold = newVer - maxArchive;

                    if (threshold >= 1)
                    {
                        await dbWriter.EasyExecuteAsync("delete from HADB_DATA with (READCOMMITTEDLOCK) where DATA_ARCHIVE = 1 and DATA_UID_ORIGINAL = @DATA_UID and DATA_VER < @DATA_VER_THRESHOLD and DATA_SNAPSHOT_NO = @DATA_SNAPSHOT_NO",
                            new
                            {
                                DATA_UID = data.Uid,
                                DATA_SYSTEMNAME = this.SystemName,
                                DATA_TYPE = typeName,
                                DATA_NAMESPACE = data.NameSpace,
                                DATA_VER_THRESHOLD = threshold,
                                DATA_SNAPSHOT_NO = tran.CurrentSnapNoForWriteMode,
                            }
                        );
                    }
                }
            }
        }

        // DB に書き込む前に DB 上で KEY1 ～ KEY4 の重複を検査する (当然、更新しようとしている自分自身への重複は例外的に許可する)
        //var existingRow = await GetRowByKeyAsync(dbWriter, typeName, data.NameSpace, keys, cancel, excludeUid: row.DATA_UID);

        //if (existingRow != null)
        //{
        //    throw new CoresLibException($"Duplicated key in the physical database. Namespace = {data.NameSpace}, Keys = {keys._ObjectToJson(compact: true)}");
        //}

        // データの内容を更新する
        row.DATA_VER = newVer;
        row.DATA_UPDATE_DT = DtOffsetNow;
        row.DATA_KEY1 = keys.Key1;
        row.DATA_KEY2 = keys.Key2;
        row.DATA_KEY3 = keys.Key3;
        row.DATA_KEY4 = keys.Key4;
        row.DATA_LABEL1 = labels.Label1;
        row.DATA_LABEL2 = labels.Label2;
        row.DATA_LABEL3 = labels.Label3;
        row.DATA_LABEL4 = labels.Label4;
        row.DATA_VALUE = data.GetUserDataJsonString();
        row.DATA_EXT1 = data.Ext1;
        row.DATA_EXT2 = data.Ext2;
        row.DATA_LAZY_COUNT1 = 0;
        row.DATA_SNAPSHOT_NO = tran.CurrentSnapNoForWriteMode;
        row.DATA_NAMESPACE = data.NameSpace;

        await dbWriter.EasyUpdateAsync(row, true, cancel);

        return new HadbObject(this.JsonToHadbData(row.DATA_VALUE, typeName), row.DATA_EXT1, row.DATA_EXT2, row.DATA_UID, row.DATA_VER, row.DATA_ARCHIVE, row.DATA_SNAPSHOT_NO, row.DATA_NAMESPACE, row.DATA_DELETED, row.DATA_CREATE_DT, row.DATA_UPDATE_DT, row.DATA_DELETE_DT);
    }

    protected internal override async Task<HadbObject?> AtomicGetDataFromDatabaseImplAsync(HadbTran tran, string uid, string typeName, string nameSpace, CancellationToken cancel = default)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        typeName = typeName._NonNullTrim();

        var dbReader = ((HadbSqlTran)tran).Db;

        HadbSqlDataRow? row = await GetRowByUidAsync(dbReader, typeName, nameSpace, uid, cancel);

        if (row == null) return null;

        HadbObject ret = new HadbObject(this.JsonToHadbData(row.DATA_VALUE, typeName), row.DATA_EXT1, row.DATA_EXT2, row.DATA_UID, row.DATA_VER, row.DATA_ARCHIVE, row.DATA_SNAPSHOT_NO, row.DATA_NAMESPACE, row.DATA_DELETED, row.DATA_CREATE_DT, row.DATA_UPDATE_DT, row.DATA_DELETE_DT);

        return ret;
    }

    protected internal override async Task<IEnumerable<HadbObject>> AtomicGetArchivedDataFromDatabaseImplAsync(HadbTran tran, int maxItems, string uid, string typeName, string nameSpace, CancellationToken cancel = default)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        typeName = typeName._NonNullTrim();

        if (maxItems <= 0) return EmptyOf<HadbObject>();

        var db = ((HadbSqlTran)tran).Db;

        uid = uid._NormalizeUid(true);

        if (uid._IsEmpty()) return EmptyOf<HadbObject>();

        var rows = await db.EasySelectAsync<HadbSqlDataRow>($"select top {maxItems} * from HADB_DATA where (DATA_UID = @DATA_UID or DATA_UID_ORIGINAL = @DATA_UID) and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_TYPE = @DATA_TYPE and DATA_NAMESPACE = @DATA_NAMESPACE order by DATA_VER desc",
            new
            {
                DATA_UID = uid,
                DATA_SYSTEMNAME = this.SystemName,
                DATA_TYPE = typeName,
                DATA_NAMESPACE = nameSpace,
            },
            cancel: cancel);

        rows = rows.OrderByDescending(x => x.DATA_VER); // 念のため

        List<HadbObject> ret = new List<HadbObject>();

        foreach (var row in rows)
        {
            var item = new HadbObject(this.JsonToHadbData(row.DATA_VALUE, typeName), row.DATA_EXT1, row.DATA_EXT2, row.DATA_UID, row.DATA_VER, row.DATA_ARCHIVE, row.DATA_SNAPSHOT_NO, row.DATA_NAMESPACE, row.DATA_DELETED, row.DATA_CREATE_DT, row.DATA_UPDATE_DT, row.DATA_DELETE_DT);

            ret.Add(item);
        }

        return ret;
    }

    protected internal override async Task<HadbObject?> AtomicSearchDataByKeyFromDatabaseImplAsync(HadbTran tran, HadbKeys key, string typeName, string nameSpace, bool and, CancellationToken cancel = default)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        typeName = typeName._NonNullTrim();

        var dbReader = ((HadbSqlTran)tran).Db;

        var row = await GetRowByKeyAsync(dbReader, typeName, nameSpace, key, !tran.IsDbSnapshotReadMode, and, cancel);

        if (row == null) return null;

        HadbObject ret = new HadbObject(this.JsonToHadbData(row.DATA_VALUE, typeName), row.DATA_EXT1, row.DATA_EXT2, row.DATA_UID, row.DATA_VER, row.DATA_ARCHIVE, row.DATA_SNAPSHOT_NO, row.DATA_NAMESPACE, row.DATA_DELETED, row.DATA_CREATE_DT, row.DATA_UPDATE_DT, row.DATA_DELETE_DT);

        return ret;
    }

    protected internal override async Task<IEnumerable<HadbObject>> AtomicSearchDataListByLabelsFromDatabaseImplAsync(HadbTran tran, HadbLabels labels, string typeName, string nameSpace, CancellationToken cancel = default)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();

        typeName = typeName._NonNullTrim();

        var dbReader = ((HadbSqlTran)tran).Db;

        IEnumerable<HadbSqlDataRow> rows = await GetRowsByLabelsAsync(dbReader, typeName, nameSpace, labels, !tran.IsDbSnapshotReadMode, cancel);

        if (rows == null) return EmptyOf<HadbObject>();

        List<HadbObject> ret = new List<HadbObject>();

        foreach (var row in rows)
        {
            var item = new HadbObject(this.JsonToHadbData(row.DATA_VALUE, typeName), row.DATA_EXT1, row.DATA_EXT2, row.DATA_UID, row.DATA_VER, row.DATA_ARCHIVE, row.DATA_SNAPSHOT_NO, row.DATA_NAMESPACE, row.DATA_DELETED, row.DATA_CREATE_DT, row.DATA_UPDATE_DT, row.DATA_DELETE_DT);

            ret.Add(item);
        }

        return ret;
    }

    protected internal override async Task<HadbObject> AtomicDeleteDataFromDatabaseImplAsync(HadbTran tran, string uid, string typeName, string nameSpace, int maxArchive, CancellationToken cancel = default)
    {
        maxArchive = Math.Max(maxArchive, 0);
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        typeName = typeName._NonNullTrim();
        uid = uid._NormalizeUid(true);

        tran.CheckIsWriteMode();
        var dbWriter = ((HadbSqlTran)tran).Db;

        HadbSqlDataRow? row = await GetRowByUidAsync(dbWriter, typeName, nameSpace, uid, cancel);

        if (row == null)
        {
            // 現在のデータがない
            throw new CoresLibException($"No data existing in the physical database. Uid = {uid}, TypeName = {typeName}");
        }

        long newVer = row.DATA_VER + 1;

        if (true)
        {
            //// 現在のアーカイブを一段繰り上げる
            //await dbWriter.EasyExecuteAsync("update HADB_DATA set DATA_ARCHIVE = DATA_ARCHIVE + 1 where DATA_UID like @DATA_UID and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_TYPE = @DATA_TYPE and DATA_ARCHIVE >= 1 and DATA_NAMESPACE = @DATA_NAMESPACE",
            //    new
            //    {
            //        DATA_UID = data.Uid + ":%",
            //        DATA_SYSTEMNAME = this.SystemName,
            //        DATA_TYPE = typeName,
            //        DATA_NAMESPACE = data.NameSpace,
            //    });

            if (maxArchive >= 1)
            {
                // 現在のデータをアーカイブ化する
                HadbSqlDataRow rowOld = row._CloneDeep();

                rowOld.DATA_UID_ORIGINAL = rowOld.DATA_UID;
                rowOld.DATA_UID += ":" + rowOld.DATA_VER.ToString("D20");
                rowOld.DATA_ARCHIVE = true;
                rowOld.Normalize();

                await dbWriter.EasyInsertAsync(rowOld, cancel);

                // 古いアーカイブを物理的に消す
                if (maxArchive != int.MaxValue)
                {
                    long threshold = newVer - maxArchive;

                    if (threshold >= 1)
                    {
                        await dbWriter.EasyExecuteAsync("delete from HADB_DATA with (READCOMMITTEDLOCK) where DATA_ARCHIVE = 1 and DATA_UID_ORIGINAL = @DATA_UID and DATA_VER < @DATA_VER_THRESHOLD and DATA_SNAPSHOT_NO = @DATA_SNAPSHOT_NO",
                            new
                            {
                                DATA_UID = uid,
                                DATA_SYSTEMNAME = this.SystemName,
                                DATA_TYPE = typeName,
                                DATA_NAMESPACE = nameSpace,
                                DATA_VER_THRESHOLD = threshold,
                                DATA_SNAPSHOT_NO = tran.CurrentSnapNoForWriteMode,
                            }
                        );
                    }
                }
            }
        }

        // データを削除済みにする
        row.DATA_VER = newVer;
        row.DATA_UPDATE_DT = row.DATA_DELETE_DT = DtOffsetNow;
        row.DATA_DELETED = true;
        row.DATA_LAZY_COUNT1 = 0;
        row.DATA_SNAPSHOT_NO = tran.CurrentSnapNoForWriteMode;

        await dbWriter.EasyUpdateAsync(row, true, cancel);

        return new HadbObject(this.JsonToHadbData(row.DATA_VALUE, typeName), row.DATA_EXT1, row.DATA_EXT2, row.DATA_UID, row.DATA_VER, row.DATA_ARCHIVE, row.DATA_SNAPSHOT_NO, row.DATA_NAMESPACE, row.DATA_DELETED, row.DATA_CREATE_DT, row.DATA_UPDATE_DT, row.DATA_DELETE_DT);
    }





    public class HadbSqlTran : HadbTran
    {
        public Database Db { get; }

        public HadbSqlTran(bool writeMode, bool isTransaction, HadbSqlBase<TMem, TDynamicConfig> hadbSql, Database db, bool isDbSnapshotReadMode) : base(writeMode, isTransaction, isDbSnapshotReadMode, hadbSql)
        {
            try
            {
                this.Db = db;
            }
            catch (Exception ex)
            {
                this._DisposeSafe(ex);
                throw;
            }
        }

        protected override async Task CommitImplAsync(CancellationToken cancel)
        {
            await this.Db.CommitAsync(cancel);
        }

        protected override async Task CleanupImplAsync(Exception? ex)
        {
            try
            {
                await this.Db._DisposeSafeAsync();
            }
            finally
            {
                await base.CleanupImplAsync(ex);
            }
        }
    }
}

[Flags]
public enum HadbOptionFlags : long
{
    None = 0,
    NoAutoDbReloadAndUpdate = 1,
    NoInitConfigDb = 2,
    NoInitSnapshot = 4,
    DoNotTakeSnapshotAtAll = 8,
    NoMemDb = 16,
}

[Flags]
public enum HadbDebugFlags : long
{
    None = 0,
    NoCheckMemKeyDuplicate = 1,
    CauseErrorOnDatabaseReload = 2,
}

public abstract class HadbSettingsBase
{
    public string SystemName { get; }
    public HadbOptionFlags OptionFlags { get; }
    public DirectoryPath BackupDir { get; }

    public HadbSettingsBase(string systemName, HadbOptionFlags optionFlags = HadbOptionFlags.None, DirectoryPath? backupDir = null)
    {
        if (systemName._IsEmpty()) throw new CoresLibException("systemName is empty.");
        this.SystemName = systemName._NonNullTrim().ToUpper();
        this.OptionFlags = optionFlags;

        if (backupDir == null)
        {
            backupDir = new DirectoryPath(Env.AppLocalDir._CombinePath("HadbBackup", this.SystemName._MakeVerySafeAsciiOnlyNonSpaceFileName()));
        }

        this.BackupDir = backupDir;
    }
}

public struct HadbKeys : IEquatable<HadbKeys>
{
    public string Key1 { get; }
    public string Key2 { get; }
    public string Key3 { get; }
    public string Key4 { get; }

    public HadbKeys(string key1, string? key2 = null, string? key3 = null, string? key4 = null)
    {
        this.Key1 = key1._NormalizeKey(true);
        this.Key2 = key2._NormalizeKey(true);
        this.Key3 = key3._NormalizeKey(true);
        this.Key4 = key4._NormalizeKey(true);
    }

    public bool Equals(HadbKeys other)
    {
        if (this.Key1._IsSamei(other.Key1) == false) return false;
        if (this.Key2._IsSamei(other.Key2) == false) return false;
        if (this.Key3._IsSamei(other.Key3) == false) return false;
        if (this.Key4._IsSamei(other.Key4) == false) return false;
        return true;
    }
}

public struct HadbLabels : IEquatable<HadbLabels>
{
    public string Label1 { get; }
    public string Label2 { get; }
    public string Label3 { get; }
    public string Label4 { get; }

    public HadbLabels(string label1, string? label2 = null, string? label3 = null, string? label4 = null)
    {
        this.Label1 = label1._NormalizeKey(true);
        this.Label2 = label2._NormalizeKey(true);
        this.Label3 = label3._NormalizeKey(true);
        this.Label4 = label4._NormalizeKey(true);
    }

    public bool Equals(HadbLabels other)
    {
        if (this.Label1._IsSamei(other.Label1) == false) return false;
        if (this.Label2._IsSamei(other.Label2) == false) return false;
        if (this.Label3._IsSamei(other.Label3) == false) return false;
        if (this.Label4._IsSamei(other.Label4) == false) return false;
        return true;
    }
}

public abstract class HadbLog : INormalizable
{
    public virtual HadbLabels GetLabels() => new HadbLabels("");

    public abstract void Normalize();

    public Type GetLogDataType() => this.GetType();
    public string GetLogDataTypeName() => this.GetType().Name;
    public string GetLogDataJsonString()
    {
        try
        {
            this.Normalize();
        }
        catch (Exception ex)
        {
            ex._Debug();
        }
        return this._ObjectToJson(compact: true);
    }

    public T GetData<T>() where T : HadbLog
        => (T)this;
}

public abstract class HadbData : INormalizable
{
    public virtual HadbKeys GetKeys() => new HadbKeys("");
    public virtual HadbLabels GetLabels() => new HadbLabels("");
    public virtual int GetMaxArchivedCount() => int.MaxValue;

    public abstract void Normalize();

    public HadbObject ToNewObject(long snapshotNo, string nameSpace, string ext1 = "", string ext2 = "") => new HadbObject(this, snapshotNo, nameSpace, ext1, ext2);

    //public static implicit operator HadbObject(HadbData data) => data.ToNewObject();

    Type _Type => this.GetType();
    string _TypeName => this._Type.Name;

    public Type GetUserDataType() => this._Type;
    public string GetUserDataTypeName() => this._TypeName;

    public string GetUserDataJsonString()
    {
        try
        {
            this.Normalize();
        }
        catch (Exception ex)
        {
            ex._Debug();
        }
        return this._ObjectToJson(compact: true);
    }

    public T GetData<T>() where T : HadbData
        => (T)this;
}

public sealed class HadbSnapshot
{
    public string Uid { get; }
    public string SystemName { get; }
    public long Number { get; }
    public DateTimeOffset TimeStamp { get; }
    public string Description { get; }
    public string Ext1 { get; }
    public string Ext2 { get; }

    public HadbSnapshot(string uid, string systemName, long number, DateTimeOffset timeStamp, string description, string ext1, string ext2)
    {
        Uid = uid._NormalizeUid(true);
        SystemName = systemName._NormalizeKey(true);
        Number = number;
        TimeStamp = timeStamp._NormalizeDateTimeOffset();
        Description = description._NonNull();
        Ext1 = ext1._NonNull();
        Ext2 = ext2._NonNull();
    }
}

public sealed class HadbObject<T> : INormalizable // 単なるラッパー
    where T : HadbData
{
    public HadbObject TargetObject { get; }

    public CriticalSection<HadbObject> Lock => TargetObject.Lock;
    public HadbMemDataBase? MemDb => TargetObject.MemDb;
    public bool IsMemoryDbObject => TargetObject.IsMemoryDbObject;
    public string Uid => TargetObject.Uid;
    public long Ver => TargetObject.Ver;
    public bool Deleted => TargetObject.Deleted;
    public DateTimeOffset CreateDt => TargetObject.CreateDt;
    public DateTimeOffset UpdateDt => TargetObject.UpdateDt;
    public DateTimeOffset DeleteDt => TargetObject.DeleteDt;
    public bool Archive => TargetObject.Archive;
    public long SnapshotNo => TargetObject.SnapshotNo;
    public string NameSpace => TargetObject.NameSpace;
    public T UserData => (T)TargetObject.UserData;
    public string Ext1 { get => TargetObject.Ext1; set => TargetObject.Ext1 = value; }
    public string Ext2 { get => TargetObject.Ext2; set => TargetObject.Ext2 = value; }
    public long InternalFastUpdateVersion => TargetObject.InternalFastUpdateVersion;
    public string GetUserDataJsonString() => TargetObject.GetUserDataJsonString();
    public void CheckIsMemoryDbObject() => TargetObject.CheckIsMemoryDbObject();
    public void CheckIsNotMemoryDbObject() => TargetObject.CheckIsNotMemoryDbObject();
    public bool FastUpdate(Func<T, bool> updateFunc) => TargetObject.FastUpdate(updateFunc);
    public Type GetUserDataType() => TargetObject.GetUserDataType();
    public string GetUserDataTypeName() => TargetObject.GetUserDataTypeName();
    public string GetUidPrefix() => TargetObject.GetUidPrefix();
    public HadbKeys GetKeys() => TargetObject.GetKeys();
    public HadbLabels GetLabels() => TargetObject.GetLabels();
    public T GetData() => TargetObject.GetData<T>();
    public T Data => GetData();

    public HadbObject<T> CloneObject() => new HadbObject<T>(TargetObject.CloneObject());

    public HadbObject(HadbObject targetObject)
    {
        this.TargetObject = targetObject;
    }

    public void Normalize() => TargetObject.Normalize();

    [return: NotNullIfNotNull("src")]
    public static implicit operator HadbObject<T>?(HadbObject? src)
    {
        if (src == null) return null;
        return new HadbObject<T>(src);
    }

    [return: NotNullIfNotNull("src")]
    public static implicit operator HadbObject?(HadbObject<T>? src)
    {
        if (src == null) return null;
        return src.TargetObject;
    }

    [return: NotNullIfNotNull("src")]
    public static implicit operator T?(HadbObject<T>? src)
    {
        if (src == null) return null;
        return src.Data;
    }
}

public sealed class HadbSerializedObject
{
    public string Uid { get; set; } = "";

    public long Ver { get; set; }

    public DateTimeOffset CreateDt { get; set; }

    public DateTimeOffset UpdateDt { get; set; }

    public DateTimeOffset DeleteDt { get; set; }

    public long SnapshotNo { get; set; }

    public string NameSpace { get; set; } = "";

    public string Ext1 { get; set; } = "";

    public string Ext2 { get; set; } = "";

    public object UserData { get; set; } = null!;

    public string UserDataTypeName { get; set; } = "";
}

public sealed class HadbObject : INormalizable
{
    public readonly CriticalSection<HadbObject> Lock = new CriticalSection<HadbObject>();

    public HadbMemDataBase? MemDb { get; }

    public bool IsMemoryDbObject => MemDb != null;

    public string Uid { get; }

    public long Ver { get; private set; }

    public bool Deleted { get; private set; }

    public DateTimeOffset CreateDt { get; private set; }

    public DateTimeOffset UpdateDt { get; private set; }

    public DateTimeOffset DeleteDt { get; private set; }

    public bool Archive { get; }

    public long SnapshotNo { get; private set; }

    public string NameSpace { get; }

    public HadbData UserData { get; private set; }

    string _Ext1;
    string _Ext2;

    public string Ext1 { get => this._Ext1; set { this.CheckIsNotMemoryDbObject(); this._Ext1 = value._NonNull(); } }
    public string Ext2 { get => this._Ext2; set { this.CheckIsNotMemoryDbObject(); this._Ext2 = value._NonNull(); } }

    public long InternalFastUpdateVersion { get; private set; }

    public string GetUserDataJsonString() => this.UserData.GetUserDataJsonString();

    public Type UserDataType { get; }
    public string UserDataTypeName { get; }

    public HadbObject(HadbData userData, long snapshotNo, string nameSpace, string ext1 = "", string ext2 = "") : this(userData, ext1, ext2, Str.NewUid(userData.GetUserDataTypeName(), '_'), 1, false, snapshotNo, nameSpace, false, DtOffsetNow, DtOffsetNow, DtOffsetZero) { }

    public HadbObject(HadbData userData, string ext1, string ext2, string uid, long ver, bool archive, long snapshotNo, string nameSpace, bool deleted, DateTimeOffset createDt, DateTimeOffset updateDt, DateTimeOffset deleteDt, HadbMemDataBase? memDb = null)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();

        nameSpace = nameSpace._NormalizeKey(true);

        this.Uid = uid._NormalizeUid(true);

        if (this.Uid._IsEmpty())
        {
            throw new CoresLibException("uid is empty.");
        }

        userData._NullCheck(nameof(userData));

        this.UserData = userData._CloneDeep();

        this.SnapshotNo = snapshotNo;
        this.NameSpace = nameSpace;
        this.Ver = Math.Max(ver, 1);
        this.Archive = archive;
        this.Deleted = deleted;
        this.CreateDt = createDt._NormalizeDateTimeOffset();
        this.UpdateDt = updateDt._NormalizeDateTimeOffset();
        this.DeleteDt = deleteDt._NormalizeDateTimeOffset();
        this._Ext1 = ext1._NonNull();
        this._Ext2 = ext2._NonNull();

        this.MemDb = memDb;
        if (this.MemDb != null)
        {
            if (this.Archive) throw new CoresLibException("this.Archive == true");
        }

        this.UserDataType = this.UserData.GetUserDataType();
        this.UserDataTypeName = this.UserData.GetUserDataTypeName();

        this.Normalize();
    }

    public HadbObject ToMemoryDbObject(HadbMemDataBase memDb)
    {
        memDb._NullCheck();

        CheckIsNotMemoryDbObject();
        if (this.Archive) throw new CoresLibException("this.Archive == true");

        return new HadbObject(this.UserData, this.Ext1, this.Ext2, this.Uid, this.Ver, this.Archive, this.SnapshotNo, this.NameSpace, this.Deleted, this.CreateDt, this.UpdateDt, this.DeleteDt, memDb);
    }

    public HadbObject ToNonMemoryDbObject()
    {
        CheckIsMemoryDbObject();

        lock (this.Lock)
        {
            var q = new HadbObject(this.UserData, this.Ext1, this.Ext2, this.Uid, this.Ver, this.Archive, this.SnapshotNo, this.NameSpace, this.Deleted, this.CreateDt, this.UpdateDt, this.DeleteDt);

            q.InternalFastUpdateVersion = this.InternalFastUpdateVersion;

            return q;
        }
    }

    public HadbObject CloneObject()
    {
        CheckIsNotMemoryDbObject();
        return new HadbObject(this.UserData, this.Ext1, this.Ext2, this.Uid, this.Ver, this.Archive, this.SnapshotNo, this.NameSpace, this.Deleted, this.CreateDt, this.UpdateDt, this.DeleteDt);
    }

    public void CheckIsMemoryDbObject()
    {
        if (this.IsMemoryDbObject == false) throw new CoresLibException("this.IsMemoryDbObject == false");
    }

    public void CheckIsNotMemoryDbObject()
    {
        if (this.IsMemoryDbObject) throw new CoresLibException("this.IsMemoryDbObject == true");
    }

    public bool FastUpdate<T>(Func<T, bool> updateFunc) where T : HadbData
    {
        CheckIsMemoryDbObject();

        lock (this.Lock)
        {
            if (this.Deleted) throw new CoresLibException($"this.Deleted == true");
            if (this.Archive) throw new CoresLibException("this.Archive == true");

            var oldKeys = this.GetKeys();
            var oldLabels = this.GetLabels();

            var userData = this.UserData._CloneDeep().GetData<T>();

            string oldJson = userData.GetUserDataJsonString();

            bool ret = updateFunc(userData);

            if (ret == false)
            {
                return false;
            }

            try
            {
                userData.Normalize();
            }
            catch (Exception ex)
            {
                ex._Debug();
            }

            string newJson = userData.GetUserDataJsonString();

            if (oldJson._IsSamei(newJson))
            {
                return false;
            }

            var newKeys = userData.GetKeys();
            var newLables = userData.GetLabels();

            if (oldKeys.Equals(newKeys) == false)
            {
                throw new CoresLibException($"FastUpdate: updateFunc changed the key value. Old keys = {oldKeys._ObjectToJson(compact: true)}, New keys = {newKeys._ObjectToJson(compact: true)}");
            }

            if (oldLabels.Equals(newLables) == false)
            {
                throw new CoresLibException($"FastUpdate: updateFunc changed the label value. Old labels = {oldLabels._ObjectToJson(compact: true)}, New labels = {newLables._ObjectToJson(compact: true)}");
            }

            this.UserData = userData._CloneDeep();
            this.UpdateDt = DtOffsetNow;

            this.InternalFastUpdateVersion++;
        }

        this.MemDb!.AddToLazyUpdateQueueInternal(this);

        return true;
    }

    internal bool Internal_UpdateIfNew(EnsureSpecial yes, HadbObject newObj, out HadbKeys oldKeys, out HadbLabels oldLabels)
    {
        CheckIsMemoryDbObject();

        lock (this.Lock)
        {
            if (this.Uid._IsSamei(newObj.Uid) == false)
            {
                throw new CoresLibException($"this.Uid '{this.Uid}' != obj.Uid '{newObj.Uid}'");
            }

            if (this.NameSpace._IsSamei(newObj.NameSpace) == false)
            {
                throw new CoresLibException($"this.NameSpace '{this.NameSpace}' != obj.NameSpace '{newObj.NameSpace}'");
            }

            if (this.Archive)
            {
                throw new CoresLibException($"this.Archive == true");
            }

            if (newObj.Archive)
            {
                throw new CoresLibException($"obj.Archive == true");
            }

            bool update = false;

            if (this.Ver < newObj.Ver)
            {
                update = true;
            }
            else if (this.Ver == newObj.Ver && this.UpdateDt < newObj.UpdateDt)
            {
                update = true;
            }

            if (update)
            {
                oldKeys = this.GetKeys();
                oldLabels = this.GetLabels();

                this.Deleted = newObj.Deleted;
                this.CreateDt = newObj.CreateDt;
                this.UpdateDt = newObj.UpdateDt;
                this.DeleteDt = newObj.DeleteDt;
                this.UserData = newObj.UserData._CloneDeep();
                this._Ext1 = newObj.Ext1;
                this._Ext2 = newObj.Ext2;
                this.Ver = newObj.Ver;
                this.SnapshotNo = newObj.SnapshotNo;
            }
            else
            {
                oldKeys = default;
                oldLabels = default;
            }

            return update;
        }
    }

    public Type GetUserDataType() => this.UserDataType;
    public string GetUserDataTypeName() => this.UserDataTypeName;
    public string GetUidPrefix() => this.GetUserDataTypeName().ToUpper();

    public HadbKeys GetKeys() => this.Deleted == false ? this.UserData.GetKeys() : new HadbKeys("");
    public HadbLabels GetLabels() => this.Deleted == false ? this.UserData.GetLabels() : new HadbLabels("");

    public T GetData<T>() where T : HadbData
        => (T)this.UserData;

    public HadbObject<T> GetGenerics<T>() where T : HadbData
        => new HadbObject<T>(this);

    public void Normalize()
    {
        try
        {
            this.UserData.Normalize();
        }
        catch (Exception ex)
        {
            ex._Debug();
        }
    }

    public HadbSerializedObject ToSerializedObject()
    {
        CheckIsNotMemoryDbObject();
        if (this.Deleted) throw new CoresLibException("this.Deleted == true");

        HadbSerializedObject ret = new HadbSerializedObject
        {
            Uid = this.Uid,
            Ver = this.Ver,
            CreateDt = this.CreateDt,
            UpdateDt = this.UpdateDt,
            DeleteDt = this.DeleteDt,
            SnapshotNo = this.SnapshotNo,
            NameSpace = this.NameSpace,
            UserData = this.UserData,
            Ext1 = this.Ext1,
            Ext2 = this.Ext2,
            UserDataTypeName = this.UserDataTypeName,
        };

        return ret;
    }
}

public enum HadbIndexColumn
{
    Uid,
    Key1,
    Key2,
    Key3,
    Key4,
    Label1,
    Label2,
    Label3,
    Label4,
}

public abstract class HadbMemDataBase
{
    public class DataSet
    {
        public ConcurrentStrDictionary<HadbObject> AllObjectsDict = new ConcurrentStrDictionary<HadbObject>(StrComparer.IgnoreCaseComparer);
        public ImmutableDictionary<string, HadbObject> IndexedKeysTable = ImmutableDictionary<string, HadbObject>.Empty.WithComparers(StrComparer.IgnoreCaseComparer);
        public ImmutableDictionary<string, ConcurrentHashSet<HadbObject>> IndexedLabelsTable = ImmutableDictionary<string, ConcurrentHashSet<HadbObject>>.Empty.WithComparers(StrComparer.IgnoreCaseComparer);

        public void IndexedTable_DeleteObject_Critical(HadbObject obj, HadbKeys oldKeys, HadbLabels oldLabels)
        {
            obj.CheckIsMemoryDbObject();

            string typeName = obj.GetUserDataTypeName();

            IndexedKeysTable_DeleteInternal_Critical(HadbIndexColumn.Uid.ToString() + ":" + typeName + ":" + obj.NameSpace + ":" + obj.Uid);

            if (oldKeys.Key1._IsFilled()) IndexedKeysTable_DeleteInternal_Critical(HadbIndexColumn.Key1.ToString() + ":" + typeName + ":" + obj.NameSpace + ":" + oldKeys.Key1);
            if (oldKeys.Key2._IsFilled()) IndexedKeysTable_DeleteInternal_Critical(HadbIndexColumn.Key2.ToString() + ":" + typeName + ":" + obj.NameSpace + ":" + oldKeys.Key2);
            if (oldKeys.Key3._IsFilled()) IndexedKeysTable_DeleteInternal_Critical(HadbIndexColumn.Key3.ToString() + ":" + typeName + ":" + obj.NameSpace + ":" + oldKeys.Key3);
            if (oldKeys.Key4._IsFilled()) IndexedKeysTable_DeleteInternal_Critical(HadbIndexColumn.Key4.ToString() + ":" + typeName + ":" + obj.NameSpace + ":" + oldKeys.Key4);

            if (oldLabels.Label1._IsFilled()) IndexedLabelsTable_DeleteInternal_Critical(HadbIndexColumn.Label1.ToString() + ":" + typeName + ":" + obj.NameSpace + ":" + oldLabels.Label1, obj);
            if (oldLabels.Label2._IsFilled()) IndexedLabelsTable_DeleteInternal_Critical(HadbIndexColumn.Label2.ToString() + ":" + typeName + ":" + obj.NameSpace + ":" + oldLabels.Label2, obj);
            if (oldLabels.Label3._IsFilled()) IndexedLabelsTable_DeleteInternal_Critical(HadbIndexColumn.Label3.ToString() + ":" + typeName + ":" + obj.NameSpace + ":" + oldLabels.Label3, obj);
            if (oldLabels.Label4._IsFilled()) IndexedLabelsTable_DeleteInternal_Critical(HadbIndexColumn.Label4.ToString() + ":" + typeName + ":" + obj.NameSpace + ":" + oldLabels.Label4, obj);
        }

        public void IndexedTable_UpdateObject_Critical(HadbObject obj, HadbKeys oldKeys, HadbLabels oldLabels)
        {
            obj.CheckIsMemoryDbObject();

            HadbKeys newKeys = obj.GetKeys();
            HadbLabels newLabels = obj.GetLabels();

            if (oldKeys.Key1._IsSamei(newKeys.Key1) == false) IndexedKeysTable_ReplaceObject_Critical(obj, HadbIndexColumn.Key1, oldKeys.Key1, newKeys.Key1);
            if (oldKeys.Key2._IsSamei(newKeys.Key2) == false) IndexedKeysTable_ReplaceObject_Critical(obj, HadbIndexColumn.Key2, oldKeys.Key2, newKeys.Key2);
            if (oldKeys.Key3._IsSamei(newKeys.Key3) == false) IndexedKeysTable_ReplaceObject_Critical(obj, HadbIndexColumn.Key3, oldKeys.Key3, newKeys.Key3);
            if (oldKeys.Key4._IsSamei(newKeys.Key4) == false) IndexedKeysTable_ReplaceObject_Critical(obj, HadbIndexColumn.Key4, oldKeys.Key4, newKeys.Key4);

            if (oldLabels.Label1._IsSamei(newLabels.Label1) == false) IndexedLabelsTable_ReplaceObject_Critical(obj, HadbIndexColumn.Label1, oldLabels.Label1, newLabels.Label1);
            if (oldLabels.Label2._IsSamei(newLabels.Label2) == false) IndexedLabelsTable_ReplaceObject_Critical(obj, HadbIndexColumn.Label2, oldLabels.Label2, newLabels.Label2);
            if (oldLabels.Label3._IsSamei(newLabels.Label3) == false) IndexedLabelsTable_ReplaceObject_Critical(obj, HadbIndexColumn.Label3, oldLabels.Label3, newLabels.Label3);
            if (oldLabels.Label4._IsSamei(newLabels.Label4) == false) IndexedLabelsTable_ReplaceObject_Critical(obj, HadbIndexColumn.Label4, oldLabels.Label4, newLabels.Label4);
        }

        public void IndexedTable_AddObject_Critical(HadbObject obj)
        {
            obj.CheckIsMemoryDbObject();

            var newKeys = obj.GetKeys();
            var newLabels = obj.GetLabels();

            string typeName = obj.GetUserDataTypeName();

            IndexedKeysTable_AddOrUpdateInternal_Critical(HadbIndexColumn.Uid.ToString() + ":" + typeName + ":" + obj.NameSpace + ":" + obj.Uid, obj);

            if (newKeys.Key1._IsFilled()) IndexedKeysTable_AddOrUpdateInternal_Critical(HadbIndexColumn.Key1.ToString() + ":" + typeName + ":" + obj.NameSpace + ":" + newKeys.Key1, obj);
            if (newKeys.Key2._IsFilled()) IndexedKeysTable_AddOrUpdateInternal_Critical(HadbIndexColumn.Key2.ToString() + ":" + typeName + ":" + obj.NameSpace + ":" + newKeys.Key2, obj);
            if (newKeys.Key3._IsFilled()) IndexedKeysTable_AddOrUpdateInternal_Critical(HadbIndexColumn.Key3.ToString() + ":" + typeName + ":" + obj.NameSpace + ":" + newKeys.Key3, obj);
            if (newKeys.Key4._IsFilled()) IndexedKeysTable_AddOrUpdateInternal_Critical(HadbIndexColumn.Key4.ToString() + ":" + typeName + ":" + obj.NameSpace + ":" + newKeys.Key4, obj);

            if (newLabels.Label1._IsFilled()) IndexedLabelsTable_AddInternal_Critical(HadbIndexColumn.Label1.ToString() + ":" + typeName + ":" + obj.NameSpace + ":" + newLabels.Label1, obj);
            if (newLabels.Label2._IsFilled()) IndexedLabelsTable_AddInternal_Critical(HadbIndexColumn.Label2.ToString() + ":" + typeName + ":" + obj.NameSpace + ":" + newLabels.Label2, obj);
            if (newLabels.Label3._IsFilled()) IndexedLabelsTable_AddInternal_Critical(HadbIndexColumn.Label3.ToString() + ":" + typeName + ":" + obj.NameSpace + ":" + newLabels.Label3, obj);
            if (newLabels.Label4._IsFilled()) IndexedLabelsTable_AddInternal_Critical(HadbIndexColumn.Label4.ToString() + ":" + typeName + ":" + obj.NameSpace + ":" + newLabels.Label4, obj);
        }

        public void IndexedKeysTable_ReplaceObject_Critical(HadbObject obj, HadbIndexColumn column, string? oldKey, string? newKey)
        {
            obj.CheckIsMemoryDbObject();

            oldKey = oldKey._NormalizeKey(true);
            newKey = newKey._NormalizeKey(true);

            if (newKey._IsSamei(oldKey) == false)
            {
                if (newKey._IsFilled())
                {
                    IndexedKeysTable_AddOrUpdateInternal_Critical(column.ToString() + ":" + obj.GetUserDataTypeName() + ":" + obj.NameSpace + ":" + newKey, obj);
                }

                if (oldKey._IsFilled())
                {
                    IndexedKeysTable_DeleteInternal_Critical(column.ToString() + ":" + obj.GetUserDataTypeName() + ":" + obj.NameSpace + ":" + oldKey);
                }
            }
        }

        public HadbObject? IndexedKeysTable_SearchObject(HadbIndexColumn column, string typeName, string key, string nameSpace)
        {
            nameSpace = nameSpace._HadbNameSpaceNormalize();
            key = key._NormalizeKey(true);
            typeName = typeName._NonNullTrim();

            if (key._IsEmpty()) return null;

            return this.IndexedKeysTable._GetOrDefault(column.ToString() + ":" + typeName + ":" + nameSpace + ":" + key);
        }

        public void IndexedKeysTable_AddOrUpdateInternal_Critical(string keyStr, HadbObject obj)
        {
            keyStr = keyStr._NormalizeKey(true);

            ImmutableInterlocked.AddOrUpdate(ref this.IndexedKeysTable, keyStr, obj, (k, old) => obj);
        }

        public bool IndexedKeysTable_DeleteInternal_Critical(string keyStr)
        {
            keyStr = keyStr._NormalizeKey(true);

            return ImmutableInterlocked.TryRemove(ref this.IndexedKeysTable, keyStr, out _);
        }

        public void IndexedLabelsTable_ReplaceObject_Critical(HadbObject obj, HadbIndexColumn column, string? oldLabel, string? newLabel)
        {
            obj.CheckIsMemoryDbObject();

            oldLabel = oldLabel._NormalizeKey(true);
            newLabel = newLabel._NormalizeKey(true);

            if (oldLabel._IsSamei(newLabel) == false)
            {
                string typeName = obj.GetUserDataTypeName();

                if (newLabel._IsFilled())
                {
                    IndexedLabelsTable_AddInternal_Critical(column.ToString() + ":" + typeName + ":" + obj.NameSpace + ":" + newLabel, obj);
                }

                if (oldLabel._IsFilled())
                {
                    IndexedLabelsTable_DeleteInternal_Critical(column.ToString() + ":" + typeName + ":" + obj.NameSpace + ":" + oldLabel, obj);
                }
            }
        }

        public IEnumerable<HadbObject> IndexedLabelsTable_SearchObjects(HadbIndexColumn column, string typeName, string label, string nameSpace)
        {
            nameSpace = nameSpace._HadbNameSpaceNormalize();
            label = label._NormalizeKey(true);
            typeName = typeName._NonNullTrim();

            if (label._IsEmpty()) return EmptyOf<HadbObject>();

            var list = this.IndexedLabelsTable._GetOrDefault(column.ToString() + ":" + typeName + ":" + nameSpace + ":" + label);
            if (list == null) return EmptyOf<HadbObject>();

            return list.Keys;
        }

        public void IndexedLabelsTable_AddInternal_Critical(string labelKeyStr, HadbObject obj)
        {
            labelKeyStr = labelKeyStr._NormalizeKey(true);

            var list = ImmutableInterlocked.GetOrAdd(ref this.IndexedLabelsTable, labelKeyStr, k => new ConcurrentHashSet<HadbObject>());

            list.Add(obj);
        }

        public bool IndexedLabelsTable_DeleteInternal_Critical(string labelKeyStr, HadbObject obj)
        {
            labelKeyStr = labelKeyStr._NormalizeKey(true);

            var list = this.IndexedLabelsTable._GetOrDefault(labelKeyStr);
            if (list == null) return false;

            if (list.Remove(obj) == false) return false;

            if (list.Count == 0)
            {
                ImmutableInterlocked.TryRemove(ref this.IndexedLabelsTable, labelKeyStr, out _);
            }

            return true;
        }
    }

    protected abstract List<Type> GetDefinedUserDataTypesImpl();
    protected abstract List<Type> GetDefinedUserLogTypesImpl();

    public readonly AsyncLock CriticalLockAsync = new AsyncLock();

    public DataSet InternalData = new DataSet();

    readonly CriticalSection<HadbMemDataBase> LazyUpdateQueueLock = new CriticalSection<HadbMemDataBase>();
    internal ImmutableDictionary<HadbObject, int> _LazyUpdateQueue = ImmutableDictionary<HadbObject, int>.Empty;

    public int GetLazyUpdateQueueLength() => this._LazyUpdateQueue.Count;

    public void Debug(string str)
    {
        string pos = Dbg.GetCurrentExecutingPositionInfoString();
        $"{pos}: {str}"._Debug();
    }

    public StrDictionary<Type> GetDefinedUserDataTypesByName()
    {
        StrDictionary<Type> ret = new StrDictionary<Type>(StrComparer.SensitiveCaseComparer);

        List<Type> tmp = GetDefinedUserDataTypesImpl();

        foreach (var type in tmp)
        {
            if (type.IsSubclassOf(typeof(HadbData)) == false)
            {
                throw new CoresLibException($"type {type.ToString()} is not a subclass of {nameof(HadbData)}.");
            }

            ret.Add(type.Name, type);
        }

        return ret;
    }

    public StrDictionary<Type> GetDefinedUserLogTypesByName()
    {
        StrDictionary<Type> ret = new StrDictionary<Type>(StrComparer.SensitiveCaseComparer);

        List<Type> tmp = GetDefinedUserLogTypesImpl();

        foreach (var type in tmp)
        {
            if (type.IsSubclassOf(typeof(HadbLog)) == false)
            {
                throw new CoresLibException($"type {type.ToString()} is not a subclass of {nameof(HadbLog)}.");
            }

            ret.Add(type.Name, type);
        }

        return ret;
    }

    public void GetAllObjects(out ConcurrentStrDictionary<HadbObject> dict, out AsyncLock criticalLock)
    {
        DataSet data = this.InternalData;

        dict = data.AllObjectsDict;
        criticalLock = this.CriticalLockAsync;
    }

    public async Task ReloadFromDatabaseAsync(IEnumerable<HadbObject> objectList, bool fullReloadMode, DateTimeOffset partialReloadMinUpdateTime, string dataSourceInfo, CancellationToken cancel = default)
    {
        int countInserted = 0;
        int countUpdated = 0;
        int countRemoved = 0;
        List<HadbObject> objectList2;

        using (await this.CriticalLockAsync.LockWithAwait(cancel))
        {
            DataSet data = this.InternalData;

            if (fullReloadMode)
            {
                data = new DataSet();
            }

            objectList2 = objectList.ToList();

            // 並列化の試み 2021/12/06 うまくいくか未定
            await objectList2._ProcessParallelAsync(someObjects =>
            {
                foreach (var newObj in someObjects)
                {
                    try
                    {
                        if (data.AllObjectsDict.TryGetValue(newObj.Uid, out HadbObject? currentObj))
                        {
                            if (currentObj.Internal_UpdateIfNew(EnsureSpecial.Yes, newObj, out HadbKeys oldKeys, out HadbLabels oldLabels))
                            {
                                if (currentObj.Deleted == false)
                                {
                                    countUpdated++;
                                    data.IndexedTable_UpdateObject_Critical(currentObj, oldKeys, oldLabels);
                                }
                                else
                                {
                                    countRemoved++;
                                    data.IndexedTable_DeleteObject_Critical(currentObj, oldKeys, oldLabels); // おそらく正しい? 2021/12/06
                                }
                            }
                        }
                        else
                        {
                            var obj2 = newObj.ToMemoryDbObject(this);
                            data.AllObjectsDict[newObj.Uid] = obj2;

                            data.IndexedTable_AddObject_Critical(obj2);
                            countInserted++;
                        }
                    }
                    catch (Exception ex)
                    {
                        ex._Debug();
                    }
                }

                return TR();
            });


            this.InternalData = data;
        }

        Debug($"ReloadFromDatabaseAsync: Update Local Memory from {dataSourceInfo}: Mode={(fullReloadMode ? "FullReload" : $"PartialReload since '{partialReloadMinUpdateTime._ToDtStr()}'")}, DB_ReadObjs={objectList2.Count._ToString3()}, New={countInserted._ToString3()}, Update={countUpdated._ToString3()}, Remove={countRemoved._ToString3()}");
    }

    public HadbObject ApplyObjectToMemDb_Critical(HadbObject newObj)
    {
        newObj.Normalize();
        if (newObj.Archive) throw new CoresLibException("obj.Archive == true");

        var data = this.InternalData;

        if (data.AllObjectsDict.TryGetValue(newObj.Uid, out HadbObject? currentObject))
        {
            if (currentObject.Internal_UpdateIfNew(EnsureSpecial.Yes, newObj, out HadbKeys oldKeys, out HadbLabels oldLabels))
            {
                if (currentObject.Deleted == false)
                {
                    data.IndexedTable_UpdateObject_Critical(currentObject, oldKeys, oldLabels);
                }
                else
                {
                    data.IndexedTable_DeleteObject_Critical(currentObject, oldKeys, oldLabels);
                }
            }

            return currentObject;
        }
        else
        {
            if (newObj.Deleted == false)
            {
                var newObj2 = newObj.ToMemoryDbObject(this);
                data.AllObjectsDict[newObj.Uid] = newObj2;
                data.IndexedTable_AddObject_Critical(newObj2);
                return newObj2;
            }
            else
            {
                return newObj.ToMemoryDbObject(this);
            }
        }
    }

    internal void AddToLazyUpdateQueueInternal(HadbObject obj)
    {
        if (obj.Deleted) return;

        ImmutableInterlocked.TryAdd(ref this._LazyUpdateQueue, obj, 0);
    }

    internal void DeleteFromLazyUpdateQueueInternal(HadbObject obj)
    {
        ImmutableInterlocked.TryRemove(ref this._LazyUpdateQueue, obj, out _);
    }

    public HadbObject? IndexedKeysTable_SearchByUid(string uid, string typeName, string nameSpace)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        uid = uid._NormalizeKey(true);

        typeName = typeName._NonNullTrim();

        if (uid._IsEmpty()) return null;

        HadbObject? ret = this.InternalData.IndexedKeysTable_SearchObject(HadbIndexColumn.Uid, typeName, uid, nameSpace);

        return ret;
    }

    public HadbObject? IndexedKeysTable_SearchByKey(HadbKeys key, string typeName, string nameSpace)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        var list = IndexedKeysTable_SearchByKeys(key, typeName, nameSpace);
        return list.SingleOrDefault();
    }

    public IEnumerable<HadbObject> IndexedKeysTable_SearchByKeys(HadbKeys key, string typeName, string nameSpace)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        typeName = typeName._NonNullTrim();

        List<HadbObject> list = new List<HadbObject>();

        var data = this.InternalData;

        if (key.Key1._IsFilled())
        {
            var obj = data.IndexedKeysTable_SearchObject(HadbIndexColumn.Key1, typeName, key.Key1, nameSpace);
            if (obj != null) list.Add(obj);
        }

        if (key.Key2._IsFilled())
        {
            var obj = data.IndexedKeysTable_SearchObject(HadbIndexColumn.Key2, typeName, key.Key2, nameSpace);
            if (obj != null) list.Add(obj);
        }

        if (key.Key3._IsFilled())
        {
            var obj = data.IndexedKeysTable_SearchObject(HadbIndexColumn.Key3, typeName, key.Key3, nameSpace);
            if (obj != null) list.Add(obj);
        }

        if (key.Key4._IsFilled())
        {
            var obj = data.IndexedKeysTable_SearchObject(HadbIndexColumn.Key4, typeName, key.Key4, nameSpace);
            if (obj != null) list.Add(obj);
        }

        return list;
    }

    public IEnumerable<HadbObject> IndexedLabelsTable_SearchByLabels(HadbLabels labels, string typeName, string nameSpace)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        typeName = typeName._NonNullTrim();

        IEnumerable<HadbObject>? ret = null;

        IEnumerable<HadbObject>? tmp1 = null;
        IEnumerable<HadbObject>? tmp2 = null;
        IEnumerable<HadbObject>? tmp3 = null;
        IEnumerable<HadbObject>? tmp4 = null;

        var data = this.InternalData;

        if (labels.Label1._IsFilled())
        {
            tmp1 = data.IndexedLabelsTable_SearchObjects(HadbIndexColumn.Label1, typeName, labels.Label1, nameSpace);
            if (ret == null) ret = tmp1;
        }

        if (labels.Label2._IsFilled())
        {
            tmp2 = data.IndexedLabelsTable_SearchObjects(HadbIndexColumn.Label2, typeName, labels.Label2, nameSpace);
            if (ret == null) ret = tmp2;
        }

        if (labels.Label3._IsFilled())
        {
            tmp3 = data.IndexedLabelsTable_SearchObjects(HadbIndexColumn.Label3, typeName, labels.Label3, nameSpace);
            if (ret == null) ret = tmp3;
        }

        if (labels.Label4._IsFilled())
        {
            tmp4 = data.IndexedLabelsTable_SearchObjects(HadbIndexColumn.Label4, typeName, labels.Label4, nameSpace);
            if (ret == null) ret = tmp4;
        }

        if (ret == null) return EmptyOf<HadbObject>();

        if (tmp1 != null && object.ReferenceEquals(ret, tmp1) == false) ret = ret.Intersect(tmp1);
        if (tmp2 != null && object.ReferenceEquals(ret, tmp2) == false) ret = ret.Intersect(tmp2);
        if (tmp3 != null && object.ReferenceEquals(ret, tmp3) == false) ret = ret.Intersect(tmp3);
        if (tmp4 != null && object.ReferenceEquals(ret, tmp4) == false) ret = ret.Intersect(tmp4);

        return ret;
    }
}

public class HadbLogQuery : INormalizable
{
    public int MaxReturmItems { get; set; } = 0;
    public string Uid { get; set; } = "";
    public DateTimeOffset TimeStart { get; set; } = Util.ZeroDateTimeOffsetValue;
    public DateTimeOffset TimeEnd { get; set; } = Util.ZeroDateTimeOffsetValue;
    public long SnapshotNoStart { get; set; } = 0;
    public long SnapshotNoEnd { get; set; } = 0;
    public HadbLabels Labels { get; set; } = new HadbLabels("");
    public HadbLog? SearchTemplate { get; set; } = null;

    public void Normalize()
    {
        this.Uid = this.Uid._NormalizeUid(true);
        this.TimeStart = this.TimeStart._NormalizeDateTimeOffset();
        this.TimeEnd = this.TimeEnd._NormalizeDateTimeOffset();
    }
}

public class HadbBackupDatabase
{
    public DateTimeOffset TimeStamp = ZeroDateTimeOffsetValue;
    public List<HadbSerializedObject> ObjectsList = null!;
}

public abstract class HadbBase<TMem, TDynamicConfig> : AsyncService
    where TMem : HadbMemDataBase, new()
    where TDynamicConfig : HadbDynamicConfig
{
    Task ReloadMainLoopTask;
    Task LazyUpdateMainLoopTask;

    public bool IsLoopStarted { get; private set; } = false;

    public int DbLastFullReloadTookMsecs { get; private set; } = 0;
    public int DbLastPartialReloadTookMsecs { get; private set; } = 0;

    public int MemLastFullReloadTookMsecs { get; private set; } = 0;
    public int MemLastPartialReloadTookMsecs { get; private set; } = 0;

    public int LastLazyUpdateTookMsecs { get; private set; } = 0;
    public bool IsDatabaseConnectedForReload { get; private set; } = false;
    public Exception LastDatabaseConnectForReloadError { get; private set; } = new CoresException("Unknown error.");
    public int DatabaseConnectForReloadErrorCount { get; private set; } = 0;
    public bool IsDatabaseConnectedForLazyWrite { get; private set; } = false;

    public long LastDatabaseReloadTick { get; private set; } = 0;
    public long LastDatabaseFullReloadTick { get; private set; } = 0;
    public DateTimeOffset LastDatabaseReloadTime { get; private set; } = ZeroDateTimeOffsetValue;
    public DateTimeOffset LastDatabaseFullReloadTime { get; private set; } = ZeroDateTimeOffsetValue;

    public Exception? LastMemDbBackupLocalLoadError { get; private set; } = null;

    protected HadbSettingsBase Settings { get; }
    public string SystemName => Settings.SystemName;
    bool IsEnabled_NoMemDb => Settings.OptionFlags.Bit(HadbOptionFlags.NoMemDb);

    public TDynamicConfig CurrentDynamicConfig { get; private set; }

    public TMem? MemDb { get; private set; } = null;

    public int GetLazyUpdateQueueLength() => this.MemDb?.GetLazyUpdateQueueLength() ?? 0;

    readonly AsyncLock DynamicConfigValueDbLockAsync = new AsyncLock();

    protected abstract Task<HadbTran> BeginDatabaseTransactionImplAsync(bool writeMode, bool isTransaction, CancellationToken cancel = default);

    protected internal abstract Task AtomicAddDataListToDatabaseImplAsync(HadbTran tran, IEnumerable<HadbObject> dataList, CancellationToken cancel = default);
    protected internal abstract Task<HadbObject?> AtomicGetDataFromDatabaseImplAsync(HadbTran tran, string uid, string typeName, string nameSpace, CancellationToken cancel = default);
    protected internal abstract Task<IEnumerable<HadbObject>> AtomicGetArchivedDataFromDatabaseImplAsync(HadbTran tran, int maxItems, string uid, string typeName, string nameSpace, CancellationToken cancel = default);
    protected internal abstract Task<HadbObject?> AtomicSearchDataByKeyFromDatabaseImplAsync(HadbTran tran, HadbKeys keys, string typeName, string nameSpace, bool and, CancellationToken cancel = default);
    protected internal abstract Task<IEnumerable<HadbObject>> AtomicSearchDataListByLabelsFromDatabaseImplAsync(HadbTran tran, HadbLabels labels, string typeName, string nameSpace, CancellationToken cancel = default);
    protected internal abstract Task<HadbObject> AtomicDeleteDataFromDatabaseImplAsync(HadbTran tran, string uid, string typeName, string nameSpace, int maxArchive, CancellationToken cancel = default);
    protected internal abstract Task<HadbObject> AtomicUpdateDataOnDatabaseImplAsync(HadbTran tran, HadbObject data, CancellationToken cancel = default);
    protected internal abstract Task<string> AtomicGetKvImplAsync(HadbTran tran, string key, CancellationToken cancel = default);
    protected internal abstract Task<StrDictionary<string>> AtomicGetKvListImplAsync(HadbTran tran, CancellationToken cancel = default);
    protected internal abstract Task AtomicSetKvImplAsync(HadbTran tran, string key, string value, CancellationToken cancel = default);
    protected internal abstract Task AtomicAddSnapImplAsync(HadbTran tran, HadbSnapshot snap, CancellationToken cancel = default);

    protected internal abstract Task AtomicAddLogImplAsync(HadbTran tran, HadbLog log, string nameSpace, string ext1, string ext2, CancellationToken cancel = default);
    protected internal abstract Task<IEnumerable<HadbLog>> AtomicSearchLogImplAsync(HadbTran tran, string typeName, HadbLogQuery query, string nameSpace, CancellationToken cancel = default);

    protected internal abstract Task<bool> LazyUpdateImplAsync(HadbTran tran, HadbObject data, CancellationToken cancel = default);

    protected abstract Task<KeyValueList<string, string>> LoadDynamicConfigFromDatabaseImplAsync(CancellationToken cancel = default);
    protected abstract Task AppendMissingDynamicConfigToDatabaseImplAsync(KeyValueList<string, string> missingValues, CancellationToken cancel = default);
    protected abstract Task<List<HadbObject>> ReloadDataFromDatabaseImplAsync(bool fullReloadMode, DateTimeOffset partialReloadMinUpdateTime, Ref<DateTimeOffset>? lastStatTimestamp, CancellationToken cancel = default);

    protected abstract Task WriteStatImplAsync(HadbTran tran, DateTimeOffset dt, string generator, string value, string ext1, string ext2, CancellationToken cancel = default);

    protected abstract bool IsDeadlockExceptionImpl(Exception ex);


    protected virtual Task AddAdditionalStatAsync(EasyJsonStrAttributes stat, List<HadbObject> objectList, CancellationToken cancel = default) => TaskCompleted;

    public StrDictionary<Type> DefinedDataTypesByName { get; }
    public StrDictionary<Type> DefinedLogTypesByName { get; }

    public DeadlockRetryConfig DefaultDeadlockRetryConfig { get; set; }

    public HadbDebugFlags DebugFlags { get; set; } = HadbDebugFlags.None;

    public HadbBase(HadbSettingsBase settings, TDynamicConfig dynamicConfig)
    {
        try
        {
            settings._NullCheck(nameof(settings));

            if (settings.SystemName._IsEmpty()) throw new CoresLibException("SystemName is empty.");

            this.Settings = settings._CloneDeep();

            dynamicConfig._NullCheck(nameof(dynamicConfig));
            this.CurrentDynamicConfig = dynamicConfig;
            this.CurrentDynamicConfig.Normalize();

            this.ReloadMainLoopTask = ReloadMainLoopAsync(this.GrandCancel)._LeakCheck();
            this.LazyUpdateMainLoopTask = LazyUpdateMainLoopAsync(this.GrandCancel)._LeakCheck();

            TMem tmpMem = new TMem();
            this.DefinedDataTypesByName = tmpMem.GetDefinedUserDataTypesByName();
            this.DefinedLogTypesByName = tmpMem.GetDefinedUserLogTypesByName();

            this.DefaultDeadlockRetryConfig = new DeadlockRetryConfig(CoresConfig.Database.DefaultDatabaseTransactionRetryAverageIntervalSecs, CoresConfig.Database.DefaultDatabaseTransactionRetryCount, CoresConfig.Database.DefaultDatabaseTransactionRetryIntervalMaxFactor);
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    public Type? GetDataTypeByTypeName(string name) => this.DefinedDataTypesByName._GetOrDefault(name);

    public Type GetDataTypeByTypeName(string name, EnsureSpecial notFoundError)
    {
        Type? ret = GetDataTypeByTypeName(name);

        if (ret == null)
        {
            throw new CoresException($"Type name '{name}' not found.");
        }

        return ret;
    }

    public HadbData JsonToHadbData(string json, string typeName)
    {
        Type t = GetDataTypeByTypeName(typeName, EnsureSpecial.Yes);

        HadbData? ret = (HadbData?)json._JsonToObject(t);
        if (ret == null)
        {
            throw new CoresLibException("_JsonToObject() returned null.");
        }

        return ret;
    }


    public Type? GetLogTypeByTypeName(string name) => this.DefinedLogTypesByName._GetOrDefault(name);

    public Type GetLogTypeByTypeName(string name, EnsureSpecial notFoundError)
    {
        Type? ret = GetLogTypeByTypeName(name);

        if (ret == null)
        {
            throw new CoresException($"Type name '{name}' not found.");
        }

        return ret;
    }

    public HadbLog JsonToHadbLog(string json, string typeName)
    {
        Type t = GetLogTypeByTypeName(typeName, EnsureSpecial.Yes);

        HadbLog? ret = (HadbLog?)json._JsonToObject(t);
        if (ret == null)
        {
            throw new CoresLibException("_JsonToObject() returned null.");
        }

        return ret;
    }


    public void Start()
    {
        this.IsLoopStarted = true;
    }

    readonly AsyncLock Lock_ReloadDynamicConfigValuesAsync = new AsyncLock();

    async Task ReloadDynamicConfigValuesAsync(CancellationToken cancel)
    {
        using (await Lock_ReloadDynamicConfigValuesAsync.LockWithAwait(cancel))
        {
            using (await DynamicConfigValueDbLockAsync.LockWithAwait(cancel))
            {
                // DynamicConfig の最新値を DB から読み込む
                var loadedDynamicConfigValues = await this.LoadDynamicConfigFromDatabaseImplAsync(cancel);

                // 読み込んだ DynamicConfig の最新値を適用する
                var missingDynamicConfigValues = this.CurrentDynamicConfig.UpdateFromDatabaseAndReturnMissingValues(loadedDynamicConfigValues);

                // 不足している DynamicConfig のデフォルト値を DB に書き込む
                if (missingDynamicConfigValues.Any())
                {
                    if (this.Settings.OptionFlags.Bit(HadbOptionFlags.NoInitConfigDb) == false) // オプションで書き込まない設定になっていない限り
                    {
                        await this.AppendMissingDynamicConfigToDatabaseImplAsync(missingDynamicConfigValues, cancel);
                    }
                }
            }
        }
    }

    readonly AsyncLock Lock_UpdateCoreAsync = new AsyncLock();

    public async Task LazyUpdateCoreAsync(EnsureSpecial yes, CancellationToken cancel = default)
    {
        using (await Lock_UpdateCoreAsync.LockWithAwait(cancel))
        {
            try
            {
                // 非トランザクションの SQL 接続を開始する
                await using var tran = await this.BeginDatabaseTransactionImplAsync(true, false, cancel);

                // 現在 キューに入っている項目に対する Lazy Update の実行
                // キューは Immutable なので、現在の Queue を取得する
                var queue = this.MemDb!._LazyUpdateQueue;

                foreach (var kv in queue)
                {
                    var q = kv.Key;
                    var copyOfQ = q.ToNonMemoryDbObject();

                    bool ok = true;

                    if (copyOfQ.Deleted == false)
                    {
                        // 1 つの要素について DB 更新を行なう
                        await this.LazyUpdateImplAsync(tran, copyOfQ, cancel);
                    }

                    // DB 更新に成功した場合は、DB 更新中にこのオブジェクトの内容の変更があったかどうか確認する
                    if (ok)
                    {
                        if (copyOfQ.InternalFastUpdateVersion == q.InternalFastUpdateVersion)
                        {
                            // このオブジェクトの内容の変化がなければキューからこのオブジェクトを削除する
                            this.MemDb!.DeleteFromLazyUpdateQueueInternal(q);
                        }
                    }
                }

                this.IsDatabaseConnectedForLazyWrite = true;
            }
            catch
            {
                this.IsDatabaseConnectedForLazyWrite = false;

                throw;
            }
        }
    }

    public void UpdateSystemRelatimeStat(EasyJsonStrAttributes stat)
    {
        // 追加の統計情報 (標準)
        long processMemory = 0;
        int numThreads = 0;
        int numHandles = 0;
        try
        {
            var proc = Process.GetCurrentProcess();
            processMemory = proc.PrivateMemorySize64;
            numThreads = proc.Threads.Count;
            numHandles = proc.HandleCount;
        }
        catch { }

        CoresRuntimeStat sys = new CoresRuntimeStat();
        sys.Refresh(forceGc: false);
        stat.Set("Sys/DotNet/NumRunningTasks", sys.Task);
        stat.Set("Sys/DotNet/NumDelayedTasks", sys.D);
        stat.Set("Sys/DotNet/NumTimerTasks", sys.Q);
        stat.Set("Sys/DotNet/NumObjects", sys.Obj);
        stat.Set("Sys/DotNet/CpuUsage", sys.Cpu);
        stat.Set("Sys/DotNet/ManagedMemory_MBytes", (double)sys.Mem / 1024.0);
        stat.Set("Sys/DotNet/ProcessMemory_MBytes", (double)processMemory / 1024.0 / 1024.0);
        stat.Set("Sys/DotNet/NumNativeThreads", numThreads);
        stat.Set("Sys/DotNet/NumNativeHandles", numHandles);
        stat.Set("Sys/DotNet/GcTotal", sys.Gc);
        stat.Set("Sys/DotNet/Gc0", sys.Gc0);
        stat.Set("Sys/DotNet/Gc1", sys.Gc1);
        stat.Set("Sys/DotNet/Gc2", sys.Gc2);
        stat.Set("Sys/DotNet/BootDays", (double)Time.Tick64 / (double)(24 * 60 * 60 * 1000));
        stat.Set("Hadb/Sys/DbLazyUpdateQueueLength", this.GetLazyUpdateQueueLength());
        stat.Set("Hadb/Sys/DbConcurrentTranRead", this.DbConcurrentTranReadTotal);
        stat.Set("Hadb/Sys/DbConcurrentTranWrite", this.DbConcurrentTranWriteTotal);
        stat.Set("Hadb/Sys/DbTotalDeadlockCount", this.DbTotalDeadlockCountTotal);
        stat.Set("Hadb/Sys/DbLastFullReloadTookMsecs", this.DbLastFullReloadTookMsecs);
        stat.Set("Hadb/Sys/DbLastPartialReloadTookMsecs", this.DbLastPartialReloadTookMsecs);
        stat.Set("Hadb/Sys/IsDatabaseConnectedForLazyWrite", this.IsDatabaseConnectedForLazyWrite);
        stat.Set("Hadb/Sys/IsDatabaseConnectedForReload", this.IsDatabaseConnectedForReload);
        stat.Set("Hadb/Sys/MemLastFullReloadTookMsecs", this.MemLastFullReloadTookMsecs);
        stat.Set("Hadb/Sys/MemLastPartialReloadTookMsecs", this.MemLastPartialReloadTookMsecs);
    }

    async Task<EasyJsonStrAttributes> GenerateStatInternalAsync(List<HadbObject> objectList, CancellationToken cancel = default)
    {
        EasyJsonStrAttributes stat = new EasyJsonStrAttributes();

        Dictionary<string, long> dict = new Dictionary<string, long>(StrCmpi);

        long totalCount = 0;

        // オブジェクトごとの個数のカウント
        // hadb/count/NameSpace/TypeName
        foreach (var obj in objectList)
        {
            if (obj.Deleted == false && obj.Archive == false)
            {
                string key = $"Hadb/Count/{obj.NameSpace}/{obj.GetUserDataTypeName()}";

                dict._Inc(key);

                totalCount++;
            }
        }

        // すべてのオブジェクト総数
        dict["Hadb/Count/_AllObjects"] = totalCount;

        foreach (var kv in dict)
        {
            stat.TryAdd(kv.Key, kv.Value.ToString());
        }

        // 追加の統計情報 (開発者が定義)
        await this.AddAdditionalStatAsync(stat, objectList, cancel);

        // リアルタイム統計 (システム状態) の更新
        UpdateSystemRelatimeStat(stat);

        return stat;
    }

    public async Task ReloadCoreAsync(EnsureSpecial yes, bool fullReloadMode = true, DateTimeOffset partialReloadMinUpdateTime = default, CancellationToken cancel = default)
    {
        if (fullReloadMode == false)
        {
            if (partialReloadMinUpdateTime._IsZeroDateTime())
            {
                partialReloadMinUpdateTime = this.LastDatabaseReloadTime - this.CurrentDynamicConfig.HadbReloadTimeShiftMarginMsecs._ToTimeSpanMSecs();
            }
        }

        FilePath backupDatabasePath = this.Settings.BackupDir.Combine(Consts.FileNames.HadbBackupDatabaseFileName);
        FilePath backupDynamicConfigPath = this.Settings.BackupDir.Combine(Consts.FileNames.HadbBackupDynamicConfigFileName);

        try
        {
            long nowTick;
            DateTimeOffset nowTime;

            if (this.DebugFlags.Bit(HadbDebugFlags.CauseErrorOnDatabaseReload))
            {
                throw new CoresException("Dummy Exception for Debug: CauseErrorOnDatabaseReload");
            }

            // Dynamic Config の値の読み込み
            await ReloadDynamicConfigValuesAsync(cancel);

            using (await Lock_UpdateCoreAsync.LockWithAwait(cancel))
            {
                nowTick = Time.Tick64;
                nowTime = DtOffsetNow;

                if (this.IsEnabled_NoMemDb == false)
                {
                    // DB からオブジェクト一覧を読み込む
                    Ref<DateTimeOffset> lastStatWrittenTimeStamp = new Ref<DateTimeOffset>(ZeroDateTimeOffsetValue);

                    long dbStartTick = Time.HighResTick64;
                    List<HadbObject> loadedObjectsList = await this.ReloadDataFromDatabaseImplAsync(fullReloadMode, partialReloadMinUpdateTime, lastStatWrittenTimeStamp, cancel);
                    long dbEndTick = Time.HighResTick64;

                    if (fullReloadMode)
                    {
                        DbLastFullReloadTookMsecs = (int)(dbEndTick - dbStartTick);
                    }
                    else
                    {
                        DbLastPartialReloadTookMsecs = (int)(dbEndTick - dbStartTick);
                    }

                    TMem? currentMemDb = this.MemDb;
                    if (currentMemDb == null) currentMemDb = new TMem();

                    long memStartTick = Time.HighResTick64;
                    await currentMemDb.ReloadFromDatabaseAsync(loadedObjectsList, fullReloadMode, partialReloadMinUpdateTime, "Database", cancel);
                    long memEndTick = Time.HighResTick64;

                    if (fullReloadMode)
                    {
                        MemLastFullReloadTookMsecs = (int)(memEndTick - memStartTick);
                    }
                    else
                    {
                        MemLastPartialReloadTookMsecs = (int)(memEndTick - memStartTick);
                    }

                    this.MemDb = currentMemDb;

                    if (fullReloadMode)
                    {
                        // DB のデータをローカルバックアップファイルに書き込む
                        await backupDynamicConfigPath.FileSystem.WriteJsonToFileAsync(backupDynamicConfigPath.PathString, this.CurrentDynamicConfig,
                            backupDynamicConfigPath.Flags | FileFlags.AutoCreateDirectory | FileFlags.OnCreateSetCompressionFlag,
                            cancel: cancel, withBackup: true);

                        HadbBackupDatabase backupData = new HadbBackupDatabase
                        {
                            ObjectsList = await loadedObjectsList._ProcessParallelAndAggregateAsync(x =>
                            {
                                List<HadbSerializedObject> ret = new List<HadbSerializedObject>();

                                foreach (var obj in x)
                                {
                                    if (obj.Archive == false && obj.Deleted == false)
                                    {
                                        ret.Add(obj.ToSerializedObject());
                                    }
                                }

                                return TR(ret);
                            }, cancel: cancel),
                            TimeStamp = nowTime,
                        };

                        await backupDatabasePath.FileSystem.WriteJsonToFileAsync(backupDatabasePath.PathString, backupData,
                            backupDynamicConfigPath.Flags | FileFlags.AutoCreateDirectory | FileFlags.OnCreateSetCompressionFlag,
                            cancel: cancel, withBackup: true);
                    }

                    this.IsDatabaseConnectedForReload = true;

                    if (fullReloadMode)
                    {
                        // stat を定期的に生成する
                        bool saveStat = false;

                        if (this.CurrentDynamicConfig.HadbAutomaticStatIntervalMsecs >= 1 && (lastStatWrittenTimeStamp.Value._IsZeroDateTime() || (DtOffsetNow >= (lastStatWrittenTimeStamp.Value + this.CurrentDynamicConfig.HadbAutomaticStatIntervalMsecs._ToTimeSpanMSecs()))))
                        {
                            saveStat = true;
                        }

                        if (saveStat)
                        {
                            var stat = await this.GenerateStatInternalAsync(loadedObjectsList, cancel);

                            // stat をデータベースに追記する
                            try
                            {
                                await this.TranAsync(true, async tran =>
                                {
                                    await this.WriteStatImplAsync(tran, DtOffsetNow, Env.DnsFqdnHostName, stat.ToJsonString(), "", "", cancel);

                                    return true;
                                },
                                cancel: cancel,
                                ignoreQuota: true);
                            }
                            catch (Exception ex)
                            {
                                ex._Error();
                            }
                        }
                    }
                }
                else
                {
                    // DB からオブジェクト一覧は読み込まない
                    TMem? currentMemDb = this.MemDb;
                    if (currentMemDb == null) currentMemDb = new TMem(); // 適当に空を食わせておく
                    this.MemDb = currentMemDb;
                }
            }

            // 成功した場合は内部変数を更新
            this.LastDatabaseReloadTick = nowTick;
            this.LastDatabaseReloadTime = nowTime;

            if (fullReloadMode)
            {
                this.LastDatabaseFullReloadTick = nowTick;
                this.LastDatabaseFullReloadTime = nowTime;
            }

            this.IsDatabaseConnectedForReload = true;
        }
        catch (Exception ex)
        {
            ex._Debug();

            this.DatabaseConnectForReloadErrorCount++;
            this.LastDatabaseConnectForReloadError = ex;
            this.IsDatabaseConnectedForReload = false;

            if (fullReloadMode)
            {
                DbLastFullReloadTookMsecs = 0;
                MemLastFullReloadTookMsecs = 0;
            }
            else
            {
                DbLastPartialReloadTookMsecs = 0;
                MemLastPartialReloadTookMsecs = 0;
            }

            // データベースからもバックアップファイルからもまだデータが読み込まれていない場合は、バックアップファイルから読み込む
            if (this.MemDb == null)
            {
                if (this.IsEnabled_NoMemDb == false)
                {
                    try
                    {
                        try
                        {
                            // DynamicConfig
                            TDynamicConfig loadDynamicConfig = await backupDynamicConfigPath.FileSystem.ReadJsonFromFileAsync<TDynamicConfig>(backupDynamicConfigPath.PathString,
                                cancel: cancel,
                                withBackup: true);

                            loadDynamicConfig.Normalize();

                            this.CurrentDynamicConfig = loadDynamicConfig;
                        }
                        catch (Exception ex2)
                        {
                            // DynamicConfig の読み込みには失敗しても良いことにする
                            // ただし、エラーはログに出力する
                            ex2._Error();
                        }

                        // データ本体
                        HadbBackupDatabase loadData = await backupDatabasePath.FileSystem.ReadJsonFromFileAsync<HadbBackupDatabase>(backupDatabasePath.PathString,
                            maxSize: Consts.Numbers.LocalDatabaseJsonFileMaxSize,
                            cancel: cancel,
                            withBackup: true);

                        var loadedObjectsList = await loadData.ObjectsList._ProcessParallelAndAggregateAsync(x =>
                        {
                            List<HadbObject> ret = new List<HadbObject>();
                            var serializer = Json.CreateSerializer();

                            foreach (var obj in x)
                            {
                                JObject jo = (JObject)obj.UserData;
                                Type type = GetDataTypeByTypeName(obj.UserDataTypeName, EnsureSpecial.Yes);
                                HadbData data = (HadbData)jo.ToObject(type, serializer);
                                HadbObject a = new HadbObject(data, obj.Ext1, obj.Ext2, obj.Uid, obj.Ver, false, obj.SnapshotNo, obj.NameSpace, false, obj.CreateDt, obj.UpdateDt, obj.DeleteDt);
                                ret.Add(a);
                            }
                            return TR(ret);
                        },
                        cancel: cancel);

                        TMem? currentMemDb = new TMem();

                        long memStartTick = Time.HighResTick64;
                        await currentMemDb.ReloadFromDatabaseAsync(loadedObjectsList, true, default, $"Local Database Backup File '{backupDatabasePath.PathString}'", cancel);
                        long memEndTick = Time.HighResTick64;

                        this.MemDb = currentMemDb;
                    }
                    catch (Exception ex3)
                    {
                        // ローカルファイルからのデータベース読み込みに失敗
                        LastMemDbBackupLocalLoadError = ex3;
                    }
                }
                else
                {
                    // ローカルファイルからのデータベース読み込みがそもそも禁止されている
                    LastMemDbBackupLocalLoadError = ex;
                }
            }

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
        await TaskUtil.AwaitWithPollAsync(Timeout.Infinite, 100, () => this.IsLoopStarted, cancel, true);
        Debug($"ReloadMainLoopAsync: Started.");

        while (cancel.IsCancellationRequested == false)
        {
            numCycle++;
            Debug($"ReloadMainLoopAsync: numCycle={numCycle}, numError={numError} Start.");

            long startTick = Time.HighResTick64;
            bool ok = false;

            long now = Time.Tick64;

            bool fullReloadMode = false;

            if (this.LastDatabaseFullReloadTick == 0 || (now >= (this.LastDatabaseFullReloadTick + this.CurrentDynamicConfig.HadbFullReloadIntervalMsecs)) || this.MemDb == null)
            {
                fullReloadMode = true;
            }

            try
            {
                // リロード
                await ReloadCoreAsync(EnsureSpecial.Yes, fullReloadMode, cancel: cancel);
                ok = true;
            }
            catch (Exception ex)
            {
                ex._Error();
            }

            long endTick = Time.HighResTick64;

            Debug($"ReloadMainLoopAsync: numCycle={numCycle}, numError={numError} End. Took time: {(endTick - startTick)._ToString3()} msecs.");

            if (this.IsDatabaseConnectedForReload)
            {
                if (this.Settings.OptionFlags.Bit(HadbOptionFlags.NoAutoDbReloadAndUpdate)) return;
            }

            int nextWaitTime = Util.GenRandInterval(ok ? this.CurrentDynamicConfig.HadbReloadIntervalMsecsLastOk : this.CurrentDynamicConfig.HadbReloadIntervalMsecsLastError);
            Debug($"ReloadMainLoopAsync: Waiting for {nextWaitTime._ToString3()} msecs for next DB read.");
            await cancel._WaitUntilCanceledAsync(nextWaitTime);
        }

        Debug($"ReloadMainLoopAsync: Finished.");
    }

    async Task LazyUpdateMainLoopAsync(CancellationToken cancel)
    {
        if (this.Settings.OptionFlags.Bit(HadbOptionFlags.NoAutoDbReloadAndUpdate)) return;

        int numCycle = 0;
        int numError = 0;
        await Task.Yield();
        Debug($"LazyUpdateMainLoopAsync: Waiting for start.");
        await TaskUtil.AwaitWithPollAsync(Timeout.Infinite, 100, () => (this.CheckIfReady(requireDatabaseConnected: true, EnsureSpecial.Yes).IsOk), cancel, true);
        Debug($"LazyUpdateMainLoopAsync: Started.");

        while (cancel.IsCancellationRequested == false)
        {
            numCycle++;
            Debug($"LazyUpdateMainLoopAsync: numCycle={numCycle}, numError={numError} Start.");

            long startTick = Time.HighResTick64;
            bool ok = false;

            try
            {
                await LazyUpdateCoreAsync(EnsureSpecial.Yes, cancel);
                ok = true;
            }
            catch (Exception ex)
            {
                ex._Error();
            }

            long endTick = Time.HighResTick64;
            if (ok)
            {
                LastLazyUpdateTookMsecs = (int)(endTick - startTick);
            }
            else
            {
                LastLazyUpdateTookMsecs = 0;
            }

            Debug($"LazyUpdateMainLoopAsync: numCycle={numCycle}, numError={numError} End. Took time: {(endTick - startTick)._ToString3()} msecs.");

            int nextWaitTime = Util.GenRandInterval(this.CurrentDynamicConfig.HadbLazyUpdateIntervalMsecs);
            Debug($"LazyUpdateMainLoopAsync: Waiting for {nextWaitTime._ToString3()} msecs for next DB read.");
            await cancel._WaitUntilCanceledAsync(nextWaitTime);
        }

        Debug($"LazyUpdateMainLoopAsync: Finished.");
    }

    public void Debug(string str)
    {
        $"{this.GetType().Name}: {str}"._Debug();
    }

    public void CheckIfReady(bool requireDatabaseConnected = true)
    {
        var ret = CheckIfReady(requireDatabaseConnected, doNotThrowError: EnsureSpecial.Yes);
        ret.ThrowIfException();
    }

    public void CheckMemDb()
    {
        if (this.IsEnabled_NoMemDb) throw new CoresLibException("NoMemDB option is enabled.");
    }

    public ResultOrExeption<bool> CheckIfReady(bool requireDatabaseConnected, EnsureSpecial doNotThrowError)
    {
        if (requireDatabaseConnected)
        {
            if (this.IsDatabaseConnectedForReload == false)
            {
                return new CoresLibException("IsDatabaseConnectedForReload == false");
            }
        }

        if (this.MemDb == null)
        {
            return new CoresLibException("MemDb is not loaded yet.");
        }

        return true;
    }

    public async Task WaitUntilReadyForAtomicAsync(int maxDbTryCountError = 1, CancellationToken cancel = default)
    {
        maxDbTryCountError = Math.Max(maxDbTryCountError, 1);
        await Task.Yield();
        int startErrorCount = this.DatabaseConnectForReloadErrorCount;
        await TaskUtil.AwaitWithPollAsync(Timeout.Infinite, 100, () =>
        {
            bool ret = CheckIfReady(requireDatabaseConnected: true, doNotThrowError: EnsureSpecial.Yes).IsOk;
            if (ret == false)
            {
                if (this.DatabaseConnectForReloadErrorCount >= (startErrorCount + maxDbTryCountError))
                {
                    throw this.LastDatabaseConnectForReloadError;
                }
            }
            return ret;
        }, cancel, true);
    }

    public async Task WaitUntilReadyForFastAsync(CancellationToken cancel = default)
    {
        await Task.Yield();
        await TaskUtil.AwaitWithPollAsync(Timeout.Infinite, 100, () =>
        {
            bool ret = CheckIfReady(requireDatabaseConnected: false, doNotThrowError: EnsureSpecial.Yes).IsOk;
            if (ret == false)
            {
                if (this.LastMemDbBackupLocalLoadError != null)
                {
                    throw this.LastMemDbBackupLocalLoadError;
                }
            }
            return ret;
        }, cancel, true);
    }


    public int DbConcurrentTranWriteTotal => _DbConcurrentTranWriteTotal;
    public int DbConcurrentTranReadTotal => _DbConcurrentTranReadTotal;
    public int DbTotalDeadlockCountTotal => _DbTotalDeadlockCountTotal;

    int _DbConcurrentTranWriteTotal = 0;
    int _DbConcurrentTranReadTotal = 0;
    int _DbTotalDeadlockCountTotal = 0;

    readonly ConcurrentLimiter<string> DbConcurrentTranLimiterWritePerClient = new ConcurrentLimiter<string>();
    readonly ConcurrentLimiter<string> DbConcurrentTranLimiterReadPerClient = new ConcurrentLimiter<string>();

    public async Task<bool> TranAsync(bool writeMode, Func<HadbTran, Task<bool>> task, bool takeSnapshot = false, RefLong? snapshotNoRet = null, CancellationToken cancel = default, DeadlockRetryConfig? retryConfig = null, bool ignoreQuota = false, string clientName = "")
    {
        CheckIfReady();

        retryConfig ??= this.DefaultDeadlockRetryConfig;
        int numRetry = 0;

        // 同時並列実行トランザクション数の制限
        using IDisposable concurrentLimitEntry = (ignoreQuota || clientName._IsEmpty()) ? new EmptyDisposable() :
            (writeMode ? this.DbConcurrentTranLimiterWritePerClient.EnterWithUsing(clientName, out _, this.CurrentDynamicConfig.HadbMaxDbConcurrentWriteTransactionsPerClient) :
                         this.DbConcurrentTranLimiterReadPerClient.EnterWithUsing(clientName, out _, this.CurrentDynamicConfig.HadbMaxDbConcurrentReadTransactionsPerClient));

        if (writeMode)
        {
            int current = Interlocked.Increment(ref _DbConcurrentTranWriteTotal);
            if (ignoreQuota == false && current > this.CurrentDynamicConfig.HadbMaxDbConcurrentWriteTransactionsTotal)
            {
                Interlocked.Decrement(ref _DbConcurrentTranWriteTotal);
                throw new CoresException("Too many concurrent write transactions running. Please wait for moment and try again later.");
            }
        }
        else
        {
            int current = Interlocked.Increment(ref _DbConcurrentTranReadTotal);
            if (ignoreQuota == false && current > this.CurrentDynamicConfig.HadbMaxDbConcurrentReadTransactionsTotal)
            {
                Interlocked.Decrement(ref _DbConcurrentTranReadTotal);
                throw new CoresException("Too many concurrent read transactions running. Please wait for moment and try again later.");
            }
        }

        try
        {
            LABEL_RETRY:
            try
            {
                await using var tran = await this.BeginDatabaseTransactionImplAsync(writeMode, true, cancel);

                await tran.BeginAsync(takeSnapshot, cancel);

                if (await task(tran))
                {
                    await tran.CommitAsync(cancel);

                    snapshotNoRet?.Set(tran.CurrentSnapNoForWriteMode);
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                if (this.IsDeadlockExceptionImpl(ex))
                {
                    // デッドロック発生
                    Interlocked.Increment(ref _DbTotalDeadlockCountTotal);

                    numRetry++;
                    if (numRetry <= retryConfig.RetryCount)
                    {
                        int nextInterval = Util.GenRandIntervalWithRetry(retryConfig.RetryAverageInterval, numRetry, retryConfig.RetryAverageInterval * retryConfig.RetryIntervalMaxFactor, 60.0);

                        $"Deadlock retry occured. numRetry = {numRetry}. Waiting for {nextInterval} msecs. {ex.ToString()}"._Debug();

                        await Task.Delay(nextInterval);

                        goto LABEL_RETRY;
                    }

                    throw;
                }
                else
                {
                    throw;
                }
            }
        }
        finally
        {
            if (writeMode)
            {
                Interlocked.Decrement(ref _DbConcurrentTranWriteTotal);
            }
            else
            {
                Interlocked.Decrement(ref _DbConcurrentTranReadTotal);
            }
        }
    }


    public HadbObject<T>? FastGet<T>(string uid, string nameSpace = Consts.Strings.HadbDefaultNameSpace) where T : HadbData
        => FastGet(uid, typeof(T).Name, nameSpace);

    public HadbObject? FastGet(string uid, string typeName, string nameSpace = Consts.Strings.HadbDefaultNameSpace)
    {
        this.CheckIfReady(requireDatabaseConnected: false);
        this.CheckMemDb();
        nameSpace = nameSpace._HadbNameSpaceNormalize();

        var mem = this.MemDb!;

        var ret = mem.IndexedKeysTable_SearchByUid(uid, typeName, nameSpace);

        if (ret == null) return null;

        if (ret.Deleted) return null;

        return ret;
    }

    public HadbObject<T>? FastSearchByKey<T>(T model, string nameSpace = Consts.Strings.HadbDefaultNameSpace) where T : HadbData
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        model.Normalize();
        return FastSearchByKey<T>(model.GetKeys(), nameSpace);
    }

    public HadbObject<T>? FastSearchByKey<T>(HadbKeys keys, string nameSpace = Consts.Strings.HadbDefaultNameSpace) where T : HadbData
        => FastSearchByKey(keys, typeof(T).Name, nameSpace);

    public HadbObject? FastSearchByKey(HadbKeys keys, string typeName, string nameSpace = Consts.Strings.HadbDefaultNameSpace)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        this.CheckIfReady(requireDatabaseConnected: false);
        this.CheckMemDb();

        var mem = this.MemDb!;

        var ret = mem.IndexedKeysTable_SearchByKey(keys, typeName, nameSpace);

        if (ret == null) return null;

        if (ret.Deleted) return null;

        return ret;
    }

    public IEnumerable<HadbObject<T>> FastSearchByLabels<T>(T model, string nameSpace = Consts.Strings.HadbDefaultNameSpace) where T : HadbData
    {
        model.Normalize();
        return FastSearchByLabels(model.GetLabels(), typeof(T).Name, nameSpace).Select(x => x.GetGenerics<T>());
    }

    public IEnumerable<HadbObject<T>> FastSearchByLabels<T>(HadbLabels labels, string nameSpace = Consts.Strings.HadbDefaultNameSpace) where T : HadbData
        => FastSearchByLabels(labels, typeof(T).Name, nameSpace).Select(x => x.GetGenerics<T>());

    public IEnumerable<HadbObject> FastSearchByLabels(HadbLabels labels, string typeName, string nameSpace = Consts.Strings.HadbDefaultNameSpace)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        this.CheckIfReady(requireDatabaseConnected: false);
        this.CheckMemDb();

        var mem = this.MemDb!;

        var items = mem.IndexedLabelsTable_SearchByLabels(labels, typeName, nameSpace);

        return items.Where(x => x.Deleted == false);
    }

    public void FastGetAllObjects(out ConcurrentStrDictionary<HadbObject> dict, out AsyncLock criticalLock)
    {
        this.CheckIfReady(requireDatabaseConnected: false);
        this.CheckMemDb();

        var mem = this.MemDb!;

        mem.GetAllObjects(out dict, out criticalLock);
    }

    public IEnumerable<HadbObject<T>> FastEnumObjects<T>(string nameSpace = Consts.Strings.HadbDefaultNameSpace) where T : HadbData
        => FastEnumObjects(typeof(T).Name, nameSpace).Select(x => x.GetGenerics<T>());

    public IEnumerable<HadbObject> FastEnumObjects(string typeName, string nameSpace = Consts.Strings.HadbDefaultNameSpace)
    {
        nameSpace = nameSpace._HadbNameSpaceNormalize();
        this.CheckIfReady(requireDatabaseConnected: false);
        this.CheckMemDb();

        FastGetAllObjects(out ConcurrentStrDictionary<HadbObject> dict, out AsyncLock criticalLock);

        List<HadbObject> ret = new List<HadbObject>();

        using (criticalLock.LockLegacy())
        {
            foreach (var obj in dict.Values)
            {
                if (obj.UserDataTypeName == typeName && obj.NameSpace == nameSpace && obj.Deleted == false)
                {
                    ret.Add(obj);
                }
            }
        }

        return ret;
    }


    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await ReloadMainLoopTask._TryAwait();
            await LazyUpdateMainLoopTask._TryAwait();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }


    public abstract class HadbTran : AsyncService
    {
        public bool IsWriteMode { get; }
        public bool IsTransaction { get; }
        public bool TakeSnapshot { get; private set; }
        public bool IsDbSnapshotReadMode { get; }
        public long CurrentSnapNoForWriteMode { get; private set; }
        public HadbMemDataBase MemDb { get; }
        List<HadbObject> ApplyObjectsList = new List<HadbObject>();

        public IReadOnlyList<HadbObject> GetApplyObjectsList() => this.ApplyObjectsList;

        protected abstract Task CommitImplAsync(CancellationToken cancel);

        public HadbBase<TMem, TDynamicConfig> Hadb;

        public HadbTran(bool writeMode, bool isTransaction, bool isDbSnapshotReadMode, HadbBase<TMem, TDynamicConfig> hadb)
        {
            try
            {
                this.IsDbSnapshotReadMode = isDbSnapshotReadMode;
                this.IsWriteMode = writeMode;
                this.IsTransaction = isTransaction;
                this.Hadb = hadb;
                this.MemDb = this.Hadb.MemDb!;
            }
            catch (Exception ex)
            {
                this._DisposeSafe(ex);
                throw;
            }
        }

        Once BeganFlag = new Once();

        void CheckBegan()
        {
            if (BeganFlag.IsSet == false)
            {
                throw new CoresLibException("Transaction is not began.");
            }
            if (CommitedFlag.IsSet || finished.IsSet)
            {
                throw new CoresLibException("Transaction is already finished.");
            }
        }

        public async Task BeginAsync(bool takeSnapshot, CancellationToken cancel = default)
        {
            long autoSnapshotInterval = this.Hadb.CurrentDynamicConfig.HadbAutomaticSnapshotIntervalMsecs;

            if (BeganFlag.IsFirstCall())
            {
                if (this.IsWriteMode)
                {
                    var now = DtOffsetNow;

                    this.TakeSnapshot = takeSnapshot;

                    // Snapshot 処理をする。
                    // まず現在の Snapshot 番号を取得する。
                    var kvList = await this.AtomicGetKvListAsync(cancel);
                    this.CurrentSnapNoForWriteMode = kvList.SingleOrDefault(x => x.Key._IsSamei("_hadb_sys_current_snapshot_no")).Value._ToLong();

                    // 最後に Snapshot が撮られた日時を取得する
                    var lastTaken = Str.DtstrToDateTimeOffset(kvList.SingleOrDefault(x => x.Key._IsSamei("_hadb_sys_current_snapshot_timestamp")).Value);

                    string snapshotDescrption = "Normal Snapshot";

                    if (this.CurrentSnapNoForWriteMode == 0)
                    {
                        // 初めての場合は、必ず Snapshot を撮る。
                        this.TakeSnapshot = true;
                        snapshotDescrption = "Initial Snapshot";

                        if (this.Hadb.Settings.OptionFlags.Bit(HadbOptionFlags.NoInitSnapshot))
                        {
                            // NoInitSnapshot が設定されている場合は、擬似的にスナップショットを撮るが、実際には撮らない。(主にデバッグ用である。)
                            this.CurrentSnapNoForWriteMode = 1;
                            return;
                        }
                    }

                    if (this.Hadb.Settings.OptionFlags.Bit(HadbOptionFlags.DoNotTakeSnapshotAtAll))
                    {
                        // スナップショットを全く撮らない。(主にデバッグ用である。)
                        if (this.CurrentSnapNoForWriteMode == 0)
                        {
                            this.CurrentSnapNoForWriteMode = 1;
                        }
                        return;
                    }

                    if (this.TakeSnapshot == false && autoSnapshotInterval != 0 && lastTaken.AddMilliseconds(autoSnapshotInterval) < now)
                    {
                        // 自動スナップショットを撮る。
                        this.TakeSnapshot = true;
                        snapshotDescrption = $"Automatic Snapshot (Interval = {Str.TimeSpanToTsStr(autoSnapshotInterval._ToTimeSpanMSecs(), true)})";
                    }

                    if (this.TakeSnapshot)
                    {
                        // Snapshot を撮る場合、Snapshot 番号をインクリメントする。
                        this.CurrentSnapNoForWriteMode++;

                        // インクリメントされた Snapshot 番号を書き込む。
                        await this.AtomicSetKvAsync("_hadb_sys_current_snapshot_no", this.CurrentSnapNoForWriteMode.ToString(), cancel);

                        var snap = new HadbSnapshot(Str.NewUid("SNAPSHOT", '_'), Hadb.SystemName, this.CurrentSnapNoForWriteMode, now, snapshotDescrption, "", "");

                        // 最後の Snapshot 情報を書き込む。
                        await this.AtomicSetKvAsync("_hadb_sys_current_snapshot_uid", snap.Uid, cancel);
                        await this.AtomicSetKvAsync("_hadb_sys_current_snapshot_timestamp", snap.TimeStamp._ToDtStr(withMSsecs: true, withNanoSecs: true), cancel);

                        // Snapshot テーブルを追記する。
                        await this.AtomicAddSnapshotInternalAsync(EnsureInternal.Yes, snap, cancel);
                    }
                }
            }
            else
            {
                throw new CoresLibException("Transaction already began.");
            }
        }

        public void CheckIsWriteMode()
        {
            if (this.IsWriteMode == false)
            {
                throw new CoresLibException("Database transaction is read only.");
            }
        }

        public void AddApplyObject(HadbObject obj)
            => AddApplyObjects(obj._SingleArray());

        public void AddApplyObjects(IEnumerable<HadbObject> objs)
        {
            List<HadbObject> tmp = new List<HadbObject>();

            foreach (var obj in objs)
            {
                obj.CheckIsNotMemoryDbObject();
                tmp.Add(obj.CloneObject());
            }

            this.ApplyObjectsList.AddRange(tmp);
        }

        Once CommitedFlag = new Once();

        public async Task CommitAsync(CancellationToken cancel = default)
        {
            if (finished == false)
            {
                if (CommitedFlag.IsFirstCall())
                {
                    if (this.IsWriteMode)
                    {
                        await this.CommitImplAsync(cancel);

                        await FinishInternalAsync(cancel);
                    }
                }
                else
                {
                    throw new CoresLibException("Transaction already committed.");
                }
            }
            else
            {
                throw new CoresLibException("Transaction already finished.");
            }
        }

        Once finished = new Once();

        async Task FinishInternalAsync(CancellationToken cancel = default)
        {
            if (finished.IsFirstCall())
            {
                using (await this.MemDb.CriticalLockAsync.LockWithAwait(cancel))
                {
                    foreach (var obj in this.ApplyObjectsList)
                    {
                        try
                        {
                            this.MemDb.ApplyObjectToMemDb_Critical(obj);
                        }
                        catch (Exception ex)
                        {
                            ex._Debug();
                        }
                    }
                }
            }
        }

        protected override async Task CleanupImplAsync(Exception? ex)
        {
            try
            {
                if (this.IsWriteMode == false)
                {
                    await this.FinishInternalAsync();
                }
            }
            finally
            {
                await base.CleanupImplAsync(ex);
            }
        }

        public async Task<HadbObject<T>> AtomicAddAsync<T>(T data, string nameSpace = Consts.Strings.HadbDefaultNameSpace, string ext1 = "", string ext2 = "", CancellationToken cancel = default)
            where T : HadbData
            => (await AtomicAddAsync(data._SingleArray(), nameSpace, ext1, ext2, cancel)).Single();

        public async Task<List<HadbObject>> AtomicAddAsync(IEnumerable<HadbData> dataList, string nameSpace = Consts.Strings.HadbDefaultNameSpace, string ext1 = "", string ext2 = "", CancellationToken cancel = default)
        {
            nameSpace = nameSpace._HadbNameSpaceNormalize();

            CheckBegan();
            Hadb.CheckIfReady();

            List<HadbObject> objList = new List<HadbObject>();

            foreach (var _data in dataList)
            {
                var data = _data;

                data._NullCheck(nameof(data));

                data = data._CloneDeep();
                data.Normalize();

                var keys = data.GetKeys();

                if (Hadb.DebugFlags.Bit(HadbDebugFlags.NoCheckMemKeyDuplicate) == false)
                {
                    var existingList = this.MemDb!.IndexedKeysTable_SearchByKeys(keys, data.GetUserDataTypeName(), nameSpace);

                    if (existingList.Any())
                    {
                        throw new CoresLibException($"Duplicated key in the memory database. Namespace = {nameSpace}, Keys = {keys._ObjectToJson(compact: true)}");
                    }
                }

                objList.Add(data.ToNewObject(this.CurrentSnapNoForWriteMode, nameSpace, ext1, ext2));
            }

            await Hadb.AtomicAddDataListToDatabaseImplAsync(this, objList, cancel);

            this.AddApplyObjects(objList);

            return objList;
        }

        public async Task<HadbObject<T>?> AtomicGetAsync<T>(string uid, string nameSpace = Consts.Strings.HadbDefaultNameSpace, CancellationToken cancel = default) where T : HadbData
            => await AtomicGetAsync(uid, typeof(T).Name, nameSpace, cancel);

        public async Task<HadbObject?> AtomicGetAsync(string uid, string typeName, string nameSpace = Consts.Strings.HadbDefaultNameSpace, CancellationToken cancel = default)
        {
            nameSpace = nameSpace._HadbNameSpaceNormalize();

            CheckBegan();
            Hadb.CheckIfReady();

            HadbObject? ret = await Hadb.AtomicGetDataFromDatabaseImplAsync(this, uid, typeName, nameSpace, cancel);

            if (ret == null) return null;

            this.AddApplyObject(ret);

            if (ret.Deleted) return null;

            return ret;
        }

        public async Task<IEnumerable<HadbObject<T>>> AtomicGetArchivedAsync<T>(string uid, int maxItems = int.MaxValue, string nameSpace = Consts.Strings.HadbDefaultNameSpace, CancellationToken cancel = default) where T : HadbData
            => (await AtomicGetArchivedAsync(uid, typeof(T).Name, maxItems, nameSpace, cancel)).Select(x => x.GetGenerics<T>());

        public async Task<IEnumerable<HadbObject>> AtomicGetArchivedAsync(string uid, string typeName, int maxItems = int.MaxValue, string nameSpace = Consts.Strings.HadbDefaultNameSpace, CancellationToken cancel = default)
        {
            nameSpace = nameSpace._HadbNameSpaceNormalize();

            CheckBegan();
            Hadb.CheckIfReady();

            return await Hadb.AtomicGetArchivedDataFromDatabaseImplAsync(this, maxItems, uid, typeName, nameSpace, cancel);
        }

        public async Task<HadbObject> AtomicUpdateAsync(HadbObject obj, CancellationToken cancel = default)
        {
            CheckBegan();
            Hadb.CheckIfReady();

            obj._NullCheck(nameof(obj));

            obj.CheckIsNotMemoryDbObject();
            obj.Normalize();

            var keys = obj.GetKeys();

            if (Hadb.DebugFlags.Bit(HadbDebugFlags.NoCheckMemKeyDuplicate) == false)
            {
                var existingList = this.MemDb!.IndexedKeysTable_SearchByKeys(keys, obj.GetUserDataTypeName(), obj.NameSpace);

                if (existingList.Where(x => x.Uid._IsSamei(obj.Uid) == false).Any())
                {
                    throw new CoresLibException($"Duplicated key in the memory database. Namespace = {obj.NameSpace}, Keys = {keys._ObjectToJson(compact: true)}");
                }
            }

            var obj2 = await Hadb.AtomicUpdateDataOnDatabaseImplAsync(this, obj, cancel);

            this.AddApplyObject(obj2);

            return obj2;
        }

        public async Task<HadbObject<T>?> AtomicSearchByKeyAsync<T>(T model, string nameSpace = Consts.Strings.HadbDefaultNameSpace, bool and = false, CancellationToken cancel = default) where T : HadbData
        {
            nameSpace = nameSpace._HadbNameSpaceNormalize();
            CheckBegan();
            model.Normalize();
            return await AtomicSearchByKeyAsync<T>(model.GetKeys(), nameSpace, and, cancel);
        }

        public async Task<HadbObject<T>?> AtomicSearchByKeyAsync<T>(HadbKeys keys, string nameSpace = Consts.Strings.HadbDefaultNameSpace, bool and = false, CancellationToken cancel = default) where T : HadbData
            => await AtomicSearchByKeyAsync(keys, typeof(T).Name, nameSpace, and, cancel);

        public async Task<HadbObject?> AtomicSearchByKeyAsync(HadbKeys keys, string typeName, string nameSpace = Consts.Strings.HadbDefaultNameSpace, bool and = false, CancellationToken cancel = default)
        {
            nameSpace = nameSpace._HadbNameSpaceNormalize();
            CheckBegan();
            Hadb.CheckIfReady();

            HadbObject? ret = await Hadb.AtomicSearchDataByKeyFromDatabaseImplAsync(this, keys, typeName, nameSpace, and, cancel);

            if (ret == null) return null;

            this.AddApplyObject(ret);

            if (ret.Deleted) return null;

            return ret;
        }

        public async Task<IEnumerable<HadbObject<T>>> AtomicSearchByLabelsAsync<T>(T model, string nameSpace = Consts.Strings.HadbDefaultNameSpace, CancellationToken cancel = default) where T : HadbData
        {
            nameSpace = nameSpace._HadbNameSpaceNormalize();
            model.Normalize();
            return (await AtomicSearchByLabelsAsync(model.GetLabels(), typeof(T).Name, nameSpace, cancel)).Select(x => x.GetGenerics<T>());
        }

        public async Task<IEnumerable<HadbObject<T>>> AtomicSearchByLabelsAsync<T>(HadbLabels labels, string nameSpace = Consts.Strings.HadbDefaultNameSpace, CancellationToken cancel = default) where T : HadbData
            => (await AtomicSearchByLabelsAsync(labels, typeof(T).Name, nameSpace, cancel)).Select(x => x.GetGenerics<T>());

        public async Task<IEnumerable<HadbObject>> AtomicSearchByLabelsAsync(HadbLabels labels, string typeName, string nameSpace = Consts.Strings.HadbDefaultNameSpace, CancellationToken cancel = default)
        {
            nameSpace = nameSpace._HadbNameSpaceNormalize();
            CheckBegan();
            Hadb.CheckIfReady();

            IEnumerable<HadbObject> items = await Hadb.AtomicSearchDataListByLabelsFromDatabaseImplAsync(this, labels, typeName, nameSpace, cancel);

            this.AddApplyObjects(items);

            return items.Where(x => x.Deleted == false);
        }

        public async Task<HadbObject<T>> AtomicDeleteByKeyAsync<T>(T model, string nameSpace = Consts.Strings.HadbDefaultNameSpace, bool and = false, CancellationToken cancel = default) where T : HadbData
        {
            CheckBegan();
            model.Normalize();
            return await AtomicDeleteByKeyAsync<T>(model.GetKeys(), nameSpace, model.GetMaxArchivedCount(), and, cancel);
        }

        public async Task<HadbObject<T>> AtomicDeleteByKeyAsync<T>(HadbKeys keys, string nameSpace = Consts.Strings.HadbDefaultNameSpace, int maxArchive = int.MaxValue, bool and = false, CancellationToken cancel = default) where T : HadbData
            => await AtomicDeleteByKeyAsync(keys, typeof(T).Name, nameSpace, maxArchive, and, cancel);

        public async Task<HadbObject> AtomicDeleteByKeyAsync(HadbKeys keys, string typeName, string nameSpace = Consts.Strings.HadbDefaultNameSpace, int maxArchive = int.MaxValue, bool and = false, CancellationToken cancel = default)
        {
            nameSpace = nameSpace._HadbNameSpaceNormalize();
            CheckBegan();
            var obj = await this.AtomicSearchByKeyAsync(keys, typeName, nameSpace, and, cancel);
            if (obj == null)
            {
                throw new CoresLibException($"Object not found. keys = {keys._ObjectToJson(compact: true)}");
            }

            return await AtomicDeleteAsync(obj.Uid, typeName, nameSpace, maxArchive, cancel);
        }

        public async Task<HadbObject<T>> AtomicDeleteAsync<T>(string uid, string nameSpace = Consts.Strings.HadbDefaultNameSpace, int maxArchive = int.MaxValue, CancellationToken cancel = default) where T : HadbData
            => await AtomicDeleteAsync(uid, typeof(T).Name, nameSpace, maxArchive, cancel);

        public async Task<HadbObject> AtomicDeleteAsync(string uid, string typeName, string nameSpace = Consts.Strings.HadbDefaultNameSpace, int maxArchive = int.MaxValue, CancellationToken cancel = default)
        {
            nameSpace = nameSpace._HadbNameSpaceNormalize();
            CheckBegan();
            Hadb.CheckIfReady();

            HadbObject ret = await Hadb.AtomicDeleteDataFromDatabaseImplAsync(this, uid, typeName, nameSpace, maxArchive, cancel);

            this.AddApplyObject(ret);

            return ret;
        }

        public async Task<string> AtomicGetKvAsync(string key, CancellationToken cancel = default)
        {
            CheckBegan();
            Hadb.CheckIfReady();

            string ret = await Hadb.AtomicGetKvImplAsync(this, key, cancel);

            return ret;
        }

        public async Task<StrDictionary<string>> AtomicGetKvListAsync(CancellationToken cancel = default)
        {
            CheckBegan();
            Hadb.CheckIfReady();

            return await Hadb.AtomicGetKvListImplAsync(this, cancel);
        }

        public async Task AtomicSetKvAsync(string key, string value, CancellationToken cancel = default)
        {
            CheckBegan();
            Hadb.CheckIfReady();

            await Hadb.AtomicSetKvImplAsync(this, key, value, cancel);
        }

        internal async Task AtomicAddSnapshotInternalAsync(EnsureInternal yes, HadbSnapshot snap, CancellationToken cancel = default)
        {
            CheckBegan();
            Hadb.CheckIfReady();

            await Hadb.AtomicAddSnapImplAsync(this, snap, cancel);
        }

        public async Task AtomicAddLogAsync(HadbLog log, string nameSpace = Consts.Strings.HadbDefaultNameSpace, string ext1 = "", string ext2 = "", CancellationToken cancel = default)
        {
            nameSpace = nameSpace._HadbNameSpaceNormalize();
            CheckBegan();
            Hadb.CheckIfReady();

            await Hadb.AtomicAddLogImplAsync(this, log, nameSpace, ext1, ext2, cancel);
        }

        public async Task<IEnumerable<HadbLog>> AtomicSearchLogAsync<T>(HadbLogQuery query, string nameSpace = Consts.Strings.HadbDefaultNameSpace, CancellationToken cancel = default) where T : HadbLog
            => await AtomicSearchLogAsync(typeof(T).Name, query, nameSpace, cancel);

        public async Task<IEnumerable<HadbLog>> AtomicSearchLogAsync(string typeName, HadbLogQuery query, string nameSpace = Consts.Strings.HadbDefaultNameSpace, CancellationToken cancel = default)
        {
            nameSpace = nameSpace._HadbNameSpaceNormalize();
            CheckBegan();
            Hadb.CheckIfReady();

            return await Hadb.AtomicSearchLogImplAsync(this, typeName, query, nameSpace, cancel);
        }

        //public async Task<bool> LazyUpdateAsync(HadbObject obj, CancellationToken cancel = default)
        //{
        //    CheckBegan();
        //    Hadb.CheckIfReady();
        //    if (obj.Deleted || obj.Archive) return false;
        //    obj.CheckIsNotMemoryDbObject();

        //    return await Hadb.LazyUpdateImplAsync(this, obj, cancel);
        //}
    }
}


#endif
