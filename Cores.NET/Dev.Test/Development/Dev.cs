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
using Newtonsoft.Json.Linq;

namespace IPA.Cores.Basic;








public class HadbBasedServiceStartupParam : INormalizable
{
    public string HiveDataName = "DefaultApp";
    public string HadbSystemName = "DEFAULT_HADB";
    public string ServerProductName = "DefaultServerProduct";
    public double HeavyRequestRateLimiter_LimitPerSecond = 50.0;
    public double HeavyRequestRateLimiter_Burst = 5.0;

    public virtual void Normalize() { }
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

public class HadbBasedService_BasicLogBasedQuota : HadbLog
{
    public string QuotaName = "";
    public string MatchKey = "";

    public override void Normalize()
    {
        QuotaName = QuotaName._NormalizeKey(true);
        MatchKey = MatchKey._NormalizeKey(true);
    }

    public override HadbLabels GetLabels()
        => new HadbLabels(this.QuotaName, this.MatchKey);
}

public abstract class HadbBasedServiceMemDb : HadbMemDataBase
{
    protected abstract void AddDefinedUserDataTypesImpl(List<Type> ret);
    protected abstract void AddDefinedUserLogTypesImpl(List<Type> ret);

    protected sealed override List<Type> GetDefinedUserDataTypesImpl()
    {
        List<Type> ret = new List<Type>();

        this.AddDefinedUserDataTypesImpl(ret);

        return ret;
    }

    protected sealed override List<Type> GetDefinedUserLogTypesImpl()
    {
        List<Type> ret = new List<Type>();

        ret.Add(typeof(HadbBasedService_BasicLogBasedQuota));

        this.AddDefinedUserLogTypesImpl(ret);

        return ret;
    }
}

public class HadbBasedServiceDynConfig : HadbDynamicConfig
{
    public bool Service_HideJsonRpcErrorDetails = false;
    public string Service_AdminBasicAuthUsername = "";
    public string Service_AdminBasicAuthPassword = "";
    public string Service_HeavyRequestRateLimiterAcl = "_initial_";
    public int Service_ClientIpRateLimit_SubnetLength_IPv4 = 0;
    public int Service_ClientIpRateLimit_SubnetLength_IPv6 = 0;
    public string Service_SendMail_SmtpServer_Hostname = "";
    public int Service_SendMail_SmtpServer_Port = 0;
    public bool Service_SendMail_SmtpServer_EnableSsl;
    public string Service_SendMail_SmtpServer_Username = "";
    public string Service_SendMail_SmtpServer_Password = "";
    public string Service_SendMail_MailFromAddress = "";
    public int Service_BasicQuota_DurationSecs = 3600;
    public int Service_BasicQuota_LimitationCount = 10;
    public int Service_FullTextSearchResultsCountMax = 0;
    public int Service_FullTextSearchResultsCountStandard = 0;
    public int Service_FullTextSearchResultsCountInternalMemory = 0;


