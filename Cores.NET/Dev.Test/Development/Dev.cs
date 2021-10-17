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
using System.Reflection;

namespace IPA.Cores.Basic
{
    public class MikakaDDnsHost : HadbData
    {
        public string HostName = "";
        public string IPv4Address = "";
        public string IPv6Address = "";
        public int TestInt = 0;

        protected override string GetKey1() => this.HostName;

        protected override void NormalizeImpl()
        {
            this.HostName = this.HostName._NonNullTrim().ToLower();
            this.IPv4Address = this.IPv4Address._NormalizeIp();
            this.IPv6Address = this.IPv6Address._NormalizeIp();
        }

    }

    public class MikakaDDnsDynamicConfig : HadbDynamicConfig
    {
        public int TestAbc;
        public string[] TestDef = new string[0];
        public string TestStr1 = "";
        public string TestStr2 = "";

        protected override void NormalizeImpl()
        {
            this.TestAbc = this.TestAbc._ZeroOrDefault(100, max: 1000);
        }
    }

    public class MikakaDDnsMem : HadbMemSqlBase<MikakaDDnsDynamicConfig>
    {
        protected override void ReloadAllTablesFromDatabaseCoreImpl(IEnumerable<HadbSqlDataRow> fetchedRows)
        {
            this.ReloadTableFromDatabase(this.HostTable, fetchedRows);
        }

        // --- Bulk 取得されるべき生テーブルデータ群 (バックアップファイルとして保存される生テーブルデータ群。TableLock でロックした上で読み書きすること！) ---
        public StrDictionary<MikakaDDnsHost> HostTable = new StrDictionary<MikakaDDnsHost>();

        // --- 上記データをもとにハッシュ化したメモリ上の高速アクセス可能なハッシュ化された中間データ。ロックなしで読み取りをして安全である。書き込みは禁止である。 ---
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

    public abstract class HadbMemSqlBase<TDynamicConfig> : HadbMemBase<TDynamicConfig> // 注意! ファイル保存するため、むやみに [JsonIgnore] を書かないこと！！
        where TDynamicConfig : HadbDynamicConfig
    {
        protected abstract void ReloadAllTablesFromDatabaseCoreImpl(IEnumerable<HadbSqlDataRow> fetchedRows);

        public void ReloadAllTablesFromDatabase(IEnumerable<HadbSqlDataRow> fetchedRows)
        {
            lock (this.TableLock)
            {
                this.ReloadAllTablesFromDatabaseCoreImpl(fetchedRows);
            }
        }

        protected void ReloadTableFromDatabase<TData>(StrDictionary<TData> table, IEnumerable<HadbSqlDataRow> fetchedRows)
            where TData: HadbData
        {
            int countInserted = 0;
            int countUpdated = 0;
            int countRemoved = 0;

            string typeName = typeof(TData).Name;

            var rows = fetchedRows.Where(x => x.DATA_TYPE._IsSamei(typeName));

            foreach (var row in rows)
            {
                bool insert = false;
                bool update = false;

                if (table.TryGetValue(row.DATA_UID, out TData? currentData))
                {
                    if (currentData.Hadb_Ver < row.DATA_VER)
                    {
                        update = true;
                    }
                }
                else
                {
                    insert = true;
                }

                if (insert || update)
                {
                    TData? a = row.DATA_VALUE._JsonToObject<TData>();
                    if (a != null)
                    {
                        if (a.Hadb_Deleted == false)
                        {
                            a.Hadb_Uid = row.DATA_UID;
                            a.Hadb_Ver = row.DATA_VER;
                            a.Hadb_Deleted = row.DATA_DELETED;
                            a.Hadb_CreateDt = row.DATA_CREATE_DT;
                            a.Hadb_DeleteDt = row.DATA_DELETE_DT;
                            a.Hadb_UpdateDt = row.DATA_UPDATE_DT;

                            a.Normalize();

                            table[row.DATA_UID] = a;

                            if (update)
                            {
                                countUpdated++;
                            }
                            else
                            {
                                countInserted++;
                            }
                        }
                        else
                        {
                            table.Remove(row.DATA_UID);
                            countRemoved++;
                        }
                    }
                }
            }

            //if (countInserted > 0 || countRemoved > 0 || countUpdated > 0)
            {
                Debug($"Update Local Memory from Database: TypeName={typeName}, New={countInserted._ToString3()}, Update={countUpdated._ToString3()}, Remove={countRemoved._ToString3()}");
            }
        }
    }

