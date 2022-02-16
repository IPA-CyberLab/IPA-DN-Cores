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
        public int DDns_MaxHostPerCreateClientIpAddress;
        public int DDns_MaxHostPerCreateClientIpNetwork;
        public int DDns_CreateRequestedIpNetwork_SubnetLength_IPv4;
        public int DDns_CreateRequestedIpNetwork_SubnetLength_IPv6;
        public string DDns_ProhibitedHostnamesStartWith = "_initial_";
        public int DDns_MaxUserDataJsonStrLength = 10 * 1024;
        public bool DDns_RequireUnlockKey = false;
        public string DDns_NewHostnamePrefix = "";
        public bool DDns_Prohibit_IPv4AddressRegistration = false;
        public bool DDns_Prohibit_IPv6AddressRegistration = false;
        public int DDns_MinHostLabelLen;
        public int DDns_MaxHostLabelLen;

        protected override void NormalizeImpl()
        {
            if (DDns_MaxHostPerCreateClientIpAddress <= 0) DDns_MaxHostPerCreateClientIpAddress = 100;
            if (DDns_MaxHostPerCreateClientIpNetwork <= 0) DDns_MaxHostPerCreateClientIpNetwork = 1000;

            if (DDns_CreateRequestedIpNetwork_SubnetLength_IPv4 <= 0 || DDns_CreateRequestedIpNetwork_SubnetLength_IPv4 > 32) DDns_CreateRequestedIpNetwork_SubnetLength_IPv4 = 24;
            if (DDns_CreateRequestedIpNetwork_SubnetLength_IPv6 <= 0 || DDns_CreateRequestedIpNetwork_SubnetLength_IPv6 > 128) DDns_CreateRequestedIpNetwork_SubnetLength_IPv6 = 56;

            if (DDns_ProhibitedHostnamesStartWith._IsSamei("_initial_"))
                DDns_ProhibitedHostnamesStartWith = new string[] {
                   "ddns",
                   "www",
                   "dns",
                   "register",
                   "admin",
                   "mail",
                   "ns00",
                   "smtp",
                   "root",
                   "pop3",
                   "imap",
                   "ftp",
                   "ws-",
                   "websocket-",
                   "_acme",
                }._Combine(";");

            DDns_ProhibitedHostnamesStartWith = DDns_ProhibitedHostnamesStartWith._NonNullTrim()
                ._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ' ', ';', ',', '/', '\t')._Combine(",").ToLowerInvariant();

            if (DDns_NewHostnamePrefix._IsEmpty()) DDns_NewHostnamePrefix = "vpn";
            DDns_NewHostnamePrefix = DDns_NewHostnamePrefix._NonNullTrim().ToLowerInvariant()._MakeStringUseOnlyChars();

            if (DDns_MinHostLabelLen <= 0) DDns_MinHostLabelLen = 3;
            if (DDns_MaxHostLabelLen <= 0) DDns_MaxHostLabelLen = 32;
            if (DDns_MaxHostLabelLen >= 64) DDns_MaxHostLabelLen = 63;
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

    public class Host : HadbData
    {
        public string HostLabel = "";
        public string HostSecretKey = "";

        public string AuthLogin_LastIpAddress = "";
        public string AuthLogin_LastFqdn = "";

        public DateTimeOffset AuthRequested_FirstTime = DtOffsetZero;
        public DateTimeOffset AuthRequested_LastTime = DtOffsetZero;

        public string CreateRequestedIpAddress = "";
        public string CreateRequestedIpNetwork = "";

        public long HostLabel_NumUpdates = 0;
        public DateTimeOffset HostLabel_LastUpdateTime = DtOffsetZero;

        public string HostAddress_IPv4 = "";
        public DateTimeOffset HostAddress_IPv4_FirstUpdateTime = DtOffsetZero;
        public DateTimeOffset HostAddress_IPv4_LastUpdateTime = DtOffsetZero;
        public long HostAddress_IPv4_NumUpdates = 0;

        public string HostAddress_IPv6 = "";
        public DateTimeOffset HostAddress_IPv6_FirstUpdateTime = DtOffsetZero;
        public DateTimeOffset HostAddress_IPv6_LastUpdateTime = DtOffsetZero;
        public long HostAddress_IPv6_NumUpdates = 0;

        public DateTimeOffset DnsQuery_FirstAccessTime = DtOffsetZero;
        public DateTimeOffset DnsQuery_LastAccessTime = DtOffsetZero;
        public long DnsQuery_Count = 0;
        public string DnsQuery_FirstAccessDnsClientIp = "";
        public string DnsQuery_LastAccessDnsClientIp = "";

        public string UsedUnlockKey = "";

        public JObject UserData = Json.NewJsonObject();

        public override HadbKeys GetKeys() => new HadbKeys(this.HostSecretKey, this.HostLabel, this.UsedUnlockKey);
        public override HadbLabels GetLabels() => new HadbLabels(this.CreateRequestedIpAddress, this.CreateRequestedIpNetwork);

        public override void Normalize()
        {
            this.HostSecretKey = this.HostSecretKey._NormalizeKey(true);
            this.HostLabel = this.HostLabel._NormalizeFqdn();

            this.CreateRequestedIpAddress = this.CreateRequestedIpAddress._NormalizeIp();
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

            this.AuthRequested_FirstTime = this.AuthRequested_FirstTime._NormalizeDateTimeOffset();
            this.AuthRequested_LastTime = this.AuthRequested_LastTime._NormalizeDateTimeOffset();
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
            UserData = "{'key1' : 'value1', 'key2' : 'value2'}"._JsonToJsonObject()!,
        };
    }

    public class MemDb : HadbMemDataBase
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

    public class HiveSettings : HadbBasedServiceHiveSettingsBase { }

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

        [RpcMethodHelp("DDNS ホストレコードを作成または更新します。")]
        public Task<Host> DDNS_Register_Or_Update_Host(
            [RpcParamHelp("ホストシークレットキーを指定します。新しいホストを作成する際には、新しいキーを指定してください。キーはクライアント側でランダムな 40 文字以内の半角英数字 (0-9 および A-Z の 36 種類の文字) を指定してください。通常は、20 バイトの乱数で生成したユニークなバイナリデータを 16 進数に変換したものを使用してください。既存のホストを更新する際には、既存のキーを指定してください。すでに存在するホストのキーが正確に指定された場合は、そのホストに関する情報の更新を希望するとみなされます。それ以外の場合は、新しいホストの作成を希望するとみなされます。ホストシークレットキーは、大文字・小文字を区別しません。ホストシークレットキーを省略した場合は、新たなホストを作成するものとみなされ、DDNS サーバー側でランダムでユニークなホストシークレットキーが新規作成されます。", "00112233445566778899AABBCCDDEEFF01020304")]
                string hostSecretKey = "",

            [RpcParamHelp("新しいホストラベル (ホストラベルとは、ダイナミック DNS のホスト FQDN の先頭部分のホスト名に相当します。) を作成するか、または既存のホスト名を変更する場合は、作成または変更後の希望ホスト名を指定します。新しくホストを登録する場合で、かつ、希望ホスト名が指定されていない場合は、ランダムな文字列で新しいホスト名が作成されます。", "tanaka001")]
            string newHostLabel = "",

            [RpcParamHelp("この DDNS サーバーで新しいホストを作成する際に、DDNS サーバーの運営者によって、登録キーの指定を必須としている場合は、未使用の登録キーを 1 つ指定します。登録キーは 25 桁の半角数字です。ハイフンは省略できます。登録キーは DDNS サーバーの運営者から発行されます。一度使用された登録キーは、再度利用することができなくなります。ただし、登録キーを用いて作成されたホストが削除された場合は、その登録キーを再び使用することができるようになります。ホストの更新時には、登録キーは省略できます。この DDNS サーバーが登録キーを不要としている場合は、登録キーは省略できます。", "12345-67890-12345-97865-89450")]
            string unlockKey = "",

            [RpcParamHelp("この DDNS サーバーで新しいホストを作成する際に、DDNS サーバーの運営者によって、固定使用許諾文字列の指定を必須としている場合は、固定使用許諾文字列を指定します。固定使用許諾文字列は DDNS サーバーの運営者から通知されます。", "The_ancient_pond_A_frog_leaps_in_The_sound_of_the_water")]
            string licenseString = "",

            [RpcParamHelp("ホストの IP アドレスを登録または更新するには、登録したい新しい IPv4 アドレスを指定します。なお、IPv4 アドレスと IPv6 アドレスの両方を登録することも可能です。この場合は、IPv4 アドレスと IPv6 アドレスの両方を表記し、その間をカンマ文字 ',' で区切ります。IPv4 アドレスと IPv6 アドレスは、1 つずつしか指定できません。", "1.2.3.4,2041:AF80:123::456")]
            string ipAddress = "",

            [RpcParamHelp("このパラメータを指定すると、DDNS ホストレコードに付随する永続的なユーザーデータとして、任意の JSON データを記録することができます。記録される JSON データの内容は、DDNS の動作に影響を与えません。たとえば、個人的なメモ等を記録することができます。記録内容は、Key-Value 形式の文字列である必要があります。Key の値は、重複してはなりません。", "{'key1' : 'value1', 'key2' : 'value2'}")]
                JObject? userData = null)
        {
            return new Host()._TaskResult();
        }
    }

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
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }

    protected override DynConfig CreateInitialDynamicConfigImpl()
    {
        return new DynConfig();
    }

    public Task<string> Test(int i) => $"Hello {i}"._TaskResult();
}







#endif

