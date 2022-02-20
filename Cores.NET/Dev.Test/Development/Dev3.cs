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
using Newtonsoft.Json.Linq;

namespace IPA.Cores.Basic;


public static partial class DevCoresConfig
{
    public static partial class MikakaDDnsServiceSettings
    {
        public static readonly Copenhagen<int> HostRecordMaxArchivedCount = 10;
    }
}

public class MikakaDDnsService : HadbBasedServiceBase<MikakaDDnsService.MemDb, MikakaDDnsService.DynConfig, MikakaDDnsService.HiveSettings>, MikakaDDnsService.IRpc
{
    public class DynConfig : HadbDynamicConfig
    {
        public int DDns_MaxHostPerCreateClientIpAddress_Total;
        public int DDns_MaxHostPerCreateClientIpNetwork_Total;
        public int DDns_MaxHostPerCreateClientIpAddress_Daily;
        public int DDns_MaxHostPerCreateClientIpNetwork_Daily;
        public int DDns_CreateRequestedIpNetwork_SubnetLength_IPv4;
        public int DDns_CreateRequestedIpNetwork_SubnetLength_IPv6;
        public string DDns_ProhibitedHostnamesStartWith = "_initial_";
        public int DDns_MaxUserDataJsonStrLength = 10 * 1024;
        public bool DDns_RequireUnlockKey = false;
        public string DDns_NewHostnamePrefix = "";
        public int DDns_NewHostnameRandomDigits = 12;
        public bool DDns_Prohibit_IPv4AddressRegistration = false;
        public bool DDns_Prohibit_IPv6AddressRegistration = false;
        public int DDns_MinHostLabelLen;
        public int DDns_MaxHostLabelLen;
        public int DDns_Protocol_Ttl_Secs;
        public int DDns_Protocol_Ttl_Secs_NS_Record;
        public string DDns_Protocol_SOA_MasterNsServerFqdn = "";
        public string DDns_Protocol_SOA_ResponsibleFieldFqdn = "";
        public int DDns_Protocol_SOA_NegativeCacheTtlSecs;
        public int DDns_Protocol_SOA_RefreshIntervalSecs;
        public int DDns_Protocol_SOA_RetryIntervalSecs;
        public int DDns_Protocol_SOA_ExpireIntervalSecs;

        public string[] DDns_DomainName = new string[0];
        public string DDns_DomainNamePrimary = "";

        public string[] DDns_StaticRecord = new string[0];

        protected override void NormalizeImpl()
        {
            if (DDns_DomainName == null || DDns_DomainName.Any() == false)
            {
                var tmpList = new List<string>();
                tmpList.Add("ddns_example.net");
                tmpList.Add("ddns_example.org");
                tmpList.Add("ddns_example.com");
                DDns_DomainName = tmpList.ToArray();
                DDns_DomainNamePrimary = "ddns_example.org";
            }

            HashSet<string> tmp = new HashSet<string>();
            foreach (var domainName in DDns_DomainName)
            {
                string domainName2 = domainName._NormalizeFqdn();
                if (domainName2._IsFilled())
                {
                    tmp.Add(domainName2);
                }
            }
            DDns_DomainName = tmp.OrderBy(x => x).ToArray();

            DDns_DomainNamePrimary = DDns_DomainNamePrimary._NormalizeFqdn();

            if (DDns_DomainName.Contains(DDns_DomainNamePrimary) == false || DDns_DomainNamePrimary._IsEmpty())
            {
                if (DDns_DomainName.Length >= 1)
                {
                    DDns_DomainNamePrimary = DDns_DomainName[0];
                }
                else
                {
                    DDns_DomainNamePrimary = "";
                }
            }

            if (DDns_MaxHostPerCreateClientIpAddress_Total <= 0) DDns_MaxHostPerCreateClientIpAddress_Total = 1000;
            if (DDns_MaxHostPerCreateClientIpNetwork_Total <= 0) DDns_MaxHostPerCreateClientIpNetwork_Total = 10000;

            if (DDns_MaxHostPerCreateClientIpAddress_Daily <= 0) DDns_MaxHostPerCreateClientIpAddress_Daily = 100;
            if (DDns_MaxHostPerCreateClientIpNetwork_Daily <= 0) DDns_MaxHostPerCreateClientIpNetwork_Daily = 1000;

            if (DDns_NewHostnameRandomDigits <= 0 || DDns_NewHostnameRandomDigits >= 32) DDns_NewHostnameRandomDigits = 12;

            if (DDns_CreateRequestedIpNetwork_SubnetLength_IPv4 <= 0 || DDns_CreateRequestedIpNetwork_SubnetLength_IPv4 > 32) DDns_CreateRequestedIpNetwork_SubnetLength_IPv4 = 24;
            if (DDns_CreateRequestedIpNetwork_SubnetLength_IPv6 <= 0 || DDns_CreateRequestedIpNetwork_SubnetLength_IPv6 > 128) DDns_CreateRequestedIpNetwork_SubnetLength_IPv6 = 56;

            if (DDns_ProhibitedHostnamesStartWith._IsSamei("_initial_"))
                DDns_ProhibitedHostnamesStartWith = new string[] {
                   "ddns",
                   "www",
                   "dns",
                   "register",
                   "admin",
                   "ns0",
                   "sample",
                   "subdomain",
                   "ws-",
                   "websocket-",
                   "_acme",
                }._Combine(",");

            DDns_ProhibitedHostnamesStartWith = DDns_ProhibitedHostnamesStartWith._NonNullTrim()
                ._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ' ', ';', ',', '/', '\t')._Combine(",").ToLowerInvariant();

            if (DDns_NewHostnamePrefix._IsEmpty()) DDns_NewHostnamePrefix = "neko";
            DDns_NewHostnamePrefix = DDns_NewHostnamePrefix._NonNullTrim().ToLowerInvariant()._MakeStringUseOnlyChars("0123456789abcdefghijklmnopqrstuvwxyz");

            if (DDns_MinHostLabelLen <= 0) DDns_MinHostLabelLen = 3;
            if (DDns_MaxHostLabelLen <= 0) DDns_MaxHostLabelLen = 32;
            if (DDns_MaxHostLabelLen >= 64) DDns_MaxHostLabelLen = 63;

