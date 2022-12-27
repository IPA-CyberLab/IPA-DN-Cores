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

#if CORES_BASIC_SECURITY

using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Collections;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;

using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Security;

using IPA.Cores.Basic;
using IPA.Cores.Basic.DnsLib;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class EasyDnsResponderSettings
    {
        public static Copenhagen<int> Default_RefreshIntervalSecs = 60;
        public static Copenhagen<int> Default_RetryIntervalSecs = 60;
        public static Copenhagen<int> Default_ExpireIntervalSecs = 88473600;
        public static Copenhagen<int> Default_NegativeCacheTtlSecs = 10;

        public static Copenhagen<ushort> Default_MxPreference = 100;

        public static Copenhagen<int> Default_ForwarderTimeoutMsecs = 10000;
        public static Copenhagen<int> Default_ForwarderMaxSessions = 10000;
    }
}

public static class DnsUtil
{
    [MethodImpl(Inline)]
    public static DnsMessageBase ParsePacket(ReadOnlySpan<byte> data)
    {
        return DnsMessageBase.CreateByFlag(data, null, null);
    }

    [MethodImpl(Inline)]
    public static Span<byte> BuildPacket(this DnsMessageBase message)
    {
        return message.Encode(false);
    }

    public static readonly DateTimeOffset DnsDtStartDay = (new DateTime(2021, 1, 1)).AddHours(-9)._AsDateTimeOffset(false, false);

    public static uint GenerateSoaSerialNumberFromDateTime(DateTimeOffset dt)
    {
        var diff = (dt - DnsDtStartDay);
        int days = (int)diff.TotalDays;

        days = Math.Max(days, 1);
        days = (days % 32000) + 10000;

        string hhmmss = diff.Hours.ToString("D2") + diff.Minutes.ToString("D2") + diff.Seconds.ToString("D2");

        string str = days.ToString("D5") + hhmmss.Substring(0, 5);

        return str._ToUInt();
    }

    [MethodImpl(Inline)]
    public static bool IsEmptyDomain(this DomainName dn)
    {
        if (dn.LabelCount <= 0) return true;

        return false;
    }

    [MethodImpl(Inline)]
    public static EasyDnsResponderRecordType DnsLibRecordTypeToEasyDnsResponderRecordType(RecordType src)
    {
        switch (src)
        {
            case RecordType.Any: return EasyDnsResponderRecordType.Any;
            case RecordType.A: return EasyDnsResponderRecordType.A;
            case RecordType.Aaaa: return EasyDnsResponderRecordType.AAAA;
            case RecordType.Ns: return EasyDnsResponderRecordType.NS;
            case RecordType.CName: return EasyDnsResponderRecordType.CNAME;
            case RecordType.Soa: return EasyDnsResponderRecordType.SOA;
            case RecordType.Ptr: return EasyDnsResponderRecordType.PTR;
            case RecordType.Mx: return EasyDnsResponderRecordType.MX;
            case RecordType.Txt: return EasyDnsResponderRecordType.TXT;
            case RecordType.CAA: return EasyDnsResponderRecordType.CAA;
            case RecordType.Srv: return EasyDnsResponderRecordType.SRV;
        }

        return EasyDnsResponderRecordType.None;
    }

    // DNS ゾーン一覧から最長一致するゾーンを検索
    public static T? SearchLongestMatchDnsZone<T>(StrDictionary<T> zoneList, string normalizedFqdn, out string hostLabelStr, out ReadOnlyMemory<string> hostLabels)
    {
        // a.b.c.d のような FQDN を検索要求された場合、
        // 1. a.b.c.d
        // 2. b.c.d
        // 3. c.d
        // 4. d
        // の順で一致するゾーンがないかどうか検索する。
        // つまり、複数の一致する可能性があるゾーンがある場合、一致する文字長が最も長いゾーンを選択するのである。
        ReadOnlyMemory<string> labels = normalizedFqdn.Split(".").AsMemory();
        int numLabels = labels.Length;

        T? zone = default;

        hostLabelStr = "";

        hostLabels = default;

        for (int i = numLabels; i >= 1; i--)
        {
            var zoneLabels = labels.Slice(numLabels - i, i);

            string zoneLabelsStr = zoneLabels._Combine(".", estimatedLength: normalizedFqdn.Length);

            // 一致する Dict エントリがあるか？
            if (zoneList.TryGetValue(zoneLabelsStr, out T? zoneTmp))
            {
                // あった
                zone = zoneTmp;

                hostLabels = labels.Slice(0, numLabels - i);

                hostLabelStr = hostLabels._Combine(".", estimatedLength: normalizedFqdn.Length);

                break;
            }
        }

        return zone;
    }
}

[Flags]
public enum DnsUdpPacketFlags
{
    None = 0,
    ViaDnsProxy = 1,
    HasEDnsClientSubnetInfo = 2,
}

public class DnsUdpPacket
{
    [JsonConverter(typeof(StringEnumConverter))]
    public DnsUdpPacketFlags Flags { get; }

    public IPEndPoint RemoteEndPoint { get; } // UDP パケット送付元の DNS クライアントの情報 (dnsdist 等の DNS プロキシ経由の場合はプロキシの情報)
    public IPEndPoint LocalEndPoint { get; }
    public IPEndPoint OriginalRemoteEndPoint { get; } // dnsdist 等の DNS プロキシを経由してきた場合の元の DNS クライアントの情報

    public DnsMessageBase Message { get; }

    public string? DnsResolver { get; }
    public string? EdnsClientSubnet { get; }

    public DnsUdpPacket(IPEndPoint remoteEndPoint, IPEndPoint localEndPoint, DnsMessageBase message, IPEndPoint? originalRemoteEndPoint = null, DnsUdpPacketFlags flags = DnsUdpPacketFlags.None)
    {
        originalRemoteEndPoint ??= remoteEndPoint;

        if (remoteEndPoint.AddressFamily != localEndPoint.AddressFamily)
        {
            throw new CoresException("remoteEndPoint.AddressFamily != localEndPoint.AddressFamily");
        }

        this.RemoteEndPoint = remoteEndPoint;
        this.LocalEndPoint = localEndPoint;
        this.OriginalRemoteEndPoint = originalRemoteEndPoint;
        this.Message = message;

        if (this.OriginalRemoteEndPoint.Equals(this.RemoteEndPoint) == false)
        {
            flags |= DnsUdpPacketFlags.ViaDnsProxy;
        }

        if (this.Message.IsQuery)
        {
            this.DnsResolver = originalRemoteEndPoint.ToString();

            if (this.Message.IsEDnsEnabled && this.Message.EDnsOptions != null)
            {
                var edns = this.Message.EDnsOptions;

                foreach (var item in edns.Options)
                {
                    if (item.Type == EDnsOptionType.ClientSubnet)
                    {
                        ClientSubnetOption? clientSubnet = item as ClientSubnetOption;
                        if (clientSubnet != null)
                        {
                            flags |= DnsUdpPacketFlags.HasEDnsClientSubnetInfo;

                            this.EdnsClientSubnet = $"{clientSubnet.Address.ToString()}/{clientSubnet.SourceNetmask}";

                            break;
                        }
                    }
                }
            }
        }

        this.Flags = flags;
    }
}




public class EasyDnsServerDynOptions : INormalizable
{
    public int UdpRecvLoopPollingIntervalMsecs { get; set; } = 0;
    public int UdpDelayedProcessTaskQueueLength { get; set; } = 0;
    public int UdpDelayedResponsePacketQueueLength { get; set; } = 0;
    public bool ParseUdpProxyProtocolV2 { get; set; } = false;
    public string DnsProxyProtocolAcceptSrcIpAcl { get; set; } = "";

    public EasyDnsServerDynOptions()
    {
        this.Normalize();
    }

    public void Normalize()
    {
        if (UdpRecvLoopPollingIntervalMsecs <= 0)
            UdpRecvLoopPollingIntervalMsecs = 10;

        if (UdpDelayedProcessTaskQueueLength <= 0)
            UdpDelayedProcessTaskQueueLength = 1024;

        if (UdpDelayedResponsePacketQueueLength <= 0)
            UdpDelayedResponsePacketQueueLength = 4096;

        if (this.DnsProxyProtocolAcceptSrcIpAcl._IsEmpty())
        {
            this.DnsProxyProtocolAcceptSrcIpAcl = Consts.Strings.EasyAclAllowAllRule;
        }
    }
}

public class EasyDnsServerSetting
{
    public int UdpPort { get; }
    public Func<EasyDnsServer, List<DnsUdpPacket>, List<DnsUdpPacket>> Callback { get; }


    public EasyDnsServerSetting(Func<EasyDnsServer, List<DnsUdpPacket>, List<DnsUdpPacket>> callback, int udpPort = Consts.Ports.Dns)
    {
        this.Callback = callback;
        this.UdpPort = udpPort;
    }
}

public class EasyDnsServer : AsyncServiceWithMainLoop
{
    public EasyDnsServerSetting Setting { get; }

    public EasyDnsServerDynOptions GetCurrentDynOptions() => this._DynOptions._CloneDeepWithNormalize();
    public void SetCurrentDynOptions(EasyDnsServerDynOptions value) => this._DynOptions = value._CloneDeepWithNormalize();

    EasyDnsServerDynOptions _DynOptions;

