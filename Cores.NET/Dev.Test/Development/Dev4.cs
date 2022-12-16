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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using IPA.Cores.Basic.DnsLib;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
//using Microsoft.EntityFrameworkCore.Query.Internal;

namespace IPA.Cores.Basic;

public static partial class DevCoresConfig
{
    public static partial class OpenIspDnsServiceSettings
    {
        public static readonly Copenhagen<string> Default_Ns1 = "ns01.example.org";
        public static readonly Copenhagen<string> Default_Ns2 = "ns02.example.org";
        public static readonly Copenhagen<string> Default_Responsible = "nobody.example.org";
        public static readonly Copenhagen<int> Default_NegativeCacheTtl = 13;
        public static readonly Copenhagen<int> Default_RefreshInterval = 60;
        public static readonly Copenhagen<int> Default_RetryInterval = 60;
        public static readonly Copenhagen<int> Default_ExpireInterval = 88473600;
        public static readonly Copenhagen<int> Default_DefaultTtl = 60;
    }
}

public static class OpenIspDnsServiceGlobal
{
    // デフォルトの static 証明書
    // SHA1: 80E38CE13AE4FCB033439A6C17B0145D77AD4960
    // SHA256: 35188460BC0767A6B738D118442EFEE5711CC5A8CB62387249E12D459D8E65A4
    static readonly Singleton<PalX509Certificate> OpenIspDnsServerSampleStaticCert_Singleton = new Singleton<PalX509Certificate>(() => new PalX509Certificate(new FilePath(Res.Cores, "SampleDefaultCert/221125MikakaDDnsServerSampleStaticCert-20221125.pfx")));
    public static PalX509Certificate OpenIspDnsServerSampleStaticCert => OpenIspDnsServerSampleStaticCert_Singleton;

    public static void Init()
    {
        GlobalCertVault.SetDefaultCertificateGenerator(_ => OpenIspDnsServiceGlobal.OpenIspDnsServerSampleStaticCert);

        GlobalCertVault.SetDefaultSettingsGenerator(() =>
        {
            var s = new CertVaultSettings(EnsureSpecial.Yes);

            s.MaxAcmeCerts = 16;
            s.NonAcmeEnableAutoGenerateSubjectNameCert = false;
            s.ForceUseDefaultCertFqdnList = new string[] { "*-static.*", "*-static-v4.*", "*-static-v6.*" };

            return s;
        });
    }
}

public class OpenIspDnsServiceStartupParam : HadbBasedServiceStartupParam
{
    public OpenIspDnsServiceStartupParam(string hiveDataName = "OpenIspDnsService", string hadbSystemName = "MIKAKA_DDNS")
    {
        this.HiveDataName = hiveDataName;
        this.HadbSystemName = hadbSystemName;
    }
}

public class OpenIspDnsServiceHook : HadbBasedServiceHookBase
{
}

public class OpenIspDnsService : HadbBasedServiceBase<OpenIspDnsService.MemDb, OpenIspDnsService.DynConfig, OpenIspDnsService.HiveSettings, OpenIspDnsServiceHook>, OpenIspDnsService.IRpc
{
    [Flags]
    public enum ZoneDefType
    {
        Forward = 0,
        Reverse,
    }

    public class Vars
    {
        public StrDictionary<string> VarsList = new StrDictionary<string>(StrCmpi);

        public void Set(string name, string value)
        {
            name = name._NonNullTrim();
            value = value._NonNullTrim();

            if (name._IsEmpty()) return;

            if (value._IsFilled())
            {
                this.VarsList[name] = value;
            }
            else
            {
                this.VarsList.Remove(name);
            }
        }

        public void Unset(string name)
        {
            this.VarsList.Remove(name);
        }
    }

    public class ZoneDefOptions : INormalizable
    {
        public List<string> NameServersFqdnList = new List<string>();
        public string Responsible = "";
        public int NegativeCacheTtl;
        public int RefreshInterval;
        public int RetryInterval;
        public int ExpireInterval;
        public int DefaultTtl;

        public ZoneDefOptions() { }

        public ZoneDefOptions(Vars vars)
        {
            var o = vars.VarsList;

            string[] nsList = o["Ns"]._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.None, " ", "\t", "　", ",", ";");

