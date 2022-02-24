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

public class HadbBasedServiceDynConfig : HadbDynamicConfig
{
    public string Service_AdminBasicAuthUsername = "";
    public string Service_AdminBasicAuthPassword = "";

    protected override void NormalizeImpl()
    {
        Service_AdminBasicAuthUsername = Service_AdminBasicAuthUsername._FilledOrDefault(Consts.Strings.DefaultAdminUsername);
        Service_AdminBasicAuthPassword = Service_AdminBasicAuthPassword._FilledOrDefault(Consts.Strings.DefaultAdminPassword);

        base.NormalizeImpl();
    }
}

public abstract class HadbBasedServiceBase<TMemDb, TDynConfig, THiveSettings> : AsyncService
    where TMemDb : HadbMemDataBase, new()
    where TDynConfig : HadbBasedServiceDynConfig
    where THiveSettings : HadbBasedServiceHiveSettingsBase, new()
{
    public DateTimeOffset BootDateTime { get; } = DtOffsetNow; // サービス起動日時
    public HadbBase<TMemDb, TDynConfig> Hadb { get; }
    public AsyncEventListenerList<HadbBase<TMemDb, TDynConfig>, HadbEventType> HadbEventListenerList => this.Hadb.EventListenerList;
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

    protected abstract void StartImpl();

    public void Require_AdminBasicAuth(string realm = "")
    {
        if (realm._IsEmpty())
        {
            realm = "Basic auth for " + this.GetType().Name;
        }

        JsonRpcServerApi.TryAuth((user, pass) =>
        {
            var config = Hadb.CurrentDynamicConfig;

            return user._IsSamei(config.Service_AdminBasicAuthUsername) && pass._IsSame(config.Service_AdminBasicAuthPassword);
        }, realm);
    }

    public void Start()
    {
        StartedFlag.FirstCallOrThrowException();

        StartImpl(); // HADB の開始前でなければならない！！

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














[Flags]
public enum PortRangeStyle
{
    Normal = 0,
}

public class PortRange
{
    readonly Memory<bool> PortArray = new bool[Consts.Numbers.PortMax + 1];

    public PortRange()
    {
    }

    public PortRange(string rangeString)
    {
        Add(rangeString);
    }

    public void Add(string rangeString)
    {
        var span = PortArray.Span;

        string[] tokens = rangeString._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ',', ';', ' ', '　', '\t');

        foreach (var token in tokens)
        {
            string[] tokens2 = token._Split(StringSplitOptions.TrimEntries, '-');
            if (tokens2.Length == 1)
            {
                int number = tokens2[0]._ToInt();

                if (number._IsValidPortNumber())
                {
                    span[number] = true;
                }
            }
            else if (tokens2.Length == 2)
            {
                int number1 = tokens2[0]._ToInt();
                int number2 = tokens2[1]._ToInt();
                int start = Math.Min(number1, number2);
                int end = Math.Max(number1, number2);
                if (start._IsValidPortNumber() && end._IsValidPortNumber())
                {
                    for (int i = start; i <= end; i++)
                    {
                        span[i] = true;
                    }
                }
            }
        }
    }

    public List<int> ToArray()
    {
        List<int> ret = new List<int>();
        var span = this.PortArray.Span;
        for (int i = Consts.Numbers.PortMin; i <= Consts.Numbers.PortMax; i++)
        {
            if (span[i])
            {
                ret.Add(i);
            }
        }
        return ret;
    }

    public override string ToString() => ToString(PortRangeStyle.Normal);

    public string ToString(PortRangeStyle style)
    {
        var span = PortArray.Span;

        List<WPair2<int, int>> segments = new List<WPair2<int, int>>();

        WPair2<int, int>? current = null;

        for (int i = Consts.Numbers.PortMin; i <= Consts.Numbers.PortMax; i++)
        {
            if (span[i])
            {
                if (current == null)
                {
                    current = new WPair2<int, int>(i, i);
                    segments.Add(current);
                }
                else
                {
                    current.B = i;
                }
            }
            else
            {
                if (current != null)
                {
                    current = null;
                }
            }
        }

        switch (style)
        {
            case PortRangeStyle.Normal:
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (var segment in segments)
                    {
                        string str;

                        if (segment.A == segment.B)
                        {
                            str = segment.A.ToString();
                        }
                        else
                        {
                            str = $"{segment.A}-{segment.B}";
                        }

                        sb.Append(str);

                        sb.Append(",");
                    }

                    return sb.ToString().TrimEnd(',');
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(style));
        }
    }
}






#endif