    public EasyDnsServer(EasyDnsServerSetting setting, EasyDnsServerDynOptions? dynOptions = null)
    {
        dynOptions ??= new EasyDnsServerDynOptions();

        try
        {
            this.Setting = setting;
            this._DynOptions = dynOptions._CloneDeep();
            this._DynOptions.Normalize();

            this.StartMainLoop(this.MainLoopAsync);
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    readonly ConcurrentHashSet<Task> CurrentProcessingTasks = new ConcurrentHashSet<Task>();
    readonly List<Datagram> DelayedReplyUdpPacketsList = new List<Datagram>();
    readonly AsyncAutoResetEvent UdpRecvCancelEvent = new AsyncAutoResetEvent();

    async Task MainLoopAsync(CancellationToken cancel)
    {
        await using var udpListener = LocalNet.CreateUdpListener(new NetUdpListenerOptions(TcpDirectionType.Server));

        udpListener.AddEndPoint(new IPEndPoint(IPAddress.Any, this.Setting.UdpPort));
        udpListener.AddEndPoint(new IPEndPoint(IPAddress.IPv6Any, this.Setting.UdpPort));

        await using var udpSock = udpListener.GetSocket(true);

        while (cancel.IsCancellationRequested == false)
        {
            try
            {
                // UDP パケットを受信するまで待機して受信を実行 (タイムアウトが発生した場合も抜ける)
                var allRecvList = await udpSock.ReceiveDatagramsListAsync(cancel: cancel, timeout: this._DynOptions.UdpRecvLoopPollingIntervalMsecs, noTimeoutException: true, cancelEvent: UdpRecvCancelEvent);

                var proxyAcl = new EasyIpAcl(this._DynOptions.DnsProxyProtocolAcceptSrcIpAcl, EasyIpAclAction.Deny, EasyIpAclAction.Deny, false);

                // 受信した UDP パケットを複数の CPU で処理
                var allSendList = await allRecvList._ProcessParallelAndAggregateAsync((perTaskRecvList, taskIndex) =>
                {
                    // 以下の処理は各 CPU で分散
                    List<DnsUdpPacket> perTaskRequestPacketsList = new List<DnsUdpPacket>(perTaskRecvList.Count);

                    // 受信した UDP パケットをすべてパースし DNS パケットをパースする
                    foreach (var item in perTaskRecvList)
                    {
                        if (item != null)
                        {
                            try
                            {
                                DnsUdpPacketFlags flags = DnsUdpPacketFlags.None;

                                IPEndPoint? originalRemoteEndPoint = null;

                                var dnsPacketData = item.Data.Span;

                                if (this._DynOptions.ParseUdpProxyProtocolV2)
                                {
                                    // Proxy Protocol のパースを試行
                                    if (ProxyProtocolV2Parsed.TryParse(ref dnsPacketData, out var proxyProtocolParsed))
                                    {
                                        // パースに成功した場合 ACL を確認
                                        if (proxyAcl.Evaluate(item.RemoteIPEndPoint!.Address) == EasyIpAclAction.Permit)
                                        {
                                            // ACL が一致した場合のみ Proxy Protocol の結果を採用。それ以外の場合は無視
                                            originalRemoteEndPoint = proxyProtocolParsed.SrcEndPoint;
                                            if (originalRemoteEndPoint != null)
                                            {
                                                flags |= DnsUdpPacketFlags.ViaDnsProxy;
                                            }
                                        }
                                    }
                                }

                                var request = DnsUtil.ParsePacket(dnsPacketData);

                                DnsUdpPacket pkt = new DnsUdpPacket(item.RemoteIPEndPoint!, item.LocalIPEndPoint!, request, originalRemoteEndPoint, flags);

                                perTaskRequestPacketsList.Add(pkt);
                            }
                            catch { }
                        }
                    }

                    // 指定されたコールバック関数を呼び出して DNS クエリを解決し、回答すべき DNS レスポンス一覧を生成する
                    List<DnsUdpPacket> perTaskReaponsePacketsList = Setting.Callback(this, perTaskRequestPacketsList);

                    List<Datagram> perTaskSendList = new List<Datagram>(perTaskReaponsePacketsList.Count);

                    // 回答すべき DNS レスポンス一覧をバイナリとしてビルドし UDP パケットを生成
                    foreach (var responsePkt in perTaskReaponsePacketsList)
                    {
                        try
                        {
                            Memory<byte> packetData = DnsUtil.BuildPacket(responsePkt.Message).ToArray();

                            var datagram = new Datagram(packetData, responsePkt.RemoteEndPoint, responsePkt.LocalEndPoint);

                            perTaskSendList.Add(datagram);
                        }
                        catch { }
                    }

                    return TR(perTaskSendList);
                },
                operation: MultitaskDivideOperation.RoundRobin,
                cancel: cancel);

                // UDP パケットを応答 (通常分)
                await udpSock.SendDatagramsListAsync(allSendList.ToArray(), cancel: cancel);

                // UDP パケットを応答 (Delay 回答分)
                Datagram[] delayedUdpPacketsArray;
                lock (this.DelayedReplyUdpPacketsList)
                {
                    delayedUdpPacketsArray = DelayedReplyUdpPacketsList.ToArray();
                    DelayedReplyUdpPacketsList.Clear();
                }

                await udpSock.SendDatagramsListAsync(delayedUdpPacketsArray, cancel: cancel);
            }
            catch (Exception ex)
            {
                ex._Debug();
            }
        }
    }

    public bool BeginDelayDnsPacketProcessing(DnsUdpPacket requestDnsPacket, Func<DnsUdpPacket, Task<DnsUdpPacket?>> proc)
    {
        Dbg.RunDebugProcIntervalOnce(() =>
        {
            $"{this.CurrentProcessingTasks.Count}  {this.DelayedReplyUdpPacketsList.Count}"._Print();
        });

        if (this.CurrentProcessingTasks.Count >= this._DynOptions.UdpDelayedProcessTaskQueueLength)
        {
            // これ以上の個数の遅延タスクを開始できない
            return false;
        }

        if (this.DelayedReplyUdpPacketsList.Count >= this._DynOptions.UdpDelayedResponsePacketQueueLength)
        {
            // これ以上の応答パケットのキューメモリがない
            return false;
        }

        try
        {
            Task task = TaskUtil.StartAsyncTaskSlowAsync(async currentTask =>
            {
                try
                {
                    DnsUdpPacket? responsePkt = await proc(requestDnsPacket);
                    if (responsePkt != null)
                    {
                        Memory<byte> packetData = DnsUtil.BuildPacket(responsePkt.Message).ToArray();

                        var datagram = new Datagram(packetData, responsePkt.RemoteEndPoint, responsePkt.LocalEndPoint);

                        if (this.DelayedReplyUdpPacketsList.Count < this._DynOptions.UdpDelayedResponsePacketQueueLength)
                        {
                            lock (DelayedReplyUdpPacketsList)
                            {
                                this.DelayedReplyUdpPacketsList.Add(datagram);
                            }

                            UdpRecvCancelEvent.Set(softly: true);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }
                finally
                {
                    this.CurrentProcessingTasks.Remove(currentTask);
                }
            },
            task =>
            {
                this.CurrentProcessingTasks.Add(task);
            });

            return true;
        }
        catch (Exception ex)
        {
            // 普通ここには来ないはず
            ex._Debug();

            return false;
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
    CAA,
    SRV,
}

[Flags]
public enum EasyDnsResponderRecordAttribute
{
    None = 0,
    DynamicRecord = 1,
}

public class EasyDnsResponderRecord
{
    [JsonConverter(typeof(StringEnumConverter))]
    public EasyDnsResponderRecordAttribute Attribute { get; set; } = EasyDnsResponderRecordAttribute.None;

    public string Name { get; set; } = ""; // アスタリスク文字を用いたワイルドカード指定可能。

    [JsonConverter(typeof(StringEnumConverter))]
    public EasyDnsResponderRecordType Type { get; set; } = EasyDnsResponderRecordType.None;

    public string Contents { get; set; } = ""; // DynamicRecord の場合はコールバック ID (任意の文字列) を指定

    public EasyDnsResponderRecordSettings? Settings { get; set; } = null;

    [JsonIgnore]
    public object? Param = null;

    public static EasyDnsResponderRecordType StrToRecordType(string typeStr)
    {
        var type = EasyDnsResponderRecordType.None.ParseAsDefault(typeStr);

        return type;
    }


    static readonly EasyDnsResponder.Zone dummyZoneForTryParse = new EasyDnsResponder.Zone(new EasyDnsResponder.DataSet(new EasyDnsResponderSettings()), new EasyDnsResponderZone { DomainName = "_dummy_domain.example.org" });

    public static EasyDnsResponderRecord TryParseFromString(string str, string? parentDomainFqdn = null, EasyDnsResponderRecordSettings? settings = null)
    {
        var r = FromString(str, parentDomainFqdn, settings: settings);

        EasyDnsResponder.Record.CreateFrom(dummyZoneForTryParse, r);

        return r;
    }

    public static EasyDnsResponderRecord FromString(string str, string? parentDomainFqdn = null, object? param = null, EasyDnsResponderRecordSettings? settings = null)
    {
        if (str._GetKeysListAndValue(2, out var keys, out string value) == false)
        {
            throw new CoresLibException($"DNS Record String: Invalid Format. Str = '{str}'");
        }

        string typeStr = keys[0];
        var type = StrToRecordType(typeStr);
        if (type == EasyDnsResponderRecordType.None || type == EasyDnsResponderRecordType.SOA)
        {
            throw new CoresLibException($"DNS Record String: Invalid Record Type. Str = '{str}'");
        }

        string name = keys[1];

        if (name == "@")
        {
            name = "";
        }

        if (parentDomainFqdn._IsFilled())
        {
            value = value._ReplaceStr("@", parentDomainFqdn);
        }
        else
        {
            if (value._InStr("@"))
            {
                throw new CoresLibException($"FQDN has an invalid character '@'");
            }
        }

        return new EasyDnsResponderRecord
        {
            Attribute = EasyDnsResponderRecordAttribute.None,
            Contents = value,
            Type = type,
            Name = name,
            Settings = settings,
            Param = param,
        };
    }

    public override string ToString()
    {
        return $"{Attribute} {Type} {Name._FilledOrDefault("@")} {Contents}";
    }
}

public class EasyDnsResponderForwarder
{
    public string Selector { get; set; } = "";        // ドメイン名、ワイルドカード付き FQDN、またはサブネット表記

    public string TargetServers { get; set; } = "";     // ターゲットの DNS サーバー一覧

    public string CallbackId { get; set; } = "";

    public int TimeoutMsecs { get; set; } = CoresConfig.EasyDnsResponderSettings.Default_ForwarderTimeoutMsecs;
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

    public List<EasyDnsResponderForwarder> ForwarderList { get; set; } = new List<EasyDnsResponderForwarder>();

    public bool SaveAccessLogForDebug { get; set; } = false;

    public bool CopyQueryAdditionalRecordsToResponse { get; set; } = false;

    public int ForwarderMaxSessions { get; set; } = CoresConfig.EasyDnsResponderSettings.Default_ForwarderMaxSessions;
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
    public DnsUdpPacket? RequestPacket { init; get; } = null;
}

// ダイナミックレコードのコールバック関数で返却すべきデータ
public class EasyDnsResponderDynamicRecordCallbackResult
{
    public List<IPAddress>? IPAddressList { get; set; } // A, AAAA の場合
    public List<DomainName>? MxFqdnList { get; set; } // MX の場合
    public List<ushort>? MxPreferenceList { get; set; } // MX の場合の Preference 値のリスト
    public List<DomainName>? CNameFqdnList { get; set; } // CNAME の場合
    public List<DomainName>? PtrFqdnList { get; set; } // PTR の場合
    public List<DomainName>? NsFqdnList { get; set; } // NS の場合
    public List<string>? TextList { get; set; } // TXT の場合
    public List<Tuple<byte, string, string>>? CaaList { get; set; } // CAA の場合
    public List<ushort>? SrvPriorityList { get; set; } // SRV の場合
    public List<ushort>? SrvWeightList { get; set; } // SRV の場合
    public List<ushort>? SrvPortList { get; set; } // SRV の場合
    public List<DomainName>? SrvTargetList { get; set; } // SRV の場合

    public EasyDnsResponderRecordSettings? Settings { get; set; } // TTL 等

    public bool NotFound { get; set; } // 発見されず
}

// フォワーダのコールバック関数に渡されるリクエストデータ
public class EasyDnsResponderForwarderCallbackRequest
{
    public string RequestFqdn { init; get; } = null!;
    public string CallbackId { init; get; } = null!;
    public DnsUdpPacket? OriginalRequestPacket { init; get; } = null;
    public EasyDnsResponderForwarder ForwarderDef { init; get; } = null!;
    public EasyDnsResponder.Forwarder ForwarderInternal { init; get; } = null!;
}

// フォワーダのコールバック関数から返却されるべきデータ
public class EasyDnsResponderForwarderCallbackResult
{
    public bool SkipForwarder { get; set; } = false;
    public ReturnCode ErrorCode { get; set; } = ReturnCode.NoError;
    public DnsMessageBase? ModifiedDnsMessage { get; set; } = null;
}

public class EasyDnsResponder
{
    FwdTranslateEntry[] FwdTranslateTable = new FwdTranslateEntry[65536];
    ushort[] FwdTranslateIdSeed = new ushort[65536];
    int FwdTranslateIdSeedPos = 0;

    public EasyDnsResponder()
    {
    }

    void FwdRandomizeTranslateIdSeedCritical()
    {
        List<ushort> tmp = new List<ushort>();
        for (int i = 0; i < 65536; i++)
        {
            ushort us = (ushort)i;
            tmp.Add(us);
        }
        tmp = tmp._Shuffle().ToList();

        tmp.CopyTo(FwdTranslateIdSeed);
    }

    int FwdGetNextTranslateIdCritical()
    {
        for (int i = 0; i < 65536 * 2; i++)
        {
            ushort candidate = FwdGetNextTranslateIdCandidateCritical();

            if (FwdTranslateTable[candidate] == null)
            {
                return candidate;
            }
        }
        return -1;
    }

    ushort FwdGetNextTranslateIdCandidateCritical()
    {
        ushort ret = 0;
        ret = FwdTranslateIdSeed[FwdTranslateIdSeedPos];
        FwdTranslateIdSeedPos++;
        if (FwdTranslateIdSeedPos >= 65536)
        {
            FwdRandomizeTranslateIdSeedCritical();
            FwdTranslateIdSeedPos = 0;
        }
        return ret;
    }

    // ダイナミックレコードのコールバック関数
    public Func<EasyDnsResponderDynamicRecordCallbackRequest, EasyDnsResponderDynamicRecordCallbackResult?>? DynamicRecordCallback { get; set; }

    // フォワーダのコールバック関数
    public Func<EasyDnsResponderForwarderCallbackRequest, EasyDnsResponderForwarderCallbackResult>? ForwarderCallback { get; set; }

    // 内部データセット
    public class Record_A : Record
    {
        public IPAddress IPv4Address;
        public IPAddress IPv4SubnetMask;
        public int IPv4SubnetMaskLength;
        public bool IsSubnet;

        public Record_A(Zone parent, EasyDnsResponderRecord src) : base(parent, src)
        {
            string[] tokens = src.Contents._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ';', ',', ' ', '\t');
            string? tmp = tokens.ElementAtOrDefault(0);
            if (tmp._IsEmpty()) throw new CoresLibException("Contents is empty.");

            if (tmp._InStr("/") == false)
            {
                // 単体の IPv4 アドレス
                this.IPv4Address = IPAddress.Parse(tmp);
                this.IPv4SubnetMask = IPAddress.Broadcast;
                this.IPv4SubnetMaskLength = 32;
                this.IsSubnet = false;
            }
            else
            {
                // サブネットマスク付き IPv4 アドレス
                IPUtil.ParseIPAndSubnetMask(tmp, out this.IPv4Address, out this.IPv4SubnetMask);
                this.IPv4SubnetMaskLength = IPUtil.SubnetMaskToInt(this.IPv4SubnetMask);
                this.IsSubnet = !IPUtil.IsSubnetLenHostAddress(this.IPv4Address.AddressFamily, this.IPv4SubnetMaskLength);
            }

            if (this.IPv4Address.AddressFamily != AddressFamily.InterNetwork)
                throw new CoresLibException($"AddressFamily of '{tmp}' is not IPv4.");
        }

        public Record_A(Zone parent, EasyDnsResponderRecordSettings settings, string nameNormalized, IPAddress ipv4address) : base(parent, EasyDnsResponderRecordType.A, settings, nameNormalized)
        {
            this.IPv4Address = ipv4address;
            this.IPv4SubnetMask = IPAddress.Broadcast;
            this.IPv4SubnetMaskLength = 32;
            this.IsSubnet = false;

            if (this.IPv4Address.AddressFamily != AddressFamily.InterNetwork)
                throw new CoresLibException($"AddressFamily of '{this.IPv4Address}' is not IPv4.");
        }

        public Record_A Clone()
        {
            return (Record_A)this.MemberwiseClone();
        }

        protected override string ToStringForCompareImpl()
        {
            return this.IPv4Address.ToString();
        }
    }

    public class Record_AAAA : Record
    {
        public IPAddress IPv6Address;
        public IPAddress IPv6SubnetMask;
        public int IPv6SubnetMaskLength;
        public bool IsSubnet;

        public Record_AAAA(Zone parent, EasyDnsResponderRecord src) : base(parent, src)
        {
            string[] tokens = src.Contents._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ';', ',', ' ', '\t');
            string? tmp = tokens.ElementAtOrDefault(0);
            if (tmp._IsEmpty()) throw new CoresLibException("Contents is empty.");

            if (tmp._InStr("/") == false)
            {
                // 単体の IPv6 アドレス
                this.IPv6Address = IPAddress.Parse(tmp);
                this.IPv6SubnetMask = IPUtil.IPv6AllFilledAddressCache;
                this.IPv6SubnetMaskLength = 128;
                this.IsSubnet = false;
            }
            else
            {
                // サブネットマスク付き IPv6 アドレス
                IPUtil.ParseIPAndSubnetMask(tmp, out this.IPv6Address, out this.IPv6SubnetMask);
                this.IPv6SubnetMaskLength = IPUtil.SubnetMaskToInt(this.IPv6SubnetMask);
                this.IsSubnet = !IPUtil.IsSubnetLenHostAddress(this.IPv6Address.AddressFamily, this.IPv6SubnetMaskLength);
            }

            if (this.IPv6Address.AddressFamily != AddressFamily.InterNetworkV6)
                throw new CoresLibException($"AddressFamily of '{tmp}' is not IPv6.");

            this.IPv6Address.ScopeId = 0;
        }

        public Record_AAAA(Zone parent, EasyDnsResponderRecordSettings settings, string nameNormalized, IPAddress ipv6address) : base(parent, EasyDnsResponderRecordType.AAAA, settings, nameNormalized)
        {
            this.IPv6Address = ipv6address;
            this.IPv6SubnetMask = IPUtil.IPv6AllFilledAddressCache;
            this.IPv6SubnetMaskLength = 128;
            this.IsSubnet = false;

            if (this.IPv6Address.AddressFamily != AddressFamily.InterNetworkV6)
                throw new CoresLibException($"AddressFamily of '{this.IPv6Address}' is not IPv6.");
        }

        public Record_AAAA Clone()
        {
            return (Record_AAAA)this.MemberwiseClone();
        }

        protected override string ToStringForCompareImpl()
        {
            return this.IPv6Address.ToString();
        }
    }

    public class Record_NS : Record
    {
        public DomainName ServerName;
        public List<Record> GlueRecordList = new List<Record>();

        public Record_NS(Zone parent, EasyDnsResponderRecord src) : base(parent, src)
        {
            if (this.Name._InStr("*") || this.Name._InStr("?"))
            {
                throw new CoresLibException($"NS record doesn't allow wildcard names. Specified name: '{this.Name}'");
            }

            string[] tokens = src.Contents._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ';', ',', ' ', '\t', ';', ',', '=');
            string? nameServerName = tokens.ElementAtOrDefault(0);
            if (nameServerName._IsEmpty()) throw new CoresLibException("Contents is empty.");

            // ns1.example.org=1.2.3.4,5.6.7.8,2001::cafe のような書式で Glue レコードも記載されている場合がある。
            // この場合は、当該記載をパースする。

            List<IPAddress> glueIpList = new List<IPAddress>();
            if (tokens.Length >= 2)
            {
                for (int i = 1; i < tokens.Length; i++)
                {
                    string s = tokens[i];

                    if (s._IsFilled())
                    {
                        if (IPAddress.TryParse(s, out IPAddress? ip))
                        {
                            if (ip._IsIPv4OrIPv6AddressFaimly())
                            {
                                ip = ip._RemoveScopeId();
                                if (glueIpList.Contains(ip, IpComparer.Comparer) == false)
                                {
                                    glueIpList.Add(ip);
                                }
                            }
                        }
                    }
                }
            }

            this.ServerName = DomainName.Parse(nameServerName);

            var rootVirtualZone = new Zone(isVirtualRootZone: EnsureSpecial.Yes, this.ParentZone);

            foreach (var glueIp in glueIpList)
            {
                if (glueIp.AddressFamily == AddressFamily.InterNetwork)
                {
                    this.GlueRecordList.Add(new Record_A(rootVirtualZone, src.Settings ?? rootVirtualZone.Settings, this.ServerName.ToNormalizedFqdnFast(), glueIp));
                }
                else
                {
                    this.GlueRecordList.Add(new Record_AAAA(rootVirtualZone, src.Settings ?? rootVirtualZone.Settings, this.ServerName.ToNormalizedFqdnFast(), glueIp));
                }
            }

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
            string[] tokens = src.Contents._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ';', ',', ' ', '\t');
            string? tmp = tokens.ElementAtOrDefault(0);
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
            if (this.RefreshIntervalSecs <= 0) this.RefreshIntervalSecs = CoresConfig.EasyDnsResponderSettings.Default_RefreshIntervalSecs;

            this.RetryIntervalSecs = tokens.ElementAtOrDefault(4)._ToInt();
            if (this.RetryIntervalSecs <= 0) this.RetryIntervalSecs = CoresConfig.EasyDnsResponderSettings.Default_RetryIntervalSecs;

            this.ExpireIntervalSecs = tokens.ElementAtOrDefault(5)._ToInt();
            if (this.ExpireIntervalSecs <= 0) this.ExpireIntervalSecs = CoresConfig.EasyDnsResponderSettings.Default_ExpireIntervalSecs;

            this.NegativeCacheTtlSecs = tokens.ElementAtOrDefault(6)._ToInt();
            if (this.NegativeCacheTtlSecs <= 0) this.NegativeCacheTtlSecs = CoresConfig.EasyDnsResponderSettings.Default_NegativeCacheTtlSecs;
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
            string[] tokens = src.Contents._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ';', ',', ' ', '\t');
            string? tmp = tokens.ElementAtOrDefault(0);
            if (tmp._IsEmpty()) throw new CoresLibException("Contents is empty.");

            this.Ptr = DomainName.Parse(tmp);

            if (this.Ptr.IsEmptyDomain()) throw new CoresLibException("PTR field is empty.");
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

    public class Record_SRV : Record
    {
        public ushort Priority;
        public ushort Weight;
        public ushort Port;
        public DomainName Target;

        public Record_SRV(Zone parent, EasyDnsResponderRecord src) : base(parent, src)
        {
            string[] tokens = src.Contents._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ';', ',', '\t', ' ');

            if (tokens.Length == 0) throw new CoresLibException("Contents is empty.");
            if (tokens.Length < 4) throw new CoresLibException("SRV record contents must have priority, weight, port and target.");

            this.Priority = (ushort)tokens[0]._ToUInt();
            this.Weight = (ushort)tokens[1]._ToUInt();
            this.Port = (ushort)tokens[2]._ToUInt();
            this.Target = DomainName.Parse(tokens[3]._NonNullTrim());
        }

        public Record_SRV(Zone parent, EasyDnsResponderRecordSettings settings, string nameNormalized, ushort priority, ushort weight, ushort port, DomainName target) : base(parent, EasyDnsResponderRecordType.SRV, settings, nameNormalized)
        {
            this.Priority = priority;
            this.Weight = weight;
            this.Port = port;
            this.Target = target;
        }

        protected override string ToStringForCompareImpl()
        {
            return $"{Priority} {Weight} {Port} {Target.ToString()}";
        }
    }

    public class Record_MX : Record
    {
        public DomainName MailServer;
        public ushort Preference;

        public Record_MX(Zone parent, EasyDnsResponderRecord src) : base(parent, src)
        {
            string[] tokens = src.Contents._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ';', ',', '\t', ' ');

            if (tokens.Length == 0) throw new CoresLibException("Contents is empty.");

            this.MailServer = DomainName.Parse(tokens.ElementAt(0));

            this.Preference = (ushort)tokens.ElementAtOrDefault(1)._ToUInt();
            if (this.Preference <= 0) this.Preference = CoresConfig.EasyDnsResponderSettings.Default_MxPreference;
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

    public class Record_CAA : Record
    {
        public byte Flags;
        public string Tag;
        public string Value;

        public Record_CAA(Zone parent, EasyDnsResponderRecord src) : base(parent, src)
        {
            string[] tokens = src.Contents._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, '\t', ' ');

            if (tokens.Length < 3) throw new CoresLibException("Contents must have <flag> <tag> <value>.");

            this.Flags = (byte)tokens.ElementAt(0)._ToInt();
            this.Tag = tokens.ElementAt(1);
            this.Value = tokens.ElementAt(2);
        }

        public Record_CAA(Zone parent, EasyDnsResponderRecordSettings settings, string nameNormalized, byte flags, string tag, string value) : base(parent, EasyDnsResponderRecordType.CAA, settings, nameNormalized)
        {
            this.Flags = flags;
            this.Tag = tag;
            this.Value = value;
        }

        protected override string ToStringForCompareImpl()
        {
            return $"{this.Flags} {this.Tag} {this.Value}";
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
                case EasyDnsResponderRecordType.Any:
                case EasyDnsResponderRecordType.A:
                case EasyDnsResponderRecordType.AAAA:
                case EasyDnsResponderRecordType.CNAME:
                case EasyDnsResponderRecordType.MX:
                case EasyDnsResponderRecordType.NS:
                case EasyDnsResponderRecordType.PTR:
                case EasyDnsResponderRecordType.TXT:
                case EasyDnsResponderRecordType.CAA:
                case EasyDnsResponderRecordType.SRV:
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

        [JsonIgnore]
        public object? Param;

        protected abstract string ToStringForCompareImpl();

        readonly CachedProperty<string>? _StringForCompareCache;
        public string ToStringForCompare() => _StringForCompareCache ?? "";

        public Record(Zone parent, EasyDnsResponderRecordType type, EasyDnsResponderRecordSettings settings, string nameNormalized)
        {
            this.ParentZone = parent;
            this.Type = type;
            this.Settings = settings;
            this.Name = nameNormalized;

            this._StringForCompareCache = new CachedProperty<string>(getter: () =>
            {
                return $"{Name} {Type} {this.ToStringForCompareImpl()}";
            });
        }

        public Record(Zone parent, EasyDnsResponderRecord src)
        {
            this.ParentZone = parent;

            this.Param = src.Param;

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

                case EasyDnsResponderRecordType.CAA:
                    return new Record_CAA(parent, src);

                case EasyDnsResponderRecordType.SRV:
                    return new Record_SRV(parent, src);
            }

            throw new CoresLibException($"Unknown record type: {src.Type}");
        }

        public DnsRecordBase? ToDnsLibRecordBase(DomainName domainName, DateTimeOffset timeStampForSoa)
        {
            int ttl = this.Settings.TtlSecs;

            switch (this)
            {
                case Record_A a:
                    return new ARecord(domainName, ttl, a.IPv4Address);

                case Record_AAAA aaaa:
                    return new AaaaRecord(domainName, ttl, aaaa.IPv6Address);

                case Record_NS ns:
                    return new NsRecord(domainName, ttl, ns.ServerName);

                case Record_CNAME cname:
                    return new CNameRecord(domainName, ttl, cname.CName);

                case Record_SOA soa:
                    uint serialNumber = soa.SerialNumber;
                    if (serialNumber == Consts.Numbers.MagicNumber_u32)
                    {
                        serialNumber = DnsUtil.GenerateSoaSerialNumberFromDateTime(timeStampForSoa);
                    }
                    return new SoaRecord(domainName, ttl, soa.MasterName, soa.ResponsibleName, serialNumber, soa.RefreshIntervalSecs, soa.RetryIntervalSecs, soa.ExpireIntervalSecs, soa.NegativeCacheTtlSecs);

                case Record_PTR ptr:
                    return new PtrRecord(domainName, ttl, ptr.Ptr);

                case Record_MX mx:
                    return new MxRecord(domainName, ttl, mx.Preference, mx.MailServer);

                case Record_TXT txt:
                    return new TxtRecord(domainName, ttl, txt.TextData);

                case Record_CAA caa:
                    return new CAARecord(domainName, ttl, caa.Flags, caa.Tag, caa.Value);

                case Record_SRV srv:
                    return new SrvRecord(domainName, ttl, srv.Priority, srv.Weight, srv.Port, srv.Target);
            }

            return null;
        }

        public DnsRecordBase? ToDnsLibRecordBase(DnsQuestion q, DateTimeOffset timeStampForSoa)
            => ToDnsLibRecordBase(q.Name, timeStampForSoa);
    }

    // フォワーダの種類
    [Flags]
    public enum ForwarderType
    {
        DomainName = 0,
        WildcardFqdn,
        Subnet,
    }

    // 内部フォワーダデータ
    public class Forwarder
    {
        public DataSet ParentDataSet;
        public ForwarderType Type;
        public string DomainFqdn = "";
        public string WildcardFqdn = "";
        public IPAddress Subnet_IpNetwork = IPAddress.Any;
        public IPAddress Subnet_SubnetMask = IPAddress.Broadcast;
        public int Subnet_SubnetLength = 32;
        public int TimeoutMsecs;
        public string CallbackId = "";
        public List<IPEndPoint> TargetServersList = new List<IPEndPoint>();
        public EasyDnsResponderForwarder Src;

        public Forwarder(DataSet parent, EasyDnsResponderForwarder src)
        {
            this.ParentDataSet = parent;

            string selector = src.Selector;

            if (selector._InStri("/"))
            {
                // IP アドレス / サブネットマスク形式
                this.Type = ForwarderType.Subnet;
                IPUtil.ParseIPAndSubnetMask(selector, out Subnet_IpNetwork, out Subnet_SubnetMask);
                this.Subnet_SubnetLength = IPUtil.SubnetMaskToInt(Subnet_SubnetMask);
            }
            else if (selector._InStri("*") || selector._InStri("?"))
            {
                // ワイルドカード形式
                this.Type = ForwarderType.WildcardFqdn;
                this.WildcardFqdn = selector.ToLowerInvariant().Trim();
            }
            else
            {
                // ドメイン名形式
                this.Type = ForwarderType.DomainName;
                this.DomainFqdn = selector._NormalizeFqdn();

                if (this.DomainFqdn._IsEmpty() || this.DomainFqdn._IsValidFqdn() == false)
                {
                    throw new CoresException($"Domain name '{src.Selector}' is an invalid FQDN.");
                }
            }

            this.TimeoutMsecs = src.TimeoutMsecs;
            if (this.TimeoutMsecs <= 0) this.TimeoutMsecs = CoresConfig.EasyDnsResponderSettings.Default_ForwarderTimeoutMsecs;

            string[] targetToken = src.TargetServers._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, " ", "\t", ";", ",", "/");

            HashSet<string> distinctCheck = new HashSet<string>(StrCmpi);

            foreach (var target in targetToken)
            {
                if (IPUtil.TryParseHostPort(target, out string host, out int port, Consts.Ports.Dns))
                {
                    IPEndPoint ep = new IPEndPoint(IPUtil.StrToIP(host)!, port);

                    string test = ep.ToString();

                    if (distinctCheck.Add(test))
                    {
                        this.TargetServersList.Add(ep);
                    }
                }
            }

            this.CallbackId = src.CallbackId._NonNull();
            this.Src = src._CloneDeep();
        }
    }

    // 内部ゾーンデータ
    public class Zone
    {
        public DataSet ParentDataSet;
        public string DomainFqdn;
        public DomainName DomainName;
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

        public bool IsVirtualRootZone = false;

        // Glue レコード等の内部的生成に便宜上必要となるルートゾーン (.) の定義
        public Zone(EnsureSpecial isVirtualRootZone, Zone templateZone)
            : this(templateZone.ParentDataSet,
                  new EasyDnsResponderZone
                  {
                      DefaultSettings = templateZone.Settings,
                      DomainName = ".",
                  })
        {
            this.IsVirtualRootZone = true;
        }

        // ゾーンの定義
        public Zone(DataSet parent, EasyDnsResponderZone src)
        {
            this.ParentDataSet = parent;

            this.Settings = (src.DefaultSettings ?? parent.Settings)._CloneDeep();

            this.DomainFqdn = src.DomainName._NormalizeFqdn();

            this.DomainName = new DomainName(this.DomainFqdn._Split(StringSplitOptions.None, '.').AsMemory());

            Record_SOA? soa = null;

            // レコード情報のコンパイル
            foreach (var srcRecord in src.RecordList)
            {
                try
                {
                    var record = Record.CreateFrom(this, srcRecord);

                    if (record.Type != EasyDnsResponderRecordType.SOA)
                    {
                        string tmp1 = record.ToStringForCompare();
                        if (this.RecordList.Where(x => x.Type == record.Type && x.ToStringForCompare() == tmp1).Any() == false)
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
                            $"Domain '{this.DomainFqdn}': SOA record is duplicating."._Error();
                        }
                        else
                        {
                            soa = (Record_SOA)record;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 1 つのレコード情報のコンパイル失敗
                    $"Domain '{this.DomainFqdn}': {ex._GetOneLineExceptionString()}"._Error();
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
                        Contents = this.DomainFqdn._FilledOrDefault("."),
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
                            this.SubDomainList.Add(labels.Slice(i)._Combine(".", estimatedLength: r.Name.Length));
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
                        $"NS record Name: {r.Name} is duplicating with the existing sub domain record."._Error();

                        // エラーを記録するものの、この関数は失敗させない。
                        continue;
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

            bool isWildcardMatch = false;

            // まず完全一致するものがないか確かめる
            if (this.RecordDictByName.TryGetValue(hostLabelNormalized, out List<Record>? found))
            {
                // 完全一致あり
                answers = found._CloneListFast();
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
                        if (this.NSDelegationRecordList.TryGetValue(hostLabelSpan.Slice(i)._Combine(".", estimatedLength: hostLabelNormalized.Length), out List<Record>? found2))
                        {
                            // 権限委譲ドメイン情報が見つかった
                            SearchResult ret2 = new SearchResult
                            {
                                SOARecord = this.SOARecord,
                                Zone = this,
                                RecordList = found2,
                                ResultFlags = SearchResultFlags.SubDomainIsDelegated,
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
                            answers = r.Value._CloneListFast();
                            isWildcardMatch = true;
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
                            answers = r.Value._CloneListFast();
                            isWildcardMatch = true;
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
                    answers = this.WildcardAnyRecordList._CloneListFast();

                    isWildcardMatch = true;
                }
            }

            if (answers != null && isWildcardMatch)
            {
                // この時点でワイルドカード一致による結果が整っている場合で、
                // サブネットを示す A/AAAA レコードがある場合は、適切なフィルタを実施する
                List<Record> newList = new List<Record>(answers.Count);

                for (int i = 0; i < answers.Count; i++)
                {
                    var answer = answers[i];
                    bool ok = true;
                    if (answer is Record_A a && a.IsSubnet)
                    {
                        ok = false;
                        if (IPUtil.TryParseWildCardDnsFqdn(hostLabelNormalized, out IPAddress? embedIp)) // ホスト名部分に埋め込まれている IP アドレスをパースする
                        {
                            // このパースされた IP アドレスがサブネット範囲に属するかどうか検査する
                            if (IPUtil.IsInSameNetwork(a.IPv4Address, embedIp, a.IPv4SubnetMask, true))
                            {
                                // 宜しい
                                ok = true;

                                var new_a = a.Clone();
                                new_a.IPv4Address = embedIp;
                                new_a.IPv4SubnetMask = IPAddress.Broadcast;
                                new_a.IsSubnet = false;
                                answer = new_a;
                            }
                        }
                    }
                    else if (answer is Record_AAAA aaaa && aaaa.IsSubnet)
                    {
                        ok = false;
                        if (IPUtil.TryParseWildCardDnsFqdn(hostLabelNormalized, out IPAddress? embedIp)) // ホスト名部分に埋め込まれている IP アドレスをパースする
                        {
                            // このパースされた IP アドレスがサブネット範囲に属するかどうか検査する
                            if (IPUtil.IsInSameNetwork(aaaa.IPv6Address, embedIp, aaaa.IPv6SubnetMask, true))
                            {
                                // 宜しい
                                ok = true;

                                var new_aaaa = aaaa.Clone();
                                new_aaaa.IPv6Address = embedIp;
                                new_aaaa.IPv6SubnetMask = IPUtil.IPv6AllFilledAddressCache;
                                new_aaaa.IsSubnet = false;
                                answer = new_aaaa;
                            }
                        }
                    }

                    if (ok)
                    {
                        newList.Add(answer);
                    }
                }

                answers = newList;
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
                        if (this.SubDomainList.Contains(hostLabelSpan.Slice(i)._Combine(".", estimatedLength: hostLabelNormalized.Length)))
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
                // このゾーン名を完全一致でクエリをしてきている場合、このゾーンに関する NS と SOA レコードも追加する
                if (hostLabelSpan.Length == 0)
                {
                    foreach (var ns in this.NSRecordList)
                    {
                        answers.Add(ns);
                    }
                    answers.Add(this.SOARecord);
                }
            }

            SearchResult ret = new SearchResult
            {
                RecordList = answers,
                SOARecord = this.SOARecord,
                Zone = this,
                ResultFlags = (answers == null ? SearchResultFlags.NotFound : SearchResultFlags.NormalAnswer),
                RequestHostName = hostLabelNormalized,
            };

            return ret;
        }
    }

    public class DataSet
    {
        // 内部データの実体
        public StrDictionary<Zone> ZoneDict = new StrDictionary<Zone>();
        public EasyDnsResponderRecordSettings Settings;

        public StrDictionary<Forwarder> Forwarder_DomainBasedDict = new StrDictionary<Forwarder>();
        public FullRoute46<Forwarder> Forwarder_SubnetBasedRadixTrie = new FullRoute46<Forwarder>();
        public List<Forwarder> Forwarder_WildcardBasedList = new List<Forwarder>();
        public bool HasForwarder_DomainBased = false;
        public bool HasForwarder_SubnetBased = false;
        public bool HasForwarder_WildcardBased = false;

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

            // フォワーダ情報のコンパイル
            foreach (var srcForwarder in src.ForwarderList)
            {
                var forwarder = new Forwarder(this, srcForwarder);

                switch (forwarder.Type)
                {
                    case ForwarderType.DomainName:
                        // ドメイン名に基づくフォワーダ
                        this.Forwarder_DomainBasedDict.Add(forwarder.DomainFqdn, forwarder);
                        this.HasForwarder_DomainBased = true;
                        break;

                    case ForwarderType.Subnet:
                        // サブネットに基づくフォワーダ
                        this.Forwarder_SubnetBasedRadixTrie.Insert(forwarder.Subnet_IpNetwork, forwarder.Subnet_SubnetLength, forwarder);
                        this.HasForwarder_SubnetBased = true;
                        break;

                    case ForwarderType.WildcardFqdn:
                        // ワイルドカード文字列に基づくフォワーダ
                        this.Forwarder_WildcardBasedList.Add(forwarder);
                        this.HasForwarder_WildcardBased = true;
                        break;
                }
            }

            // ワイルドカード文字列に基づくフォワーダは文字列長が長い順にソートする
            this.Forwarder_WildcardBasedList._DoSortBy(x => x.OrderByDescending(y => y.WildcardFqdn.Length).ThenByDescending(y => y.WildcardFqdn));
        }

        // クエリ検索
        public SearchResult? Search(SearchRequest request)
        {
            Zone? zone = DnsUtil.SearchLongestMatchDnsZone(this.ZoneDict, request.FqdnNormalized, out string hostLabelStr, out ReadOnlyMemory<string> hostLabels);

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
        public DnsUdpPacket? RequestPacket { init; get; } = null!;
    }

    [Flags]
    public enum SearchResultFlags
    {
        NormalAnswer = 0,
        SubDomainIsDelegated = 1,
        NotFound = 2,
    }

    // 検索結果
    public class SearchResult
    {
        public List<Record>? RecordList { get; set; } = null; // null: サブドメインが全く存在しない 空リスト: サブドメインは存在するものの、レコードは存在しない

        [JsonIgnore]
        public Zone Zone { get; set; } = null!;
        public string ZoneDomainFqdn => Zone.DomainFqdn;
        public DomainName ZoneDomainName => Zone.DomainName;
        public string RequestHostName { get; set; } = null!;
        public Record_SOA SOARecord { get; set; } = null!;
        [JsonConverter(typeof(StringEnumConverter))]
        public SearchResultFlags ResultFlags { get; set; } = SearchResultFlags.NormalAnswer;
        public ReturnCode RaiseCustomError { get; set; } = ReturnCode.NoError;
    }

    // フォワーダ変換テーブル
    public class FwdTranslateEntry
    {
        public IPEndPoint ForwarderEndPoint = null!;
        public IPEndPoint TargetEndPoint = null!;
        public long ExpiresTick;
    }

    DataSet? CurrentDataSet = null;

    public void ApplySetting(EasyDnsResponderSettings setting)
    {
        var dataSet = new DataSet(setting);

        this.CurrentDataSet = dataSet;
    }

    public SearchResult? Query(SearchRequest request, EasyDnsResponderRecordType type)
    {
        var dataSet = this.CurrentDataSet;
        if (dataSet == null)
        {
            throw new CoresException("Current DNS Server Data Set is not loaded");
        }

        // 最初に一致するフォワーダがあるかどうか調べる。
        // フォワーダは最優先で使用される。
        if (dataSet.HasForwarder_DomainBased || dataSet.HasForwarder_SubnetBased || dataSet.HasForwarder_WildcardBased)
        {
            Forwarder? forwarder = null;

            // ワイルドカードベース -> サブネットベース -> ドメイン名ベースの順に走査する。

            // ワイルドカードベース
            if (dataSet.HasForwarder_WildcardBased)
            {
                foreach (var w in dataSet.Forwarder_WildcardBasedList)
                {
                    if (Str.WildcardMatch(request.FqdnNormalized, w.WildcardFqdn))
                    {
                        forwarder = w;
                        break;
                    }
                }
            }

            // サブネットベース
            if (forwarder == null)
            {
                if (dataSet.HasForwarder_SubnetBased)
                {
                    if (Str.IsEqualToOrSubdomainOf(EnsureSpecial.Yes, request.FqdnNormalized, "in-addr.arpa") ||
                        Str.IsEqualToOrSubdomainOf(EnsureSpecial.Yes, request.FqdnNormalized, "ip6.arpa"))
                    {
                        var tmp = IPUtil.PtrZoneOrFqdnToIpAddressAndSubnet(request.FqdnNormalized, true);
                        if (IPUtil.IsSubnetLenHostAddress(tmp.Item1.AddressFamily, tmp.Item2))
                        {
                            forwarder = dataSet.Forwarder_SubnetBasedRadixTrie.Lookup(tmp.Item1, out _, out _);
                        }
                    }
                }
            }

            // ドメイン名ベース
            if (forwarder == null)
            {
                if (dataSet.HasForwarder_DomainBased)
                {
                    forwarder = DnsUtil.SearchLongestMatchDnsZone<Forwarder>(dataSet.Forwarder_DomainBasedDict, request.FqdnNormalized, out _, out _);
                }
            }

            if (forwarder != null)
            {
                // 使用すべきフォワーダが見つかった。コールバックを呼ぶ。
                var callback = this.ForwarderCallback;

                EasyDnsResponderForwarderCallbackRequest fwdReq = new EasyDnsResponderForwarderCallbackRequest
                {
                    CallbackId = forwarder.CallbackId,
                    ForwarderInternal = forwarder,
                    ForwarderDef = forwarder.Src,
                    OriginalRequestPacket = request.RequestPacket,
                    RequestFqdn = request.FqdnNormalized,
                };

                EasyDnsResponderForwarderCallbackResult fwdResult;

                if (callback != null)
                {
                    fwdResult = callback(fwdReq);
                }
                else
                {
                    fwdResult = new EasyDnsResponderForwarderCallbackResult { };
                }

                if (fwdResult.SkipForwarder)
                {
                    // Skip
                }
                else if (fwdResult.ErrorCode != ReturnCode.NoError)
                {
                    // エラー
                    SearchResult ret2 = new SearchResult
                    {
                        RaiseCustomError = fwdResult.ErrorCode,
                    };
                    return ret2;
                }
                else
                {
                    // フォワーダ処理の実施
                    foreach (var destEndPoint in forwarder.TargetServersList)
                    {
                    }
                }
            }
        }

        // -- 以下は、フォワーダで処理がなされなかった場合のメイン処理の継続 --

        // 純粋な Zone の検索処理を実施する。クエリにおける要求レコードタイプは見ない。
        SearchResult? ret = dataSet.Search(request);

        // 次にクエリにおける要求レコードタイプに従って特別処理を行なう。
        if (ret != null)
        {
            var zone = ret.Zone;

            if (ret.ResultFlags.Bit(SearchResultFlags.SubDomainIsDelegated))
            {
                // 特別処理: クエリ種類が NS の場合で、権限委譲されているドメインの場合、結果には権限委譲のための NS レコードを埋め込むのである。
                // (dataSet.Search() によって、すでに埋め込みされているはずである。したがって、ここでは何もしない。)
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
                            if (r.Type == type || (r.Type == EasyDnsResponderRecordType.Any && r is Record_Dynamic))
                            {
                                tmpList.Add(r);
                            }
                        }

                        if (tmpList.Count == 0)
                        {
                            // フィルタ結果が 0 件の場合で、一致する CNAME があれば、それを追加する
                            foreach (var r in ret.RecordList)
                            {
                                if (r.Type == EasyDnsResponderRecordType.CNAME)
                                {
                                    tmpList.Add(r);
                                }
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

                bool allDynamicRecords = true;

                bool notFound = false;

                for (int i = 0; i < count; i++)
                {
                    if (ret.RecordList[i] is Record_Dynamic dynRecord)
                    {
                        if (ResolveDynamicRecord(solvedDynamicRecordResults, dynRecord, ret, request, type) == false)
                        {
                            notFound = true;
                        }

                        anyDynamicRecordExists = true;

                        originalDynamicRecords.Add(dynRecord);
                    }
                    else
                    {
                        allDynamicRecords = false;
                    }
                }

                if (anyDynamicRecordExists == false)
                {
                    allDynamicRecords = false;
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

                if (allDynamicRecords)
                {
                    if (notFound)
                    {
                        // ダイナミックレコードリストを走査したが、1 つも見つからない場合は、NotFound ビットを返す
                        ret.ResultFlags |= SearchResultFlags.NotFound;
                    }
                }
            }
        }

        return ret;
    }

    // ダイナミックレコードをコールバックを用いて実際に解決する
    bool ResolveDynamicRecord(List<Record> listToAdd, Record_Dynamic dynRecord, SearchResult result, SearchRequest request, EasyDnsResponderRecordType expectedRecordType)
    {
        EasyDnsResponderDynamicRecordCallbackRequest req = new EasyDnsResponderDynamicRecordCallbackRequest
        {
            Zone = result.Zone.SrcZone,
            Record = dynRecord.SrcRecord!,
            ExpectedRecordType = expectedRecordType,
            RequestFqdn = request.FqdnNormalized,
            RequestHostName = result.RequestHostName,
            CallbackId = dynRecord.CallbackId,
            RequestPacket = request.RequestPacket,
        };

        EasyDnsResponderDynamicRecordCallbackResult? callbackResult = null;

        if (this.DynamicRecordCallback == null) throw new CoresLibException("Callback delegate is not set.");

        callbackResult = this.DynamicRecordCallback(req);

        if (callbackResult == null)
        {
            throw new CoresLibException($"Callback delegate returns null for callback ID '{dynRecord.CallbackId}'.");
        }

        if (callbackResult.NotFound)
        {
            return false;
        }

        EasyDnsResponderRecordSettings? settings = callbackResult.Settings;
        if (settings == null)
        {
            settings = dynRecord.Settings;
        }

        if (expectedRecordType == EasyDnsResponderRecordType.A || expectedRecordType == EasyDnsResponderRecordType.Any)
        {
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
        }

        if (expectedRecordType == EasyDnsResponderRecordType.AAAA || expectedRecordType == EasyDnsResponderRecordType.Any)
        {
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
        }

        if (expectedRecordType == EasyDnsResponderRecordType.MX || expectedRecordType == EasyDnsResponderRecordType.Any)
        {
            if (callbackResult.MxFqdnList != null)
            {
                if (callbackResult.MxPreferenceList != null)
                {
                    if (callbackResult.MxFqdnList.Count == callbackResult.MxPreferenceList.Count)
                    {
                        for (int i = 0; i < callbackResult.MxFqdnList.Count; i++)
                        {
                            listToAdd.Add(new Record_MX(result.Zone, settings, result.RequestHostName, callbackResult.MxFqdnList[i], callbackResult.MxPreferenceList[i]));
                        }
                    }
                }
            }
        }

        if (expectedRecordType == EasyDnsResponderRecordType.SRV || expectedRecordType == EasyDnsResponderRecordType.Any)
        {
            if (callbackResult.SrvPortList != null && callbackResult.SrvPriorityList != null && callbackResult.SrvWeightList != null && callbackResult.SrvTargetList != null)
            {
                int count = callbackResult.SrvPortList.Count;
                if (callbackResult.SrvPriorityList.Count == count && callbackResult.SrvWeightList.Count == count && callbackResult.SrvTargetList.Count == count)
                {
                    for (int i = 0; i < count; i++)
                    {
                        listToAdd.Add(new Record_SRV(result.Zone, settings, result.RequestHostName,
                            callbackResult.SrvPriorityList[i], callbackResult.SrvWeightList[i], callbackResult.SrvPortList[i], callbackResult.SrvTargetList[i]));
                    }
                }
            }
        }

        if (expectedRecordType == EasyDnsResponderRecordType.NS || expectedRecordType == EasyDnsResponderRecordType.Any)
        {
            if (callbackResult.NsFqdnList != null)
            {
                foreach (var domain in callbackResult.NsFqdnList)
                {
                    listToAdd.Add(new Record_NS(result.Zone, settings, result.RequestHostName, domain));
                }
            }
        }

        if (expectedRecordType == EasyDnsResponderRecordType.PTR || expectedRecordType == EasyDnsResponderRecordType.Any)
        {
            if (callbackResult.PtrFqdnList != null)
            {
                foreach (var domain in callbackResult.PtrFqdnList)
                {
                    listToAdd.Add(new Record_PTR(result.Zone, settings, result.RequestHostName, domain));
                }
            }
        }

        if (expectedRecordType == EasyDnsResponderRecordType.TXT || expectedRecordType == EasyDnsResponderRecordType.Any)
        {
            if (callbackResult.TextList != null)
            {
                foreach (var text in callbackResult.TextList)
                {
                    listToAdd.Add(new Record_TXT(result.Zone, settings, result.RequestHostName, text));
                }
            }
        }

        if (expectedRecordType == EasyDnsResponderRecordType.CAA || expectedRecordType == EasyDnsResponderRecordType.Any)
        {
            if (callbackResult.CaaList != null)
            {
                foreach (var caa in callbackResult.CaaList)
                {
                    listToAdd.Add(new Record_CAA(result.Zone, settings, result.RequestHostName, caa.Item1, caa.Item2, caa.Item3));
                }
            }
        }

        //if (expectedRecordType == EasyDnsResponderRecordType.CNAME || expectedRecordType == EasyDnsResponderRecordType.Any)
        // CNAME はすべての場合に応答する
        {
            if (callbackResult.CNameFqdnList != null)
            {
                foreach (var domain in callbackResult.CNameFqdnList)
                {
                    listToAdd.Add(new Record_CNAME(result.Zone, settings, result.RequestHostName, domain));
                }
            }
        }

        return true;
    }
}


public static class EasyDnsTest
{
    public static void Test1()
    {
        EasyDnsResponderSettings st = new EasyDnsResponderSettings();

        {
            var zone = new EasyDnsResponderZone { DomainName = "test1.com" };

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "", Contents = "9.3.1.7" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.AAAA, Name = "", Contents = "2001::8181" });

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "www", Contents = "1.2.3.4" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "www", Contents = "1.9.8.4" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.AAAA, Name = "www", Contents = "2001::1234" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "ftp", Contents = "5.6.7.8" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.SOA, Contents = "ns1.test1.com nobori.softether.com 123 50 100 200 25" });

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.NS, Name = "", Contents = "ns1.ipa.go.jp" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.NS, Name = "", Contents = "ns2.ipa.go.jp" });

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.NS, Name = "subdomain1", Contents = "ns3.ipa.go.jp" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.NS, Name = "subdomain1", Contents = "ns4.ipa.go.jp" });

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.CNAME, Name = "cnametest1", Contents = "www.google.com" });

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.NS, Name = "subdomain2.subdomain1", Contents = "ns5.ipa.go.jp" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.NS, Name = "subdomain2.subdomain1", Contents = "ns6.ipa.go.jp" });

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "www.subdomain3", Contents = "8.9.4.5" });

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "*.kgb.abc123.subdomain5", Contents = "4.9.8.9" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "*.subdomain5", Contents = "5.9.6.3" });

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "*subdomain6*", Contents = "6.7.8.9" });

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "*.subdomain7", Contents = "proc0001", Attribute = EasyDnsResponderRecordAttribute.DynamicRecord });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.AAAA, Name = "*.subdomain7", Contents = "proc0002", Attribute = EasyDnsResponderRecordAttribute.DynamicRecord });

            st.ZoneList.Add(zone);
        }

        {
            var zone = new EasyDnsResponderZone { DomainName = "abc.test1.com" };

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "", Contents = "1.8.1.8" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "www", Contents = "8.9.3.1" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.AAAA, Name = "ftp", Contents = "2001::abcd" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.AAAA, Name = "aho.baka.manuke", Contents = "2001::abcd" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.SOA, Contents = "ns2.test1.com daiyuu.softether.com 894 895 896 897 898" });

            st.ZoneList.Add(zone);
        }

        EasyDnsResponder r = new EasyDnsResponder();

        r.ApplySetting(st);

        r.DynamicRecordCallback = (req) =>
        {
            switch (req.CallbackId)
            {
                case "proc0001":
                    var res1 = new EasyDnsResponderDynamicRecordCallbackResult();
                    res1.IPAddressList = new List<IPAddress>();
                    res1.IPAddressList.Add(IPAddress.Parse("1.0.0.1"));
                    res1.IPAddressList.Add(IPAddress.Parse("2.0.0.2"));
                    return res1;

                case "proc0002":
                    var res2 = new EasyDnsResponderDynamicRecordCallbackResult();
                    res2.IPAddressList = new List<IPAddress>();
                    res2.IPAddressList.Add(IPAddress.Parse("2001::1"));
                    res2.IPAddressList.Add(IPAddress.Parse("2001::2"));
                    return res2;
            }

            return null;
        };

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "test1.com" }, EasyDnsResponderRecordType.Any);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name != "").Count() == 0);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name == "").Where(x => x.Type == EasyDnsResponderRecordType.A).Count() == 1);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "").Where(x => x.Type == EasyDnsResponderRecordType.A)
                .Cast<EasyDnsResponder.Record_A>().OrderBy(x => x.IPv4Address, IpComparer.Comparer).ElementAt(0).IPv4Address.ToString() == "9.3.1.7");

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "").Where(x => x.Type == EasyDnsResponderRecordType.AAAA).Count() == 1);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "").Where(x => x.Type == EasyDnsResponderRecordType.AAAA)
                .Cast<EasyDnsResponder.Record_AAAA>().OrderBy(x => x.IPv6Address, IpComparer.Comparer).ElementAt(0).IPv6Address.ToString() == "2001::8181");

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "").Where(x => x.Type == EasyDnsResponderRecordType.NS).Count() == 2);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "").Where(x => x.Type == EasyDnsResponderRecordType.NS)
                .Cast<EasyDnsResponder.Record_NS>().OrderBy(x => x.ServerName.ToString(), StrCmpi).ElementAt(0).ServerName.ToString() == "ns1.ipa.go.jp.");
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "").Where(x => x.Type == EasyDnsResponderRecordType.NS)
                .Cast<EasyDnsResponder.Record_NS>().OrderBy(x => x.ServerName.ToString(), StrCmpi).ElementAt(1).ServerName.ToString() == "ns2.ipa.go.jp.");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "cnametest1.test1.com" }, EasyDnsResponderRecordType.A);
            var record = res!.RecordList!.Single();
            Dbg.TestTrue(record.Type == EasyDnsResponderRecordType.CNAME);
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "subdomain3.test1.com" }, EasyDnsResponderRecordType.Any);
            Dbg.TestTrue(res!.RecordList != null);
            Dbg.TestTrue(res.RecordList!.Count == 0);
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "subdomain4.test1.com" }, EasyDnsResponderRecordType.Any);
            Dbg.TestTrue(res!.RecordList == null);
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "abc123.subdomain5.test1.com" }, EasyDnsResponderRecordType.Any);
            Dbg.TestTrue(res!.RecordList!.Where(x => x.Type == EasyDnsResponderRecordType.A)
                .Cast<EasyDnsResponder.Record_A>().Single().IPv4Address.ToString() == "5.9.6.3");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "def456.abc123.subdomain5.test1.com" }, EasyDnsResponderRecordType.Any);
            Dbg.TestTrue(res!.RecordList!.Where(x => x.Type == EasyDnsResponderRecordType.A)
                .Cast<EasyDnsResponder.Record_A>().Single().IPv4Address.ToString() == "5.9.6.3");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "def456.kgb.abc123.subdomain5.test1.com" }, EasyDnsResponderRecordType.Any);
            Dbg.TestTrue(res!.RecordList!.Where(x => x.Type == EasyDnsResponderRecordType.A)
                .Cast<EasyDnsResponder.Record_A>().Single().IPv4Address.ToString() == "4.9.8.9");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "subdomain6.test1.com" }, EasyDnsResponderRecordType.Any);
            Dbg.TestTrue(res!.RecordList!.Where(x => x.Type == EasyDnsResponderRecordType.A)
                .Cast<EasyDnsResponder.Record_A>().Single().IPv4Address.ToString() == "6.7.8.9");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "1.2.3.4.subdomain7.test1.com" }, EasyDnsResponderRecordType.Any);
            res._PrintAsJson();

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Type == EasyDnsResponderRecordType.A).Count() == 2);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Type == EasyDnsResponderRecordType.A)
                .Cast<EasyDnsResponder.Record_A>().OrderBy(x => x.IPv4Address, IpComparer.Comparer).ElementAt(0).IPv4Address.ToString() == "1.0.0.1");
            Dbg.TestTrue(res.RecordList!.Where(x => x.Type == EasyDnsResponderRecordType.A)
                .Cast<EasyDnsResponder.Record_A>().OrderBy(x => x.IPv4Address, IpComparer.Comparer).ElementAt(1).IPv4Address.ToString() == "2.0.0.2");

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Type == EasyDnsResponderRecordType.AAAA).Count() == 2);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Type == EasyDnsResponderRecordType.AAAA)
                .Cast<EasyDnsResponder.Record_AAAA>().OrderBy(x => x.IPv6Address, IpComparer.Comparer).ElementAt(0).IPv6Address.ToString() == "2001::1");
            Dbg.TestTrue(res.RecordList!.Where(x => x.Type == EasyDnsResponderRecordType.AAAA)
                .Cast<EasyDnsResponder.Record_AAAA>().OrderBy(x => x.IPv6Address, IpComparer.Comparer).ElementAt(1).IPv6Address.ToString() == "2001::2");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "subdomain1.test1.com" }, EasyDnsResponderRecordType.Any);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name != "subdomain1").Count() == 0);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name == "subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.A).Count() == 0);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.AAAA).Count() == 0);

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS).Count() == 2);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS)
                .Cast<EasyDnsResponder.Record_NS>().OrderBy(x => x.ServerName.ToString(), StrCmpi).ElementAt(0).ServerName.ToString() == "ns3.ipa.go.jp.");
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS)
                .Cast<EasyDnsResponder.Record_NS>().OrderBy(x => x.ServerName.ToString(), StrCmpi).ElementAt(1).ServerName.ToString() == "ns4.ipa.go.jp.");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "subdomain2.subdomain1.test1.com" }, EasyDnsResponderRecordType.Any);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name != "subdomain2.subdomain1").Count() == 0);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name == "subdomain2.subdomain1").Where(x => x.Type != EasyDnsResponderRecordType.NS).Count() == 0);

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain2.subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS).Count() == 2);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain2.subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS)
                .Cast<EasyDnsResponder.Record_NS>().OrderBy(x => x.ServerName.ToString(), StrCmpi).ElementAt(0).ServerName.ToString() == "ns5.ipa.go.jp.");
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain2.subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS)
                .Cast<EasyDnsResponder.Record_NS>().OrderBy(x => x.ServerName.ToString(), StrCmpi).ElementAt(1).ServerName.ToString() == "ns6.ipa.go.jp.");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "xyz.abc.subdomain2.subdomain1.test1.com" }, EasyDnsResponderRecordType.Any);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name != "subdomain2.subdomain1").Count() == 0);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name == "subdomain2.subdomain1").Where(x => x.Type != EasyDnsResponderRecordType.NS).Count() == 0);

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain2.subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS).Count() == 2);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain2.subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS)
                .Cast<EasyDnsResponder.Record_NS>().OrderBy(x => x.ServerName.ToString(), StrCmpi).ElementAt(0).ServerName.ToString() == "ns5.ipa.go.jp.");
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain2.subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS)
                .Cast<EasyDnsResponder.Record_NS>().OrderBy(x => x.ServerName.ToString(), StrCmpi).ElementAt(1).ServerName.ToString() == "ns6.ipa.go.jp.");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "test123.subdomain1.test1.com" }, EasyDnsResponderRecordType.Any);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name != "subdomain1").Count() == 0);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name == "subdomain1").Where(x => x.Type != EasyDnsResponderRecordType.NS).Count() == 0);

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS).Count() == 2);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS)
                .Cast<EasyDnsResponder.Record_NS>().OrderBy(x => x.ServerName.ToString(), StrCmpi).ElementAt(0).ServerName.ToString() == "ns3.ipa.go.jp.");
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS)
                .Cast<EasyDnsResponder.Record_NS>().OrderBy(x => x.ServerName.ToString(), StrCmpi).ElementAt(1).ServerName.ToString() == "ns4.ipa.go.jp.");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "www.subdomain3.test1.com" }, EasyDnsResponderRecordType.Any);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name != "www.subdomain3").Count() == 0);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name == "www.subdomain3").Where(x => x.Type == EasyDnsResponderRecordType.A).Count() == 1);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "www.subdomain3").Where(x => x.Type == EasyDnsResponderRecordType.A)
                .Cast<EasyDnsResponder.Record_A>().OrderBy(x => x.IPv4Address, IpComparer.Comparer).ElementAt(0).IPv4Address.ToString() == "8.9.4.5");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "www.test1.com" }, EasyDnsResponderRecordType.Any);

            Dbg.TestTrue(res!.SOARecord.MasterName.ToString() == "ns1.test1.com.");
            Dbg.TestTrue(res.SOARecord.ResponsibleName.ToString() == "nobori.softether.com.");
            Dbg.TestTrue(res.SOARecord.SerialNumber == 123);
            Dbg.TestTrue(res.SOARecord.RefreshIntervalSecs == 50);
            Dbg.TestTrue(res.SOARecord.RetryIntervalSecs == 100);
            Dbg.TestTrue(res.SOARecord.ExpireIntervalSecs == 200);
            Dbg.TestTrue(res.SOARecord.NegativeCacheTtlSecs == 25);

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name != "www").Count() == 0);

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "www").Where(x => x.Type == EasyDnsResponderRecordType.A).Count() == 2);

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "www").Where(x => x.Type == EasyDnsResponderRecordType.A)
                .Cast<EasyDnsResponder.Record_A>().OrderBy(x => x.IPv4Address, IpComparer.Comparer).ElementAt(0).IPv4Address.ToString() == "1.2.3.4");

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "www").Where(x => x.Type == EasyDnsResponderRecordType.A)
                .Cast<EasyDnsResponder.Record_A>().OrderBy(x => x.IPv4Address, IpComparer.Comparer).ElementAt(1).IPv4Address.ToString() == "1.9.8.4");

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "www").Where(x => x.Type == EasyDnsResponderRecordType.AAAA).Count() == 1);

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "www").Where(x => x.Type == EasyDnsResponderRecordType.AAAA)
                .Cast<EasyDnsResponder.Record_AAAA>().OrderBy(x => x.IPv6Address, IpComparer.Comparer).ElementAt(0).IPv6Address.ToString() == "2001::1234");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "abc.test1.com" }, EasyDnsResponderRecordType.Any);

            Dbg.TestTrue(res!.SOARecord.MasterName.ToString() == "ns2.test1.com.");
            Dbg.TestTrue(res.SOARecord.ResponsibleName.ToString() == "daiyuu.softether.com.");
            Dbg.TestTrue(res.SOARecord.SerialNumber == 894);
            Dbg.TestTrue(res.SOARecord.RefreshIntervalSecs == 895);
            Dbg.TestTrue(res.SOARecord.RetryIntervalSecs == 896);
            Dbg.TestTrue(res.SOARecord.ExpireIntervalSecs == 897);
            Dbg.TestTrue(res.SOARecord.NegativeCacheTtlSecs == 898);

            Dbg.TestTrue(((EasyDnsResponder.Record_A)res.RecordList!.Single(x => x.Type == EasyDnsResponderRecordType.A)).IPv4Address.ToString() == "1.8.1.8");
        }

    }
}


#endif
