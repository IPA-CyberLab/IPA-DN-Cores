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
            this.TestAbc = this.TestAbc._ZeroOrDefault(100, max: 1000);
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
    }

    public class HadbTest : HadbSqlBase<HadbTestMem, HadbTestDynamicConfig>
    {
        public HadbTest(HadbSqlSettings settings, HadbTestDynamicConfig dynamicConfig) : base(settings, dynamicConfig) { }
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
        public long DATA_VER { get; set; } = 0;
        public bool DATA_DELETED { get; set; } = false;
        public long DATA_ARCHIVE_AGE { get; set; } = 0;
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

        public void Normalize()
        {
            this.DATA_UID = this.DATA_UID._NormalizeUid(true);
            this.DATA_SYSTEMNAME = this.DATA_SYSTEMNAME._NormalizeKey(true);
            this.DATA_TYPE = this.DATA_TYPE._NonNullTrim();
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
        {
            Microsoft.Data.SqlClient.SqlException? sqlEx = ex as Microsoft.Data.SqlClient.SqlException;

            if (sqlEx != null)
            {
                if (sqlEx.Number == 1205)
                {
                    return true;
                }
            }

            return false;
        }

        public async Task<Database> OpenSqlDatabaseAsync(bool writeMode, CancellationToken cancel = default)
        {
            Database db = new Database(
                writeMode ? this.Settings.SqlConnectStringForWrite : this.Settings.SqlConnectStringForRead,
                defaultIsolationLevel: writeMode ? IsolationLevel.Serializable : IsolationLevel.Snapshot);

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

        protected override async Task<List<HadbObject>> ReloadDataFromDatabaseImplAsync(CancellationToken cancel = default)
        {
            IEnumerable<HadbSqlDataRow> rowList = null!;

            try
            {
                await using var dbReader = await this.OpenSqlDatabaseAsync(false, cancel);

                await dbReader.TranReadSnapshotIfNecessaryAsync(async () =>
                {
                    rowList = await dbReader.EasySelectAsync<HadbSqlDataRow>("select * from HADB_DATA where DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_ARCHIVE_AGE = 0", new { DATA_SYSTEMNAME = this.SystemName }); // TODO: get only latest
                });
            }
            catch (Exception ex)
            {
                Debug($"ReloadDataFromDatabaseImplAsync: Database Connect Error for Read: {ex.ToString()}");
                throw;
            }

            rowList._NormalizeAll();

            List<HadbObject> ret = new List<HadbObject>();

            foreach (var row in rowList)
            {
                Type? type = this.GetTypeByTypeName(row.DATA_TYPE);
                if (type != null)
                {
                    HadbData? data = (HadbData?)row.DATA_VALUE._JsonToObject(type);
                    if (data != null)
                    {
                        HadbObject obj = new HadbObject(data, row.DATA_EXT1, row.DATA_EXT2, row.DATA_UID, row.DATA_VER, 0, row.DATA_DELETED, row.DATA_CREATE_DT, row.DATA_UPDATE_DT, row.DATA_DELETE_DT);

                        ret.Add(obj);
                    }
                }
            }

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

                HadbSqlTran ret = new HadbSqlTran(writeMode, isTransaction, this, db);

                return ret;
            }
            catch (Exception ex)
            {
                await db._DisposeSafeAsync(ex);
                throw;
            }
        }

        protected async Task<HadbSqlDataRow?> GetRowByKeyAsync(Database db, string typeName, HadbKeys key, CancellationToken cancel = default, string? excludeUid = null)
        {
            excludeUid = excludeUid._NormalizeKey(true);

            List<string> conditions = new List<string>();

            if (key.Key1._IsFilled()) conditions.Add("DATA_KEY1 = @DATA_KEY1");
            if (key.Key2._IsFilled()) conditions.Add("DATA_KEY2 = @DATA_KEY2");
            if (key.Key3._IsFilled()) conditions.Add("DATA_KEY3 = @DATA_KEY3");
            if (key.Key4._IsFilled()) conditions.Add("DATA_KEY4 = @DATA_KEY4");

            if (conditions.Count == 0)
            {
                return null;
            }

            string where = $"select * from HADB_DATA where (({conditions._Combine(" or ")}) and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_DELETED = 0 and DATA_ARCHIVE_AGE = 0 and DATA_TYPE = @DATA_TYPE) ";

            if (excludeUid._IsFilled())
            {
                where += "and (DATA_UID != @DATA_UID) ";
            }

            return await db.EasySelectSingleAsync<HadbSqlDataRow>(where,
                new
                {
                    DATA_KEY1 = key.Key1,
                    DATA_KEY2 = key.Key2,
                    DATA_KEY3 = key.Key3,
                    DATA_KEY4 = key.Key4,
                    DATA_SYSTEMNAME = this.SystemName,
                    DATA_TYPE = typeName,
                    DATA_UID = excludeUid,
                },
                cancel: cancel);
        }

        protected async Task<IEnumerable<HadbSqlDataRow>> GetRowsByLabelsAsync(Database db, string typeName, HadbLabels labels, CancellationToken cancel = default)
        {
            List<string> conditions = new List<string>();

            if (labels.Label1._IsFilled()) conditions.Add("DATA_LABEL1 = @DATA_LABEL1");
            if (labels.Label2._IsFilled()) conditions.Add("DATA_LABEL2 = @DATA_LABEL2");
            if (labels.Label3._IsFilled()) conditions.Add("DATA_LABEL3 = @DATA_LABEL3");
            if (labels.Label4._IsFilled()) conditions.Add("DATA_LABEL4 = @DATA_LABEL4");

            if (conditions.Count == 0)
            {
                return EmptyOf<HadbSqlDataRow>();
            }

            return await db.EasySelectAsync<HadbSqlDataRow>($"select * from HADB_DATA where ({conditions._Combine(" and ")}) and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_DELETED = 0 and DATA_ARCHIVE_AGE = 0 and DATA_TYPE = @DATA_TYPE",
                new
                {
                    DATA_LABEL1 = labels.Label1,
                    DATA_LABEL2 = labels.Label2,
                    DATA_LABEL3 = labels.Label3,
                    DATA_LABEL4 = labels.Label4,
                    DATA_SYSTEMNAME = this.SystemName,
                    DATA_TYPE = typeName,
                },
                cancel: cancel);
        }

        protected async Task<HadbSqlDataRow?> GetRowByUidAsync(Database db, string typeName, string uid, CancellationToken cancel = default)
        {
            List<string> conditions = new List<string>();

            uid = uid._NormalizeUid(true);

            if (uid._IsEmpty()) return null;

            return await db.EasySelectSingleAsync<HadbSqlDataRow>($"select * from HADB_DATA where DATA_UID = @DATA_UID and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_DELETED = 0 and DATA_ARCHIVE_AGE = 0 and DATA_TYPE = @DATA_TYPE",
                new
                {
                    DATA_UID = uid,
                    DATA_SYSTEMNAME = this.SystemName,
                    DATA_TYPE = typeName,
                },
                cancel: cancel);
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
                    DATA_ARCHIVE_AGE = 0,
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
                    DATA_EXT1 = "",
                    DATA_EXT2 = "",
                    DATA_LAZY_COUNT1 = 0,
                    DATA_LAZY_COUNT2 = 0,
                };

                // DB に書き込む前に DB 上で KEY1 ～ KEY4 の重複を検査する
                var existingRow = await GetRowByKeyAsync(dbWriter, row.DATA_TYPE, keys, cancel);

                if (existingRow != null)
                {
                    throw new CoresLibException($"Duplicated key in the physical database. Keys = {keys._ObjectToJson(compact: true)}");
                }

                // DB に書き込む
                await dbWriter.EasyInsertAsync(row, cancel);
            }
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
                "where DATA_UID = @DATA_UID and DATA_VER = @DATA_VER and DATA_UPDATE_DT < @DATA_UPDATE_DT and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_TYPE = @DATA_TYPE and DATA_ARCHIVE_AGE = 0 and DATA_DELETED = 0 and " +
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
            data.CheckIsNotMemoryDbObject();
            tran.CheckIsWriteMode();
            var dbWriter = ((HadbSqlTran)tran).Db;

            string typeName = data.GetUserDataTypeName();

            var keys = data.GetKeys();

            var labels = data.GetLabels();

            if (data.Deleted) throw new CoresLibException("data.Deleted == true");

            // 現在のデータを取得
            var row = await this.GetRowByUidAsync(dbWriter, typeName, data.Uid, cancel);
            if (row == null)
            {
                // 現在のデータがない
                throw new CoresLibException($"No data existing in the physical database. Uid = {data.Uid}, TypeName = {typeName}");
            }

            // 現在のアーカイブを一段繰り上げる
            await dbWriter.EasyExecuteAsync("update HADB_DATA set DATA_ARCHIVE_AGE = DATA_ARCHIVE_AGE + 1 where DATA_UID like @DATA_UID and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_TYPE = @DATA_TYPE and DATA_ARCHIVE_AGE >= 1",
                new
                {
                    DATA_UID = data.Uid + ":%",
                    DATA_SYSTEMNAME = this.SystemName,
                    DATA_TYPE = typeName,
                });

            // 現在のデータをアーカイブ化する
            HadbSqlDataRow rowOld = row._CloneDeep();

            rowOld.DATA_UID += ":" + rowOld.DATA_VER.ToString("D20");
            rowOld.DATA_ARCHIVE_AGE = 1;
            rowOld.Normalize();

            await dbWriter.EasyInsertAsync(rowOld, cancel);

            // DB に書き込む前に DB 上で KEY1 ～ KEY4 の重複を検査する (当然、更新しようとしている自分自身への重複は例外的に許可する)
            var existingRow = await GetRowByKeyAsync(dbWriter, typeName, keys, cancel, excludeUid: row.DATA_UID);

            if (existingRow != null)
            {
                throw new CoresLibException($"Duplicated key in the physical database. Keys = {keys._ObjectToJson(compact: true)}");
            }

            // データの内容を更新する
            row.DATA_VER++;
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

            await dbWriter.EasyUpdateAsync(row, true, cancel);

            return new HadbObject(this.JsonToHadbData(row.DATA_VALUE, typeName), row.DATA_EXT1, row.DATA_EXT2, row.DATA_UID, row.DATA_VER, row.DATA_ARCHIVE_AGE, row.DATA_DELETED, row.DATA_CREATE_DT, row.DATA_UPDATE_DT, row.DATA_DELETE_DT);
        }

        protected internal override async Task<HadbObject?> AtomicGetDataFromDatabaseImplAsync(HadbTran tran, string uid, string typeName, CancellationToken cancel = default)
        {
            typeName = typeName._NonNullTrim();

            var dbReader = ((HadbSqlTran)tran).Db;

            HadbSqlDataRow? row = await GetRowByUidAsync(dbReader, typeName, uid, cancel);

            if (row == null) return null;

            HadbObject ret = new HadbObject(this.JsonToHadbData(row.DATA_VALUE, typeName), row.DATA_EXT1, row.DATA_EXT2, row.DATA_UID, row.DATA_VER, row.DATA_ARCHIVE_AGE, row.DATA_DELETED, row.DATA_CREATE_DT, row.DATA_UPDATE_DT, row.DATA_DELETE_DT);

            return ret;
        }

        protected internal override async Task<HadbObject?> AtomicSearchDataByKeyFromDatabaseImplAsync(HadbTran tran, HadbKeys key, string typeName, CancellationToken cancel = default)
        {
            typeName = typeName._NonNullTrim();

            var dbReader = ((HadbSqlTran)tran).Db;

            var row = await GetRowByKeyAsync(dbReader, typeName, key, cancel);

            if (row == null) return null;

            HadbObject ret = new HadbObject(this.JsonToHadbData(row.DATA_VALUE, typeName), row.DATA_EXT1, row.DATA_EXT2, row.DATA_UID, row.DATA_VER, row.DATA_ARCHIVE_AGE, row.DATA_DELETED, row.DATA_CREATE_DT, row.DATA_UPDATE_DT, row.DATA_DELETE_DT);

            return ret;
        }

        protected internal override async Task<IEnumerable<HadbObject>> AtomicSearchDataListByLabelsFromDatabaseImplAsync(HadbTran tran, HadbLabels labels, string typeName, CancellationToken cancel = default)
        {
            typeName = typeName._NonNullTrim();

            var dbReader = ((HadbSqlTran)tran).Db;

            IEnumerable<HadbSqlDataRow> rows = await GetRowsByLabelsAsync(dbReader, typeName, labels, cancel);

            if (rows == null) return EmptyOf<HadbObject>();

            List<HadbObject> ret = new List<HadbObject>();

            foreach (var row in rows)
            {
                var item = new HadbObject(this.JsonToHadbData(row.DATA_VALUE, typeName), row.DATA_EXT1, row.DATA_EXT2, row.DATA_UID, row.DATA_VER, row.DATA_ARCHIVE_AGE, row.DATA_DELETED, row.DATA_CREATE_DT, row.DATA_UPDATE_DT, row.DATA_DELETE_DT);

                ret.Add(item);
            }

            return ret;
        }

        protected internal override async Task<HadbObject?> AtomicDeleteDataFromDatabaseImplAsync(HadbTran tran, string uid, string typeName, CancellationToken cancel = default)
        {
            typeName = typeName._NonNullTrim();
            uid = uid._NormalizeUid(true);

            tran.CheckIsWriteMode();
            var dbWriter = ((HadbSqlTran)tran).Db;

            HadbSqlDataRow? row = await GetRowByUidAsync(dbWriter, typeName, uid, cancel);

            if (row == null)
            {
                return null;
            }

            // 現在のアーカイブを一段繰り上げる
            await dbWriter.EasyExecuteAsync("update HADB_DATA set DATA_ARCHIVE_AGE = DATA_ARCHIVE_AGE + 1 where DATA_UID like @DATA_UID and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_TYPE = @DATA_TYPE and DATA_ARCHIVE_AGE >= 1",
                new
                {
                    DATA_UID = uid + ":%",
                    DATA_SYSTEMNAME = this.SystemName,
                    DATA_TYPE = typeName,
                });

            // 現在のデータをアーカイブ化する
            HadbSqlDataRow rowOld = row._CloneDeep();

            rowOld.DATA_UID += ":" + rowOld.DATA_VER.ToString("D20");
            rowOld.DATA_ARCHIVE_AGE = 1;
            rowOld.Normalize();

            await dbWriter.EasyInsertAsync(rowOld, cancel);

            // データを削除済みにする
            row.DATA_VER++;
            row.DATA_UPDATE_DT = row.DATA_DELETE_DT = DtOffsetNow;
            row.DATA_DELETED = true;
            row.DATA_LAZY_COUNT1 = 0;

            await dbWriter.EasyUpdateAsync(row, true, cancel);

            return new HadbObject(this.JsonToHadbData(row.DATA_VALUE, typeName), row.DATA_EXT1, row.DATA_EXT2, row.DATA_UID, row.DATA_VER, row.DATA_ARCHIVE_AGE, row.DATA_DELETED, row.DATA_CREATE_DT, row.DATA_UPDATE_DT, row.DATA_DELETE_DT);
        }





        public class HadbSqlTran : HadbTran
        {
            public Database Db { get; }

            public HadbSqlTran(bool writeMode, bool isTransaction, HadbSqlBase<TMem, TDynamicConfig> hadbSql, Database db) : base(writeMode, isTransaction, hadbSql)
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

    public abstract class HadbSettingsBase
    {
        public string SystemName { get; }
        public bool Debug_NoAutoDbUpdate { get; init; }

        public HadbSettingsBase(string systemName)
        {
            this.SystemName = systemName._NonNullTrim().ToUpper();
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

    public abstract class HadbData : INormalizable
    {
        public virtual HadbKeys GetKeys() => new HadbKeys("");
        public virtual HadbLabels GetLabels() => new HadbLabels("");

        public abstract void Normalize();

        public HadbObject ToNewObject() => new HadbObject(this);

        public static implicit operator HadbObject(HadbData data) => data.ToNewObject();

        public Type GetUserDataType() => this.GetType();
        public string GetUserDataTypeName() => this.GetType().Name;
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

        public long ArchiveAge { get; private set; }

        public HadbData UserData { get; private set; }

        public string Ext1 { get; private set; }

        public string Ext2 { get; private set; }

        public long InternalFastUpdateVersion { get; private set; }

        public string GetUserDataJsonString() => this.UserData.GetUserDataJsonString();

        public HadbObject(HadbData userData, string ext1 = "", string ext2 = "") : this(userData, ext1, ext2, Str.NewUid(userData.GetUserDataTypeName(), '_'), 1, 0, false, DtOffsetNow, DtOffsetNow, DtOffsetZero) { }

        public HadbObject(HadbData userData, string ext1, string ext2, string uid, long ver, long archiveAge, bool deleted, DateTimeOffset createDt, DateTimeOffset updateDt, DateTimeOffset deleteDt, HadbMemDataBase? memDb = null)
        {
            this.Uid = uid._NormalizeUid(true);

            if (this.Uid._IsEmpty())
            {
                throw new CoresLibException("uid is empty.");
            }

            userData._NullCheck(nameof(userData));

            this.UserData = userData._CloneDeep();

            this.Ver = Math.Max(ver, 1);
            this.ArchiveAge = Math.Max(archiveAge, 0);
            this.Deleted = deleted;
            this.CreateDt = createDt._NormalizeDateTimeOffset();
            this.UpdateDt = updateDt._NormalizeDateTimeOffset();
            this.DeleteDt = deleteDt._NormalizeDateTimeOffset();
            this.Ext1 = ext1;
            this.Ext2 = ext2;

            this.MemDb = memDb;
            if (this.MemDb != null)
            {
                if (this.ArchiveAge != 0) throw new CoresLibException("this.ArchiveAge != 0");
            }

            this.Normalize();
        }

        public HadbObject ToMemoryDbObject(HadbMemDataBase memDb)
        {
            memDb._NullCheck();

            CheckIsNotMemoryDbObject();
            if (this.ArchiveAge != 0) throw new CoresLibException("this.ArchiveAge != 0");

            return new HadbObject(this.UserData, this.Ext1, this.Ext2, this.Uid, this.Ver, this.ArchiveAge, this.Deleted, this.CreateDt, this.UpdateDt, this.DeleteDt, memDb);
        }

        public HadbObject ToNonMemoryDbObject()
        {
            CheckIsMemoryDbObject();

            lock (this.Lock)
            {
                var q = new HadbObject(this.UserData, this.Ext1, this.Ext2, this.Uid, this.Ver, this.ArchiveAge, this.Deleted, this.CreateDt, this.UpdateDt, this.DeleteDt);

                q.InternalFastUpdateVersion = this.InternalFastUpdateVersion;

                return q;
            }
        }

        public HadbObject CloneObject()
        {
            CheckIsNotMemoryDbObject();
            return new HadbObject(this.UserData, this.Ext1, this.Ext2, this.Uid, this.Ver, this.ArchiveAge, this.Deleted, this.CreateDt, this.UpdateDt, this.DeleteDt);
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
                if (this.ArchiveAge != 0) throw new CoresLibException($"this.ArchiveAge == {this.ArchiveAge}");

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

                if (this.ArchiveAge != 0)
                {
                    throw new CoresLibException($"this.ArchiveAge == {this.ArchiveAge}");
                }

                if (newObj.ArchiveAge != 0)
                {
                    throw new CoresLibException($"obj.ArchiveAge == {newObj.ArchiveAge}");
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
                    this.Ver = newObj.Ver;
                }
                else
                {
                    oldKeys = default;
                    oldLabels = default;
                }

                return update;
            }
        }

        public Type GetUserDataType() => this.UserData.GetUserDataType();
        public string GetUserDataTypeName() => this.UserData.GetUserDataTypeName();
        public string GetUidPrefix() => this.GetUserDataTypeName().ToUpper();

        public HadbKeys GetKeys() => this.Deleted == false ? this.UserData.GetKeys() : new HadbKeys("");
        public HadbLabels GetLabels() => this.Deleted == false ? this.UserData.GetLabels() : new HadbLabels("");

        public T GetData<T>() where T : HadbData
            => (T)this.UserData;

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
        protected abstract List<Type> GetDefinedUserDataTypesImpl();

        internal readonly StrDictionary<HadbObject> _AllObjectsDict = new StrDictionary<HadbObject>(StrComparer.IgnoreCaseComparer);
        public readonly AsyncLock CriticalAsyncLock = new AsyncLock();

        internal ImmutableDictionary<string, HadbObject> _IndexedKeysTable = ImmutableDictionary<string, HadbObject>.Empty.WithComparers(StrComparer.IgnoreCaseComparer);
        internal ImmutableDictionary<string, ConcurrentHashSet<HadbObject>> _IndexedLabelsTable = ImmutableDictionary<string, ConcurrentHashSet<HadbObject>>.Empty.WithComparers(StrComparer.IgnoreCaseComparer);

        readonly CriticalSection<HadbMemDataBase> LazyUpdateQueueLock = new CriticalSection<HadbMemDataBase>();
        internal ImmutableDictionary<HadbObject, int> _LazyUpdateQueue = ImmutableDictionary<HadbObject, int>.Empty;

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

        public async Task ReloadFromDatabaseAsync(IEnumerable<HadbObject> objectList, CancellationToken cancel = default)
        {
            int countInserted = 0;
            int countUpdated = 0;
            int countRemoved = 0;

            using (await this.CriticalAsyncLock.LockWithAwait(cancel))
            {
                foreach (var newObj in objectList)
                {
                    try
                    {
                        if (this._AllObjectsDict.TryGetValue(newObj.Uid, out HadbObject? currentObj))
                        {
                            if (currentObj.Internal_UpdateIfNew(EnsureSpecial.Yes, newObj, out HadbKeys oldKeys, out HadbLabels oldLabels))
                            {
                                if (currentObj.Deleted == false)
                                {
                                    countUpdated++;
                                }
                                else
                                {
                                    countRemoved++;
                                }

                                IndexedTable_UpdateObject_Critical(currentObj, oldKeys, oldLabels);
                            }
                        }
                        else
                        {
                            var obj2 = newObj.ToMemoryDbObject(this);
                            this._AllObjectsDict[newObj.Uid] = obj2;

                            IndexedTable_AddObject_Critical(obj2);
                            countInserted++;
                        }
                    }
                    catch (Exception ex)
                    {
                        ex._Debug();
                    }
                }
            }

            //if (countInserted > 0 || countRemoved > 0 || countUpdated > 0)
            {
                Debug($"Update Local Memory from Database: New={countInserted._ToString3()}, Update={countUpdated._ToString3()}, Remove={countRemoved._ToString3()}");
            }
        }

        public HadbObject ApplyObjectToMemDb_Critical(HadbObject newObj)
        {
            newObj.Normalize();
            if (newObj.ArchiveAge != 0) throw new CoresLibException("obj.ArchiveAge != 0");

            if (this._AllObjectsDict.TryGetValue(newObj.Uid, out HadbObject? currentObject))
            {
                if (currentObject.Internal_UpdateIfNew(EnsureSpecial.Yes, newObj, out HadbKeys oldKeys, out HadbLabels oldLabels))
                {
                    if (currentObject.Deleted == false)
                    {
                        IndexedTable_UpdateObject_Critical(currentObject, oldKeys, oldLabels);
                    }
                    else
                    {
                        IndexedTable_DeleteObject_Critical(currentObject, oldKeys, oldLabels);
                    }
                }

                return currentObject;
            }
            else
            {
                if (newObj.Deleted == false)
                {
                    var newObj2 = newObj.ToMemoryDbObject(this);
                    this._AllObjectsDict[newObj.Uid] = newObj2;
                    IndexedTable_AddObject_Critical(newObj2);
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

        void IndexedTable_DeleteObject_Critical(HadbObject obj, HadbKeys oldKeys, HadbLabels oldLabels)
        {
            obj.CheckIsMemoryDbObject();

            string typeName = obj.GetUserDataTypeName();

            IndexedKeysTable_DeleteInternal_Critical(HadbIndexColumn.Uid.ToString() + ":" + typeName + ":" + obj.Uid);

            if (oldKeys.Key1._IsFilled()) IndexedKeysTable_DeleteInternal_Critical(HadbIndexColumn.Key1.ToString() + ":" + typeName + ":" + oldKeys.Key1);
            if (oldKeys.Key2._IsFilled()) IndexedKeysTable_DeleteInternal_Critical(HadbIndexColumn.Key2.ToString() + ":" + typeName + ":" + oldKeys.Key2);
            if (oldKeys.Key3._IsFilled()) IndexedKeysTable_DeleteInternal_Critical(HadbIndexColumn.Key3.ToString() + ":" + typeName + ":" + oldKeys.Key3);
            if (oldKeys.Key4._IsFilled()) IndexedKeysTable_DeleteInternal_Critical(HadbIndexColumn.Key4.ToString() + ":" + typeName + ":" + oldKeys.Key4);

            if (oldLabels.Label1._IsFilled()) IndexedLabelsTable_DeleteInternal_Critical(HadbIndexColumn.Label1.ToString() + ":" + typeName + ":" + oldLabels.Label1, obj);
            if (oldLabels.Label2._IsFilled()) IndexedLabelsTable_DeleteInternal_Critical(HadbIndexColumn.Label2.ToString() + ":" + typeName + ":" + oldLabels.Label2, obj);
            if (oldLabels.Label3._IsFilled()) IndexedLabelsTable_DeleteInternal_Critical(HadbIndexColumn.Label3.ToString() + ":" + typeName + ":" + oldLabels.Label3, obj);
            if (oldLabels.Label4._IsFilled()) IndexedLabelsTable_DeleteInternal_Critical(HadbIndexColumn.Label4.ToString() + ":" + typeName + ":" + oldLabels.Label4, obj);
        }

        void IndexedTable_UpdateObject_Critical(HadbObject obj, HadbKeys oldKeys, HadbLabels oldLabels)
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

        void IndexedTable_AddObject_Critical(HadbObject obj)
        {
            obj.CheckIsMemoryDbObject();

            var newKeys = obj.GetKeys();
            var newLabels = obj.GetLabels();

            string typeName = obj.GetUserDataTypeName();

            IndexedKeysTable_AddOrUpdateInternal_Critical(HadbIndexColumn.Uid.ToString() + ":" + typeName + ":" + obj.Uid, obj);

            if (newKeys.Key1._IsFilled()) IndexedKeysTable_AddOrUpdateInternal_Critical(HadbIndexColumn.Key1.ToString() + ":" + typeName + ":" + newKeys.Key1, obj);
            if (newKeys.Key2._IsFilled()) IndexedKeysTable_AddOrUpdateInternal_Critical(HadbIndexColumn.Key2.ToString() + ":" + typeName + ":" + newKeys.Key2, obj);
            if (newKeys.Key3._IsFilled()) IndexedKeysTable_AddOrUpdateInternal_Critical(HadbIndexColumn.Key3.ToString() + ":" + typeName + ":" + newKeys.Key3, obj);
            if (newKeys.Key4._IsFilled()) IndexedKeysTable_AddOrUpdateInternal_Critical(HadbIndexColumn.Key4.ToString() + ":" + typeName + ":" + newKeys.Key4, obj);

            if (newLabels.Label1._IsFilled()) IndexedLabelsTable_AddInternal_Critical(HadbIndexColumn.Label1.ToString() + ":" + typeName + ":" + newLabels.Label1, obj);
            if (newLabels.Label2._IsFilled()) IndexedLabelsTable_AddInternal_Critical(HadbIndexColumn.Label2.ToString() + ":" + typeName + ":" + newLabels.Label2, obj);
            if (newLabels.Label3._IsFilled()) IndexedLabelsTable_AddInternal_Critical(HadbIndexColumn.Label3.ToString() + ":" + typeName + ":" + newLabels.Label3, obj);
            if (newLabels.Label4._IsFilled()) IndexedLabelsTable_AddInternal_Critical(HadbIndexColumn.Label4.ToString() + ":" + typeName + ":" + newLabels.Label4, obj);
        }

        void IndexedKeysTable_ReplaceObject_Critical(HadbObject obj, HadbIndexColumn column, string? oldKey, string? newKey)
        {
            obj.CheckIsMemoryDbObject();

            oldKey = oldKey._NormalizeKey(true);
            newKey = newKey._NormalizeKey(true);

            if (newKey._IsSamei(oldKey) == false)
            {
                if (newKey._IsFilled())
                {
                    IndexedKeysTable_AddOrUpdateInternal_Critical(column.ToString() + ":" + obj.GetUserDataTypeName() + ":" + newKey, obj);
                }

                if (oldKey._IsFilled())
                {
                    IndexedKeysTable_DeleteInternal_Critical(column.ToString() + ":" + obj.GetUserDataTypeName() + ":" + oldKey);
                }
            }
        }

        public HadbObject? IndexedKeysTable_SearchByUid(string uid, string typeName)
        {
            uid = uid._NormalizeKey(true);

            typeName = typeName._NonNullTrim();

            if (uid._IsEmpty()) return null;

            HadbObject? ret = IndexedKeysTable_SearchObject(HadbIndexColumn.Uid, typeName, uid);

            return ret;
        }

        public HadbObject? IndexedKeysTable_SearchByKey(HadbKeys key, string typeName)
        {
            typeName = typeName._NonNullTrim();

            HadbObject? ret;

            if (key.Key1._IsFilled())
            {
                ret = IndexedKeysTable_SearchObject(HadbIndexColumn.Key1, typeName, key.Key1);
                if (ret != null) return ret;
            }

            if (key.Key2._IsFilled())
            {
                ret = IndexedKeysTable_SearchObject(HadbIndexColumn.Key2, typeName, key.Key2);
                if (ret != null) return ret;
            }

            if (key.Key3._IsFilled())
            {
                ret = IndexedKeysTable_SearchObject(HadbIndexColumn.Key3, typeName, key.Key3);
                if (ret != null) return ret;
            }

            if (key.Key4._IsFilled())
            {
                ret = IndexedKeysTable_SearchObject(HadbIndexColumn.Key4, typeName, key.Key4);
                if (ret != null) return ret;
            }

            return null;
        }

        HadbObject? IndexedKeysTable_SearchObject(HadbIndexColumn column, string typeName, string key)
        {
            key = key._NormalizeKey(true);
            typeName = typeName._NonNullTrim();

            if (key._IsEmpty()) return null;

            return this._IndexedKeysTable._GetOrDefault(column.ToString() + ":" + typeName + ":" + key);
        }

        void IndexedKeysTable_AddOrUpdateInternal_Critical(string keyStr, HadbObject obj)
        {
            keyStr = keyStr._NormalizeKey(true);

            ImmutableInterlocked.AddOrUpdate(ref this._IndexedKeysTable, keyStr, obj, (k, old) => obj);
        }

        bool IndexedKeysTable_DeleteInternal_Critical(string keyStr)
        {
            keyStr = keyStr._NormalizeKey(true);

            return ImmutableInterlocked.TryRemove(ref this._IndexedKeysTable, keyStr, out _);
        }


        void IndexedLabelsTable_ReplaceObject_Critical(HadbObject obj, HadbIndexColumn column, string? oldLabel, string? newLabel)
        {
            obj.CheckIsMemoryDbObject();

            oldLabel = oldLabel._NormalizeKey(true);
            newLabel = newLabel._NormalizeKey(true);

            if (oldLabel._IsSamei(newLabel) == false)
            {
                string typeName = obj.GetUserDataTypeName();

                if (newLabel._IsFilled())
                {
                    IndexedLabelsTable_AddInternal_Critical(column.ToString() + ":" + typeName + ":" + newLabel, obj);
                }

                if (oldLabel._IsFilled())
                {
                    IndexedLabelsTable_DeleteInternal_Critical(column.ToString() + ":" + typeName + ":" + oldLabel, obj);
                }
            }
        }

        public IEnumerable<HadbObject> IndexedLabelsTable_SearchByLabels(HadbLabels labels, string typeName)
        {
            typeName = typeName._NonNullTrim();

            IEnumerable<HadbObject>? ret = null;

            IEnumerable<HadbObject>? tmp1 = null;
            IEnumerable<HadbObject>? tmp2 = null;
            IEnumerable<HadbObject>? tmp3 = null;
            IEnumerable<HadbObject>? tmp4 = null;

            if (labels.Label1._IsFilled())
            {
                tmp1 = IndexedLabelsTable_SearchObjects(HadbIndexColumn.Label1, typeName, labels.Label1);
                if (ret == null) ret = tmp1;
            }

            if (labels.Label2._IsFilled())
            {
                tmp2 = IndexedLabelsTable_SearchObjects(HadbIndexColumn.Label2, typeName, labels.Label2);
                if (ret == null) ret = tmp2;
            }

            if (labels.Label3._IsFilled())
            {
                tmp3 = IndexedLabelsTable_SearchObjects(HadbIndexColumn.Label3, typeName, labels.Label3);
                if (ret == null) ret = tmp3;
            }

            if (labels.Label4._IsFilled())
            {
                tmp4 = IndexedLabelsTable_SearchObjects(HadbIndexColumn.Label4, typeName, labels.Label4);
                if (ret == null) ret = tmp4;
            }

            if (ret == null) return EmptyOf<HadbObject>();

            if (tmp1 != null && object.ReferenceEquals(ret, tmp1) == false) ret = ret.Intersect(tmp1);
            if (tmp2 != null && object.ReferenceEquals(ret, tmp2) == false) ret = ret.Intersect(tmp2);
            if (tmp3 != null && object.ReferenceEquals(ret, tmp3) == false) ret = ret.Intersect(tmp3);
            if (tmp4 != null && object.ReferenceEquals(ret, tmp4) == false) ret = ret.Intersect(tmp4);

            return ret;
        }

        IEnumerable<HadbObject> IndexedLabelsTable_SearchObjects(HadbIndexColumn column, string typeName, string label)
        {
            label = label._NormalizeKey(true);
            typeName = typeName._NonNullTrim();

            if (label._IsEmpty()) return EmptyOf<HadbObject>();

            var list = this._IndexedLabelsTable._GetOrDefault(column.ToString() + ":" + typeName + ":" + label);
            if (list == null) return EmptyOf<HadbObject>();

            return list.Keys;
        }

        void IndexedLabelsTable_AddInternal_Critical(string labelKeyStr, HadbObject obj)
        {
            labelKeyStr = labelKeyStr._NormalizeKey(true);

            var list = ImmutableInterlocked.GetOrAdd(ref this._IndexedLabelsTable, labelKeyStr, k => new ConcurrentHashSet<HadbObject>());

            list.Add(obj);
        }

        bool IndexedLabelsTable_DeleteInternal_Critical(string labelKeyStr, HadbObject obj)
        {
            labelKeyStr = labelKeyStr._NormalizeKey(true);

            var list = this._IndexedLabelsTable._GetOrDefault(labelKeyStr);
            if (list == null) return false;

            if (list.Remove(obj) == false) return false;

            if (list.Count == 0)
            {
                ImmutableInterlocked.TryRemove(ref this._IndexedLabelsTable, labelKeyStr, out _);
            }

            return true;
        }
    }

    public abstract class HadbBase<TMem, TDynamicConfig> : AsyncService
        where TMem : HadbMemDataBase, new()
        where TDynamicConfig : HadbDynamicConfig
    {
        Task ReloadMainLoopTask;
        Task LazyUpdateMainLoopTask;

        public bool IsLoopStarted { get; private set; } = false;
        public int LastReloadTookMsecs { get; private set; } = 0;
        public int LastLazyUpdateTookMsecs { get; private set; } = 0;
        public bool IsDatabaseConnectedForReload { get; private set; } = false;
        public bool IsDatabaseConnectedForLazyWrite { get; private set; } = false;

        protected HadbSettingsBase Settings { get; }
        public string SystemName => Settings.SystemName;

        public TDynamicConfig CurrentDynamicConfig { get; private set; }

        public TMem? MemDb { get; private set; } = null;

        readonly AsyncLock DynamicConfigValueDbLockAsync = new AsyncLock();

        protected abstract Task<HadbTran> BeginDatabaseTransactionImplAsync(bool writeMode, bool isTransaction, CancellationToken cancel = default);

        protected internal abstract Task AtomicAddDataListToDatabaseImplAsync(HadbTran tran, IEnumerable<HadbObject> dataList, CancellationToken cancel = default);
        protected internal abstract Task<HadbObject?> AtomicGetDataFromDatabaseImplAsync(HadbTran tran, string uid, string typeName, CancellationToken cancel = default);
        protected internal abstract Task<HadbObject?> AtomicSearchDataByKeyFromDatabaseImplAsync(HadbTran tran, HadbKeys keys, string typeName, CancellationToken cancel = default);
        protected internal abstract Task<IEnumerable<HadbObject>> AtomicSearchDataListByLabelsFromDatabaseImplAsync(HadbTran tran, HadbLabels labels, string typeName, CancellationToken cancel = default);
        protected internal abstract Task<HadbObject?> AtomicDeleteDataFromDatabaseImplAsync(HadbTran tran, string uid, string typeName, CancellationToken cancel = default);
        protected internal abstract Task<HadbObject> AtomicUpdateDataOnDatabaseImplAsync(HadbTran tran, HadbObject data, CancellationToken cancel = default);

        protected internal abstract Task<bool> LazyUpdateImplAsync(HadbTran tran, HadbObject data, CancellationToken cancel = default);

        protected abstract Task<KeyValueList<string, string>> LoadDynamicConfigFromDatabaseImplAsync(CancellationToken cancel = default);
        protected abstract Task AppendMissingDynamicConfigToDatabaseImplAsync(KeyValueList<string, string> missingValues, CancellationToken cancel = default);
        protected abstract Task<List<HadbObject>> ReloadDataFromDatabaseImplAsync(CancellationToken cancel = default);

        protected abstract bool IsDeadlockExceptionImpl(Exception ex);

        public StrDictionary<Type> DefinedDataTypesByName { get; }

        public DeadlockRetryConfig DefaultDeadlockRetryConfig { get; set; }

        public HadbBase(HadbSettingsBase settings, TDynamicConfig dynamicConfig)
        {
            try
            {
                settings._NullCheck(nameof(settings));
                this.Settings = settings._CloneDeep();

                dynamicConfig._NullCheck(nameof(dynamicConfig));
                this.CurrentDynamicConfig = dynamicConfig;
                this.CurrentDynamicConfig.Normalize();

                this.ReloadMainLoopTask = ReloadMainLoopAsync(this.GrandCancel)._LeakCheck();
                this.LazyUpdateMainLoopTask = LazyUpdateMainLoopAsync(this.GrandCancel)._LeakCheck();

                TMem tmpMem = new TMem();
                this.DefinedDataTypesByName = tmpMem.GetDefinedUserDataTypesByName();

                this.DefaultDeadlockRetryConfig = new DeadlockRetryConfig(CoresConfig.Database.DefaultDatabaseTransactionRetryAverageIntervalSecs, CoresConfig.Database.DefaultDatabaseTransactionRetryCount);
            }
            catch
            {
                this._DisposeSafe();
                throw;
            }
        }

        public Type? GetTypeByTypeName(string name) => this.DefinedDataTypesByName._GetOrDefault(name);

        public Type GetTypeByTypeName(string name, EnsureSpecial notFoundError)
        {
            Type? ret = GetTypeByTypeName(name);

            if (ret == null)
            {
                throw new CoresException($"Type name '{name}' not found.");
            }

            return ret;
        }

        public HadbData JsonToHadbData(string json, string typeName)
        {
            Type t = GetTypeByTypeName(typeName, EnsureSpecial.Yes);

            HadbData? ret = (HadbData?)json._JsonToObject(t);
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
                        await this.AppendMissingDynamicConfigToDatabaseImplAsync(missingDynamicConfigValues, cancel);
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

        public async Task ReloadCoreAsync(EnsureSpecial yes, CancellationToken cancel = default)
        {
            try
            {
                // Dynamic Config の値の読み込み
                await ReloadDynamicConfigValuesAsync(cancel);

                using (await Lock_UpdateCoreAsync.LockWithAwait(cancel))
                {
                    // DB からオブジェクト一覧を読み込む
                    var loadedObjectsList = await this.ReloadDataFromDatabaseImplAsync(cancel);

                    TMem? currentMemDb = this.MemDb;
                    if (currentMemDb == null) currentMemDb = new TMem();

                    await currentMemDb.ReloadFromDatabaseAsync(loadedObjectsList, cancel);

                    this.MemDb = currentMemDb;
                }

                this.IsDatabaseConnectedForReload = true;
            }
            catch
            {
                this.IsDatabaseConnectedForReload = false;

                // データベースからもバックアップファイルからもまだデータが読み込まれていない場合は、バックアップファイルから読み込む
                if (this.MemDb == null)
                {
                    // TODOTODO
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

                try
                {
                    await ReloadCoreAsync(EnsureSpecial.Yes, cancel);
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

                if (this.Settings.Debug_NoAutoDbUpdate) break;
            }

            Debug($"ReloadMainLoopAsync: Finished.");
        }

        async Task LazyUpdateMainLoopAsync(CancellationToken cancel)
        {
            if (this.Settings.Debug_NoAutoDbUpdate) return;

            int numCycle = 0;
            int numError = 0;
            await Task.Yield();
            Debug($"LazyUpdateMainLoopAsync: Waiting for start.");
            await TaskUtil.AwaitWithPollAsync(Timeout.Infinite, 100, () => (this.CheckIfReady(EnsureSpecial.Yes).IsOk), cancel, true);
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

        public void CheckIfReady()
        {
            var ret = CheckIfReady(doNotThrowError: EnsureSpecial.Yes);
            ret.ThrowIfException();
        }

        public ResultOrExeption<bool> CheckIfReady(EnsureSpecial doNotThrowError)
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

        public async Task WaitUntilReadyForAtomicAsync(CancellationToken cancel = default)
        {
            await Task.Yield();
            await TaskUtil.AwaitWithPollAsync(Timeout.Infinite, 100, () => CheckIfReady(doNotThrowError: EnsureSpecial.Yes).IsOk, cancel, true);
        }

        public async Task<bool> TranAsync(bool writeMode, Func<HadbTran, Task<bool>> task, CancellationToken cancel = default, DeadlockRetryConfig? retryConfig = null)
        {
            CheckIfReady();
            retryConfig ??= this.DefaultDeadlockRetryConfig;
            int numRetry = 0;

            LABEL_RETRY:
            try
            {
                await using var tran = await this.BeginDatabaseTransactionImplAsync(writeMode, true, cancel);

                await tran.BeginAsync(cancel);

                if (await task(tran))
                {
                    await tran.CommitAsync(cancel);

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
                    numRetry++;
                    if (numRetry <= retryConfig.RetryCount)
                    {
                        int nextInterval = Util.GenRandInterval(retryConfig.RetryAverageInterval);

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


        public HadbObject? FastGet<T>(string uid) where T : HadbData
            => FastGet(uid, typeof(T).Name);

        public HadbObject? FastGet(string uid, string typeName)
        {
            this.CheckIfReady();

            var mem = this.MemDb!;

            var ret = mem.IndexedKeysTable_SearchByUid(uid, typeName);

            if (ret == null) return null;

            if (ret.Deleted) return null;

            return ret;
        }

        public HadbObject? FastSearchByKey<T>(T model) where T : HadbData
        {
            model.Normalize();
            return FastSearchByKey<T>(model.GetKeys());
        }

        public HadbObject? FastSearchByKey<T>(HadbKeys keys) where T : HadbData
            => FastSearchByKey(keys, typeof(T).Name);

        public HadbObject? FastSearchByKey(HadbKeys keys, string typeName)
        {
            this.CheckIfReady();

            var mem = this.MemDb!;

            var ret = mem.IndexedKeysTable_SearchByKey(keys, typeName);

            if (ret == null) return null;

            if (ret.Deleted) return null;

            return ret;
        }

        public IEnumerable<HadbObject> FastSearchByLabels<T>(T model) where T : HadbData
        {
            model.Normalize();
            return FastSearchByLabels(model.GetLabels(), typeof(T).Name);
        }

        public IEnumerable<HadbObject> FastSearchByLabels<T>(HadbLabels labels) where T : HadbData
            => FastSearchByLabels(labels, typeof(T).Name);

        public IEnumerable<HadbObject> FastSearchByLabels(HadbLabels labels, string typeName)
        {
            this.CheckIfReady();

            var mem = this.MemDb!;

            var items = mem.IndexedLabelsTable_SearchByLabels(labels, typeName);

            return items.Where(x => x.Deleted == false);
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
            public bool IsWriteMode;
            public bool IsTransaction;
            public HadbMemDataBase MemDb;
            List<HadbObject> ApplyObjectsList = new List<HadbObject>();

            public IReadOnlyList<HadbObject> GetApplyObjectsList() => this.ApplyObjectsList;

            protected abstract Task CommitImplAsync(CancellationToken cancel);

            AsyncLock.LockHolder? LockHolder = null;

            public HadbBase<TMem, TDynamicConfig> Hadb;

            public HadbTran(bool writeMode, bool isTransaction, HadbBase<TMem, TDynamicConfig> hadb)
            {
                try
                {
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

            public async Task BeginAsync(CancellationToken cancel = default)
            {
                if (this.LockHolder == null)
                {
                    this.LockHolder = await this.MemDb.CriticalAsyncLock.LockWithAwait(cancel);
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

            public async Task CommitAsync(CancellationToken cancel = default)
            {
                if (this.IsWriteMode)
                {
                    await this.CommitImplAsync(cancel);

                    await FinishInternalAsync(cancel);
                }
            }

            readonly Once flushed = new Once();

            Task FinishInternalAsync(CancellationToken cancel = default)
            {
                if (flushed.IsFirstCall())
                {
                    try
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
                    finally
                    {
                        this.LockHolder._DisposeSafe();
                    }
                }

                return Task.CompletedTask;
            }

            protected override async Task CleanupImplAsync(Exception? ex)
            {
                try
                {
                    if (this.IsWriteMode == false)
                    {
                        await this.FinishInternalAsync();
                    }

                    this.LockHolder._DisposeSafe();
                }
                finally
                {
                    await base.CleanupImplAsync(ex);
                }
            }


            public async Task<HadbObject> AtomicAddAsync(HadbData data, CancellationToken cancel = default)
                => (await AtomicAddAsync(data._SingleArray(), cancel)).Single();

            public async Task<List<HadbObject>> AtomicAddAsync(IEnumerable<HadbData> dataList, CancellationToken cancel = default)
            {
                Hadb.CheckIfReady();

                List<HadbObject> objList = new List<HadbObject>();

                foreach (var _data in dataList)
                {
                    var data = _data;

                    data._NullCheck(nameof(data));

                    data = data._CloneDeep();
                    data.Normalize();

                    var keys = data.GetKeys();

                    var existing = this.MemDb!.IndexedKeysTable_SearchByKey(keys, data.GetUserDataTypeName());

                    if (existing != null)
                    {
                        throw new CoresLibException($"Duplicated key in the memory database. Keys = {keys._ObjectToJson(compact: true)}");
                    }

                    objList.Add(data.ToNewObject());
                }

                await Hadb.AtomicAddDataListToDatabaseImplAsync(this, objList, cancel);

                this.AddApplyObjects(objList);

                return objList;
            }

            public async Task<HadbObject?> AtomicGetAsync<T>(string uid, CancellationToken cancel = default) where T : HadbData
                => await AtomicGetAsync(uid, typeof(T).Name, cancel);

            public async Task<HadbObject?> AtomicGetAsync(string uid, string typeName, CancellationToken cancel = default)
            {
                Hadb.CheckIfReady();

                HadbObject? ret = await Hadb.AtomicGetDataFromDatabaseImplAsync(this, uid, typeName, cancel);

                if (ret == null) return null;

                this.AddApplyObject(ret);

                if (ret.Deleted) return null;

                return ret;
            }

            public async Task<HadbObject> AtomicUpdateAsync(HadbObject obj, CancellationToken cancel = default)
            {
                Hadb.CheckIfReady();

                obj._NullCheck(nameof(obj));

                obj.CheckIsNotMemoryDbObject();
                obj.Normalize();

                var keys = obj.GetKeys();

                var existing = this.MemDb!.IndexedKeysTable_SearchByKey(keys, obj.GetUserDataTypeName());

                if (existing != null && existing.Uid._IsSamei(obj.Uid) == false)
                {
                    throw new CoresLibException($"Duplicated key in the memory database. Keys = {keys._ObjectToJson(compact: true)}");
                }

                var obj2 = await Hadb.AtomicUpdateDataOnDatabaseImplAsync(this, obj, cancel);

                this.AddApplyObject(obj2);

                return obj2;
            }

            public async Task<HadbObject?> AtomicSearchByKeyAsync<T>(T model, CancellationToken cancel = default) where T : HadbData
            {
                model.Normalize();
                return await AtomicSearchByKeyAsync<T>(model.GetKeys(), cancel);
            }

            public async Task<HadbObject?> AtomicSearchByKeyAsync<T>(HadbKeys keys, CancellationToken cancel = default) where T : HadbData
                => await AtomicSearchByKeyAsync(keys, typeof(T).Name, cancel);

            public async Task<HadbObject?> AtomicSearchByKeyAsync(HadbKeys keys, string typeName, CancellationToken cancel = default)
            {
                Hadb.CheckIfReady();

                HadbObject? ret = await Hadb.AtomicSearchDataByKeyFromDatabaseImplAsync(this, keys, typeName, cancel);

                if (ret == null) return null;

                this.AddApplyObject(ret);

                if (ret.Deleted) return null;

                return ret;
            }

            public async Task<IEnumerable<HadbObject>> AtomicSearchByLabelsAsync<T>(T model, CancellationToken cancel = default) where T : HadbData
            {
                model.Normalize();
                return await AtomicSearchByLabelsAsync(model.GetLabels(), typeof(T).Name, cancel);
            }

            public async Task<IEnumerable<HadbObject>> AtomicSearchByLabelsAsync<T>(HadbLabels labels, CancellationToken cancel = default) where T : HadbData
                => await AtomicSearchByLabelsAsync(labels, typeof(T).Name, cancel);

            public async Task<IEnumerable<HadbObject>> AtomicSearchByLabelsAsync(HadbLabels labels, string typeName, CancellationToken cancel = default)
            {
                Hadb.CheckIfReady();

                IEnumerable<HadbObject> items = await Hadb.AtomicSearchDataListByLabelsFromDatabaseImplAsync(this, labels, typeName, cancel);

                this.AddApplyObjects(items);

                return items.Where(x => x.Deleted == false);
            }

            public async Task<HadbObject?> AtomicDeleteAsync<T>(string uid, CancellationToken cancel = default) where T : HadbData
                => await AtomicDeleteAsync(uid, typeof(T).Name, cancel);

            public async Task<HadbObject?> AtomicDeleteAsync(string uid, string typeName, CancellationToken cancel = default)
            {
                Hadb.CheckIfReady();

                HadbObject? ret = await Hadb.AtomicDeleteDataFromDatabaseImplAsync(this, uid, typeName, cancel);

                if (ret == null)
                {
                    return null;
                }

                this.AddApplyObject(ret);

                return ret;
            }

            public async Task<bool> LazyUpdateAsync(HadbObject obj, CancellationToken cancel = default)
            {
                Hadb.CheckIfReady();
                if (obj.Deleted || obj.ArchiveAge != 0) return false;
                obj.CheckIsNotMemoryDbObject();

                return await Hadb.LazyUpdateImplAsync(this, obj, cancel);
            }
        }
    }
}

#endif

