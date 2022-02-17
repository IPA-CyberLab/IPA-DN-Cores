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
using Newtonsoft.Json.Converters;

namespace IPA.Cores.Basic;








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
    public bool HadbSqlServerDisableConnectionPooling = false;

    public string HadbSqlDatabaseName = "";

    public string HadbSqlDatabaseReaderUsername = "";
    public string HadbSqlDatabaseReaderPassword = "";

    public string HadbSqlDatabaseWriterUsername = "";
    public string HadbSqlDatabaseWriterPassword = "";

    public string HadbBackupFilePathOverride = "";
    public string HadbBackupDynamicConfigFilePathOverride = "";

    public int LazyUpdateParallelQueueCount = 0;

    public List<string> DnsResolverServerIpAddressList = new List<string>();

    public virtual void NormalizeImpl() { }

    public void Normalize()
    {
        this.NormalizeImpl();

        this.HadbSystemName = this.HadbSystemName._FilledOrDefault(this.GetType().Name);

        this.HadbSqlServerHostname = this.HadbSqlServerHostname._FilledOrDefault("__SQL_SERVER_HOSTNAME_HERE__"); ;

        if (this.HadbSqlServerPort <= 0) this.HadbSqlServerPort = Consts.Ports.MsSqlServer;

        this.HadbSqlDatabaseName = this.HadbSqlDatabaseName._FilledOrDefault("HADB001");

        this.HadbSqlDatabaseReaderUsername = this.HadbSqlDatabaseReaderUsername._FilledOrDefault("sql_hadb001_reader");
        this.HadbSqlDatabaseReaderPassword = this.HadbSqlDatabaseReaderPassword._FilledOrDefault("sql_hadb_reader_default_password");

        this.HadbSqlDatabaseWriterUsername = this.HadbSqlDatabaseWriterUsername._FilledOrDefault("sql_hadb001_writer");
        this.HadbSqlDatabaseWriterPassword = this.HadbSqlDatabaseWriterPassword._FilledOrDefault("sql_hadb_writer_default_password");

        if (this.DnsResolverServerIpAddressList == null || DnsResolverServerIpAddressList.Any() == false)
        {
            this.DnsResolverServerIpAddressList = new List<string>();

            this.DnsResolverServerIpAddressList.Add("8.8.8.8");
            this.DnsResolverServerIpAddressList.Add("1.1.1.1");
        }

        if (this.LazyUpdateParallelQueueCount <= 0) this.LazyUpdateParallelQueueCount = 32;
        if (this.LazyUpdateParallelQueueCount > 256) this.LazyUpdateParallelQueueCount = Consts.Numbers.HadbMaxLazyUpdateParallelQueueCount;
    }
}