            if (DDns_Protocol_Ttl_Secs <= 0) DDns_Protocol_Ttl_Secs = 60;
            if (DDns_Protocol_Ttl_Secs >= 3600) DDns_Protocol_Ttl_Secs = 3600;

            if (DDns_Protocol_Ttl_Secs_NS_Record <= 0) DDns_Protocol_Ttl_Secs_NS_Record = 15 * 60;
            if (DDns_Protocol_Ttl_Secs_NS_Record >= 3600) DDns_Protocol_Ttl_Secs_NS_Record = 3600;

            if (DDns_Protocol_SOA_NegativeCacheTtlSecs <= 0) DDns_Protocol_SOA_NegativeCacheTtlSecs = 13;
            if (DDns_Protocol_SOA_NegativeCacheTtlSecs >= 3600) DDns_Protocol_SOA_NegativeCacheTtlSecs = 3600;

            if (DDns_Protocol_SOA_RefreshIntervalSecs <= 0) DDns_Protocol_SOA_RefreshIntervalSecs = 60;
            if (DDns_Protocol_SOA_RefreshIntervalSecs >= 3600) DDns_Protocol_SOA_RefreshIntervalSecs = 3600;

            if (DDns_Protocol_SOA_RetryIntervalSecs <= 0) DDns_Protocol_SOA_RetryIntervalSecs = 60;
            if (DDns_Protocol_SOA_RetryIntervalSecs >= 3600) DDns_Protocol_SOA_RetryIntervalSecs = 3600;

            if (DDns_Protocol_SOA_ExpireIntervalSecs <= 0) DDns_Protocol_SOA_ExpireIntervalSecs = 3600 * 24 * 1024;
            if (DDns_Protocol_SOA_ExpireIntervalSecs >= 3600 * 24 * 365 * 5) DDns_Protocol_SOA_ExpireIntervalSecs = 3600 * 24 * 365 * 5;

            if (DDns_Protocol_SOA_MasterNsServerFqdn._IsEmpty()) DDns_Protocol_SOA_MasterNsServerFqdn = "ns01.ddns_example.org";
            DDns_Protocol_SOA_MasterNsServerFqdn = DDns_Protocol_SOA_MasterNsServerFqdn._NormalizeFqdn();

            if (DDns_Protocol_SOA_ResponsibleFieldFqdn._IsEmpty()) DDns_Protocol_SOA_ResponsibleFieldFqdn = "nobody.example.org";
            DDns_Protocol_SOA_ResponsibleFieldFqdn = DDns_Protocol_SOA_ResponsibleFieldFqdn._NormalizeFqdn();

