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

        public override HadbKeys GetKeys() => new HadbKeys(this.HostName);

        public override HadbLabels GetLabels() => new HadbLabels(this.IPv4Address, this.IPv6Address);

        public override void Normalize()
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

    public class MikakaDDnsMem : HadbMemDataBase
    {
        protected override List<Type> GetDefinedUserDataTypesImpl()
        {
            List<Type> ret = new List<Type>();
            ret.Add(typeof(MikakaDDnsHost));
            return ret;
        }
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
        public string DATA_EXT { get; set; } = "";

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
            this.DATA_EXT = this.DATA_EXT._NonNull();
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

        protected override async Task<KeyValueList<string, string>> LoadDynamicConfigFromDatabaseImplAsync(CancellationToken cancel = default)
        {
            IEnumerable<HadbSqlConfigRow> configList = null!;

            try
            {
                await using var dbReader = await this.OpenSqlDatabaseForReadAsync(cancel);

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

        protected override async Task<List<HadbObject>> ReloadDataFromDatabaseImplAsync(CancellationToken cancel = default)
        {
            IEnumerable<HadbSqlDataRow> rowList = null!;

            try
            {
                await using var dbReader = await this.OpenSqlDatabaseForReadAsync(cancel);

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
                        HadbObject obj = new HadbObject(data, row.DATA_UID, row.DATA_VER, 0, row.DATA_DELETED, row.DATA_CREATE_DT, row.DATA_UPDATE_DT, row.DATA_DELETE_DT);

                        ret.Add(obj);
                    }
                }
            }

            return ret;
        }

        protected async Task<HadbSqlDataRow?> GetRowByKeyAsync(Database db, string typeName, HadbKeys key, CancellationToken cancel = default)
        {
            List<string> conditions = new List<string>();

            if (key.Key1._IsFilled()) conditions.Add("DATA_KEY1 = @DATA_KEY1");
            if (key.Key2._IsFilled()) conditions.Add("DATA_KEY2 = @DATA_KEY2");
            if (key.Key3._IsFilled()) conditions.Add("DATA_KEY3 = @DATA_KEY3");
            if (key.Key4._IsFilled()) conditions.Add("DATA_KEY4 = @DATA_KEY4");

            if (conditions.Count == 0)
            {
                return null;
            }

            return await db.EasySelectSingleAsync<HadbSqlDataRow>($"select * from HADB_DATA where ({conditions._Combine(" or ")}) and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_DELETED = 0 and DATA_ARCHIVE_AGE = 0 and DATA_TYPE = @DATA_TYPE",
                new
                {
                    DATA_KEY1 = key.Key1,
                    DATA_KEY2 = key.Key2,
                    DATA_KEY3 = key.Key3,
                    DATA_KEY4 = key.Key4,
                    DATA_SYSTEMNAME = this.SystemName,
                    DATA_TYPE = typeName,
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
                return new HadbSqlDataRow[0];
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

        protected override async Task ReliableAddDataListToDatabaseImplAsync(IEnumerable<HadbObject> dataList, CancellationToken cancel = default)
        {
            await using var dbWriter = await this.OpenSqlDatabaseForWriteAsync(cancel);

            await dbWriter.TranAsync(async () =>
            {
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
                        DATA_KEY1 = keys.Key1._NormalizeKey(true),
                        DATA_KEY2 = keys.Key2._NormalizeKey(true),
                        DATA_KEY3 = keys.Key3._NormalizeKey(true),
                        DATA_KEY4 = keys.Key4._NormalizeKey(true),
                        DATA_LABEL1 = labels.Label1._NormalizeKey(true),
                        DATA_LABEL2 = labels.Label2._NormalizeKey(true),
                        DATA_LABEL3 = labels.Label3._NormalizeKey(true),
                        DATA_LABEL4 = labels.Label4._NormalizeKey(true),
                        DATA_VALUE = data.GetUserDataJsonString(),
                        DATA_EXT = "",
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

                return true;
            });
        }

        protected override async Task<HadbObject?> ReliableGetDataFromDatabaseImplAsync(string uid, string typeName, CancellationToken cancel = default)
        {
            typeName = typeName._NonNullTrim();

            await using var dbReader = await this.OpenSqlDatabaseForReadAsync(cancel);

            HadbSqlDataRow? row = null;

            await dbReader.TranReadSnapshotIfNecessaryAsync(async () =>
            {
                row = await GetRowByUidAsync(dbReader, typeName, uid, cancel);
            });

            if (row == null) return null;

            HadbObject ret = new HadbObject(this.JsonToHadbData(row.DATA_VALUE, typeName), row.DATA_UID, row.DATA_VER, row.DATA_ARCHIVE_AGE, row.DATA_DELETED, row.DATA_CREATE_DT, row.DATA_UPDATE_DT, row.DATA_DELETE_DT);

            return ret;
        }

        protected override async Task<HadbObject?> ReliableSearchDataByKeyFromDatabaseImplAsync(HadbKeys key, string typeName, CancellationToken cancel = default)
        {
            typeName = typeName._NonNullTrim();

            await using var dbReader = await this.OpenSqlDatabaseForReadAsync(cancel);

            HadbSqlDataRow? row = null;

            await dbReader.TranReadSnapshotIfNecessaryAsync(async () =>
            {
                row = await GetRowByKeyAsync(dbReader, typeName, key, cancel);
            });

            if (row == null) return null;

            HadbObject ret = new HadbObject(this.JsonToHadbData(row.DATA_VALUE, typeName), row.DATA_UID, row.DATA_VER, row.DATA_ARCHIVE_AGE, row.DATA_DELETED, row.DATA_CREATE_DT, row.DATA_UPDATE_DT, row.DATA_DELETE_DT);

            return ret;
        }

        protected override async Task<IEnumerable<HadbObject>> ReliableSearchDataListByLabelsFromDatabaseImplAsync(HadbLabels labels, string typeName, CancellationToken cancel = default)
        {
            typeName = typeName._NonNullTrim();

            await using var dbReader = await this.OpenSqlDatabaseForReadAsync(cancel);

            IEnumerable<HadbSqlDataRow> rows = null!;

            await dbReader.TranReadSnapshotIfNecessaryAsync(async () =>
            {
                rows = await GetRowsByLabelsAsync(dbReader, typeName, labels, cancel);
            });

            if (rows == null) return new HadbObject[0];

            List<HadbObject> ret = new List<HadbObject>();

            foreach (var row in rows)
            {
                var item = new HadbObject(this.JsonToHadbData(row.DATA_VALUE, typeName), row.DATA_UID, row.DATA_VER, row.DATA_ARCHIVE_AGE, row.DATA_DELETED, row.DATA_CREATE_DT, row.DATA_UPDATE_DT, row.DATA_DELETE_DT);

                ret.Add(item);
            }

            return ret;
        }

        protected override async Task<HadbObject?> ReliableDeleteDataFromDatabaseImplAsync(string uid, string typeName, CancellationToken cancel = default)
        {
            typeName = typeName._NonNullTrim();
            uid = uid._NormalizeUid(true);

            await using var dbWriter = await this.OpenSqlDatabaseForWriteAsync(cancel);

            HadbObject? ret = null;

            await dbWriter.TranAsync(async () =>
            {
                HadbSqlDataRow? row = await GetRowByUidAsync(dbWriter, typeName, uid, cancel);

                if (row == null)
                {
                    return false;
                }

                await dbWriter.EasyExecuteAsync("update HADB_DATA set DATA_ARCHIVE_AGE = DATA_ARCHIVE_AGE + 1 where DATA_UID like @DATA_UID and DATA_SYSTEMNAME = @DATA_SYSTEMNAME and DATA_TYPE = @DATA_TYPE and DATA_ARCHIVE_AGE >= 1",
                    new
                    {
                        DATA_UID = uid + ":%",
                        DATA_SYSTEMNAME = this.SystemName,
                        DATA_TYPE = typeName,
                    });

                HadbSqlDataRow rowOld = row._CloneDeep();

                rowOld.DATA_UID += ":" + rowOld.DATA_VER.ToString("D20");
                rowOld.DATA_ARCHIVE_AGE = 1;
                rowOld.Normalize();

                await dbWriter.EasyInsertAsync(rowOld, cancel);

                row.DATA_VER++;
                row.DATA_UPDATE_DT = row.DATA_DELETE_DT = DtOffsetNow;
                row.DATA_DELETED = true;

                await dbWriter.EasyUpdateAsync(row, true, cancel);

                ret = new HadbObject(this.JsonToHadbData(row.DATA_VALUE, typeName), row.DATA_UID, row.DATA_VER, row.DATA_ARCHIVE_AGE, row.DATA_DELETED, row.DATA_CREATE_DT, row.DATA_UPDATE_DT, row.DATA_DELETE_DT);

                return true;
            });

            return ret;
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

    public struct HadbKeys
    {
        public string? Key1 { get; }
        public string? Key2 { get; }
        public string? Key3 { get; }
        public string? Key4 { get; }

        public HadbKeys(string key1, string? key2 = null, string? key3 = null, string? key4 = null)
        {
            this.Key1 = key1._NormalizeKey(true)._NullIfEmpty();
            this.Key2 = key2._NormalizeKey(true)._NullIfEmpty();
            this.Key3 = key3._NormalizeKey(true)._NullIfEmpty();
            this.Key4 = key4._NormalizeKey(true)._NullIfEmpty();
        }
    }

    public struct HadbLabels
    {
        public string? Label1 { get; }
        public string? Label2 { get; }
        public string? Label3 { get; }
        public string? Label4 { get; }

        public HadbLabels(string label1, string? label2 = null, string? label3 = null, string? label4 = null)
        {
            this.Label1 = label1._NormalizeKey(true)._NullIfEmpty();
            this.Label2 = label2._NormalizeKey(true)._NullIfEmpty();
            this.Label3 = label3._NormalizeKey(true)._NullIfEmpty();
            this.Label4 = label4._NormalizeKey(true)._NullIfEmpty();
        }
    }

    public abstract class HadbData : INormalizable
    {
        public virtual HadbKeys GetKeys() => new HadbKeys();
        public virtual HadbLabels GetLabels() => new HadbLabels();

        public abstract void Normalize();

        public HadbObject ToNewObject() => new HadbObject(this);

        public static implicit operator HadbObject(HadbData data) => data.ToNewObject();

        public Type GetUserDataType() => this.GetType();
        public string GetUserDataTypeName() => this.GetType().Name;
    }

    public sealed class HadbObject : INormalizable
    {
        readonly CriticalSection<HadbObject> Lock = new CriticalSection<HadbObject>();

        public string Uid { get; }

        public long Ver { get; private set; }

        public bool Deleted { get; private set; }

        public DateTimeOffset CreateDt { get; private set; }

        public DateTimeOffset UpdateDt { get; private set; }

        public DateTimeOffset DeleteDt { get; private set; }

        public long ArchiveAge { get; private set; }

        public HadbData UserData { get; private set; }

        public string GetUserDataJsonString() => this.UserData._ObjectToJson(compact: true);

        public HadbObject(HadbData userData) : this(userData, null, 1, 0, false, DtOffsetNow, DtOffsetNow, DtOffsetZero) { }

        public HadbObject(HadbData userData, string? uid, long ver, long archiveAge, bool deleted, DateTimeOffset createDt, DateTimeOffset updateDt, DateTimeOffset deleteDt)
        {
            userData._NullCheck(nameof(userData));

            this.UserData = userData;

            if (uid._IsEmpty())
            {
                this.Uid = Str.NewUid(this.GetUidPrefix(), '_');
            }
            else
            {
                this.Uid = uid._NormalizeUid(true);
            }

            this.Ver = Math.Max(ver, 1);
            this.ArchiveAge = Math.Max(archiveAge, 0);
            this.Deleted = deleted;
            this.CreateDt = createDt._NormalizeDateTimeOffset();
            this.UpdateDt = updateDt._NormalizeDateTimeOffset();
            this.DeleteDt = deleteDt._NormalizeDateTimeOffset();

            this.Normalize();
        }

        public bool MarkAsDeleted()
        {
            lock (this.Lock)
            {
                if (this.Deleted)
                {
                    return false;
                }

                this.Deleted = true;

                return true;
            }
        }

        public bool UpdateIfNew(HadbObject obj, out HadbKeys oldKeys, out HadbLabels oldLabels)
        {
            lock (this.Lock)
            {
                if (this.Uid._IsSamei(obj.Uid) == false)
                {
                    throw new CoresLibException($"this.Uid '{this.Uid}' != obj.Uid '{obj.Uid}'");
                }

                if (this.ArchiveAge != 0)
                {
                    throw new CoresLibException($"this.ArchiveAge == {this.ArchiveAge}");
                }

                if (obj.ArchiveAge != 0)
                {
                    throw new CoresLibException($"obj.ArchiveAge == {obj.ArchiveAge}");
                }

                bool update = false;

                if (this.Ver < obj.Ver)
                {
                    update = true;
                }
                else if (this.Ver == obj.Ver && this.UpdateDt < obj.UpdateDt)
                {
                    update = true;
                }

                if (update)
                {
                    oldKeys = this.GetKeys();
                    oldLabels = this.GetLabels();

                    this.Deleted = obj.Deleted;
                    this.CreateDt = obj.CreateDt;
                    this.UpdateDt = obj.UpdateDt;
                    this.DeleteDt = obj.DeleteDt;
                    this.UserData = obj.UserData;
                    this.Ver = obj.Ver;
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

        public HadbKeys GetKeys() => this.Deleted == false ? this.UserData.GetKeys() : new HadbKeys();
        public HadbLabels GetLabels() => this.Deleted == false ? this.UserData.GetLabels() : new HadbLabels();

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
        Uid = 0,
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

        readonly StrDictionary<HadbObject> AllObjectsDict = new StrDictionary<HadbObject>(StrComparer.IgnoreCaseComparer);
        public readonly CriticalSection<HadbMemDataBase> CriticalLock = new CriticalSection<HadbMemDataBase>(); // メモリ上のデータの読み書き用ロック

        ImmutableDictionary<string, HadbObject> IndexedKeysTable = ImmutableDictionary<string, HadbObject>.Empty.WithComparers(StrComparer.IgnoreCaseComparer);
        ImmutableDictionary<string, ConcurrentHashSet<HadbObject>> IndexedLabelsTable = ImmutableDictionary<string, ConcurrentHashSet<HadbObject>>.Empty.WithComparers(StrComparer.IgnoreCaseComparer);

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

        public void ReloadFromDatabase(IEnumerable<HadbObject> objectList)
        {
            int countInserted = 0;
            int countUpdated = 0;
            int countRemoved = 0;

            objectList._NormalizeAll();

            lock (this.CriticalLock)
            {
                foreach (var obj in objectList)
                {
                    if (this.AllObjectsDict.TryGetValue(obj.Uid, out HadbObject? currentObj))
                    {
                        if (currentObj.UpdateIfNew(obj, out HadbKeys oldKeys, out HadbLabels oldLabels))
                        {
                            if (currentObj.Deleted == false)
                            {
                                countUpdated++;
                            }
                            else
                            {
                                countRemoved++;
                            }

                            IndexedTable_UpdateObject_Critical(currentObj, currentObj, oldKeys, oldLabels);
                        }
                    }
                    else
                    {
                        this.AllObjectsDict[obj.Uid] = obj;
                        IndexedTable_AddObject_Critical(obj);
                        countInserted++;
                    }
                }
            }

            //if (countInserted > 0 || countRemoved > 0 || countUpdated > 0)
            {
                Debug($"Update Local Memory from Database: New={countInserted._ToString3()}, Update={countUpdated._ToString3()}, Remove={countRemoved._ToString3()}");
            }
        }

        public bool ReliableDeleteObject(HadbObject obj)
        {
            lock (this.CriticalLock)
            {
                if (this.AllObjectsDict.TryGetValue(obj.Uid, out HadbObject? currentObject))
                {
                    if (currentObject.Deleted == false)
                    {
                        if (currentObject.UpdateIfNew(obj, out HadbKeys oldKeys, out HadbLabels oldLabels))
                        {
                            IndexedTable_DeleteObject_Critical(obj, oldKeys, oldLabels);

                            return true;
                        }
                    }
                }
            }

            return false;
        }

        public HadbObject ReliableAddObject(HadbObject obj)
        {
            obj.Normalize();
            if (obj.Deleted) throw new CoresLibException("obj.Deleted == true");
            if (obj.ArchiveAge != 0) throw new CoresLibException("obj.ArchiveAge != 0");

            lock (this.CriticalLock)
            {
                if (this.AllObjectsDict.TryGetValue(obj.Uid, out HadbObject? currentObject))
                {
                    if (currentObject.UpdateIfNew(obj, out HadbKeys oldKeys, out HadbLabels oldLables))
                    {
                        IndexedTable_UpdateObject_Critical(currentObject, currentObject, oldKeys, oldLables);
                    }

                    return currentObject;
                }
                else
                {
                    this.AllObjectsDict[obj.Uid] = obj;
                    IndexedTable_AddObject_Critical(obj);
                    return obj;
                }
            }
        }



        void IndexedTable_DeleteObject_Critical(HadbObject deleteObj, HadbKeys oldKeys, HadbLabels oldLabels)
        {
            string typeName = deleteObj.GetUserDataTypeName();

            if (oldKeys.Key1._IsFilled()) IndexedKeysTable_DeleteInternal_Critical(HadbIndexColumn.Key1.ToString() + ":" + typeName + ":" + oldKeys.Key1);
            if (oldKeys.Key2._IsFilled()) IndexedKeysTable_DeleteInternal_Critical(HadbIndexColumn.Key2.ToString() + ":" + typeName + ":" + oldKeys.Key2);
            if (oldKeys.Key3._IsFilled()) IndexedKeysTable_DeleteInternal_Critical(HadbIndexColumn.Key3.ToString() + ":" + typeName + ":" + oldKeys.Key3);
            if (oldKeys.Key4._IsFilled()) IndexedKeysTable_DeleteInternal_Critical(HadbIndexColumn.Key4.ToString() + ":" + typeName + ":" + oldKeys.Key4);

            if (oldLabels.Label1._IsFilled()) IndexedLabelsTable_DeleteInternal_Critical(HadbIndexColumn.Label1.ToString() + ":" + typeName + ":" + oldLabels.Label1, deleteObj);
            if (oldLabels.Label2._IsFilled()) IndexedLabelsTable_DeleteInternal_Critical(HadbIndexColumn.Label2.ToString() + ":" + typeName + ":" + oldLabels.Label2, deleteObj);
            if (oldLabels.Label3._IsFilled()) IndexedLabelsTable_DeleteInternal_Critical(HadbIndexColumn.Label3.ToString() + ":" + typeName + ":" + oldLabels.Label3, deleteObj);
            if (oldLabels.Label4._IsFilled()) IndexedLabelsTable_DeleteInternal_Critical(HadbIndexColumn.Label4.ToString() + ":" + typeName + ":" + oldLabels.Label4, deleteObj);
        }

        void IndexedTable_UpdateObject_Critical(HadbObject newObj, HadbObject oldObj, HadbKeys oldKeys, HadbLabels oldLabels)
        {
            HadbKeys newKeys = newObj.GetKeys();
            HadbLabels newLabels = newObj.GetLabels();

            if (oldKeys.Key1._IsSamei(newKeys.Key1) == false) IndexedKeysTable_ReplaceObject_Critical(newObj, HadbIndexColumn.Key1, oldKeys.Key1, newKeys.Key1);
            if (oldKeys.Key2._IsSamei(newKeys.Key2) == false) IndexedKeysTable_ReplaceObject_Critical(newObj, HadbIndexColumn.Key2, oldKeys.Key2, newKeys.Key2);
            if (oldKeys.Key3._IsSamei(newKeys.Key3) == false) IndexedKeysTable_ReplaceObject_Critical(newObj, HadbIndexColumn.Key3, oldKeys.Key3, newKeys.Key3);
            if (oldKeys.Key4._IsSamei(newKeys.Key4) == false) IndexedKeysTable_ReplaceObject_Critical(newObj, HadbIndexColumn.Key4, oldKeys.Key4, newKeys.Key4);

            if (oldLabels.Label1._IsSamei(newLabels.Label1) == false) IndexedLabelsTable_ReplaceObject_Critical(newObj, oldObj, HadbIndexColumn.Label1, oldLabels.Label1, newLabels.Label1);
            if (oldLabels.Label2._IsSamei(newLabels.Label2) == false) IndexedLabelsTable_ReplaceObject_Critical(newObj, oldObj, HadbIndexColumn.Label2, oldLabels.Label2, newLabels.Label2);
            if (oldLabels.Label3._IsSamei(newLabels.Label3) == false) IndexedLabelsTable_ReplaceObject_Critical(newObj, oldObj, HadbIndexColumn.Label3, oldLabels.Label3, newLabels.Label3);
            if (oldLabels.Label4._IsSamei(newLabels.Label4) == false) IndexedLabelsTable_ReplaceObject_Critical(newObj, oldObj, HadbIndexColumn.Label4, oldLabels.Label4, newLabels.Label4);
        }

        void IndexedTable_AddObject_Critical(HadbObject obj)
        {
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

            return this.IndexedKeysTable._GetOrDefault(column.ToString() + ":" + typeName + ":" + key);
        }

        void IndexedKeysTable_AddOrUpdateInternal_Critical(string keyStr, HadbObject obj)
        {
            keyStr = keyStr._NormalizeKey(true);

            ImmutableInterlocked.AddOrUpdate(ref this.IndexedKeysTable, keyStr, obj, (k, old) => obj);
        }

        bool IndexedKeysTable_DeleteInternal_Critical(string keyStr)
        {
            keyStr = keyStr._NormalizeKey(true);

            return ImmutableInterlocked.TryRemove(ref this.IndexedKeysTable, keyStr, out _);
        }


        void IndexedLabelsTable_ReplaceObject_Critical(HadbObject newObj, HadbObject oldObj, HadbIndexColumn column, string? oldLabel, string? newLabel)
        {
            oldLabel = oldLabel._NormalizeKey(true);
            newLabel = newLabel._NormalizeKey(true);

            if (oldLabel._IsSamei(newLabel) == false || object.ReferenceEquals(newObj, oldObj) == false)
            {
                if (newLabel._IsFilled())
                {
                    IndexedLabelsTable_AddInternal_Critical(column.ToString() + ":" + newObj.GetUserDataTypeName() + ":" + newLabel, newObj);
                }

                if (oldLabel._IsFilled())
                {
                    IndexedLabelsTable_DeleteInternal_Critical(column.ToString() + ":" + oldObj.GetUserDataTypeName() + ":" + oldLabel, oldObj);
                }
            }
        }

        public IEnumerable<HadbObject> IndexedLabelsTable_SearchByLabels(HadbLabels labels, string typeName)
        {
            typeName = typeName._NonNullTrim();

            IEnumerable<HadbObject>? tmp1 = null;
            IEnumerable<HadbObject>? tmp2 = null;
            IEnumerable<HadbObject>? tmp3 = null;
            IEnumerable<HadbObject>? tmp4 = null;

            if (labels.Label1._IsFilled())
                tmp1 = IndexedLabelsTable_SearchObjects(HadbIndexColumn.Label1, typeName, labels.Label1);
            else
                tmp1 = EmptyOf<HadbObject>();

            if (labels.Label2._IsFilled())
                tmp2 = IndexedLabelsTable_SearchObjects(HadbIndexColumn.Label2, typeName, labels.Label2);
            else
                tmp2 = EmptyOf<HadbObject>();

            if (labels.Label3._IsFilled())
                tmp3 = IndexedLabelsTable_SearchObjects(HadbIndexColumn.Label3, typeName, labels.Label3);
            else
                tmp3 = EmptyOf<HadbObject>();

            if (labels.Label4._IsFilled())
                tmp4 = IndexedLabelsTable_SearchObjects(HadbIndexColumn.Label4, typeName, labels.Label4);
            else
                tmp4 = EmptyOf<HadbObject>();

            return tmp1.Intersect(tmp2).Intersect(tmp3).Intersect(tmp4);
        }

        IEnumerable<HadbObject> IndexedLabelsTable_SearchObjects(HadbIndexColumn column, string typeName, string label)
        {
            label = label._NormalizeKey(true);
            typeName = typeName._NonNullTrim();

            if (label._IsEmpty()) return EmptyOf<HadbObject>();

            var list = this.IndexedLabelsTable._GetOrDefault(column.ToString() + ":" + typeName + ":" + label);
            if (list == null) return EmptyOf<HadbObject>();

            return list.Keys;
        }

        void IndexedLabelsTable_AddInternal_Critical(string labelKeyStr, HadbObject obj)
        {
            labelKeyStr = labelKeyStr._NormalizeKey(true);

            var list = ImmutableInterlocked.GetOrAdd(ref this.IndexedLabelsTable, labelKeyStr, k => new ConcurrentHashSet<HadbObject>());

            list.Add(obj);
        }

        bool IndexedLabelsTable_DeleteInternal_Critical(string labelKeyStr, HadbObject obj)
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

    public abstract class HadbBase<TMem, TDynamicConfig> : AsyncService
        where TMem : HadbMemDataBase, new()
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

        protected abstract Task ReliableAddDataListToDatabaseImplAsync(IEnumerable<HadbObject> dataList, CancellationToken cancel = default);
        protected abstract Task<HadbObject?> ReliableGetDataFromDatabaseImplAsync(string uid, string typeName, CancellationToken cancel = default);
        protected abstract Task<HadbObject?> ReliableSearchDataByKeyFromDatabaseImplAsync(HadbKeys keys, string typeName, CancellationToken cancel = default);
        protected abstract Task<IEnumerable<HadbObject>> ReliableSearchDataListByLabelsFromDatabaseImplAsync(HadbLabels labels, string typeName, CancellationToken cancel = default);
        protected abstract Task<HadbObject?> ReliableDeleteDataFromDatabaseImplAsync(string uid, string typeName, CancellationToken cancel = default);

        protected abstract Task<KeyValueList<string, string>> LoadDynamicConfigFromDatabaseImplAsync(CancellationToken cancel = default);
        protected abstract Task AppendMissingDynamicConfigToDatabaseImplAsync(KeyValueList<string, string> missingValues, CancellationToken cancel = default);
        protected abstract Task<List<HadbObject>> ReloadDataFromDatabaseImplAsync(CancellationToken cancel = default);

        public StrDictionary<Type> DefinedDataTypesByName { get; }

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

                TMem tmpMem = new TMem();
                this.DefinedDataTypesByName = tmpMem.GetDefinedUserDataTypesByName();
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

        public void StartLoop()
        {
            this.IsLoopStarted = true;
        }

        async Task ReloadCoreAsync(CancellationToken cancel)
        {
            try
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

                // DB からオブジェクト一覧を読み込む
                var loadedObjectsList = await this.ReloadDataFromDatabaseImplAsync(cancel);

                TMem? currentMemDb = this.MemDb;
                if (currentMemDb == null) currentMemDb = new TMem();

                currentMemDb.ReloadFromDatabase(loadedObjectsList);

                this.MemDb = currentMemDb;
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

        public void CheckIfReadyForReliable()
        {
            var ret = CheckIfReadyForReliable(doNotThrowError: EnsureSpecial.Yes);
            ret.ThrowIfException();
        }

        public ResultOrExeption<bool> CheckIfReadyForReliable(EnsureSpecial doNotThrowError)
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

        public async Task WaitUntilReadyForReliableAsync(CancellationToken cancel = default)
        {
            await Task.Yield();
            await TaskUtil.AwaitWithPollAsync(Timeout.Infinite, 100, () => CheckIfReadyForReliable(doNotThrowError: EnsureSpecial.Yes).IsOk, cancel, true);
        }

        public async Task<HadbObject> ReliableAddAsync(HadbData data, CancellationToken cancel = default)
            => (await ReliableAddAsync(data._SingleArray(), cancel)).Single();

        public async Task<List<HadbObject>> ReliableAddAsync(IEnumerable<HadbData> dataList, CancellationToken cancel = default)
        {
            CheckIfReadyForReliable();

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

            await this.ReliableAddDataListToDatabaseImplAsync(objList, cancel);

            for (int i = 0; i < objList.Count; i++)
            {
                objList[i] = this.MemDb!.ReliableAddObject(objList[i]);
            }

            return objList;
        }

        public async Task<HadbObject?> ReliableGetAsync<T>(string uid, CancellationToken cancel = default) where T : HadbData
            => await ReliableGetAsync(uid, typeof(T).Name, cancel);

        public async Task<HadbObject?> ReliableGetAsync(string uid, string typeName, CancellationToken cancel = default)
        {
            CheckIfReadyForReliable();

            HadbObject? ret = await this.ReliableGetDataFromDatabaseImplAsync(uid, typeName, cancel);

            if (ret == null) return null;

            if (ret.Deleted) return null;

            return this.MemDb!.ReliableAddObject(ret);
        }

        public async Task<HadbObject?> ReliableSearchByKeysAsync<T>(HadbKeys keys, CancellationToken cancel = default) where T : HadbData
            => await ReliableSearchByKeysAsync(keys, typeof(T).Name, cancel);

        public async Task<HadbObject?> ReliableSearchByKeysAsync(HadbKeys keys, string typeName, CancellationToken cancel = default)
        {
            CheckIfReadyForReliable();

            HadbObject? ret = await this.ReliableSearchDataByKeyFromDatabaseImplAsync(keys, typeName, cancel);

            if (ret == null) return null;

            if (ret.Deleted) return null;

            return this.MemDb!.ReliableAddObject(ret);
        }

        public async Task<IEnumerable<HadbObject>> ReliableSearchByLabelsAsync<T>(HadbLabels labels, CancellationToken cancel = default) where T : HadbData
            => await ReliableSearchByLabelsAsync(labels, typeof(T).Name, cancel);

        public async Task<IEnumerable<HadbObject>> ReliableSearchByLabelsAsync(HadbLabels labels, string typeName, CancellationToken cancel = default)
        {
            CheckIfReadyForReliable();

            IEnumerable<HadbObject> items = await this.ReliableSearchDataListByLabelsFromDatabaseImplAsync(labels, typeName, cancel);

            items = items.Where(x => x.Deleted == false);

            List<HadbObject> ret = new List<HadbObject>();

            items._DoForEach(x =>
            {
                var obj = this.MemDb!.ReliableAddObject(x);

                ret.Add(obj);
            });

            return ret;
        }

        public async Task<HadbObject?> ReliableDeleteAsync<T>(string uid, CancellationToken cancel = default) where T : HadbData
            => await ReliableDeleteAsync(uid, typeof(T).Name, cancel);

        public async Task<HadbObject?> ReliableDeleteAsync(string uid, string typeName, CancellationToken cancel = default)
        {
            CheckIfReadyForReliable();

            HadbObject? ret = await this.ReliableDeleteDataFromDatabaseImplAsync(uid, typeName, cancel);

            if (ret == null)
            {
                return null;
            }

            bool ok = this.MemDb!.ReliableDeleteObject(ret);

            //$"ReliableDeleteObject: {ok}"._Debug();

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
    }
}

#endif

