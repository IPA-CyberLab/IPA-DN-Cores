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
using System.Security.Authentication;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Newtonsoft.Json.Converters;

namespace IPA.Cores.Basic;

public class MikakaDDnsDynConfig : HadbDynamicConfig
{
    protected override void NormalizeImpl()
    {
        base.NormalizeImpl();
    }
}

public class MikakaDDnsMemDb : HadbMemDataBase
{
    protected override List<Type> GetDefinedUserDataTypesImpl()
    {
        List<Type> ret = new List<Type>();
        return ret;
    }

    protected override List<Type> GetDefinedUserLogTypesImpl()
    {
        List<Type> ret = new List<Type>();
        return ret;
    }
}

public class MikakaDDnsHiveSettings : HadbBasedServiceHiveSettingsBase { }

public class MikakaDDnsServiceStartupParam : HadbBasedServiceStartupParam
{
    public MikakaDDnsServiceStartupParam(string hiveDataName = "MikakaDDnsService", string defaultHadbSystemName = "MIKAKA_DDNS") : base(hiveDataName, defaultHadbSystemName)
    {
    }
}

public class MikakaDDnsService : HadbBasedServiceBase<MikakaDDnsMemDb, MikakaDDnsDynConfig, MikakaDDnsHiveSettings>
{
    public MikakaDDnsService(MikakaDDnsServiceStartupParam? startupParam = null) : base(startupParam ?? new MikakaDDnsServiceStartupParam())
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

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }

    protected override MikakaDDnsDynConfig CreateInitialDynamicConfigImpl()
    {
        return new MikakaDDnsDynConfig();
    }
}






public class HadbBasedServiceStartupParam
{
    public string HiveDataName { get; }
    public string DefaultHadbSystemName { get; }

    public HadbBasedServiceStartupParam(string hiveDataName, string defaultHadbSystemName)
    {
        this.HiveDataName = hiveDataName;
        this.DefaultHadbSystemName = defaultHadbSystemName;
    }
}

public abstract class HadbBasedServiceHiveSettingsBase : INormalizable
{
    public string HadbSystemName = "";

    [JsonConverter(typeof(StringEnumConverter))]
    public HadbOptionFlags HadbOptionFlags = HadbOptionFlags.None;

    public string HadbSqlServerHostname = "";
    public int HadbSqlServerPort = 0;
    public bool HadbSqlServerDisablePooling = false;

    public string HadbSqlDatabaseName = "";

    public string HadbSqlDatabaseReaderUsername = "";
    public string HadbSqlDatabaseReaderPassword = "";

    public string HadbSqlDatabaseWriterUsername = "";
    public string HadbSqlDatabaseWriterPassword = "";

    public string HadbBackupFilePathOverride = "";
    public string HadbBackupDynamicConfigFilePathOverride = "";

    public void Normalize()
    {
        this.HadbSystemName = this.HadbSystemName._FilledOrDefault(this.GetType().Name);

        this.HadbSqlServerHostname = this.HadbSqlServerHostname._FilledOrDefault("__SQL_SERVER_HOSTNAME_HERE__");

        if (this.HadbSqlServerPort <= 0) this.HadbSqlServerPort = Consts.Ports.MsSqlServer;

        this.HadbSqlDatabaseName = this.HadbSqlDatabaseName._FilledOrDefault("HADB001");

        this.HadbSqlDatabaseReaderUsername = this.HadbSqlDatabaseReaderUsername._FilledOrDefault("sql_hadb001_reader");
        this.HadbSqlDatabaseReaderPassword = this.HadbSqlDatabaseReaderPassword._FilledOrDefault("sql_hadb_reader_default_password");

        this.HadbSqlDatabaseWriterUsername = this.HadbSqlDatabaseWriterUsername._FilledOrDefault("sql_hadb001_writer");
        this.HadbSqlDatabaseWriterPassword = this.HadbSqlDatabaseWriterPassword._FilledOrDefault("sql_hadb_writer_default_password");
    }
}

public abstract class HadbBasedServiceBase<TMemDb, TDynConfig, THiveSettings> : AsyncService
    where TMemDb : HadbMemDataBase, new()
    where TDynConfig : HadbDynamicConfig
    where THiveSettings : HadbBasedServiceHiveSettingsBase, new()
{
    public DateTimeOffset BootDateTime { get; } = DtOffsetNow; // サービス起動日時
    public HadbBase<TMemDb, TDynConfig> Hadb { get; }
    public HadbBasedServiceStartupParam StartupParam { get; }

    // Hive
    readonly HiveData<THiveSettings> _SettingsHive;

    // Hive 設定へのアクセスを容易にするための自動プロパティ
    CriticalSection ManagedSettingsLock => this._SettingsHive.DataLock;
    THiveSettings ManagedSettings => this._SettingsHive.ManagedData;
    public THiveSettings SettingsFastSnapshot => this._SettingsHive.CachedFastSnapshot;

    public HadbBasedServiceBase(HadbBasedServiceStartupParam startupParam)
    {
        try
        {
            this.StartupParam = startupParam;

            this._SettingsHive = new HiveData<THiveSettings>(Hive.SharedLocalConfigHive,
                this.StartupParam.HiveDataName,
                () => new THiveSettings { HadbSystemName = this.StartupParam.DefaultHadbSystemName },
                HiveSyncPolicy.AutoReadWriteFile,
                HiveSerializerSelection.RichJson);

            this.Hadb = this.CreateHadb();
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }


    public class HadbSys : HadbSqlBase<TMemDb, TDynConfig>
    {
        public HadbSys(HadbSqlSettings settings, TDynConfig dynamicConfig) : base(settings, dynamicConfig) { }
    }

    protected abstract TDynConfig CreateInitialDynamicConfigImpl();

    protected virtual HadbBase<TMemDb, TDynConfig> CreateHadb()
    {
        var s = this.SettingsFastSnapshot;

        HadbSqlSettings sqlSettings = new HadbSqlSettings(
            s.HadbSystemName,
            new SqlDatabaseConnectionSetting(s.HadbSqlServerHostname, s.HadbSqlDatabaseName, s.HadbSqlDatabaseReaderUsername, s.HadbSqlDatabaseReaderPassword, !s.HadbSqlServerDisablePooling, s.HadbSqlServerPort),
            new SqlDatabaseConnectionSetting(s.HadbSqlServerHostname, s.HadbSqlDatabaseName, s.HadbSqlDatabaseWriterUsername, s.HadbSqlDatabaseWriterPassword, !s.HadbSqlServerDisablePooling, s.HadbSqlServerPort),
            optionFlags: s.HadbOptionFlags,
            backupDataFile: s.HadbBackupFilePathOverride._IsFilled() ? new FilePath(s.HadbBackupFilePathOverride) : null,
            backupDynamicConfigFile: s.HadbBackupDynamicConfigFilePathOverride._IsFilled() ? new FilePath(s.HadbBackupDynamicConfigFilePathOverride) : null
            );

        return new HadbSys(sqlSettings, CreateInitialDynamicConfigImpl());
    }

    Once StartedFlag;

    public void Start()
    {
        StartedFlag.FirstCallOrThrowException();

        this.Hadb.Start();
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.Hadb._DisposeSafeAsync(ex);

            await this._SettingsHive._DisposeSafeAsync2();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

#endif