            if (DDns_StaticRecord.Length == 0)
            {
                string initialRecordsList = @"
NS @ mikaka-ddns-ns01.your_company.com
NS @ mikaka-ddns-ns02.your_company.org

A sample1 1.2.3.4

A sample2 5.6.7.8
AAAA sample2 2401:af80::1234

A sample3 1.1.1.1
A sample3 2.2.2.2
A sample3 3.3.3.3
A sample3 4.4.4.4
AAAA sample3 2401:af80::dead:beef
AAAA sample3 2401:af80::cafe:8945
AAAA sample3 2401:af80::abcd:1234
AAAA sample3 2401:af80::5678:cafe

A *.sample4 5.9.6.1
A *.sample4 5.9.6.2
AAAA *.sample4 2401:af80::1234:cafe:8945
AAAA *.sample4 2401:af80::1234:5678:cafe

A sample4 8.0.0.1
AAAA sample4 2401:af80::1:2:3:4

CNAME sample4 www1.your_company.net

CNAME sample5 www2.your_company.net
CNAME sample5 www3.your_company.net

NS subdomain1 subdomain_ns1.your_company.com
NS subdomain1 subdomain_ns2.your_company.net

NS subdomain2 subdomain_ns3.your_company.co.jp
NS subdomain2 subdomain_ns4.your_company.ad.jp

NS _acme-challenge ssl-cert-server.your_company.net

MX @ mail1.your_company.net 100
MX @ mail2.your_company.net 200
TXT @ v=spf1 ip4:130.158.0.0/16 ip4:133.51.0.0/16 ip6:2401:af80::/32 include:spf2.@ ?all
TXT @ v=spf2 ip4:8.8.8.0/24 ip6:2401:5e40::/32 ?all


MX sample2 mail3.your_company.net 100
MX sample2 mail4.your_company.net 200
TXT sample2 v=spf1 redirect=tennoudai.net
TXT sample2 v=spf1 ip4:130.158.0.0/16 ip4:133.51.0.0/16 ip6:2401:af80::/32 include:spf2.sample2.@ ?all
TXT sample2 v=spf2 ip4:8.8.8.0/24 ip6:2401:5e40::/32 ?all

MX sample3 mail5.your_company.net 100
MX sample3 mail6.your_company.net 200
TXT sample3 v=spf1 redirect=tennoudai.net
TXT sample3 v=spf1 ip4:130.158.0.0/16 ip4:133.51.0.0/16 ip6:2401:af80::/32 include:spf2.sample3.@ ?all
TXT sample3 v=spf2 ip4:8.8.8.0/24 ip6:2401:5e40::/32 ?all


";

                List<string> records = new List<string>();
                foreach (var line in initialRecordsList._GetLines(removeEmpty: true, trim: true))
                {
                    records.Add(line);
                }

                this.DDns_StaticRecord = records.ToArray();
            }
        }
    }

    public class UnlockKey : HadbData
    {
        public string Key = "";

        public override void Normalize()
        {
            this.Key = this.Key._NormalizeKey(true);
        }
    }

    [Flags]
    public enum HostApiResult
    {
        NoChange = 0,
        Created,
        Modified,
    }

    public class Host : HadbData
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public HostApiResult? ApiResult; // RPC Only

        public string HostLabel = "";
        public string? HostFqdnPrimary; // RPC Only

        public string HostAddress_IPv4 = "";
        public string HostAddress_IPv6 = "";
        public string HostSecretKey = "";

        public DateTimeOffset CreatedTime = DtOffsetZero;

        public string AuthLogin_LastIpAddress = "";
        public string AuthLogin_LastFqdn = "";
        public long AuthLogin_Count = 0;

        public DateTimeOffset AuthLogin_FirstTime = DtOffsetZero;
        public DateTimeOffset AuthLogin_LastTime = DtOffsetZero;

        public string CreateRequestedIpAddress = "";
        public string CreateRequestedFqdn = "";
        public string CreateRequestedIpNetwork = "";

        public long HostLabel_NumUpdates = 0;
        public DateTimeOffset HostLabel_LastUpdateTime = DtOffsetZero;

        public DateTimeOffset HostAddress_IPv4_FirstUpdateTime = DtOffsetZero;
        public DateTimeOffset HostAddress_IPv4_LastUpdateTime = DtOffsetZero;
        public long HostAddress_IPv4_NumUpdates = 0;

        public DateTimeOffset HostAddress_IPv6_FirstUpdateTime = DtOffsetZero;
        public DateTimeOffset HostAddress_IPv6_LastUpdateTime = DtOffsetZero;
        public long HostAddress_IPv6_NumUpdates = 0;

        public DateTimeOffset DnsQuery_FirstAccessTime = DtOffsetZero;
        public DateTimeOffset DnsQuery_LastAccessTime = DtOffsetZero;
        public long DnsQuery_Count = 0;
        public string DnsQuery_FirstAccessDnsClientIp = "";
        public string DnsQuery_LastAccessDnsClientIp = "";

        public string UsedUnlockKey = "";

        public string UserGroupSecretKey = "";
        public string Email = "";

        public JObject UserData = Json.NewJsonObject();

        public override HadbKeys GetKeys() => new HadbKeys(this.HostSecretKey, this.HostLabel, this.UsedUnlockKey);
        public override HadbLabels GetLabels() => new HadbLabels(this.UserGroupSecretKey, this.CreateRequestedIpAddress, this.CreateRequestedIpNetwork, this.Email);

        public override void Normalize()
        {
            this.HostSecretKey = this.HostSecretKey._NormalizeKey(true);
            this.HostLabel = this.HostLabel._NormalizeFqdn();
            this.UserGroupSecretKey = this.UserGroupSecretKey._NormalizeKey(true);

            this.CreatedTime = this.CreatedTime._NormalizeDateTimeOffset();

            this.CreateRequestedIpAddress = this.CreateRequestedIpAddress._NormalizeIp();
            this.CreateRequestedFqdn = this.CreateRequestedFqdn._NormalizeFqdn();
            this.CreateRequestedIpNetwork = this.CreateRequestedIpNetwork._NormalizeIp();

            this.HostAddress_IPv4 = this.HostAddress_IPv4._NormalizeIp();
            this.HostAddress_IPv4_LastUpdateTime = this.HostAddress_IPv4_LastUpdateTime._NormalizeDateTimeOffset();
            this.HostAddress_IPv4_FirstUpdateTime = this.HostAddress_IPv4_FirstUpdateTime._NormalizeDateTimeOffset();

            this.HostAddress_IPv6 = this.HostAddress_IPv6._NormalizeIp();
            this.HostAddress_IPv6_LastUpdateTime = this.HostAddress_IPv6_LastUpdateTime._NormalizeDateTimeOffset();
            this.HostAddress_IPv6_FirstUpdateTime = this.HostAddress_IPv6_FirstUpdateTime._NormalizeDateTimeOffset();

            this.UsedUnlockKey = this.UsedUnlockKey._NonNull();

            this.UserData = this.UserData._NormalizeEasyJsonStrAttributes();

            this.HostLabel_LastUpdateTime = this.HostLabel_LastUpdateTime._NormalizeDateTimeOffset();

            this.AuthLogin_LastIpAddress = this.AuthLogin_LastIpAddress._NormalizeIp();
            this.AuthLogin_LastFqdn = this.AuthLogin_LastFqdn._NormalizeFqdn();

            this.DnsQuery_FirstAccessTime = this.DnsQuery_FirstAccessTime._NormalizeDateTimeOffset();
            this.DnsQuery_LastAccessTime = this.DnsQuery_LastAccessTime._NormalizeDateTimeOffset();

            this.DnsQuery_FirstAccessDnsClientIp = this.DnsQuery_FirstAccessDnsClientIp._NormalizeIp();
            this.DnsQuery_LastAccessDnsClientIp = this.DnsQuery_LastAccessDnsClientIp._NormalizeIp();

            this.AuthLogin_FirstTime = this.AuthLogin_FirstTime._NormalizeDateTimeOffset();
            this.AuthLogin_LastTime = this.AuthLogin_LastTime._NormalizeDateTimeOffset();

            this.Email = this.Email._NonNullTrim();
        }

        public override int GetMaxArchivedCount() => DevCoresConfig.MikakaDDnsServiceSettings.HostRecordMaxArchivedCount;

        public static Host _Sample => new Host
        {
            HostLabel = "tanaka001",
            HostSecretKey = "00112233445566778899AABBCCDDEEFF01020304",
            AuthLogin_LastIpAddress = "10.20.30.40",
            AuthLogin_LastFqdn = "host1.example.org",
            CreateRequestedIpAddress = "3.4.5.6",
            CreateRequestedIpNetwork = "3.4.5.0",
            HostLabel_NumUpdates = 123,
            HostLabel_LastUpdateTime = DtOffsetSample(0.5),
            HostAddress_IPv4 = "8.9.3.1",
            HostAddress_IPv4_FirstUpdateTime = DtOffsetSample(0.1),
            HostAddress_IPv4_LastUpdateTime = DtOffsetSample(0.3),
            HostAddress_IPv4_NumUpdates = 384,
            HostAddress_IPv6 = "2401:AF80:1234:5678:dead:beef:cafe:8945",
            HostAddress_IPv6_FirstUpdateTime = DtOffsetSample(0.1),
            HostAddress_IPv6_LastUpdateTime = DtOffsetSample(0.3),
            HostAddress_IPv6_NumUpdates = 5963,
            DnsQuery_FirstAccessTime = DtOffsetSample(0.1),
            DnsQuery_LastAccessTime = DtOffsetSample(0.5),
            DnsQuery_Count = 12345,
            DnsQuery_FirstAccessDnsClientIp = "1.9.8.4",
            DnsQuery_LastAccessDnsClientIp = "5.9.6.3",
            UsedUnlockKey = "12345-67890-12345-97865-89450",
            AuthLogin_Count = 121,
            AuthLogin_FirstTime = DtOffsetSample(0.05),
            AuthLogin_LastTime = DtOffsetSample(0.6),
            Email = "optos@example.org",
            UserGroupSecretKey = "33884422AAFFCCBB66992244AAAABBBBCCCCDDDD",
            UserData = "{'key1' : 'value1', 'key2' : 'value2'}"._JsonToJsonObject()!,
            CreatedTime = DtOffsetSample(0.1),
            CreateRequestedFqdn = "abc123.pppoe.example.org",
            HostFqdnPrimary = "tanaka001.ddns_example.org",
            ApiResult = HostApiResult.Modified,
        };
    }

    public class MemDb : HadbMemDataBase
    {
        protected override List<Type> GetDefinedUserDataTypesImpl()
        {
            List<Type> ret = new List<Type>();
            ret.Add(typeof(Host));
            return ret;
        }

        protected override List<Type> GetDefinedUserLogTypesImpl()
        {
            List<Type> ret = new List<Type>();
            return ret;
        }
    }

    public class HiveSettings : HadbBasedServiceHiveSettingsBase
    {
        public int DDns_UdpListenPort;

        public override void NormalizeImpl()
        {
            if (DDns_UdpListenPort <= 0) DDns_UdpListenPort = Consts.Ports.Dns;
        }
    }

    public class StartupParam : HadbBasedServiceStartupParam
    {
        public StartupParam(string hiveDataName = "MikakaDDnsService", string defaultHadbSystemName = "MIKAKA_DDNS") : base(hiveDataName, defaultHadbSystemName)
        {
        }
    }

    [RpcInterface]
    public interface IRpc
    {
        [RpcMethodHelp("テスト関数。パラメータで int 型で指定された値を文字列に変換し、Hello という文字列を前置して返却します。RPC を呼び出すためのテストコードを実際に記述する際のテストとして便利です。", "Hello 123")]
        public Task<string> Test([RpcParamHelp("テスト入力整数値", 123)] int i);

        [RpcMethodHelp("DDNS ホストレコードを作成、更新または取得します。")]
        public Task<Host> DDNS_Host(
            [RpcParamHelp("ホストシークレットキーを指定します。新しいホストを作成する際には、新しいキーを指定してください。キーはクライアント側でランダムな 40 文字以内の半角英数字 (0-9、A-Z、ハイフン、アンダーバー の 38 種類の文字) を指定してください。通常は、20 バイトの乱数で生成したユニークなバイナリデータを 16 進数に変換したものを使用してください。既存のホストを更新する際には、既存のキーを指定してください。すでに存在するホストのキーが正確に指定された場合は、そのホストに関する情報の更新を希望するとみなされます。それ以外の場合は、新しいホストの作成を希望するとみなされます。ホストシークレットキーは、大文字・小文字を区別しません。ホストシークレットキーを省略した場合は、新たなホストを作成するものとみなされ、DDNS サーバー側でランダムでユニークなホストシークレットキーが新規作成されます。", "00112233445566778899AABBCCDDEEFF01020304")]
                string secretKey = "",

            [RpcParamHelp("新しいホストラベル (ホストラベルとは、ダイナミック DNS のホスト FQDN の先頭部分のホスト名に相当します。) を作成するか、または既存のホスト名を変更する場合は、作成または変更後の希望ホスト名を指定します。新しくホストを登録する場合で、かつ、希望ホスト名が指定されていない場合は、ランダムな文字列で新しいホスト名が作成されます。", "tanaka001")]
            string label = "",

            [RpcParamHelp("この DDNS サーバーで新しいホストを作成する際に、DDNS サーバーの運営者によって、登録キーの指定を必須としている場合は、未使用の登録キーを 1 つ指定します。登録キーは 25 桁の半角数字です。ハイフンは省略できます。登録キーは DDNS サーバーの運営者から発行されます。一度使用された登録キーは、再度利用することができなくなります。ただし、登録キーを用いて作成されたホストが削除された場合は、その登録キーを再び使用することができるようになります。ホストの更新時には、登録キーは省略できます。この DDNS サーバーが登録キーを不要としている場合は、登録キーは省略できます。", "12345-67890-12345-97865-89450")]
            string unlockKey = "",

            [RpcParamHelp("この DDNS サーバーで新しいホストの作成または既存ホストの変更操作を行なう際に、DDNS サーバーの運営者によって、固定使用許諾文字列の指定を必須としている場合は、固定使用許諾文字列を指定します。固定使用許諾文字列は DDNS サーバーの運営者から通知されます。", "Hello")]
            string licenseString = "",

            [RpcParamHelp("ホストの IP アドレスを登録または更新するには、登録したい新しい IP アドレスを指定します。IP アドレスは明示的に文字列で指定することもできますが、\"myip\" という固定文字列を指定すると、この API の呼び出し元であるホストのグローバル IP アドレスを指定したものとみなされます。なお、IPv4 アドレスと IPv6 アドレスの両方を登録することも可能です。この場合は、IPv4 アドレスと IPv6 アドレスの両方を表記し、その間をカンマ文字 ',' で区切ります。IPv4 アドレスと IPv6 アドレスは、1 つずつしか指定できません。", "myip")]
            string ip = "",

            [RpcParamHelp("ユーザーグループシークレットキーを設定または変更する場合は、ユーザーグループシークレットキーを指定します。ユーザーグループシークレットキーはクライアント側でランダムな 40 文字以内の半角英数字 (0-9、A-Z、ハイフン、アンダーバー の 38 種類の文字) を指定してください。通常は、20 バイトの乱数で生成したユニークなバイナリデータを 16 進数に変換したものを使用してください。複数のホストで、同一のユーザーグループシークレットキーを指定することが可能ですし、それが便利です。ユーザーグループシークレットキーを指定している場合、ユーザーグループシークレットキーを用いたホストレコードの列挙 API を使用すると、同じユーザーグループシークレットキーが登録されているすべてのホストレコードの情報 (各ホストのシークレットキーを含む) を列挙することができます。そのため、ユーザーグループシークレットキーを登録しておけば、同じユーザーグループシークレットキーを有する各ホストレコードの一覧やホストシークレットキーを保持していなくても、いつでも紐付けられたホストレコードにアクセスすることができて便利です。したがって、ユーザーグループシークレットキーは厳重に秘密に管理する必要があります。 \"delete\" という文字列を指定すると、すでに登録されているユーザーグループシークレットキーを消去することができます。", "33884422AAFFCCBB66992244AAAABBBBCCCCDDDD")]
            string userGroupSecretKey = "",

            [RpcParamHelp("このホストレコードの所有者の連絡先メールアドレスを設定するためには、メールアドレス文字列を指定します。メールアドレスは省略することができます。\"delete\" という文字列を指定すると、すでに登録されているメールアドレスを消去することができます。メールアドレスは、この DDNS サーバーのシステム管理者のみが閲覧することができます。DDNS サーバーのシステム管理者は、DDNS サーバーに関するメンテナンスのお知らせを、指定したメールアドレス宛に送付することができます。また、メールアドレスを登録しておけば、ホストキーの回復用 API を呼び出すことにより、そのメールアドレスに紐付いたすべてのホストシークレットキーの一覧を電子メールで送付することができます。これにより、ホストシークレットキーを紛失した場合でも、予めメールアドレスを登録しておくことにより、救済が可能です。", "optos@example.org")]
            string email = "",

            [RpcParamHelp("このパラメータを指定すると、DDNS ホストレコードに付随する永続的なユーザーデータとして、任意の JSON データを記録することができます。記録される JSON データの内容は、DDNS の動作に影響を与えません。たとえば、個人的なメモ等を記録することができます。記録内容は、Key-Value 形式の文字列である必要があります。Key の値は、重複してはなりません。", "{'key1' : 'value1', 'key2' : 'value2'}")]
                JObject? userData = null);
    }

    public EasyDnsResponderBasedDnsServer DnsServer { get; private set; } = null!;

    public MikakaDDnsService(StartupParam? startupParam = null) : base(startupParam ?? new StartupParam())
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
            await this.DnsServer._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }

    protected override void StartImpl()
    {
        this.DnsServer = new EasyDnsResponderBasedDnsServer(
            new EasyDnsResponderBasedDnsServerSettings
            {
                UdpPort = this.SettingsFastSnapshot.DDns_UdpListenPort,
            }
            );

        this.HadbEventListenerList.RegisterCallback(async (caller, type, state, param) =>
        {
            switch (type)
            {
                case HadbEventType.DynamicConfigChanged:
                    ReloadDnsServerSettingFromHadbDynamicConfig();
                    break;
            }
            await Task.CompletedTask;
        });
    }

    // HADB の DynamicConfig を元に DDNS サーバーの設定を構築してリロードする
    void ReloadDnsServerSettingFromHadbDynamicConfig()
    {
        var config = this.Hadb.CurrentDynamicConfig;

        EasyDnsResponderSettings settings = new EasyDnsResponderSettings
        {
            DefaultSettings = new EasyDnsResponderRecordSettings
            {
                TtlSecs = config.DDns_Protocol_Ttl_Secs,
            },
        };

        foreach (var domainFqdn in config.DDns_DomainName)
        {
            var zone = new EasyDnsResponderZone
            {
                DefaultSettings = settings.DefaultSettings,
                DomainName = domainFqdn,
            };

            foreach (var staticRecord in config.DDns_StaticRecord)
            {
                try
                {
                    EasyDnsResponderRecord rec = EasyDnsResponderRecord.FromString(staticRecord, domainFqdn);

                    if (rec.Type == EasyDnsResponderRecordType.NS)
                    {
                        rec.Settings = new EasyDnsResponderRecordSettings { TtlSecs = config.DDns_Protocol_Ttl_Secs_NS_Record };
                    }

                    zone.RecordList.Add(rec);
                }
                catch (Exception ex)
                {
                    ex._Error();
                }
            }

            zone.RecordList.Add(new EasyDnsResponderRecord
            {
                Type = EasyDnsResponderRecordType.SOA,
                Contents = $"{config.DDns_Protocol_SOA_MasterNsServerFqdn} {config.DDns_Protocol_SOA_ResponsibleFieldFqdn} {Consts.Numbers.MagicNumber_u32} {config.DDns_Protocol_SOA_RefreshIntervalSecs} {config.DDns_Protocol_SOA_RetryIntervalSecs} {config.DDns_Protocol_SOA_ExpireIntervalSecs} {config.DDns_Protocol_SOA_NegativeCacheTtlSecs}",
            });

            settings.ZoneList.Add(zone);
        }

        this.DnsServer.LoadSetting(settings);
    }

    protected override DynConfig CreateInitialDynamicConfigImpl()
    {
        return new DynConfig();
    }

    public Task<string> Test(int i) => $"Hello {i}"._TaskResult();

    public async Task<Host> DDNS_Host(string hostSecretKey = "", string newHostLabel = "", string unlockKey = "", string licenseString = "", string ipAddress = "", string userGroupSecretKey = "", string email = "", JObject? userData = null)
    {
        var client = JsonRpcServerApi.GetCurrentRpcClientInfo();

        var now = DtOffsetNow;

        IPAddress clientIp = client.RemoteIP._ToIPAddress()!._RemoveScopeId();
        IPAddress clientNetwork = IPUtil.NormalizeIpNetworkAddressIPv4v6(clientIp, Hadb.CurrentDynamicConfig.DDns_CreateRequestedIpNetwork_SubnetLength_IPv4, Hadb.CurrentDynamicConfig.DDns_CreateRequestedIpNetwork_SubnetLength_IPv6);
        string clientNameStr = clientIp.ToString();

        // パラメータの検査と正規化
        hostSecretKey = hostSecretKey._NonNullTrim().ToUpperInvariant();

        if (hostSecretKey._IsFilled())
        {
            hostSecretKey._CheckUseOnlyChars($"Specified {nameof(hostSecretKey)} contains invalid character.", "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-_");
            hostSecretKey._CheckStrLenException(40, $"Specified {nameof(hostSecretKey)} is too long.");
        }
        else
        {
            // hostSecretKey が指定されていない場合は、新たに乱数で指定されたものとみなす
            hostSecretKey = Str.GenRandStr();
        }

        newHostLabel = newHostLabel._NonNullTrim().ToLowerInvariant();

        if (newHostLabel._IsFilled())
        {
            newHostLabel._CheckUseOnlyChars($"Specified {nameof(newHostLabel)} contains invalid character", "0123456789abcdefghijklmnopqrstuvwxyz-");

            if (newHostLabel.Length < Hadb.CurrentDynamicConfig.DDns_MinHostLabelLen)
                throw new CoresException($"Specified {nameof(newHostLabel)} is too long. {nameof(newHostLabel)} must be longer or equal than {Hadb.CurrentDynamicConfig.DDns_MinHostLabelLen} letters.");

            newHostLabel._CheckStrLenException(Hadb.CurrentDynamicConfig.DDns_MaxHostLabelLen, $"Specified {nameof(newHostLabel)} is too long. {nameof(newHostLabel)} must be shorter or equal than {Hadb.CurrentDynamicConfig.DDns_MaxHostLabelLen} letters.");

            foreach (var item in Hadb.CurrentDynamicConfig.DDns_ProhibitedHostnamesStartWith._NonNullTrim()
                ._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ' ', ';', ',', '/', '\t'))
            {
                if (item._IsFilled())
                {
                    string tmp = item.ToLowerInvariant();
                    if (newHostLabel.StartsWith(tmp, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new CoresException($"Specified {nameof(newHostLabel)} contains prohibited strings. Please try to use another.");
                    }
                }
            }

            if (newHostLabel.StartsWith("-", StringComparison.OrdinalIgnoreCase) ||
                newHostLabel.EndsWith("-", StringComparison.OrdinalIgnoreCase))
            {
                throw new CoresException($"Specified {nameof(newHostLabel)} must not start with or end with hyphon (-) character.");
            }

            if (newHostLabel._InStri("--"))
                throw new CoresException($"Specified {nameof(newHostLabel)} must not contain double hyphon (--) string.");
        }

        unlockKey = unlockKey._MakeStringUseOnlyChars("0123456789");
        licenseString = licenseString._NonNull();

        ipAddress = ipAddress._NonNullTrim();

        IPAddress? ipv4 = null;
        IPAddress? ipv6 = null;
        string ipv4str = "";
        string ipv6str = "";

        void InitIpAddressVariable()
        {
            if (ipAddress._IsFilled())
            {
                if (ipAddress.StartsWith("my", StringComparison.OrdinalIgnoreCase))
                {
                    if (clientIp.AddressFamily == AddressFamily.InterNetwork)
                        ipv4 = clientIp;
                    else if (clientIp.AddressFamily == AddressFamily.InterNetworkV6)
                        ipv6 = clientIp;
                    else
                        throw new CoresException("clientIp has invalid network family.");
                }
                else
                {
                    var ipTokens = ipAddress._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ',', ' ', '\t', ';');

                    if (ipTokens.Length == 0)
                    {
                        ipAddress = "";
                    }
                    else if (ipTokens.Length == 1)
                    {
                        var ip = ipTokens[0]._ToIPAddress(AllowedIPVersions.All, false)!;

                        if (ip.AddressFamily == AddressFamily.InterNetwork)
                            ipv4 = ip;
                        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
                            ipv6 = ip;
                        else
                            throw new CoresException($"{nameof(ipAddress)} has invalid network family.");
                    }
                    else if (ipTokens.Length == 2)
                    {
                        for (int i = 0; i < ipTokens.Length; i++)
                        {
                            var ip = ipTokens[i]._ToIPAddress(AllowedIPVersions.All, false)!;

                            if (ip.AddressFamily == AddressFamily.InterNetwork)
                            {
                                if (ipv4 == null)
                                {
                                    ipv4 = ip;
                                }
                                else
                                {
                                    throw new CoresException($"{nameof(ipAddress)} contains two or more IPv4 addresses");
                                }
                            }
                            else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
                            {
                                if (ipv6 == null)
                                {
                                    ipv6 = ip;
                                }
                                else
                                {
                                    throw new CoresException($"{nameof(ipAddress)} contains two or more IPv6 addresses");
                                }
                            }
                            else
                            {
                                throw new CoresException($"{nameof(ipAddress)} has invalid network family.");
                            }
                        }
                    }
                    else
                    {
                        throw new CoresException($"{nameof(ipAddress)} has invalid format.");
                    }
                }
            }

            ipv4 = ipv4._RemoveScopeId();
            ipv6 = ipv6._RemoveScopeId();

            ipv4str = ipv4?.ToString() ?? "";
            ipv6str = ipv6?.ToString() ?? "";
        }

        InitIpAddressVariable();

        userGroupSecretKey = userGroupSecretKey._NonNullTrim().ToUpperInvariant();

        if (userGroupSecretKey._IsFilled())
        {
            userGroupSecretKey._CheckUseOnlyChars($"Specified {nameof(userGroupSecretKey)} contains invalid character.", "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-_");
            userGroupSecretKey._CheckStrLenException(40, $"Specified {nameof(userGroupSecretKey)} is too long.");
        }

        email = email._NonNullTrim();
        email._CheckUseOnlyChars($"Specified {nameof(email)} contains invalid character", "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ-_@.");

        if (email._IsFilled() && email._IsSamei("delete") == false)
        {
            if (Str.CheckMailAddress(email) == false)
            {
                throw new CoresException($"{nameof(email)} has invalid email address format.");
            }
        }

        if (userData != null) userData = userData._NormalizeEasyJsonStrAttributes();

        string testJsonStr = userData._ObjectToJson(compact: true);
        if (testJsonStr.Length > Hadb.CurrentDynamicConfig.DDns_MaxUserDataJsonStrLength)
        {
            throw new CoresException($"{nameof(userData)} is too large.");
        }

        HadbObject<Host>? retCurrentHostObj = null;

        // まず、指定されたホストシークレットキーに該当するホストがメモリデータベース上にあるかどうか調べる
        var memoryObj = Hadb.FastSearchByKey(new Host { HostSecretKey = hostSecretKey });

        bool anyChangePossibility = true;
        bool needToCreate = false;

        if (memoryObj != null)
        {
            // メモリデータベースを指定したホストキーで検索してみる。
            // すでにメモリデータベース上にあれば、変更点がありそうかどうか確認する。もしメモリデータベース上で変更点がないのに、物理データベース上へのクエリを発生させても、CPU 時間が無駄になるだけの可能性があるためである。
            // ※ メモリデータベース上になければ、変更点がありそうかどうかの検査を保留する。つまり、物理データベースを叩いてみなければ分からない。
            anyChangePossibility = false;

            var current = memoryObj.Data;

            if (newHostLabel._IsFilled() && current.HostLabel._IsSamei(newHostLabel) == false) anyChangePossibility = true;
            if (ipv4str._IsFilled() || ipv6str._IsFilled())
            {
                if (current.HostAddress_IPv4._IsSamei(ipv4str) == false) anyChangePossibility = true;
                if (current.HostAddress_IPv6._IsSamei(ipv6str) == false) anyChangePossibility = true;
            }
            if (userGroupSecretKey._IsSamei("delete"))
            {
                if (current.UserGroupSecretKey._IsFilled()) anyChangePossibility = true;
            }
            else if (userGroupSecretKey._IsFilled())
            {
                if (current.UserGroupSecretKey._IsSamei(userGroupSecretKey) == false) anyChangePossibility = true;
            }
            if (email._IsSamei("delete"))
            {
                if (current.Email._IsFilled()) anyChangePossibility = true;
            }
            else if (email._IsFilled())
            {
                if (current.Email._IsSame(email) == false) anyChangePossibility = true;
            }

            if (userData != null)
            {
                if (current.UserData._ObjectToJson(compact: true)._IsSame(userData._ObjectToJson(compact: true)) == false) anyChangePossibility = true;
            }

            retCurrentHostObj = memoryObj;
        }

        if (anyChangePossibility)
        {
            // メモリデータベース上での変更点があるか、または、メモリデータベース上でオブジェクトが見つからないならば、物理データベースを指定されたホストキーで検索してみる。
            // ここでは、まだ、スナップショット読み取りモードで取得するのである。
            anyChangePossibility = false;

            HadbObject<Host>? dbObj = null;

            await Hadb.TranAsync(false, async tran =>
            {
                dbObj = await tran.AtomicSearchByKeyAsync(new Host { HostSecretKey = hostSecretKey });

                bool checkNewHostKey = false;

                if (dbObj != null)
                {
                    var current = dbObj.Data;
                    if (newHostLabel._IsFilled() && current.HostLabel._IsSamei(newHostLabel) == false)
                    {
                        checkNewHostKey = true;
                    }
                    retCurrentHostObj = dbObj;
                }
                else
                {
                    checkNewHostKey = true;

                    if (newHostLabel._IsEmpty())
                    {
                        // 新しいホストを作成する場合で、newHostLabel が明示的に指定されていない場合は、prefix + 12 桁 (デフォルト) のランダムホスト名を使用する。
                        while (true)
                        {
                            string candidate = Hadb.CurrentDynamicConfig.DDns_NewHostnamePrefix + Str.GenerateRandomDigit(Hadb.CurrentDynamicConfig.DDns_NewHostnameRandomDigits);

                            var existing = await tran.AtomicSearchByKeyAsync(new Host { HostLabel = candidate });

                            if (existing == null)
                            {
                                // 既存のホストと一致しない場合は、これに決める。
                                newHostLabel = candidate;
                                break;
                            }

                            // 既存のホストと一致する場合は、一致しなくなるまで乱数で新たに生成を試みる。
                        }
                    }
                }

                if (checkNewHostKey)
                {
                    // ホストラベルの変更または新規作成を要求された場合は、新しいホストラベルが既存のものと重複しないかどうか確認する。
                    // 重複する場合は、ここで例外を発生させる。
                    // (一意キー制約によって重複は阻止されるが、ここで一応手動でチェックし、重複することが明らかである場合はわかりやすい例外を発生して要求を拒否するのである。)
                    var sameHostLabelExists = await tran.AtomicSearchByKeyAsync(new Host { HostLabel = newHostLabel });

                    if (sameHostLabelExists != null)
                    {
                        throw new CoresException($"The same {nameof(newHostLabel)} '{newHostLabel}' already exists on the DDNS server. Please consider to choose another {nameof(newHostLabel)}.");
                    }
                }
                return false;
            },
            clientName: clientNameStr);

            if (dbObj == null)
            {
                // 物理データベース上にもレコードが存在しない。新規作成する必要があるようだ。
                anyChangePossibility = true;
                needToCreate = true;
            }
            else
            {
                // 物理データベース上のレコードの取得に成功した。同様に変更の可能性があるかどうか確認する。
                var current = dbObj.Data;

                if (newHostLabel._IsFilled() && current.HostLabel._IsSamei(newHostLabel) == false) anyChangePossibility = true;
                if (ipv4str._IsFilled() || ipv6str._IsFilled())
                {
                    if (current.HostAddress_IPv4._IsSamei(ipv4str) == false) anyChangePossibility = true;
                    if (current.HostAddress_IPv6._IsSamei(ipv6str) == false) anyChangePossibility = true;
                }
                if (userGroupSecretKey._IsSamei("delete"))
                {
                    if (current.UserGroupSecretKey._IsFilled()) anyChangePossibility = true;
                }
                else if (userGroupSecretKey._IsFilled())
                {
                    if (current.UserGroupSecretKey._IsSamei(userGroupSecretKey) == false) anyChangePossibility = true;
                }
                if (email._IsSamei("delete"))
                {
                    if (current.Email._IsFilled()) anyChangePossibility = true;
                }
                else if (email._IsFilled())
                {
                    if (current.Email._IsSame(email) == false) anyChangePossibility = true;
                }

                if (userData != null)
                {
                    if (current.UserData._ObjectToJson(compact: true)._IsSame(userData._ObjectToJson(compact: true)) == false) anyChangePossibility = true;
                }
            }
        }

        if (needToCreate)
        {
            // 新規作成の必要がある場合は、データベース上で新規作成をする。
            // なお、前回の DB 読み取りの際にはホストシークレットキーを持つレコードが存在せず、この DB 書き込みの際には存在する可能性
            // もタイミングによっては生じるが、この場合は、DB の一意キー制約によりエラーになるので、二重にホストシークレットキーを有する
            // レコードが存在することとなるおそれはない。

            string clientFqdn = await this.DnsResolver.GetHostNameSingleOrIpAsync(clientIp);

            if (userData == null) userData = Json.NewJsonObject();

            Host newHost = null!;

            if (ipv4str._IsEmpty() && ipv6str._IsEmpty())
            {
                // IP アドレスが IPv4, IPv6 の両方とも指定されていない場合は、クライアントの IP アドレスが改めて指定されたものとみなす。
                ipAddress = "myip";
                InitIpAddressVariable();
            }

            await Hadb.TranAsync(true, async tran =>
            {
                newHost = new Host
                {
                    HostLabel = newHostLabel,
                    HostSecretKey = hostSecretKey,
                    AuthLogin_LastIpAddress = clientIp.ToString(),
                    AuthLogin_LastFqdn = clientFqdn,
                    AuthLogin_Count = 1,
                    AuthLogin_FirstTime = now,
                    AuthLogin_LastTime = now,
                    CreatedTime = now,
                    CreateRequestedIpAddress = clientIp.ToString(),
                    CreateRequestedFqdn = clientFqdn,
                    CreateRequestedIpNetwork = clientNetwork.ToString(),
                    HostLabel_NumUpdates = 1,
                    HostLabel_LastUpdateTime = now,
                    HostAddress_IPv4 = ipv4str,
                    HostAddress_IPv4_FirstUpdateTime = ipv4str._IsFilled() ? now : DtOffsetZero,
                    HostAddress_IPv4_LastUpdateTime = ipv4str._IsFilled() ? now : DtOffsetZero,
                    HostAddress_IPv4_NumUpdates = ipv4str._IsFilled() ? 1 : 0,
                    HostAddress_IPv6 = ipv6str,
                    HostAddress_IPv6_FirstUpdateTime = ipv6str._IsFilled() ? now : DtOffsetZero,
                    HostAddress_IPv6_LastUpdateTime = ipv6str._IsFilled() ? now : DtOffsetZero,
                    HostAddress_IPv6_NumUpdates = ipv6str._IsFilled() ? 1 : 0,
                    UsedUnlockKey = "", // TODO
                    UserGroupSecretKey = userGroupSecretKey,
                    Email = email,
                    UserData = userData,
                };

                await tran.AtomicAddAsync(newHost);

                return true;
            },
            clientName: clientNameStr);

            // 作成に成功したので、ホストオブジェクトを返却する。
            newHost.HostFqdnPrimary = newHost.HostLabel + "." + Hadb.CurrentDynamicConfig.DDns_DomainNamePrimary;
            newHost.ApiResult = HostApiResult.Created;
            return newHost;
        }

        if (anyChangePossibility)
        {
            // データベース上で変更をする。
            Host current = null!;

            await Hadb.TranAsync(true, async tran =>
            {
                // まず最新の DB 上のデータを取得する。これは上の事前の確認時から変更されている可能性もある。
                var currentObj = await tran.AtomicSearchByKeyAsync(new Host { HostSecretKey = hostSecretKey });

                if (currentObj == null)
                {
                    // ここで hostSecretKey が見つからないケースは稀であるが、分散データベースのタイミング上あり得る。
                    // この場合は、再試行するようエラーを出す。
                    throw new CoresException($"{nameof(hostSecretKey)} is not found on the database. Please try again later.");
                }

                current = currentObj.Data;

                if (newHostLabel._IsFilled() && current.HostLabel._IsSamei(newHostLabel) == false)
                {
                    current.HostLabel = newHostLabel;
                    current.HostLabel_LastUpdateTime = now;
                    current.HostLabel_NumUpdates++;
                }

                if (ipv4str._IsFilled() || ipv6str._IsFilled())
                {
                    if (current.HostAddress_IPv4._IsSamei(ipv4str) == false)
                    {
                        current.HostAddress_IPv4 = ipv4str;
                        current.HostAddress_IPv4_LastUpdateTime = now;
                        if (current.HostAddress_IPv4_FirstUpdateTime._IsZeroDateTime())
                        {
                            current.HostAddress_IPv4_FirstUpdateTime = now;
                        }
                        current.HostAddress_IPv4_NumUpdates++;
                    }

                    if (current.HostAddress_IPv6._IsSamei(ipv6str) == false)
                    {
                        current.HostAddress_IPv6 = ipv6str;
                        current.HostAddress_IPv6_LastUpdateTime = now;
                        if (current.HostAddress_IPv6_FirstUpdateTime._IsZeroDateTime())
                        {
                            current.HostAddress_IPv6_FirstUpdateTime = now;
                        }
                        current.HostAddress_IPv6_NumUpdates++;
                    }
                }

                if (userGroupSecretKey._IsSamei("delete"))
                {
                    current.UserGroupSecretKey = "";
                }
                else if (userGroupSecretKey._IsFilled())
                {
                    current.UserGroupSecretKey = userGroupSecretKey;
                }

                if (email._IsSamei("delete"))
                {
                    current.Email = "";
                }
                else if (email._IsFilled())
                {
                    current.Email = email;
                }

                if (userData != null)
                {
                    current.UserData = userData;
                }

                current.AuthLogin_LastIpAddress = clientIp.ToString();
                current.AuthLogin_LastTime = now;
                current.AuthLogin_Count++;

                // DB 上のデータを書き込みする。
                await tran.AtomicUpdateAsync(currentObj);

                return true;
            },
            clientName: clientNameStr);

            // 変更に成功したので、ホストオブジェクトを返却する。
            current.HostFqdnPrimary = current.HostLabel + "." + Hadb.CurrentDynamicConfig.DDns_DomainNamePrimary;
            current.ApiResult = HostApiResult.Modified;
            return current;
        }

        retCurrentHostObj._NullCheck(nameof(retCurrentHostObj));

        var retCurrentHost = retCurrentHostObj.Data;
        string clientFqdn2 = "";

        if (retCurrentHost.AuthLogin_LastIpAddress != clientIp.ToString())
        {
            // Last FQDN は、Last IP Address が変更になった場合のみ DNS を用いて取得する。
            // IP アドレスが前回から変更になっていない場合は、更新しない。
            clientFqdn2 = await this.DnsResolver.GetHostNameSingleOrIpAsync(clientIp);
        }

        // 特に変更がないので、ステータスを Lazy 更新する。
        retCurrentHost = retCurrentHostObj.FastUpdate(h =>
        {
            h.AuthLogin_Count++;
            h.AuthLogin_LastIpAddress = clientIp.ToString();
            if (clientFqdn2._IsFilled())
            {
                h.AuthLogin_LastFqdn = clientFqdn2;
            }
            h.AuthLogin_LastTime = now;
            return true;
        });

        // 現在のオブジェクト情報を返却する。
        retCurrentHost._NullCheck(nameof(retCurrentHost));
        retCurrentHost.HostFqdnPrimary = retCurrentHost.HostLabel + "." + Hadb.CurrentDynamicConfig.DDns_DomainNamePrimary;
        retCurrentHost.ApiResult = HostApiResult.NoChange;

        return retCurrentHost;
    }
}







#endif

