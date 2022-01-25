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

namespace IPA.Cores.Basic;

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
    public string RequestName { init; get; } = null!;
}

// ダイナミックレコードのコールバック関数で返却すべきデータ
public class EasyDnsResponderDynamicRecordCallbackResult
{
    public IEnumerable<IPAddress>? IPAddressList { get; set; } // A, AAAA の場合
    public IEnumerable<DomainName>? DomainNameList { get; set; } // CNAME, MX, NS, PTR の場合
    public IEnumerable<ushort>? MxPreferenceList { get; set; } // MX の場合の Preference 値のリスト
    public IEnumerable<string>? TextList { get; set; } // TXT の場合

    public EasyDnsResponderRecordSettings? Settings { get; set; } // TTL 等
}

public class EasyDnsResponder
{
    // ダイナミックレコードのコールバック関数
    public Func<EasyDnsResponderDynamicRecordCallbackRequest, EasyDnsResponderDynamicRecordCallbackResult>? Callback { get; set; }

    // 内部データセット
    public class DataSet
    {
        // 内部レコードデータ
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
        }

        public class Record_AAAA : Record
        {
            public IPAddress IPv6Address;

            public Record_AAAA(Zone parent, EasyDnsResponderRecord src) : base(parent, src)
            {
                string tmp = src.Contents._NonNullTrim();
                if (tmp._IsEmpty()) throw new CoresLibException("Contents is empty.");

                this.IPv6Address = IPAddress.Parse(tmp);

                if (this.IPv6Address.AddressFamily != AddressFamily.InterNetworkV6)
                    throw new CoresLibException($"AddressFamily of '{tmp}' is not IPv6.");
            }
        }

        public class Record_NS : Record
        {
            public DomainName ServerName;

            public Record_NS(Zone parent, EasyDnsResponderRecord src) : base(parent, src)
            {
                string tmp = src.Contents._NonNullTrim();
                if (tmp._IsEmpty()) throw new CoresLibException("Contents is empty.");

                this.ServerName = DomainName.Parse(tmp);

                if (this.ServerName.IsEmptyDomain()) throw new CoresLibException("NS server field is empty.");
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
                string[] tokens = src.Contents._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ';', ',');

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
        }

        public class Record_TXT : Record
        {
            public string TextData;

            public Record_TXT(Zone parent, EasyDnsResponderRecord src) : base(parent, src)
            {
                this.TextData = src.Contents._NonNull();
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
                        break;
                }

                throw new CoresLibException($"Invalid Dynamic Record Type '{this.Name}': {src.Type}");
            }
        }

        public abstract class Record
        {
            public Zone ParentZone;
            public string Name;
            public EasyDnsResponderRecordType Type;
            public EasyDnsResponderRecordSettings Settings;
            public EasyDnsResponderRecord SrcRecord;

            public Record(Zone parent, EasyDnsResponderRecord src)
            {
                this.ParentZone = parent;

                this.Settings = (src.Settings ?? parent.Settings)._CloneDeep();

                this.Name = src.Name._NormalizeFqdn();

                this.Type = src.Type;

                this.SrcRecord = src._CloneDeep();
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

            public Record_SOA SOARecord;

            public List<Record> WildcardAnyRecordList = new List<Record>(); // "*" という名前のワイルドカード
            public KeyValueList<string, List<Record>> WildcardEndWithRecordList = new KeyValueList<string, List<Record>>(); // "*abc" または "*.abc" という先頭ワイルドカード
            public KeyValueList<string, List<Record>> WildcardInStrRecordList = new KeyValueList<string, List<Record>>(); // "*abc*" とか "abc*def" とか "abc?def" という複雑なワイルドカード

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
                        this.RecordList.Add(record);
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

                // レコード情報を検索を高速化するためにハッシュテーブル等として並べて保持する
                foreach (var r in this.RecordList)
                {
                    if (r.Type != EasyDnsResponderRecordType.SOA)
                    {
                        this.RecordDictByName._GetOrNew(r.Name).Add(r);

                        if (r.Name._InStr("*") || r.Name._InStr("?"))
                        {
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
                    }
                }

                // 先頭ワイルドカードリストと複雑なワイルドカードリストは、文字列長で逆ソートする。
                // つまり、できるだけ文字列長が長い候補が優先してマッチするようにするのである。
                this.WildcardEndWithRecordList._DoSortBy(x => x.OrderByDescending(y => y.Key.Length).ThenByDescending(y => y.Key));
                this.WildcardInStrRecordList._DoSortBy(x => x.OrderByDescending(y => y.Key.Length).ThenByDescending(y => y.Key));

                this.SrcZone = src._CloneDeep();
            }

            public SearchResult Search(SearchRequest request, string hostLabelNormalized)
            {
                List<Record>? records = null;

                // まず完全一致するものがないか確かめる
                if (this.RecordDictByName.TryGetValue(hostLabelNormalized, out List<Record>? found))
                {
                    // 完全一致あり
                    records = found;
                }

                if (records == null)
                {
                    // もし完全一致するものが 1 つも無ければ、
                    // 先頭ワイルドカード一致を検索し、一致するものがないかどうか調べる
                    foreach (var r in this.WildcardEndWithRecordList)
                    {
                        if (hostLabelNormalized.EndsWith(r.Key))
                        {
                            // 後方一致あり
                            records = r.Value;
                            break;
                        }
                    }
                }

                if (records == null)
                {
                    // もし完全一致または後方一致するものが 1 つも無ければ、
                    // 複雑なワイルドカード一致を検索し、一致するものがないかどうか調べる
                    foreach (var r in this.WildcardInStrRecordList)
                    {
                        if (hostLabelNormalized._WildcardMatch(r.Key))
                        {
                            // 一致あり
                            records = r.Value;
                            break;
                        }
                    }
                }

                // この状態でまだ一致するものがなければ、空リストを返す
                if (records == null)
                {
                    records = new List<Record>();
                }

                SearchResult ret = new SearchResult
                {
                    RecordList = records,
                    SOARecord = this.SOARecord,
                    Zone = this,
                };

                return ret;
            }
        }

        // 検索要求
        public class SearchRequest
        {
            public string FqdnNormalized { init; get; } = null!;
        }

        // 検索結果
        public class SearchResult
        {
            public List<Record> RecordList { init; get; } = null!;
            public Zone Zone { init; get; } = null!;
            public Record_SOA SOARecord { init; get; } = null!;
        }

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

            var labels = request.FqdnNormalized.Split(".").AsSpan();
            int numLabels = labels.Length;

            Zone? zone = null;
            string hostLabelStr = "";

            for (int i = numLabels; i >= 1; i--)
            {
                var zoneLabels = labels.Slice(numLabels - i, i);

                string zoneLabelsStr = zoneLabels._Combine(".");

                // 一致する Dict エントリがあるか？
                if (this.ZoneDict.TryGetValue(zoneLabelsStr, out Zone? zoneTmp))
                {
                    // あった
                    zone = zoneTmp;

                    var hostLabels = labels.Slice(0, numLabels - i);

                    hostLabelStr = hostLabels._Combine(".");

                    break;
                }
            }

            if (zone == null)
            {
                // 一致するゾーンが 1 つもありません！
                return null;
            }

            return zone.Search(request, hostLabelStr);
        }
    }
}

#endif