    public class HadbDynamicConfig : INormalizable
    {
        protected virtual void NormalizeImpl() { }

        public int HadbReloadIntervalMsecsLastOk;
        public int HadbReloadIntervalMsecsLastError;
        public int HadbLazyUpdateIntervalMsecs;
        public int HadbBackupFileWriteIntervalMsecs;
        public int HadbRecordStatIntervalMsecs;

        public HadbDynamicConfig()
        {
            this.Normalize();
        }

        public void Normalize()
        {
            this.HadbReloadIntervalMsecsLastOk = this.HadbReloadIntervalMsecsLastOk._ZeroOrDefault(Consts.HadbDynamicConfigDefaultValues.HadbReloadIntervalMsecsLastOk, max: Consts.HadbDynamicConfigMaxValues.HadbReloadIntervalMsecsLastOk);
            this.HadbReloadIntervalMsecsLastError = this.HadbReloadIntervalMsecsLastError._ZeroOrDefault(Consts.HadbDynamicConfigDefaultValues.HadbReloadIntervalMsecsLastError, max: Consts.HadbDynamicConfigMaxValues.HadbReloadIntervalMsecsLastError);
            this.HadbLazyUpdateIntervalMsecs = this.HadbLazyUpdateIntervalMsecs._ZeroOrDefault(Consts.HadbDynamicConfigDefaultValues.HadbLazyUpdateIntervalMsecs, max: Consts.HadbDynamicConfigMaxValues.HadbLazyUpdateIntervalMsecs);
            this.HadbBackupFileWriteIntervalMsecs = this.HadbBackupFileWriteIntervalMsecs._ZeroOrDefault(Consts.HadbDynamicConfigDefaultValues.HadbBackupFileWriteIntervalMsecs, max: Consts.HadbDynamicConfigMaxValues.HadbBackupFileWriteIntervalMsecs);
            this.HadbRecordStatIntervalMsecs = this.HadbRecordStatIntervalMsecs._ZeroOrDefault(Consts.HadbDynamicConfigDefaultValues.HadbRecordStatIntervalMsecs, max: Consts.HadbDynamicConfigMaxValues.HadbRecordStatIntervalMsecs);

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
                            currentArray = new string[0];
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

        public HadbSqlSettings(string systemName, string sqlConnectStringForRead, string sqlConnectStringForWrite) : base(systemName)
        {
            this.SqlConnectStringForRead = sqlConnectStringForRead;
            this.SqlConnectStringForWrite = sqlConnectStringForWrite;
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
            this.CONFIG_SYSTEMNAME = this.CONFIG_SYSTEMNAME._NonNullTrim();
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
        where TMem : HadbMemSqlBase<TDynamicConfig>, new()
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
            IEnumerable<HadbSqlConfigRow> configList = null!;
            IEnumerable<HadbSqlDataRow> dataList = null!;

            try
            {
                await using var dbReader = await this.OpenSqlDatabaseForReadAsync(cancel);

                await dbReader.TranReadSnapshotIfNecessaryAsync(async () =>
                {
                    configList = await dbReader.EasySelectAsync<HadbSqlConfigRow>("select * from HADB_CONFIG where CONFIG_SYSTEMNAME = @CONFIG_SYSTEMNAME", new { CONFIG_SYSTEMNAME = this.SystemName });
                    dataList = await dbReader.EasySelectAsync<HadbSqlDataRow>("select * from HADB_DATA where DATA_SYSTEMNAME = @DATA_SYSTEMNAME", new { DATA_SYSTEMNAME = this.SystemName }); // TODO: get only latest
                });
            }
            catch (Exception ex)
            {
                Debug($"ReloadImplAsync: Database Connect Error for Read: {ex.ToString()}");
                throw;
            }

            configList._NormalizeAll();
            dataList._NormalizeAll();

            // 取得した configList の内容を元に TDynamicConfig のデータを更新する
            KeyValueList<string, string> configListValuesFromDb = new KeyValueList<string, string>();
            foreach (var configRow in configList)
            {
                configListValuesFromDb.Add(configRow.CONFIG_NAME, configRow.CONFIG_VALUE);
            }
            var missingValues = this.CurrentDynamicConfig.UpdateFromDatabaseAndReturnMissingValues(configListValuesFromDb);

            if (missingValues.Any())
            {
                // DB にまだ存在しないが定義されるべき TDynamicConfig のフィールドがある場合は DB に初期値を書き込む
                await using var dbWriter = await this.OpenSqlDatabaseForWriteAsync(cancel);

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

            if (current == null)
            {
                current = new TMem();
            }

            current.ReloadAllTablesFromDatabase(dataList);

            return current;
        }

        protected async Task<HadbSqlDataRow?> GetRowByKeyAsync(Database db, string typeName, string key1, string key2, string key3, string key4, CancellationToken cancel = default)
        {
            List<string> conditions = new List<string>();

            if (key1._IsFilled()) conditions.Add("DATA_KEY1 = @DATA_KEY1");
            if (key2._IsFilled()) conditions.Add("DATA_KEY2 = @DATA_KEY2");
            if (key3._IsFilled()) conditions.Add("DATA_KEY3 = @DATA_KEY3");
            if (key4._IsFilled()) conditions.Add("DATA_KEY4 = @DATA_KEY4");

            if (conditions.Count == 0)
            {
                return null;
            }

            return await db.EasySelectSingleAsync<HadbSqlDataRow>($"select * from HADB_DATA where ({conditions._Combine(" or ")}) and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_DELETED = 0 and DATA_TYPE = @DATA_TYPE",
                new
                {
                    DATA_KEY1 = key1,
                    DATA_KEY2 = key2,
                    DATA_KEY3 = key3,
                    DATA_KEY4 = key4,
                    DATA_SYSTEMNAME = this.SystemName,
                    DATA_TYPE = typeName,
                },
                cancel: cancel);
        }

        protected override async Task CommitAddDataListImplAsync<T>(IEnumerable<T> dataList, CancellationToken cancel = default)
        {
            await using var dbWriter = await this.OpenSqlDatabaseForWriteAsync(cancel);

            await dbWriter.TranAsync(async () =>
            {
                foreach (T data in dataList)
                {
                    if (data.Hadb_Deleted) throw new CoresLibException("data.Deleted == true");

                    HadbSqlDataRow row = new HadbSqlDataRow
                    {
                        DATA_UID = data.Hadb_Uid,
                        DATA_SYSTEMNAME = this.SystemName,
                        DATA_TYPE = data.GetTypeName(),
                        DATA_VER = data.Hadb_Ver,
                        DATA_DELETED = data.Hadb_Deleted,
                        DATA_CREATE_DT = data.Hadb_CreateDt,
                        DATA_UPDATE_DT = data.Hadb_UpdateDt,
                        DATA_DELETE_DT = data.Hadb_DeleteDt,
                        DATA_KEY1 = data.GetKey1Value(),
                        DATA_KEY2 = data.GetKey2Value(),
                        DATA_KEY3 = data.GetKey3Value(),
                        DATA_KEY4 = data.GetKey4Value(),
                        DATA_VALUE = data._ObjectToJson(compact: true),
                        DATA_EXT = "",
                    };

                    // DB に書き込む前に KEY1 ～ KEY4 の重複を検査する
                    var existingRow = await GetRowByKeyAsync(dbWriter, row.DATA_TYPE, row.DATA_KEY1, row.DATA_KEY2, row.DATA_KEY3, row.DATA_KEY4, cancel);

                    if (existingRow != null)
                    {
                        throw new CoresLibException("Duplicated key in the database.");
                    }

                    // DB に書き込む
                    await dbWriter.EasyInsertAsync(row, cancel);
                }

                return true;
            });
        }
    }

    public abstract class HadbSettingsBase
    {
        public string SystemName { get; }

        public HadbSettingsBase(string systemName)
        {
            this.SystemName = systemName._NonNullTrim().ToUpper();
        }
    }

    public abstract class HadbData : INormalizable
    {
        [JsonIgnore]
        public string Hadb_Uid = "";

        [JsonIgnore]
        public long Hadb_Ver = 0;

        [JsonIgnore]
        public bool Hadb_Deleted = false;

        [JsonIgnore]
        public DateTimeOffset Hadb_CreateDt = Util.ZeroDateTimeOffsetValue;

        [JsonIgnore]
        public DateTimeOffset Hadb_UpdateDt = Util.ZeroDateTimeOffsetValue;

        [JsonIgnore]
        public DateTimeOffset Hadb_DeleteDt = Util.ZeroDateTimeOffsetValue;

        public string GetTypeName() => this.GetType().Name;
        public string GetUidPrefix() => this.GetTypeName().ToUpper();

        protected virtual void NormalizeImpl() { }

        protected virtual string GetKey1() => "";
        protected virtual string GetKey2() => "";
        protected virtual string GetKey3() => "";
        protected virtual string GetKey4() => "";

        public string GetKey1Value() => NormalizeKeyValue(GetKey1());
        public string GetKey2Value() => NormalizeKeyValue(GetKey2());
        public string GetKey3Value() => NormalizeKeyValue(GetKey3());
        public string GetKey4Value() => NormalizeKeyValue(GetKey4());

        static string NormalizeKeyValue(string src)
        {
            src = src._NonNullTrim().ToUpper();

            //if (src._IsEmpty())
            //{
            //    src = Consts.Strings.HadbDummyKeyPrefix + Str.GenRandStr();
            //    src = src.ToLower();
            //}

            return src;
        }

        public void Normalize()
        {
            if (this.Hadb_Uid._IsEmpty()) this.Hadb_Uid = Str.NewUid(this.GetUidPrefix(), '_');
            this.Hadb_Uid = this.Hadb_Uid._NonNullTrim().ToUpper();
            this.Hadb_CreateDt = this.Hadb_CreateDt._NormalizeDateTimeOffset();
            this.Hadb_UpdateDt = this.Hadb_UpdateDt._NormalizeDateTimeOffset();
            this.Hadb_DeleteDt = this.Hadb_DeleteDt._NormalizeDateTimeOffset();
            this.Hadb_Ver = Math.Max(this.Hadb_Ver, 1);

            this.NormalizeImpl();
        }
    }

    public abstract class HadbMemBase<TDynamicConfig> // 注意! ファイル保存するため、むやみに [JsonIgnore] を書かないこと！！
        where TDynamicConfig : HadbDynamicConfig
    {
        [JsonIgnore]
        public readonly CriticalSection<HadbMemBase<TDynamicConfig>> TableLock = new CriticalSection<HadbMemBase<TDynamicConfig>>(); // メモリ上のデータの読み書き用ロック

        public void Debug(string str)
        {
            string pos = Dbg.GetCurrentExecutingPositionInfoString();
            $"{pos}: {str}"._Debug();
        }

        // --- Bulk 取得されるべき生テーブルデータ群 (バックアップファイルとして保存される生テーブルデータ群。TableLock でロックした上で読み書きすること！) ---

        // --- 上記データをもとにハッシュ化したメモリ上の高速アクセス可能なハッシュ化された中間データ。ロックなしで読み取りをして安全である。書き込みは禁止である。 ---
        // 以下はすべてのフィールドに [JsonIgnore] を付けること！！
    }

    public abstract class HadbBase<TMem, TDynamicConfig> : AsyncService
        where TMem : HadbMemBase<TDynamicConfig>, new()
        where TDynamicConfig : HadbDynamicConfig
    {
        Task ReloadMainLoopTask;
        Task LazyUpdateMainLoopTask;

        public bool IsLoopStarted { get; private set; } = false;
        public int LastReloadTookMsecs { get; private set; } = 0;
        public bool IsDatabaseConnectedForReload { get; private set; } = false;
        public bool IsDatabaseConnectedForLazyWrite { get; private set; } = false;

        public HadbSettingsBase Settings { get; }
        public string SystemName => Settings.SystemName;

        public TDynamicConfig CurrentDynamicConfig { get; private set; }

        public TMem? MemDb { get; private set; } = null;

        protected abstract Task<TMem> ReloadImplAsync(TMem? current, CancellationToken cancel);
        protected abstract Task CommitAddDataListImplAsync<T>(IEnumerable<T> dataList, CancellationToken cancel = default) where T : HadbData;

        public HadbBase(HadbSettingsBase settings, TDynamicConfig dynamicConfig)
        {
            try
            {
                settings._NullCheck(nameof(settings));
                this.Settings = settings;

                dynamicConfig._NullCheck(nameof(dynamicConfig));
                this.CurrentDynamicConfig = dynamicConfig;
                this.CurrentDynamicConfig.Normalize();

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
            await TaskUtil.AwaitWithPollAsync(Timeout.Infinite, 100, () => this.IsLoopStarted, cancel, true);
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

                Debug($"ReloadMainLoopAsync: numCycle={numCycle}, numError={numError} End. Took time: {(endTick - startTick)._ToString3()} msecs.");

                int nextWaitTime = Util.GenRandInterval(ok ? this.CurrentDynamicConfig.HadbReloadIntervalMsecsLastOk : this.CurrentDynamicConfig.HadbReloadIntervalMsecsLastError);
                Debug($"ReloadMainLoopAsync: Waiting for {nextWaitTime._ToString3()} msecs for next DB read.");
                await cancel._WaitUntilCanceledAsync(nextWaitTime);
            }

            Debug($"ReloadMainLoopAsync: Finished.");
        }

        async Task LazyUpdateMainLoopAsync(CancellationToken cancel)
        {
            await Task.Yield();
            await TaskUtil.AwaitWithPollAsync(Timeout.Infinite, 100, () => this.IsLoopStarted, cancel, true);
        }

        public void Debug(string str)
        {
            $"{this.GetType().Name}: {str}"._Debug();
        }

        public void CheckIfReadyToCommit()
        {
            var ret = CheckIfReadyToCommit(doNotThrowError: EnsureSpecial.Yes);
            ret.ThrowIfException();
        }

        public ResultOrExeption<bool> CheckIfReadyToCommit(EnsureSpecial doNotThrowError)
        {
            if (this.IsDatabaseConnectedForReload == false)
            {
                return new CoresLibException("IsDatabaseConnectedForReload == false");
            }

            if (this.MemDb == null)
            {
                return new CoresLibException("MemDb is not loaded yet.");
            }

            return true;
        }

        public async Task WaitUntilReadyToCommitAsync(CancellationToken cancel = default)
        {
            await Task.Yield();
            await TaskUtil.AwaitWithPollAsync(Timeout.Infinite, 100, () => CheckIfReadyToCommit(doNotThrowError: EnsureSpecial.Yes).IsOk, cancel, true);
        }

        public async Task<T> CommitCreateDataAsync<T>(T data, CancellationToken cancel = default) where T : HadbData
            => (await CommitCreateDataAsync(data._SingleArray(), cancel)).Single();

        public async Task<List<T>> CommitCreateDataAsync<T>(IEnumerable<T> dataList, CancellationToken cancel = default) where T: HadbData
        {
            CheckIfReadyToCommit();

            List<T> dataList2 = new List<T>();

            foreach (var _data in dataList)
            {
                var data = _data;

                data._NullCheck(nameof(data));

                data = data._CloneDeep();
                data.Hadb_CreateDt = data.Hadb_UpdateDt = DtNow;
                data.Hadb_DeleteDt = ZeroDateTimeOffsetValue;
                data.Hadb_Uid = "";
                data.Normalize();

                dataList2.Add(data);
            }

            await this.CommitAddDataListImplAsync(dataList2, cancel);

            return dataList2;
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
    }
}

#endif

