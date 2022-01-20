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

    public string Name { get; set; } = ""; // アスタリスク文字を用いたワイルドカード指定可能。ただし、 "*"、"*abc"、"*.abc" のいずれかの形式に限る。

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

public class EasyDnsResponder
{
    // 内部レコードデータ
    public class Record_A : Record
    {
        public IPAddress IPv4Address;

        public Record_A(Zone parent, EasyDnsResponderRecord src) : base(parent, src)
        {
            string tmp = src.Contents._NonNullTrim();
            if (tmp._IsEmpty()) throw new CoresLibException("IP address is empty.");
            this.IPv4Address = IPAddress.Parse(tmp);
            if (this.IPv4Address.AddressFamily != AddressFamily.InterNetwork)
                throw new CoresLibException($"AddressFamily of '{tmp}' is not IPv4.");
        }
    }

    public abstract class Record
    {
        public Zone ParentZone;
        public string Name;
        public EasyDnsResponderRecordType Type;
        public EasyDnsResponderRecordSettings Settings;

        public Record(Zone parent, EasyDnsResponderRecord src)
        {
            this.ParentZone = parent;

            this.Settings = (src.Settings ?? parent.Settings)._CloneDeep();

            this.Name = src.Name._NormalizeFqdn();

            this.Type = src.Type;
        }

        public static Record CreateFrom(Zone parent, EasyDnsResponderRecord src)
        {
            var type = src.Type;

            switch (type)
            {
                case EasyDnsResponderRecordType.A:
                    return new Record_A(parent, src);
            }

            throw new CoresLibException($"Unknown record type: {type}");
        }
    }

    // 内部ゾーンデータ
    public class Zone
    {
        public DataSet ParentDataSet;
        public string DomainFqdn;
        public EasyDnsResponderRecordSettings Settings;

        public List<Record> RecordList = new List<Record>();

        public Zone(DataSet parent, EasyDnsResponderZone src)
        {
            this.ParentDataSet = parent;

            this.Settings = (src.DefaultSettings ?? parent.Settings)._CloneDeep();

            this.DomainFqdn = src.DomainName._NormalizeFqdn();

            if (this.DomainFqdn._IsEmpty())
            {
                throw new CoresLibException("Invalid FQDN in Zone");
            }

            // レコード情報のコンパイル
            foreach (var srcRecord in src.RecordList)
            {
                var record = Record.CreateFrom(this, srcRecord);

                this.RecordList.Add(record);
            }

            // レコード情報を検索を高速化するために
        }
    }

    // 内部データセット
    public class DataSet
    {
        public Dictionary<string, Zone> ZoneDict = new Dictionary<string, Zone>();
        public EasyDnsResponderRecordSettings Settings;

        // Settings からコンパイルかる
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
    }
}

#endif