    protected override void NormalizeImpl()
    {
        Service_AdminBasicAuthUsername = Service_AdminBasicAuthUsername._FilledOrDefault(Consts.Strings.DefaultAdminUsername);
        Service_AdminBasicAuthPassword = Service_AdminBasicAuthPassword._FilledOrDefault(Consts.Strings.DefaultAdminPassword);

        if (Service_ClientIpRateLimit_SubnetLength_IPv4 <= 0 || Service_ClientIpRateLimit_SubnetLength_IPv4 > 32) Service_ClientIpRateLimit_SubnetLength_IPv4 = 24;
        if (Service_ClientIpRateLimit_SubnetLength_IPv6 <= 0 || Service_ClientIpRateLimit_SubnetLength_IPv6 > 128) Service_ClientIpRateLimit_SubnetLength_IPv6 = 56;

        if (Service_HeavyRequestRateLimiterAcl == "_initial_")
        {
            Service_HeavyRequestRateLimiterAcl = "127.0.0.0/8; 192.168.0.0/16; 172.16.0.0/24; 10.0.0.0/8; 1.2.3.4/32; 2041:af80:1234::/48";
        }

        if (Service_SendMail_SmtpServer_Port <= 0) Service_SendMail_SmtpServer_Port = Consts.Ports.Smtp;
        Service_SendMail_SmtpServer_Hostname = Service_SendMail_SmtpServer_Hostname._FilledOrDefault("your-smtp-server-address.your_company.org");

        Service_SendMail_MailFromAddress = Service_SendMail_MailFromAddress._FilledOrDefault("noreply@your_company.org");

        if (Service_BasicQuota_DurationSecs <= 0) Service_BasicQuota_DurationSecs = 3600;
        if (Service_BasicQuota_LimitationCount <= 0) Service_BasicQuota_LimitationCount = 10;

        if (this.Service_FullTextSearchResultsCountMax <= 0) Service_FullTextSearchResultsCountMax = Consts.Numbers.HadbFullTextSearchResultsMaxDefault;
        if (this.Service_FullTextSearchResultsCountStandard <= 0) Service_FullTextSearchResultsCountStandard = Consts.Numbers.HadbFullTextSearchResultsStandardDefault;
        if (this.Service_FullTextSearchResultsCountInternalMemory <= 0) Service_FullTextSearchResultsCountInternalMemory = Consts.Numbers.HadbFullTextSearchResultsInternalMemoryDefault;

        base.NormalizeImpl();
    }
}

public abstract class JsonRpcSingleReturnWithMetaData<T> : IToJsonString
    where T : HadbData
{
    public abstract T Data { get; }

    public string ToJsonString(bool includeNull = false, bool escapeHtml = false, int? maxDepth = 8, bool compact = false, bool referenceHandling = false, bool base64url = false, Type? type = null, JsonFlags jsonFlags = JsonFlags.None)
    {
        string tmp = this.Data._ObjectToJson(includeNull, escapeHtml, maxDepth, compact, referenceHandling, base64url, type);

        JObject jobject = tmp._JsonToObject<JObject>(includeNull, maxDepth, base64url)!;

        var rw = FieldReaderWriter.GetCached(this.GetType());

        JsonSerializerSettings setting = new JsonSerializerSettings()
        {
            MaxDepth = maxDepth,
            NullValueHandling = includeNull ? NullValueHandling.Include : NullValueHandling.Ignore,
            ReferenceLoopHandling = ReferenceLoopHandling.Error,
            PreserveReferencesHandling = referenceHandling ? PreserveReferencesHandling.All : PreserveReferencesHandling.None,
            StringEscapeHandling = escapeHtml ? StringEscapeHandling.EscapeHtml : StringEscapeHandling.Default,
            Formatting = compact ? Formatting.None : Formatting.Indented,
        };

        Json.AddStandardSettingsToJsonConverter(setting, jsonFlags);

        setting.Converters.Add(new StringEnumConverter());

        JsonSerializer serializer = JsonSerializer.Create(setting);

        var dataProperties = jobject.Properties().ToList();
        foreach (var p in dataProperties)
        {
            p.Remove();
        }

        foreach (var name in rw.FieldOrPropertyNamesList.Where(x => x._IsDiff(nameof(Data))))
        {
            object? obj = rw.GetValue(this, name);

            var jtoken = JToken.FromObject(obj, serializer);

            jobject.Add(name, jtoken);
        }

        foreach (var p in dataProperties)
        {
            jobject.Add(p);
        }

        return jobject._ObjectToJson(includeNull, escapeHtml, maxDepth, compact, referenceHandling, base64url);
    }
}

public abstract class HadbBasedServiceHookBase
{
}

public class HadbFullTextSearchResult
{
    public int NumResultObjects;
    public int NumTotalObjects;
    public bool HasMore;
    public List<HadbSearchResultJsonObject> ObjectsList = new List<HadbSearchResultJsonObject>();
}

[RpcInterface]
public interface IHadbBasedServiceRpcBase
{
    public Task<EasyJsonStrAttributes> ServiceAdmin_GetHadbStat();
    public Task<HadbFullTextSearchResult> ServiceAdmin_FullTextSearch(string queryText = "", string sortBy = "", bool wordMode = false, bool fieldNameMode = false, string typeName = "", string nameSpace = "", int maxResults = 0);
}

public abstract class HadbBasedServiceBase<TMemDb, TDynConfig, THiveSettings, THook> : AsyncService, IHadbBasedServiceRpcBase, IHadbBasedServicePoint
    where TMemDb : HadbBasedServiceMemDb, new()
    where TDynConfig : HadbBasedServiceDynConfig
    where THiveSettings : HadbBasedServiceHiveSettingsBase, new()
    where THook : HadbBasedServiceHookBase
{
    public DateTimeOffset BootDateTime { get; } = DtOffsetNow; // サービス起動日時
    public HadbBase<TMemDb, TDynConfig> Hadb { get; }
    public AsyncEventListenerList<HadbBase<TMemDb, TDynConfig>, HadbEventType> HadbEventListenerList => this.Hadb.EventListenerList;

    HadbBasedServiceStartupParam ServiceStartupParam { get; } // copy

    public DnsResolver DnsResolver { get; }

    // Hive
    readonly HiveData<THiveSettings> _SettingsHive;

    // Hive 設定へのアクセスを容易にするための自動プロパティ
    CriticalSection ManagedSettingsLock => this._SettingsHive.DataLock;
    THiveSettings ManagedSettings => this._SettingsHive.ManagedData;
    public THiveSettings SettingsFastSnapshot => this._SettingsHive.CachedFastSnapshot;

    public RateLimiter<string> HeavyRequestRateLimiter { get; }
    public TDynConfig CurrentDynamicConfig => Hadb.CurrentDynamicConfig;

    public THook Hook { get; }

    public HadbBasedServiceBase(HadbBasedServiceStartupParam startupParam, THook hook)
    {
        try
        {
            this.Hook = hook;

            this.ServiceStartupParam = startupParam._CloneDeep();

            this.HeavyRequestRateLimiter = new RateLimiter<string>(new RateLimiterOptions(burst: this.ServiceStartupParam.HeavyRequestRateLimiter_Burst, limitPerSecond: this.ServiceStartupParam.HeavyRequestRateLimiter_LimitPerSecond, mode: RateLimiterMode.NoPenalty));

            this._SettingsHive = new HiveData<THiveSettings>(Hive.SharedLocalConfigHive,
                this.ServiceStartupParam.HiveDataName,
                () => new THiveSettings { HadbSystemName = this.ServiceStartupParam.HadbSystemName },
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

    protected JsonRpcClientInfo GetClientInfo() => JsonRpcServerApi.GetCurrentRpcClientInfo();
    protected IPAddress GetClientIpAddress() => GetClientInfo().RemoteIP._ToIPAddress()!._RemoveScopeId();
    protected IPAddress GetClientIpNetworkForRateLimit() => IPUtil.NormalizeIpNetworkAddressIPv4v6(GetClientIpAddress(), Hadb.CurrentDynamicConfig.Service_ClientIpRateLimit_SubnetLength_IPv4, Hadb.CurrentDynamicConfig.Service_ClientIpRateLimit_SubnetLength_IPv6);

    protected string GetClientIpStr() => GetClientIpAddress().ToString();
    protected string GetClientIpNetworkForRateLimitStr() => GetClientIpNetworkForRateLimit().ToString();

    protected Task<string> GetClientFqdnAsync(CancellationToken cancel = default, bool noCache = false)
        => this.DnsResolver.GetHostNameSingleOrIpAsync(GetClientIpAddress(), cancel, noCache);

    public async Task<HadbObject?> AdminForm_DirectGetObjectAsync(string uid, CancellationToken cancel = default)
        => await this.Hadb.DirectGetObjectAsync(uid, cancel);

    public async Task<HadbObject> AdminForm_DirectSetObjectAsync(string uid, string jsonData, HadbObjectSetFlag flag, string typeName, string nameSpace, CancellationToken cancel = default)
        => await this.Hadb.DirectSetObjectAsync(uid, jsonData, flag, typeName, nameSpace, cancel);

    public async Task<string> AdminForm_DirectGetObjectExAsync(string uid, int maxItems = int.MaxValue, HadbObjectGetExFlag flag = HadbObjectGetExFlag.None, CancellationToken cancel = default)
        => await this.Hadb.AdminForm_DirectGetObjectExAsync(uid, maxItems, flag, cancel);

    public async Task<string> AdminForm_GetDynamicConfigAsync(CancellationToken cancel = default)
    {
        return await this.Hadb.GetDynamicConfigStringAsync(cancel);
    }

    public async Task AdminForm_SetDynamicConfigAsync(string newConfig, CancellationToken cancel = default)
    {
        await this.Hadb.SetDynamincConfigStringAsync(newConfig, cancel);
    }

    public Task Basic_Require_AdminBasicAuthAsync(string realm = "")
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

        return Task.CompletedTask;
    }

    protected void Basic_Check_HeavyRequestRateLimiter(double amount = 1.0)
    {
        if (EasyIpAcl.Evaluate(this.Hadb.CurrentDynamicConfig.Service_HeavyRequestRateLimiterAcl, this.GetClientIpAddress(), EasyIpAclAction.Deny, EasyIpAclAction.Deny, true) == EasyIpAclAction.Deny)
        {
            if (this.HeavyRequestRateLimiter.TryInput(this.GetClientIpNetworkForRateLimitStr(), out var e, amount) == false)
            {
                throw new CoresException($"Request rate limit exceeded with your IP address {this.GetClientIpAddress()} and your request is rejected. Too many requests from your IP address or your network. Please wait for minutes. If this issue remains, please concact to the server administrator.");
            }
        }
    }

    public async Task Basic_CheckAndAddLogBasedQuotaByClientIpAsync(string quotaName, int? allowedMax = null, int? durationSecs = null, CancellationToken cancel = default)
    {
        var ip = this.GetClientIpAddress();

        if (EasyIpAcl.Evaluate(this.Hadb.CurrentDynamicConfig.Service_HeavyRequestRateLimiterAcl, ip, EasyIpAclAction.Deny, EasyIpAclAction.Deny, true) == EasyIpAclAction.Deny)
        {
            await Basic_CheckAndAddLogBasedQuotaAsync(quotaName, ip.ToString(), allowedMax, durationSecs, cancel);
        }
    }

    public async Task Basic_CheckAndAddLogBasedQuotaAsync(string quotaName, string matchKey, int? allowedMax = null, int? durationSecs = null, CancellationToken cancel = default)
    {
        allowedMax ??= CurrentDynamicConfig.Service_BasicQuota_LimitationCount;
        durationSecs ??= CurrentDynamicConfig.Service_BasicQuota_DurationSecs;

        string quotaName2 = quotaName._NormalizeKey(true);
        matchKey = matchKey._NormalizeKey(true);

        if (allowedMax <= 0 || quotaName2._IsEmpty() || matchKey._IsEmpty()) return;

        bool ok = true;

        await this.Hadb.TranAsync(false, async tran =>
        {
            HadbLogQuery query = new HadbLogQuery
            {
                SearchTemplate = new HadbBasedService_BasicLogBasedQuota { QuotaName = quotaName2, MatchKey = matchKey },
                TimeStart = DtOffsetNow.AddSeconds(-durationSecs.Value),
            };

            var logs = await tran.AtomicSearchLogAsync<HadbBasedService_BasicLogBasedQuota>(query, cancel: cancel);
            if (logs.Count() >= allowedMax)
            {
                ok = false;
            }

            return false;
        });

        if (ok == false)
        {
            throw new CoresException($"Request quota exceeded ({quotaName}). Please wait for minutes and try again. If problem remains please contact to the service administrator.");
        }

        await this.Hadb.TranAsync(true, async tran =>
        {
            await tran.AtomicAddLogAsync(new HadbBasedService_BasicLogBasedQuota { QuotaName = quotaName2, MatchKey = matchKey }, cancel: cancel);

            return true;
        }, options: HadbTranOptions.NoTransactionOnWrite);
    }

    public async Task Basic_SendMailAsync(string to, string subject, string body, string? from = null, CancellationToken cancel = default)
    {
        if (from._IsEmpty())
        {
            from = CurrentDynamicConfig.Service_SendMail_MailFromAddress;
        }

        if (from._IsEmpty())
        {
            from = "noreply@example.org";
        }

        to = to._NonNull();
        subject = subject._NonNull();
        body = body._NonNull();

        var smtpConfig = new SmtpConfig(CurrentDynamicConfig.Service_SendMail_SmtpServer_Hostname,
            CurrentDynamicConfig.Service_SendMail_SmtpServer_Port,
            CurrentDynamicConfig.Service_SendMail_SmtpServer_EnableSsl,
            CurrentDynamicConfig.Service_SendMail_SmtpServer_Username,
            CurrentDynamicConfig.Service_SendMail_SmtpServer_Password);

        await SmtpUtil.SendAsync(smtpConfig, from, to, subject, body, true, cancel);
    }

    public void Start()
    {
        StartedFlag.FirstCallOrThrowException();

        StartImpl(); // HADB の開始前でなければならない！！

        // HADB からの特殊なコールバック
        this.HadbEventListenerList.RegisterCallback(async (caller, type, state, param) =>
        {
            switch (type)
            {
                case HadbEventType.DynamicConfigChanged: // DynamicConfig の設定が変更された
                    break;
            }

            if (this.Hadb.IsDynamicConfigInited)
            {
                // JSON-RPC サーバーの詳細エラーを隠すかどうかのフラグ
                JsonRpcHttpServer.HideErrorDetails = this.Hadb.CurrentDynamicConfig.Service_HideJsonRpcErrorDetails;
            }

            await Task.CompletedTask;
        });

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

    public Task<EasyJsonStrAttributes> ServiceAdmin_GetHadbStat()
    {
        return this.Hadb.LatestStatData!._TR();
    }

    public Task<HadbFullTextSearchResult> ServiceAdmin_FullTextSearch(string queryText, string sortBy, bool wordMode, bool fieldNameMode, string typeName, string nameSpace, int maxResults)
    {
        if (maxResults <= 0 || maxResults == int.MaxValue)
        {
            maxResults = Hadb.CurrentDynamicConfig.Service_FullTextSearchResultsCountStandard;
        }
        else
        {
            if (maxResults > Hadb.CurrentDynamicConfig.Service_FullTextSearchResultsCountMax)
            {
                throw new CoresException($"FullTextSearch: {nameof(maxResults)} must be equal or less than {Hadb.CurrentDynamicConfig.Service_FullTextSearchResultsCountMax}.");
            }
        }

        RefBool hasMore = new RefBool();

        var objList = Hadb.FastSearchByFullText(queryText, sortBy, wordMode, fieldNameMode, typeName, nameSpace, maxResults, Hadb.CurrentDynamicConfig.Service_FullTextSearchResultsCountInternalMemory, hasMore: hasMore);
        var metrics = Hadb.GetCurrentMetrics();

        HadbFullTextSearchResult ret = new HadbFullTextSearchResult
        {
            NumTotalObjects = metrics.NumMemoryObjects,
            NumResultObjects = objList.Count,
            HasMore = hasMore,
        };

        foreach (var obj in objList)
        {
            ret.ObjectsList.Add(new HadbSearchResultJsonObject(obj));
        }

        return ret._TR();
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