            nsList._DoForEach(x => this.NameServersFqdnList.Add(x));

            this.Responsible = o["Responsible"];
            this.NegativeCacheTtl = o["NegativeCacheTtl"]._ToInt();
            this.RefreshInterval = o["RefreshInterval"]._ToInt();
            this.RetryInterval = o["RetryInterval"]._ToInt();
            this.ExpireInterval = o["ExpireInterval"]._ToInt();
            this.DefaultTtl = o["DefaultTtl"]._ToInt();

            this.Normalize();
        }

        public void Normalize()
        {
            if (this.NameServersFqdnList == null)
            {
                this.NameServersFqdnList = new List<string>();
            }

            HashSet<string> o = new HashSet<string>(StrCmpi);
            foreach (var ns in this.NameServersFqdnList)
            {
                string tmp = ns._NormalizeFqdn();
                if (tmp._IsFilled())
                {
                    o.Add(tmp);
                }
            }
            this.NameServersFqdnList = o._OrderByValue(StrCmpi).Distinct().ToList();

            if (this.NameServersFqdnList.Any())
            {
                this.NameServersFqdnList.Add(DevCoresConfig.OpenIspDnsServiceSettings.Default_Ns1);
                this.NameServersFqdnList.Add(DevCoresConfig.OpenIspDnsServiceSettings.Default_Ns2);
            }

            if (this.Responsible._IsEmpty()) this.Responsible = DevCoresConfig.OpenIspDnsServiceSettings.Default_Responsible;
            if (this.NegativeCacheTtl <= 0) this.NegativeCacheTtl = DevCoresConfig.OpenIspDnsServiceSettings.Default_NegativeCacheTtl;
            if (this.RefreshInterval <= 0) this.RefreshInterval = DevCoresConfig.OpenIspDnsServiceSettings.Default_RefreshInterval;
            if (this.RetryInterval <= 0) this.RetryInterval = DevCoresConfig.OpenIspDnsServiceSettings.Default_RetryInterval;
            if (this.ExpireInterval <= 0) this.ExpireInterval = DevCoresConfig.OpenIspDnsServiceSettings.Default_ExpireInterval;
            if (this.DefaultTtl <= 0) this.ExpireInterval = DevCoresConfig.OpenIspDnsServiceSettings.Default_DefaultTtl;
        }
    }

    public class CustomRecord
    {
        public string Fqdn = "";
        public string Data = "";
    }

    [Flags]
    public enum StandardRecordType
    {
        ForwardSingle = 0,      // aaa.example.org -> 1.2.3.4
        ForwardWildcard,        // *.aaa.example.org -> 1.2.3.4
        ForwardSubnet,          // 1-*-*-*.aaa.example.org (データ上は *.aaa.example.org) -> 1.0.0.0/24 とかの特定のサブネット
        ReverseSingle,          // 1.2.3.4 -> aaa.example.org
        ReverseSubnet,          // 1.0.0.0/24 -> 1-*-*-*.aaa.example.org
        ReverseCNameSingle,     // 1.2.3.4 -> CNAME 4.3.2.1.aaa.example.org
        ReverseCNameSubnet,     // 1.0.0.0/24 -> CNAME 4.3.2.1.aaa.example.org
    }

    public class StandardRecord
    {
        public StandardRecordType Type;
        public string Fqdn = "";
        public IPAddress IpNetwork = IPAddress.Any;
        public IPAddress IpSubnetMask = IPAddress.Any;
        public int SubnetLength;
    }

    public class ZoneDef
    {
        public ZoneDefType Type;
        public ZoneDefOptions Options = new ZoneDefOptions();
        public string Fqdn = "";
        public IPAddress Reverse_IpNetwork = IPAddress.Any;
        public IPAddress Reverse_IpSubnetMask = IPAddress.Any;
        public int Reverse_SubnetLength;

        public List<CustomRecord> CustomRecordList = new List<CustomRecord>();
        public List<StandardRecord> StandardForwardRecordList = new List<StandardRecord>();

        public ZoneDef() { }

        public ZoneDef(string str, Vars vars)
        {
            if (Str.CheckFqdn(Fqdn) == false)
            {
                throw new CoresException($"Zone string '{Fqdn}' is invalid");
            }

            Fqdn = str._NormalizeFqdn();

            if (Fqdn._InStri("/"))
            {
                // 逆引きゾーン: サブネット表記
                IPUtil.ParseIPAndSubnetMask(Fqdn, out IPAddress net, out IPAddress subnet);
                int subnetLen = IPUtil.SubnetMaskToInt(subnet);
                this.Type = ZoneDefType.Reverse;

                this.Reverse_IpNetwork = net;
                this.Reverse_IpSubnetMask = subnet;
                this.Reverse_SubnetLength = subnetLen;
                this.Fqdn = IPUtil.IPAddressOrSubnetToPtrZoneOrFqdn(net, subnetLen);
            }
            else if (Fqdn.EndsWith("in-addr.arpa", StrCmpi) || Fqdn.EndsWith("ip6.arpa", StrCmpi))
            {
                // 逆引きゾーン: in-addr.arpa または ip6.arpa 表記
                var ipAndSubnet = IPUtil.PtrZoneOrFqdnToIpAddressAndSubnet(Fqdn);

                this.Type = ZoneDefType.Reverse;
                this.Reverse_IpNetwork = ipAndSubnet.Item1;
                this.Reverse_SubnetLength = ipAndSubnet.Item2;
                this.Reverse_IpSubnetMask = IPUtil.IntToSubnetMask(ipAndSubnet.Item1.AddressFamily, ipAndSubnet.Item2);
            }
            else
            {
                // 正引きゾーン
                this.Type = ZoneDefType.Forward;
            }

            this.Options = new ZoneDefOptions(vars);
        }
    }

    public class Config
    {
        public StrDictionary<ZoneDef> ZoneList = new StrDictionary<ZoneDef>();
        public FullRoute46<StandardRecord> ReverseRadixTrie = new FullRoute46<StandardRecord>();
        public List<StandardRecord> ReverseRecordsList = new List<StandardRecord>();

        public Config() { }

        public Config(string body, StringWriter err)
        {
            var lines = body._GetLines(false, false, null, false, false);

            Vars vars = new Vars();

            for (int j = 0; j < 2; j++) // 走査は 2 回行なう。1 回目は変数とゾーン名定義の読み込みである。2 回目はゾーンのレコード情報の読み込みである。
            {
                //if (j == 1)
                //{
                //    // 1 回目の操作で ZoneList が形成されているので、2 回目の操作の最初に ZoneList から逆引きゾーンのフルルートを生成する
                //    foreach (var zone in this.ZoneList.OrderBy(x => x.Key, StrCmpi).Select(x=>x.Value).Where(x=>x.Type == ZoneDefType.Reverse))
                //    {
                //        this.ReverseZoneFullRouteTable.Insert(zone.Reverse_IpNetwork, zone.Reverse_SubnetLength, zone);
                //    }
                //}

                for (int i = 0; i < lines.Length; i++)
                {
                    string lineSrc = lines[i];

                    try
                    {
                        // コメント除去
                        string line = lineSrc.Trim()._StripCommentFromLine(new[] { "#" }).Trim();

                        if (line._IsFilled() && line._GetKeyAndValue(out string key, out string value, " \t"))
                        {
                            key = key.Trim();
                            value = value.Trim();

                            if (key._IsFilled() && value._IsFilled())
                            {
                                if (j == 0)
                                {
                                    // 1 回目の走査
                                    if (key.StartsWith("!"))
                                    {
                                        if (key._IsSamei("!Set"))
                                        {
                                            // 変数の設定
                                            vars.Set(key, value);
                                        }
                                        else if (key._IsSamei("!Unset"))
                                        {
                                            // 変数の削除
                                            vars.Unset(key);
                                        }
                                        else if (key._IsSamei("!DefineZone"))
                                        {
                                            // ゾーンの定義
                                            var zonesTokenList = value._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, " ", "\t", "　", ",", ";");
                                            foreach (var zoneToken in zonesTokenList)
                                            {
                                                var zone = new ZoneDef(zoneToken, vars);
                                                this.ZoneList.Add(zone.Fqdn, zone);
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    // 2 回目の走査
                                    if (key.StartsWith("!") == false)
                                    {
                                        string str1 = key;
                                        string str2;
                                        string paramsStr = "";

                                        // value 部の ! で続くパラメータ制御文字列を除く
                                        int startParamsIndex = value._Search("!");
                                        if (startParamsIndex == -1)
                                        {
                                            // ! がない
                                            str2 = value;
                                        }
                                        else
                                        {
                                            // ! がある
                                            str2 = value.Substring(0, startParamsIndex);
                                            paramsStr = value.Substring(startParamsIndex + 1);
                                        }

                                        QueryStringList paramsList = new QueryStringList(paramsStr, splitChar: ',', trimKeyAndValue: true);

                                        if (str1._InStr(":") == false && str1._InStr(".") == false && str1._InStr("/") == false)
                                        {
                                            string recordTypeStr = str1; // "MX" とか
                                            string fqdnAndData = str2;

                                            // MX とかの手動レコード設定である。
                                            var recordType = EasyDnsResponderRecord.StrToRecordType(recordTypeStr);
                                            if (recordType == EasyDnsResponderRecordType.None)
                                            {
                                                // レコードタイプ文字列が不正である
                                                throw new CoresException($"Specified manual DNS record type '{recordTypeStr}' is invalid");
                                            }
                                            if (recordType.EqualsAny(EasyDnsResponderRecordType.Any, EasyDnsResponderRecordType.SOA))
                                            {
                                                // 手動指定できない特殊なレコードタイプである
                                                throw new CoresException($"Specified manual DNS record type '{recordType}' cannot be specified here");
                                            }

                                            // パースの試行 (ここでは、単に文法チェックためにパースを試行するだけであり、結果は不要である)
                                            EasyDnsResponderRecord.TryParseFromString(recordTypeStr + " " + fqdnAndData);

                                            // FQDN 部分とデータ部分に分ける
                                            if (fqdnAndData._GetKeysListAndValue(1, out var tmp1, out string data) == false)
                                            {
                                                throw new CoresException($"DNS Record String: Invalid Format. Str = '{fqdnAndData}'");
                                            }

                                            // FQDN 部の検査
                                            if (recordType == EasyDnsResponderRecordType.PTR)
                                            {
                                                // PTR は特別処理を行なう (ゾーンが /24 の広さで、PTR のワイルドカード指定が /22 の広さ、というようなことが起こり得るので、ゾーンとは直接関連付けない)
                                                // PTR の場合、FQDN 部は IP アドレスまたは IP サブネット、または in-addr.arpa または ip6.arpa 形式でなければならない
                                                string ipOrSubnetStr = tmp1[0]._NonNullTrim();
                                                string fqdn = tmp1[0]._NormalizeFqdn();
                                                int subnetLength;
                                                IPAddress ipOrSubnet;

                                                if (fqdn == "in-addr.arpa" || fqdn.EndsWith(".in-addr.arpa") || fqdn == "ip6.addr" || fqdn.EndsWith(".ip6.addr"))
                                                {
                                                    var tmp = IPUtil.PtrZoneOrFqdnToIpAddressAndSubnet(fqdn);
                                                    ipOrSubnet = tmp.Item1;
                                                    subnetLength = tmp.Item2;
                                                }
                                                else
                                                {
                                                    IPUtil.ParseIPAndMask(ipOrSubnetStr, out ipOrSubnet, out IPAddress subnetMask);
                                                    subnetLength = IPUtil.SubnetMaskToInt(subnetMask);
                                                }

                                                bool isHostAddress = IPUtil.IsSubnetLenHostAddress(ipOrSubnet.AddressFamily, subnetLength);

                                                //if (isHostAddress)
                                                //{
                                                //    // 1.2.3.4 -> aaa.example.org のようなホストアドレスが指定される場合は、FQDN は Wildcard 不可である。
                                                //    if (fqdn._IsValidFqdn(false) == false)
                                                //    {
                                                //        throw new CoresException("PTR record's target hostname must be a valid single host FQDN");
                                                //    }
                                                //}
                                                //else
                                                //{
                                                //    // 1.0.0.0/24 -> *.aaa.example.org のようなホストアドレスが指定される場合は、FQDN は Wildcard 可である。
                                                //    // ただし、Wildcard は *.aaa は可であるが、abc-***.aaa は不可であるから、厳密にチェックをする。
                                                if (fqdn._IsValidFqdn(true, true) == false)
                                                {
                                                    throw new CoresException("PTR record's target hostname must be a valid single host FQDN or wildcard FQDN");
                                                }
                                                //}

                                                StandardRecord r = new StandardRecord
                                                {
                                                    Type = isHostAddress ? StandardRecordType.ReverseSingle : StandardRecordType.ReverseSubnet,
                                                    Fqdn = fqdn,
                                                    IpNetwork = ipOrSubnet,
                                                    IpSubnetMask = IPUtil.IntToSubnetMask(ipOrSubnet.AddressFamily, subnetLength),
                                                    SubnetLength = subnetLength,
                                                };

                                                this.ReverseRecordsList.Add(r);
                                            }
                                            else
                                            {
                                                string fqdn = tmp1[0]._NormalizeFqdn();

                                                if (fqdn._IsValidFqdn(recordType != EasyDnsResponderRecordType.NS) == false) // NS レコードではワイルドカードは使用できない
                                                {
                                                    throw new CoresException($"DNS Record String: Invalid FQDN: '{fqdn}'");
                                                }

                                                // FQDN 部分を元に、DefineZone 済みのゾーンのいずれに一致するか検索する (最長一致)
                                                var zone = DnsUtil.SearchLongestMatchDnsZone<ZoneDef>(this.ZoneList, fqdn, out string hostLabel, out _);

                                                if (zone == null)
                                                {
                                                    // 対応するゾーンがない
                                                    throw new CoresException($"Specified DNS record doesn't match any DefineZone zones");
                                                }

                                                if (recordType == EasyDnsResponderRecordType.NS)
                                                {
                                                    // NS レコードの場合は、必ず、定義済み Zone レコードの FQDN よりも長くなければならない (サブドメインでなければならない)。
                                                    // (つまり、定義済み Zone レコードの FQDN と完全一致してはならない。)
                                                    if (zone.Fqdn._NormalizeFqdn() == fqdn._NormalizeFqdn())
                                                    {
                                                        throw new CoresException($"NS record's target FQDN must be a subdomain of the parent domain (meaning that it must not be exact match to the parent domain)");
                                                    }
                                                }

                                                // ゾーンのカスタムレコードとして追加
                                                zone.CustomRecordList.Add(new CustomRecord { Fqdn = fqdn, Data = data });
                                            }
                                        }
                                        else
                                        {
                                            // 普通の正引きまたは逆引きレコード
                                            // 1.2.3.0/24   *.aaa.example.org
                                            // 1.2.3.4      abc.example.org とか
                                            string ipMaskListStr;
                                            string fqdnListStr;

                                            // str1 と str2 は入れ換え可能である。
                                            // 1 と 2 のいずれに IP アドレスが含まれているのか判別をする。
                                            if (IsIpSubnetStr(str1))
                                            {
                                                ipMaskListStr = str1;
                                                fqdnListStr = str2;
                                            }
                                            else
                                            {
                                                fqdnListStr = str1;
                                                ipMaskListStr = str2;
                                            }

                                            // 複数書いてあることもあるので、パースを行なう
                                            string[] ipMaskList = ipMaskListStr._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, " ", "\t", "　", ",", ";");
                                            string[] fqdnList = fqdnListStr._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, " ", "\t", "　", ",", ";");

                                            foreach (var ipMask in ipMaskList)
                                            {
                                                foreach (var tmp2 in fqdnList)
                                                {
                                                    string tmp1 = tmp2;
                                                    int mode = 0;
                                                    IPUtil.ParseIPAndSubnetMask(ipMask, out IPAddress ipOrSubnet, out IPAddress mask);
                                                    int subnetLength = IPUtil.SubnetMaskToInt(mask);
                                                    bool isHostAddress = IPUtil.IsSubnetLenHostAddress(ipOrSubnet.AddressFamily, subnetLength);

                                                    if (tmp1.StartsWith("+"))
                                                    {
                                                        // +aaa.example.org のような形式である
                                                        // これは、1.2.3.4 -> CNAME 4.3.2.1.aaa.example.org のような特殊な CNAME 変換を意味する
                                                        mode = 1;
                                                        tmp1 = tmp1.Substring(1);
                                                    }
                                                    else if (tmp1.StartsWith("@"))
                                                    {
                                                        // @ns01.example.org のような形式である。
                                                        // これは、1.2.3.0/24 -> NS ns01.example.org のような NS Delegate を意味する
                                                        mode = 2;
                                                        tmp1 = tmp1.Substring(1);
                                                    }
                                                    string fqdn = tmp1._NormalizeFqdn();
                                                    if (fqdn._IsValidFqdn(mode == 0, true) == false)
                                                    {
                                                        // おかしな FQDN である
                                                        throw new CoresException($"FQDN '{fqdn}' is not a valid single host or wildcard FQDN");
                                                    }

                                                    if (mode == 1)
                                                    {
                                                        // CNAME 特殊処理
                                                        StandardRecord r = new StandardRecord
                                                        {
                                                            Type = isHostAddress ? StandardRecordType.ReverseCNameSingle : StandardRecordType.ReverseCNameSubnet,
                                                            Fqdn = fqdn,
                                                            IpNetwork = ipOrSubnet,
                                                            IpSubnetMask = IPUtil.IntToSubnetMask(ipOrSubnet.AddressFamily, subnetLength),
                                                            SubnetLength = subnetLength,
                                                        };

                                                        this.ReverseRecordsList.Add(r);
                                                    }
                                                    else if (mode == 2)
                                                    {
                                                        // NS 特殊処理
                                                        // NS の場合、内部的に in-addr.arpa または ip6.arpa に変換する
                                                        string reverseFqdn = IPUtil.IPAddressOrSubnetToPtrZoneOrFqdn(ipOrSubnet, subnetLength);

                                                        // reverseFqdn を元に、DefineZone 済みのゾーンのいずれに一致するか検索する (最長一致)
                                                        var zone = DnsUtil.SearchLongestMatchDnsZone<ZoneDef>(this.ZoneList, reverseFqdn, out string hostLabel, out _);

                                                        if (zone == null)
                                                        {
                                                            // 対応するゾーンがない
                                                            throw new CoresException($"Specified reverse DNS zone '{reverseFqdn}' doesn't match any DefineZone zones");
                                                        }

                                                        // NS レコードの場合は、必ず、定義済み Zone レコードの FQDN よりも長くなければならない (サブドメインでなければならない)。
                                                        // (つまり、定義済み Zone レコードの FQDN と完全一致してはならない。)
                                                        if (zone.Fqdn._NormalizeFqdn() == reverseFqdn._NormalizeFqdn())
                                                        {
                                                            throw new CoresException($"Reverse DNS zone '{reverseFqdn}' must be a subdomain of the parent domain '{zone.Fqdn}' (meaning that it must not be exact match to the parent domain)");
                                                        }

                                                        // ゾーンのカスタムレコードとして追加
                                                        zone.CustomRecordList.Add(new CustomRecord { Fqdn = fqdn, Data = fqdn });
                                                    }
                                                    else
                                                    {
                                                        // 普通の正引き + 逆引き同時定義レコード

                                                        // 最初に、正引きの処理をする。
                                                        // ゾーン検索
                                                        var zone = DnsUtil.SearchLongestMatchDnsZone<ZoneDef>(this.ZoneList, fqdn, out string hostLabel, out _);
                                                        if (zone != null)
                                                        {
                                                            // 対応するゾーンがある場合のみ処理を行なう。対応するゾーンがない場合は、何もエラーを出さずに無視する。
                                                            StandardRecord r = new StandardRecord
                                                            {
                                                                Type = isHostAddress ? StandardRecordType.ForwardSingle : StandardRecordType.ForwardSubnet,
                                                                Fqdn = fqdn,
                                                                IpNetwork = ipOrSubnet,
                                                                IpSubnetMask = IPUtil.IntToSubnetMask(ipOrSubnet.AddressFamily, subnetLength),
                                                                SubnetLength = subnetLength,
                                                            };

                                                            // ゾーンの通常レコードとして追加
                                                            zone.StandardForwardRecordList.Add(r);
                                                        }

                                                        // 次に、逆引きの処理をする。
                                                        // これは、ゾーンに無関係に登録をすればよい。
                                                        if (true)
                                                        {
                                                            StandardRecord r = new StandardRecord
                                                            {
                                                                Type = isHostAddress ? StandardRecordType.ReverseSingle : StandardRecordType.ReverseSubnet,
                                                                Fqdn = fqdn,
                                                                IpNetwork = ipOrSubnet,
                                                                IpSubnetMask = IPUtil.IntToSubnetMask(ipOrSubnet.AddressFamily, subnetLength),
                                                                SubnetLength = subnetLength,
                                                            };

                                                            this.ReverseRecordsList.Add(r);
                                                        }
                                                    }
                                                }
                                            }
                                        }

                                        // "IP/サブネットマスク" 形式の表記かどうか検索するユーティリティ関数
                                        bool IsIpSubnetStr(string str)
                                        {
                                            str = str._NonNull();
                                            int i = str._Search("/");
                                            if (i == -1)
                                            {
                                                if (IPAddress.TryParse(str, out _))
                                                {
                                                    return true;
                                                }
                                            }
                                            else
                                            {
                                                string ip = str.Substring(0, i).Trim();
                                                string mask = str.Substring(i + 1).Trim();
                                                if (IPAddress.TryParse(ip, out _))
                                                {
                                                    if (int.TryParse(mask, out _) || IPAddress.TryParse(mask, out _))
                                                    {
                                                        return true;
                                                    }
                                                }
                                            }
                                            return false;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        err.WriteLine($"Line #{i + 1}: '{lineSrc}'");
                        err.WriteLine($"  Error: {ex.Message}");
                    }
                }
            }

            // すべてのレコードのデータ登録が終わったら、まとめて、逆引きロンゲストマッチ用 Radix Trie を生成する。
            foreach (var r in this.ReverseRecordsList.Where(x => x.Type.EqualsAny(StandardRecordType.ReverseCNameSingle, StandardRecordType.ReverseCNameSubnet, StandardRecordType.ReverseSingle, StandardRecordType.ReverseSubnet)))
            {
                // 上から順に優先して登録する。全く同一のサブネットについては最初の 1 つしか登録されないが、これは正常である。
                // (PTR レコードで 2 つ以上の互いに異なる応答があるのはおかしい)
                this.ReverseRadixTrie.Insert(r.IpNetwork, r.SubnetLength, r);
            }
        }
    }

    public class DynConfig : HadbBasedServiceDynConfig
    {
        [SimpleComment("Set true to make DNS server to write DNS debug access logs to the Log/Access directory")]
        public bool Dns_SaveDnsQueryAccessLogForDebug = false;

        [SimpleComment("Copy query packet's Additional Records field to the response packet (including EDNS fields). This might confuse DNS cache server like dnsdist.")]
        public bool Dns_Protocol_CopyQueryAdditionalRecordsToResponse = false;

        [SimpleComment("Set true if you want this DDNS server to accept UDP Proxy Protocol Version 2.0 (defined by haproxy and supported by some DNS proxies such as dnsdist)")]
        public bool Dns_Protocol_ParseUdpProxyProtocolV2 = false;

        [SimpleComment("If DDns_Protocol_AcceptUdpProxyProtocolV2 is true you can specify the source IP address ACL to accept UDP Proxy Protocol (You can specify multiple items. e.g. 127.0.0.0/8,1.2.3.0/24)")]
        public string Dns_Protocol_ProxyProtocolAcceptSrcIpAcl = "";

        protected override void NormalizeImpl()
        {
            Dns_Protocol_ProxyProtocolAcceptSrcIpAcl = EasyIpAcl.NormalizeRules(Dns_Protocol_ProxyProtocolAcceptSrcIpAcl, false, true);

            base.NormalizeImpl();
        }
    }

    public class MemDb : HadbBasedServiceMemDb
    {
        protected override void AddDefinedUserDataTypesImpl(List<Type> ret)
        {
        }

        protected override void AddDefinedUserLogTypesImpl(List<Type> ret)
        {
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

    [RpcInterface]
    public interface IRpc : IHadbBasedServiceRpcBase
    {
        [RpcMethodHelp("テスト関数。パラメータで int 型で指定された値を文字列に変換し、Hello という文字列を前置して返却します。RPC を呼び出すためのテストコードを実際に記述する際のテストとして便利です。", "Hello 123")]
        public Task<string> Test_HelloWorld([RpcParamHelp("テスト入力整数値", 123)] int i);
    }

    public EasyDnsResponderBasedDnsServer DnsServer { get; private set; } = null!;

    AsyncLoopManager LoopManager = null!;

    public OpenIspDnsService(OpenIspDnsServiceStartupParam startupParam, OpenIspDnsServiceHook hook) : base(startupParam, hook)
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

    protected override void StartImpl()
    {
        this.DnsServer = new EasyDnsResponderBasedDnsServer(
            new EasyDnsResponderBasedDnsServerSettings
            {
                UdpPort = this.SettingsFastSnapshot.DDns_UdpListenPort,
            }
            );

        this.LoopManager = new AsyncLoopManager(new AsyncLoopManagerSettings(this.ReloadLoopTaskAsync));

        this.HadbEventListenerList.RegisterCallback(async (caller, type, state, param) =>
        {
            switch (type)
            {
                case HadbEventType.DynamicConfigChanged:
                    this.LoopManager.Fire();
                    break;

                case HadbEventType.ReloadDataFull:
                case HadbEventType.ReloadDataPartially:
                case HadbEventType.DatabaseStateChangedToRecovery:
                    this.DnsServer.LastDatabaseHealtyTimeStamp = DateTime.Now;
                    break;
            }

            if (type == HadbEventType.DatabaseStateChangedToFailure || type == HadbEventType.DatabaseStateChangedToRecovery)
            {
                // DB への接続状態が変化した (エラー → 成功 / 成功 → エラー) の場合は直ちに LoopManager を Fire して Config を Reload する
                this.LoopManager.Fire();
            }

            await Task.CompletedTask;
        });
    }

    protected override async Task StopImplAsync(Exception? ex)
    {
        await this.LoopManager._DisposeSafeAsync(ex);

        await this.DnsServer._DisposeSafeAsync(ex);
    }

    protected override async Task<OkOrExeption> HealthCheckImplAsync(CancellationToken cancel)
    {
        await Task.CompletedTask;

        this.Hadb.CheckIfReady(true);

        return new OkOrExeption();
    }

    // HADB の DynamicConfig を元に DDNS サーバーの設定を構築してリロードする
    async Task ReloadLoopTaskAsync(AsyncLoopManager manager, CancellationToken cancel)
    {
        await Task.CompletedTask;

        var config = this.Hadb.CurrentDynamicConfig;

        EasyDnsResponderSettings settings = new EasyDnsResponderSettings
        {
            DefaultSettings = new EasyDnsResponderRecordSettings
            {
                TtlSecs = 1234,
            },
        };

        settings.SaveAccessLogForDebug = config.Dns_SaveDnsQueryAccessLogForDebug;
        settings.CopyQueryAdditionalRecordsToResponse = config.Dns_Protocol_CopyQueryAdditionalRecordsToResponse;

        this.DnsServer.ApplySetting(settings);

        var currentDynOptions = this.DnsServer.DnsServer.GetCurrentDynOptions();

        currentDynOptions.ParseUdpProxyProtocolV2 = config.Dns_Protocol_ParseUdpProxyProtocolV2;
        currentDynOptions.DnsProxyProtocolAcceptSrcIpAcl = config.Dns_Protocol_ProxyProtocolAcceptSrcIpAcl;

        this.DnsServer.DnsServer.SetCurrentDynOptions(currentDynOptions);

        this.DnsServer.DnsResponder.Callback = (req) =>
        {
            var config = this.Hadb.CurrentDynamicConfig;

            var ret = new EasyDnsResponderDynamicRecordCallbackResult
            {
                IPAddressList = new List<IPAddress>(),
                MxFqdnList = new List<DomainName>(),
                MxPreferenceList = new List<ushort>(),
                TextList = new List<string>(),
                CaaList = new List<Tuple<byte, string, string>>(),
            };

            string targetLabel = req.RequestHostName;

            return ret;
        };

        //if (config.DDns_HealthCheck_IntervalSecs >= 1)
        {
            //manager.LoopIntervalMsecs = config.DDns_HealthCheck_IntervalSecs * 1000;
        }
    }

    protected override DynConfig CreateInitialDynamicConfigImpl()
    {
        return new DynConfig();
    }

    public Task<string> Test_HelloWorld(int i) => $"Hello {i}"._TaskResult();
}







#endif