public abstract class HadbBasedServiceBase<TMemDb, TDynConfig, THiveSettings> : AsyncService
    where TMemDb : HadbMemDataBase, new()
    where TDynConfig : HadbDynamicConfig
    where THiveSettings : HadbBasedServiceHiveSettingsBase, new()
{
    public DateTimeOffset BootDateTime { get; } = DtOffsetNow; // サービス起動日時
    public HadbBase<TMemDb, TDynConfig> Hadb { get; }
    public HadbBasedServiceStartupParam ServiceStartupParam { get; }

    public DnsResolver DnsResolver {get;}

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
            this.ServiceStartupParam = startupParam;

            this._SettingsHive = new HiveData<THiveSettings>(Hive.SharedLocalConfigHive,
                this.ServiceStartupParam.HiveDataName,
                () => new THiveSettings { HadbSystemName = this.ServiceStartupParam.DefaultHadbSystemName },
                HiveSyncPolicy.AutoReadWriteFile,
                HiveSerializerSelection.RichJson);

            this.DnsResolver = new DnsClientLibBasedDnsResolver(new DnsResolverSettings(flags: DnsResolverFlags.RoundRobinServers,
                dnsServersList: this.SettingsFastSnapshot.DnsResolverServerIpAddressList.Select(x => x._ToIPEndPoint(Consts.Ports.Dns, noExceptionAndReturnNull: true))
                .Where(x => x != null)!));

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
            new SqlDatabaseConnectionSetting(s.HadbSqlServerHostname, s.HadbSqlDatabaseName, s.HadbSqlDatabaseReaderUsername, s.HadbSqlDatabaseReaderPassword, !s.HadbSqlServerDisableConnectionPooling, s.HadbSqlServerPort),
            new SqlDatabaseConnectionSetting(s.HadbSqlServerHostname, s.HadbSqlDatabaseName, s.HadbSqlDatabaseWriterUsername, s.HadbSqlDatabaseWriterPassword, !s.HadbSqlServerDisableConnectionPooling, s.HadbSqlServerPort),
            optionFlags: s.HadbOptionFlags,
            backupDataFile: s.HadbBackupFilePathOverride._IsFilled() ? new FilePath(s.HadbBackupFilePathOverride) : null,
            backupDynamicConfigFile: s.HadbBackupDynamicConfigFilePathOverride._IsFilled() ? new FilePath(s.HadbBackupDynamicConfigFilePathOverride) : null,
            lazyUpdateParallelQueueCount: s.LazyUpdateParallelQueueCount
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

            await this.DnsResolver._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

















public static partial class DevCoresConfig
{
    public static partial class EasyDnsResponderSettings
    {
        public static Copenhagen<int> Default_RefreshIntervalSecs = 60;
        public static Copenhagen<int> Default_RetryIntervalSecs = 60;
        public static Copenhagen<int> Default_ExpireIntervalSecs = 88473600;
        public static Copenhagen<int> Default_NegativeCacheTtlSecs = 10;

        public static Copenhagen<ushort> Default_MxPreference = 100;
    }
}


// 以下はユーザー設定用構造体 (内部データ構造ではない)。ユーザー開発者が組み立てしやすいように、単なるクラスの列になっているのである。
public class EasyDnsResponderRecordSettings
{
    public int TtlSecs { get; set; } = 60;
}

[Flags]
public enum EasyDnsResponderRecordType
{
    None = 0,
    Any,
    A,
    AAAA,
    NS,
    CNAME,
    SOA,
    PTR,
    MX,
    TXT,
}

[Flags]
public enum EasyDnsResponderRecordAttribute
{
    None = 0,
    DynamicRecord = 1,
}

public class EasyDnsResponderRecord
{
    public EasyDnsResponderRecordAttribute Attribute { get; set; } = EasyDnsResponderRecordAttribute.None;

    public string Name { get; set; } = ""; // アスタリスク文字を用いたワイルドカード指定可能。

    public EasyDnsResponderRecordType Type { get; set; } = EasyDnsResponderRecordType.None;

    public string Contents { get; set; } = ""; // DynamicRecord の場合はコールバック ID (任意の文字列) を指定

    public EasyDnsResponderRecordSettings? Settings { get; set; } = null;
}

public class EasyDnsResponderZone
{
    public string DomainName { get; set; } = "";

    public List<EasyDnsResponderRecord> RecordList { get; set; } = new List<EasyDnsResponderRecord>();

    public EasyDnsResponderRecordSettings? DefaultSettings { get; set; } = null;
}

public class EasyDnsResponderSettings
{
    public List<EasyDnsResponderZone> ZoneList { get; set; } = new List<EasyDnsResponderZone>();

    public EasyDnsResponderRecordSettings? DefaultSettings { get; set; } = null;
}

// ダイナミックレコードのコールバック関数に渡されるリクエストデータ
public class EasyDnsResponderDynamicRecordCallbackRequest
{
    public EasyDnsResponderZone Zone { init; get; } = null!;
    public EasyDnsResponderRecord Record { init; get; } = null!;

    public EasyDnsResponderRecordType ExpectedRecordType { init; get; }
    public string RequestFqdn { init; get; } = null!;
    public string RequestHostName { init; get; } = null!;
    public string CallbackId { init; get; } = null!;
}

// ダイナミックレコードのコールバック関数で返却すべきデータ
public class EasyDnsResponderDynamicRecordCallbackResult
{
    public List<IPAddress>? IPAddressList { get; set; } // A, AAAA の場合
    public List<DomainName>? DomainNameList { get; set; } // CNAME, MX, NS, PTR の場合
    public List<ushort>? MxPreferenceList { get; set; } // MX の場合の Preference 値のリスト
    public List<string>? TextList { get; set; } // TXT の場合

    public EasyDnsResponderRecordSettings? Settings { get; set; } // TTL 等
}

public class EasyDnsResponder
{
    // ダイナミックレコードのコールバック関数
    public Func<EasyDnsResponderDynamicRecordCallbackRequest, EasyDnsResponderDynamicRecordCallbackResult?>? Callback { get; set; }

    // 内部データセット
    public class Record_A : Record
    {
        public IPAddress IPv4Address;

        public Record_A(Zone parent, EasyDnsResponderRecord src) : base(parent, src)
        {
            string tmp = src.Contents._NonNullTrim();
            if (tmp._IsEmpty()) throw new CoresLibException("Contents is empty.");

            this.IPv4Address = IPAddress.Parse(tmp);

            if (this.IPv4Address.AddressFamily != AddressFamily.InterNetwork)
                throw new CoresLibException($"AddressFamily of '{tmp}' is not IPv4.");
        }

        public Record_A(Zone parent, EasyDnsResponderRecordSettings settings, string nameNormalized, IPAddress ipv4address) : base(parent, EasyDnsResponderRecordType.A, settings, nameNormalized)
        {
            this.IPv4Address = ipv4address;

            if (this.IPv4Address.AddressFamily != AddressFamily.InterNetwork)
                throw new CoresLibException($"AddressFamily of '{this.IPv4Address}' is not IPv4.");
        }

        protected override string ToStringForCompareImpl()
        {
            return this.IPv4Address.ToString();
        }
    }

    public class Record_AAAA : Record
    {
        public IPAddress IPv6Address;

        public Record_AAAA(Zone parent, EasyDnsResponderRecord src) : base(parent, src)
        {
            string tmp = src.Contents._NonNullTrim();
            if (tmp._IsEmpty()) throw new CoresLibException("Contents is empty.");

            this.IPv6Address = IPAddress.Parse(tmp);
            this.IPv6Address.ScopeId = 0;

            if (this.IPv6Address.AddressFamily != AddressFamily.InterNetworkV6)
                throw new CoresLibException($"AddressFamily of '{tmp}' is not IPv6.");
        }

        public Record_AAAA(Zone parent, EasyDnsResponderRecordSettings settings, string nameNormalized, IPAddress ipv6address) : base(parent, EasyDnsResponderRecordType.AAAA, settings, nameNormalized)
        {
            this.IPv6Address = ipv6address;

            if (this.IPv6Address.AddressFamily != AddressFamily.InterNetworkV6)
                throw new CoresLibException($"AddressFamily of '{this.IPv6Address}' is not IPv6.");
        }

        protected override string ToStringForCompareImpl()
        {
            return this.IPv6Address.ToString();
        }
    }

    public class Record_NS : Record
    {
        public DomainName ServerName;

        public Record_NS(Zone parent, EasyDnsResponderRecord src) : base(parent, src)
        {
            if (this.Name._InStr("*") || this.Name._InStr("?"))
            {
                throw new CoresLibException($"NS record doesn't allow wildcard names. Specified name: '{this.Name}'");
            }

            string tmp = src.Contents._NonNullTrim();
            if (tmp._IsEmpty()) throw new CoresLibException("Contents is empty.");

            this.ServerName = DomainName.Parse(tmp);

            if (this.ServerName.IsEmptyDomain()) throw new CoresLibException("NS server field is empty.");
        }

        public Record_NS(Zone parent, EasyDnsResponderRecordSettings settings, string nameNormalized, DomainName serverName) : base(parent, EasyDnsResponderRecordType.NS, settings, nameNormalized)
        {
            this.ServerName = serverName;
        }

        protected override string ToStringForCompareImpl()
        {
            return this.ServerName.ToString();
        }
    }

    public class Record_CNAME : Record
    {
        public DomainName CName;

        public Record_CNAME(Zone parent, EasyDnsResponderRecord src) : base(parent, src)
        {
            string tmp = src.Contents._NonNullTrim();
            if (tmp._IsEmpty()) throw new CoresLibException("Contents is empty.");

            this.CName = DomainName.Parse(tmp);

            if (this.CName.IsEmptyDomain()) throw new CoresLibException("CNAME field is empty.");
        }

        public Record_CNAME(Zone parent, EasyDnsResponderRecordSettings settings, string nameNormalized, DomainName cname) : base(parent, EasyDnsResponderRecordType.CNAME, settings, nameNormalized)
        {
            this.CName = cname;
        }

        protected override string ToStringForCompareImpl()
        {
            return this.CName.ToString();
        }
    }

    public class Record_SOA : Record
    {
        public DomainName MasterName;
        public DomainName ResponsibleName;
        public uint SerialNumber;
        public int RefreshIntervalSecs;
        public int RetryIntervalSecs;
        public int ExpireIntervalSecs;
        public int NegativeCacheTtlSecs;

        public Record_SOA(Zone parent, EasyDnsResponderRecord src) : base(parent, src)
        {
            if (this.Name._IsFilled()) throw new CoresLibException($"SOA record doesn't allow Name field. Name is not empty: '{this.Name}'");

            string[] tokens = src.Contents._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ';', ',', ' ', '\t');

            if (tokens.Length == 0) throw new CoresLibException("Contents is empty.");

            this.MasterName = DomainName.Parse(tokens.ElementAt(0));
            this.ResponsibleName = DomainName.Parse(tokens._ElementAtOrDefaultStr(1, "somebody.example.org."));

            this.SerialNumber = tokens.ElementAtOrDefault(2)._ToUInt();
            if (this.SerialNumber <= 0) this.SerialNumber = 1;

            this.RefreshIntervalSecs = tokens.ElementAtOrDefault(3)._ToInt();
            if (this.RefreshIntervalSecs <= 0) this.RefreshIntervalSecs = DevCoresConfig.EasyDnsResponderSettings.Default_RefreshIntervalSecs;

            this.RetryIntervalSecs = tokens.ElementAtOrDefault(4)._ToInt();
            if (this.RetryIntervalSecs <= 0) this.RetryIntervalSecs = DevCoresConfig.EasyDnsResponderSettings.Default_RetryIntervalSecs;

            this.ExpireIntervalSecs = tokens.ElementAtOrDefault(5)._ToInt();
            if (this.ExpireIntervalSecs <= 0) this.ExpireIntervalSecs = DevCoresConfig.EasyDnsResponderSettings.Default_ExpireIntervalSecs;

            this.NegativeCacheTtlSecs = tokens.ElementAtOrDefault(6)._ToInt();
            if (this.NegativeCacheTtlSecs <= 0) this.NegativeCacheTtlSecs = DevCoresConfig.EasyDnsResponderSettings.Default_NegativeCacheTtlSecs;
        }

        protected override string ToStringForCompareImpl()
        {
            return $"{MasterName} {ResponsibleName} {SerialNumber} {RefreshIntervalSecs} {RetryIntervalSecs} {ExpireIntervalSecs} {NegativeCacheTtlSecs}";
        }
    }

    public class Record_PTR : Record
    {
        public DomainName Ptr;

        public Record_PTR(Zone parent, EasyDnsResponderRecord src) : base(parent, src)
        {
            string tmp = src.Contents._NonNullTrim();
            if (tmp._IsEmpty()) throw new CoresLibException("Contents is empty.");

            this.Ptr = DomainName.Parse(tmp);

            if (this.Ptr.IsEmptyDomain()) throw new CoresLibException("CNAME field is empty.");
        }

        public Record_PTR(Zone parent, EasyDnsResponderRecordSettings settings, string nameNormalized, DomainName ptr) : base(parent, EasyDnsResponderRecordType.PTR, settings, nameNormalized)
        {
            this.Ptr = ptr;
        }

        protected override string ToStringForCompareImpl()
        {
            return this.Ptr.ToString();
        }
    }

    public class Record_MX : Record
    {
        public DomainName MailServer;
        public ushort Preference;

        public Record_MX(Zone parent, EasyDnsResponderRecord src) : base(parent, src)
        {
            string[] tokens = src.Contents._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ';', ',');

            if (tokens.Length == 0) throw new CoresLibException("Contents is empty.");

            this.MailServer = DomainName.Parse(tokens.ElementAt(0));

            this.Preference = (ushort)tokens.ElementAtOrDefault(1)._ToUInt();
            if (this.Preference <= 0) this.Preference = DevCoresConfig.EasyDnsResponderSettings.Default_MxPreference;
        }

        public Record_MX(Zone parent, EasyDnsResponderRecordSettings settings, string nameNormalized, DomainName mailServer, ushort preference) : base(parent, EasyDnsResponderRecordType.MX, settings, nameNormalized)
        {
            this.MailServer = mailServer;
            this.Preference = preference;
        }

        protected override string ToStringForCompareImpl()
        {
            return this.MailServer.ToString() + " " + this.Preference;
        }
    }

    public class Record_TXT : Record
    {
        public string TextData;

        public Record_TXT(Zone parent, EasyDnsResponderRecord src) : base(parent, src)
        {
            this.TextData = src.Contents._NonNull();
        }

        public Record_TXT(Zone parent, EasyDnsResponderRecordSettings settings, string nameNormalized, string textData) : base(parent, EasyDnsResponderRecordType.TXT, settings, nameNormalized)
        {
            this.TextData = textData._NonNull();
        }

        protected override string ToStringForCompareImpl()
        {
            return this.TextData;
        }
    }

    public class Record_Dynamic : Record
    {
        public string CallbackId;

        public Record_Dynamic(Zone parent, EasyDnsResponderRecord src) : base(parent, src)
        {
            switch (src.Type)
            {
                case EasyDnsResponderRecordType.A:
                case EasyDnsResponderRecordType.AAAA:
                case EasyDnsResponderRecordType.CNAME:
                case EasyDnsResponderRecordType.MX:
                case EasyDnsResponderRecordType.NS:
                case EasyDnsResponderRecordType.PTR:
                case EasyDnsResponderRecordType.TXT:
                    this.CallbackId = src.Contents._NonNull();

                    if (this.CallbackId._IsEmpty()) throw new CoresLibException("Callback ID is empty.");
                    return;
            }

            throw new CoresLibException($"Invalid Dynamic Record Type '{this.Name}': {src.Type}");
        }

        protected override string ToStringForCompareImpl()
        {
            return this.CallbackId;
        }
    }

    public abstract class Record
    {
        [JsonIgnore]
        public Zone ParentZone;
        public string Name;
        [JsonConverter(typeof(StringEnumConverter))]
        public EasyDnsResponderRecordType Type;
        public EasyDnsResponderRecordSettings Settings;

        [JsonIgnore]
        public EasyDnsResponderRecord? SrcRecord;

        protected abstract string ToStringForCompareImpl();

        readonly CachedProperty<string>? _StringForCompareCache;
        public string ToStringForCompare() => _StringForCompareCache ?? "";

        public Record(Zone parent, EasyDnsResponderRecordType type, EasyDnsResponderRecordSettings settings, string nameNormalized)
        {
            this.ParentZone = parent;
            this.Type = type;
            this.Settings = settings;
            this.Name = nameNormalized;
        }

        public Record(Zone parent, EasyDnsResponderRecord src)
        {
            this.ParentZone = parent;

            this.Settings = (src.Settings ?? parent.Settings)._CloneDeep();

            this.Name = src.Name._NormalizeFqdn();

            this.Type = src.Type;

            this.SrcRecord = src._CloneDeep();

            this._StringForCompareCache = new CachedProperty<string>(getter: () =>
            {
                return $"{Name} {Type} {this.ToStringForCompareImpl()}";
            });
        }

        public static Record CreateFrom(Zone parent, EasyDnsResponderRecord src)
        {
            if (src.Attribute.Bit(EasyDnsResponderRecordAttribute.DynamicRecord))
            {
                // ダイナミックレコード
                return new Record_Dynamic(parent, src);
            }

            switch (src.Type)
            {
                case EasyDnsResponderRecordType.A:
                    return new Record_A(parent, src);

                case EasyDnsResponderRecordType.AAAA:
                    return new Record_AAAA(parent, src);

                case EasyDnsResponderRecordType.NS:
                    return new Record_NS(parent, src);

                case EasyDnsResponderRecordType.CNAME:
                    return new Record_CNAME(parent, src);

                case EasyDnsResponderRecordType.SOA:
                    return new Record_SOA(parent, src);

                case EasyDnsResponderRecordType.PTR:
                    return new Record_PTR(parent, src);

                case EasyDnsResponderRecordType.MX:
                    return new Record_MX(parent, src);

                case EasyDnsResponderRecordType.TXT:
                    return new Record_TXT(parent, src);
            }

            throw new CoresLibException($"Unknown record type: {src.Type}");
        }
    }

    // 内部ゾーンデータ
    public class Zone
    {
        public DataSet ParentDataSet;
        public string DomainFqdn;
        public EasyDnsResponderRecordSettings Settings;
        public EasyDnsResponderZone SrcZone;

        public List<Record> RecordList = new List<Record>();
        public StrDictionary<List<Record>> RecordDictByName = new StrDictionary<List<Record>>();

        public HashSet<string> SubDomainList = new HashSet<string>(); // レコードが 1 つ以上存在するサブドメインのリスト

        public Record_SOA SOARecord;

        public List<Record> WildcardAnyRecordList = new List<Record>(); // "*" という名前のワイルドカード
        public KeyValueList<string, List<Record>> WildcardEndWithRecordList = new KeyValueList<string, List<Record>>(); // "*abc" または "*.abc" という先頭ワイルドカード
        public KeyValueList<string, List<Record>> WildcardInStrRecordList = new KeyValueList<string, List<Record>>(); // "*abc*" とか "abc*def" とか "abc?def" という複雑なワイルドカード

        public List<Record> NSRecordList = new List<Record>(); // このゾーンそのものの NS レコード
        public StrDictionary<List<Record>> NSDelegationRecordList = new StrDictionary<List<Record>>(); // サブドメイン権限委譲レコード

        public bool Has_WildcardAnyRecordList = false;
        public bool Has_WildcardEndWithRecordList = false;
        public bool Has_WildcardInStrRecordList = false;
        public bool Has_WildcardNSDelegationRecordList = false;

        public Zone(DataSet parent, EasyDnsResponderZone src)
        {
            this.ParentDataSet = parent;

            this.Settings = (src.DefaultSettings ?? parent.Settings)._CloneDeep();

            this.DomainFqdn = src.DomainName._NormalizeFqdn();

            if (this.DomainFqdn._IsEmpty())
            {
                throw new CoresLibException("Invalid FQDN in Zone");
            }

            Record_SOA? soa = null;

            // レコード情報のコンパイル
            foreach (var srcRecord in src.RecordList)
            {
                var record = Record.CreateFrom(this, srcRecord);

                if (record.Type != EasyDnsResponderRecordType.SOA)
                {
                    string tmp1 = record.ToStringForCompare();
                    if (this.RecordList.Where(x => x.ToStringForCompare() == tmp1).Any() == false)
                    {
                        // 全く同じ内容のレコードが 2 つ追加されることは禁止する。最初の 1 つ目のみをリストに追加するのである。
                        this.RecordList.Add(record);
                    }
                }
                else
                {
                    // SOA レコード
                    if (soa != null)
                    {
                        // SOA レコードは 2 つ以上指定できない
                        throw new CoresLibException("SOA record is duplicating.");
                    }

                    soa = (Record_SOA)record;
                }
            }

            // SOA レコードが無い場合は、適当にでっち上げる
            if (soa == null)
            {
                soa = new Record_SOA(this,
                    new EasyDnsResponderRecord
                    {
                        Type = EasyDnsResponderRecordType.SOA,
                        Attribute = EasyDnsResponderRecordAttribute.None,
                        Contents = this.DomainFqdn,
                    });
            }

            this.SOARecord = soa;

            this.SubDomainList.Add(""); // サブドメインリストにまずこのゾーン自体を追加する

            // レコード情報を検索を高速化するためにハッシュテーブル等として並べて保持する
            foreach (var r in this.RecordList)
            {
                if (r.Type == EasyDnsResponderRecordType.SOA) { } // SOA レコードは追加しない
                else if (r.Type == EasyDnsResponderRecordType.NS) { } // NS レコードは後で特殊な処理を行なう
                else
                {
                    // 普通の種類のレコード (A など)
                    if (r.Name._InStr("*") || r.Name._InStr("?"))
                    {
                        // ワイルドカードレコード
                        if (r.Name == "*")
                        {
                            // any ワイルドカード
                            this.WildcardAnyRecordList.Add(r);
                        }
                        else if (r.Name.StartsWith("*") && r.Name.Substring(1)._InStr("*") == false && r.Name.Substring(1)._InStr("?") == false && r.Name.Substring(1).Length >= 1)
                        {
                            // 先頭ワイルドカード (*abc)
                            this.WildcardEndWithRecordList.GetSingleOrNew(r.Name.Substring(1), () => new List<Record>(), StrComparer.IgnoreCaseComparer).Add(r);
                        }
                        else
                        {
                            // 複雑なワイルドカード (abc*def とか abc*def といったもの)
                            this.WildcardInStrRecordList.GetSingleOrNew(r.Name, () => new List<Record>(), StrComparer.IgnoreCaseComparer).Add(r);
                        }
                    }
                    else
                    {
                        // 非ワイルドカードレコード
                        this.RecordDictByName._GetOrNew(r.Name).Add(r);

                        // レコード名が a.b.c の場合、 a.b.c, b.c, c をサブドメイン存在リストに追加する
                        var labels = r.Name.Split(".").AsSpan();
                        int numLabels = labels.Length;
                        for (int i = 0; i < numLabels; i++)
                        {
                            this.SubDomainList.Add(labels.Slice(i)._Combine("."));
                        }
                    }
                }
            }

            foreach (var r in this.RecordList.Where(x => x.Type == EasyDnsResponderRecordType.NS))
            {
                // NS レコードに関する処理
                if (r.Name._IsEmpty())
                {
                    // これは、このゾーンそのものに関する NS 情報である。Name は空文字である。
                    this.NSRecordList.Add(r);
                }
                else
                {
                    // これは、サブドメインに関する NS 情報である。つまり、Name にはサブドメイン名が入っている。
                    // これは、DNS における権限委譲 (delegate) と呼ばれる。
                    // たとえば abc.def である。
                    // そこで、まずこの NS サブドメインが定義済みサブドメイン (普通のサブドメイン) の一覧と重複しないかどうか検査する。
                    if (this.SubDomainList.Contains(r.Name))
                    {
                        // 一致するのでエラーとする。つまり、普通のサブドメインが存在する場合、同じ名前の NS サブドメインの登録は禁止するのである。
                        throw new CoresLibException($"NS record Name: {r.Name} is duplicating with the existing sub domain record.");
                    }

                    // 問題なければ、NS 権限委譲レコードとして追加する。
                    this.NSDelegationRecordList._GetOrNew(r.Name).Add(r);
                }
            }

            // 先頭ワイルドカードリストと複雑なワイルドカードリストは、文字列長で逆ソートする。
            // つまり、できるだけ文字列長が長い候補が優先してマッチするようにするのである。
            this.WildcardEndWithRecordList._DoSortBy(x => x.OrderByDescending(y => y.Key.Length).ThenByDescending(y => y.Key));
            this.WildcardInStrRecordList._DoSortBy(x => x.OrderByDescending(y => y.Key.Length).ThenByDescending(y => y.Key));

            this.Has_WildcardAnyRecordList = this.WildcardAnyRecordList.Any();
            this.Has_WildcardEndWithRecordList = this.WildcardEndWithRecordList.Any();
            this.Has_WildcardInStrRecordList = this.WildcardInStrRecordList.Any();
            this.Has_WildcardNSDelegationRecordList = this.NSDelegationRecordList.Any();

            this.SrcZone = src._CloneDeep();
        }

        public SearchResult Search(SearchRequest request, string hostLabelNormalized, ReadOnlyMemory<string> hostLabelSpan)
        {
            List<Record>? answers = null;

            // まず完全一致するものがないか確かめる
            if (this.RecordDictByName.TryGetValue(hostLabelNormalized, out List<Record>? found))
            {
                // 完全一致あり
                answers = found;
            }

            if (answers == null)
            {
                if (hostLabelSpan.Length >= 1)
                {
                    // 完全一致がなければ、次に NS レコードによって権限委譲されているサブドメインがあるかどうか確認する。
                    // この場合、クエリサブドメイン名が a.b.c の場合、
                    // a.b.c、b.c、c の順で検索し、最初に発見されたものを NS 委譲されているサブドメインとして扱う。
                    for (int i = 0; i < hostLabelSpan.Length; i++)
                    {
                        if (this.NSDelegationRecordList.TryGetValue(hostLabelSpan.Slice(i)._Combine("."), out List<Record>? found2))
                        {
                            // 権限委譲ドメイン情報が見つかった
                            SearchResult ret2 = new SearchResult
                            {
                                SOARecord = this.SOARecord,
                                Zone = this,
                                RecordList = found2,
                                ResultType = SearchResultType.NsDelegation,
                                RequestHostName = hostLabelNormalized,
                            };

                            return ret2;
                        }
                    }
                }
            }

            if (answers == null)
            {
                if (this.Has_WildcardEndWithRecordList) // 高速化 (効果があるかどうかは不明)
                {
                    // もし完全一致するものが 1 つも無ければ、
                    // 先頭ワイルドカード一致を検索し、一致するものがないかどうか調べる
                    foreach (var r in this.WildcardEndWithRecordList)
                    {
                        if (hostLabelNormalized.EndsWith(r.Key))
                        {
                            // 後方一致あり
                            answers = r.Value;
                            break;
                        }
                    }
                }
            }

            if (answers == null)
            {
                if (this.Has_WildcardInStrRecordList) // 高速化 (効果があるかどうかは不明)
                {
                    // もし完全一致または後方一致するものが 1 つも無ければ、
                    // 複雑なワイルドカード一致を検索し、一致するものがないかどうか調べる
                    foreach (var r in this.WildcardInStrRecordList)
                    {
                        if (hostLabelNormalized._WildcardMatch(r.Key))
                        {
                            // 一致あり
                            answers = r.Value;
                            break;
                        }
                    }
                }
            }

            if (answers == null)
            {
                if (this.Has_WildcardAnyRecordList) // 高速化 (効果があるかどうかは不明)
                {
                    // これまででまだ一致するものが無ければ、
                    // any アスタリスクレコードがあればそれを返す
                    answers = this.WildcardAnyRecordList;
                }
            }

            // この状態でまだ一致するものがなければ、サブドメイン一覧に一致する場合は空リストを返し、
            // いずれのサブドメインにも一致しない場合は null を返す。(null の場合、DNS 的には NXDOMAIN を意味することとする。)
            if (answers == null)
            {
                if (hostLabelSpan.Length == 0)
                {
                    // サブドメイン名がない (つまり、このドメインと全く一緒) の場合は、空リストを返す。
                    answers = new List<Record>();
                }
                else
                {
                    for (int i = 0; i < hostLabelSpan.Length; i++)
                    {
                        if (this.SubDomainList.Contains(hostLabelSpan.Slice(i)._Combine(".")))
                        {
                            // いずれかの階層でサブドメインリストが見つかった
                            answers = new List<Record>();
                            break;
                        }
                    }

                    // いずれの階層でもサブドメインリストが見つからなかった場合は、answers は null のままとなる。
                }
            }

            if (answers != null)
            {
                // このゾーン名を完全一致でクエリをしてきている場合、このゾーンに関する NS レコードも追加する
                if (hostLabelSpan.Length == 0)
                {
                    foreach (var ns in this.NSRecordList)
                    {
                        answers.Add(ns);
                    }
                }
            }

            SearchResult ret = new SearchResult
            {
                RecordList = answers,
                SOARecord = this.SOARecord,
                Zone = this,
                ResultType = SearchResultType.NormalAnswer,
                RequestHostName = hostLabelNormalized,
            };

            return ret;
        }
    }

    public class DataSet
    {
        // 内部データの実体
        public Dictionary<string, Zone> ZoneDict = new Dictionary<string, Zone>();
        public EasyDnsResponderRecordSettings Settings;

        // Settings からコンパイルする
        public DataSet(EasyDnsResponderSettings src)
        {
            this.Settings = (src.DefaultSettings ?? new EasyDnsResponderRecordSettings())._CloneDeep();

            // ゾーン情報のコンパイル
            foreach (var srcZone in src.ZoneList)
            {
                var zone = new Zone(this, srcZone);

                this.ZoneDict.Add(zone.DomainFqdn, zone);
            }
        }

        // クエリ検索
        public SearchResult? Search(SearchRequest request)
        {
            // a.b.c.d のような FQDN を検索要求された場合、
            // 1. a.b.c.d
            // 2. b.c.d
            // 3. c.d
            // 4. d
            // の順で一致するゾーンがないかどうか検索する。
            // つまり、複数の一致する可能性があるゾーンがある場合、一致する文字長が最も長いゾーンを選択するのである。

            ReadOnlyMemory<string> labels = request.FqdnNormalized.Split(".").AsMemory();
            int numLabels = labels.Length;

            Zone? zone = null;
            string hostLabelStr = "";

            ReadOnlyMemory<string> hostLabels = default;

            for (int i = numLabels; i >= 1; i--)
            {
                var zoneLabels = labels.Slice(numLabels - i, i);

                string zoneLabelsStr = zoneLabels._Combine(".");

                // 一致する Dict エントリがあるか？
                if (this.ZoneDict.TryGetValue(zoneLabelsStr, out Zone? zoneTmp))
                {
                    // あった
                    zone = zoneTmp;

                    hostLabels = labels.Slice(0, numLabels - i);

                    hostLabelStr = hostLabels._Combine(".");

                    break;
                }
            }

            if (zone == null)
            {
                // 一致するゾーンが 1 つもありません！
                // DNS 的には Refused を意味することとする。
                return null;
            }

            return zone.Search(request, hostLabelStr, hostLabels);
        }
    }

    // 検索要求
    public class SearchRequest
    {
        public string FqdnNormalized { init; get; } = null!;
    }

    [Flags]
    public enum SearchResultType
    {
        NormalAnswer = 0,
        NsDelegation = 1,
    }

    // 検索結果
    public class SearchResult
    {
        public List<Record>? RecordList { get; set; } = null; // null: サブドメインが全く存在しない 空リスト: サブドメインは存在するものの、レコードは存在しない

        [JsonIgnore]
        public Zone Zone { get; set; } = null!;
        public string ZoneDomainName => Zone.DomainFqdn;
        public string RequestHostName { get; set; } = null!;
        public Record_SOA SOARecord { get; set; } = null!;
        [JsonConverter(typeof(StringEnumConverter))]
        public SearchResultType ResultType { get; set; } = SearchResultType.NormalAnswer;
    }

    DataSet? CurrentDataSet = null;

    public void LoadSetting(EasyDnsResponderSettings setting)
    {
        var dataSet = new DataSet(setting);

        this.CurrentDataSet = dataSet;
    }

    public SearchResult? Query(SearchRequest request, EasyDnsResponderRecordType type)
    {
        var dataSet = this.CurrentDataSet;
        if (dataSet == null)
        {
            return null;
        }

        // 純粋な Zone の検索処理を実施する。クエリにおける要求レコードタイプは見ない。
        SearchResult? ret = dataSet.Search(request);

        // 次にクエリにおける要求レコードタイプに従って特別処理を行なう。
        if (ret != null)
        {
            var zone = ret.Zone;

            if (type == EasyDnsResponderRecordType.NS || ret.ResultType == SearchResultType.NsDelegation)
            {
                if (ret.ResultType == SearchResultType.NormalAnswer)
                {
                    // 特別処理: クエリ種類が NS の場合で、普通の結果の場合、結果にはこのゾーンの NS レコード一覧を埋め込むのである。
                    ret.RecordList = zone.NSRecordList;
                }
                else if (ret.ResultType == SearchResultType.NsDelegation)
                {
                    // 特別処理: クエリ種類が NS の場合で、権限委譲されているドメインの場合、結果には権限委譲のための NS レコードを埋め込むのである。
                    // (dataSet.Search() によって、すでに埋め込みされているはずである。したがって、ここでは何もしない。)
                }
            }
            else
            {
                // 応答リストを指定されたクエリ種類によってフィルタする。
                if (ret.RecordList != null)
                {
                    if (type != EasyDnsResponderRecordType.Any)
                    {
                        List<Record> tmpList = new List<Record>(ret.RecordList.Count);

                        foreach (var r in ret.RecordList)
                        {
                            if (r.Type == type)
                            {
                                tmpList.Add(r);
                            }
                        }

                        ret.RecordList = tmpList;
                    }
                }
            }

            // ダイナミックレコードが含まれている場合はコールバックを呼んで解決をする
            if (ret.RecordList != null)
            {
                int count = ret.RecordList.Count;
                List<Record> solvedDynamicRecordResults = new List<Record>();
                List<Record_Dynamic> originalDynamicRecords = new List<Record_Dynamic>();

                bool anyDynamicRecordExists = false;

                for (int i = 0; i < count; i++)
                {
                    if (ret.RecordList[i] is Record_Dynamic dynRecord)
                    {
                        ResolveDynamicRecord(solvedDynamicRecordResults, dynRecord, ret, request);

                        anyDynamicRecordExists = true;

                        originalDynamicRecords.Add(dynRecord);
                    }
                }

                if (anyDynamicRecordExists)
                {
                    // 結果リストから DynamicRecord をすべて除去し、Callback の結果得られたレコードを挿入
                    foreach (var dynRecord in originalDynamicRecords)
                    {
                        ret.RecordList.Remove(dynRecord);
                    }

                    foreach (var resultRecord in solvedDynamicRecordResults)
                    {
                        ret.RecordList.Add(resultRecord);
                    }
                }
            }
        }

        return ret;
    }

    // ダイナミックレコードをコールバックを用いて実際に解決する
    void ResolveDynamicRecord(List<Record> listToAdd, Record_Dynamic dynRecord, SearchResult result, SearchRequest request)
    {
        EasyDnsResponderDynamicRecordCallbackRequest req = new EasyDnsResponderDynamicRecordCallbackRequest
        {
            Zone = result.Zone.SrcZone,
            Record = dynRecord.SrcRecord!,
            ExpectedRecordType = dynRecord.Type,
            RequestFqdn = request.FqdnNormalized,
            RequestHostName = result.RequestHostName,
            CallbackId = dynRecord.CallbackId,
        };

        EasyDnsResponderDynamicRecordCallbackResult? callbackResult = null;

        if (this.Callback == null) throw new CoresLibException("Callback delegate is not set.");

        callbackResult = this.Callback(req);

        if (callbackResult == null)
        {
            throw new CoresLibException($"Callback delegate returns null for callback ID '{dynRecord.CallbackId}'.");
        }

        EasyDnsResponderRecordSettings? settings = callbackResult.Settings;
        if (settings == null)
        {
            settings = dynRecord.Settings;
        }

        switch (dynRecord.Type)
        {
            case EasyDnsResponderRecordType.A:
                if (callbackResult.IPAddressList != null)
                {
                    foreach (var ip in callbackResult.IPAddressList)
                    {
                        if (ip.AddressFamily == AddressFamily.InterNetwork)
                        {
                            listToAdd.Add(new Record_A(result.Zone, settings, result.RequestHostName, ip));
                        }
                    }
                }
                break;

            case EasyDnsResponderRecordType.AAAA:
                if (callbackResult.IPAddressList != null)
                {
                    foreach (var ip in callbackResult.IPAddressList)
                    {
                        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
                        {
                            listToAdd.Add(new Record_AAAA(result.Zone, settings, result.RequestHostName, ip));
                        }
                    }
                }
                break;

            case EasyDnsResponderRecordType.CNAME:
                if (callbackResult.DomainNameList != null)
                {
                    foreach (var domain in callbackResult.DomainNameList)
                    {
                        listToAdd.Add(new Record_CNAME(result.Zone, settings, result.RequestHostName, domain));
                    }
                }
                break;

            case EasyDnsResponderRecordType.MX:
                if (callbackResult.DomainNameList != null)
                {
                    if (callbackResult.MxPreferenceList != null)
                    {
                        if (callbackResult.DomainNameList.Count == callbackResult.MxPreferenceList.Count)
                        {
                            for (int i = 0; i < callbackResult.DomainNameList.Count; i++)
                            {
                                listToAdd.Add(new Record_MX(result.Zone, settings, result.RequestHostName, callbackResult.DomainNameList[i], callbackResult.MxPreferenceList[i]));
                            }
                        }
                    }
                }
                break;

            case EasyDnsResponderRecordType.NS:
                if (callbackResult.DomainNameList != null)
                {
                    foreach (var domain in callbackResult.DomainNameList)
                    {
                        listToAdd.Add(new Record_NS(result.Zone, settings, result.RequestHostName, domain));
                    }
                }
                break;

            case EasyDnsResponderRecordType.PTR:
                if (callbackResult.DomainNameList != null)
                {
                    foreach (var domain in callbackResult.DomainNameList)
                    {
                        listToAdd.Add(new Record_PTR(result.Zone, settings, result.RequestHostName, domain));
                    }
                }
                break;

            case EasyDnsResponderRecordType.TXT:
                if (callbackResult.TextList != null)
                {
                    foreach (var text in callbackResult.TextList)
                    {
                        listToAdd.Add(new Record_TXT(result.Zone, settings, result.RequestHostName, text));
                    }
                }
                break;
        }
    }
}

#endif

