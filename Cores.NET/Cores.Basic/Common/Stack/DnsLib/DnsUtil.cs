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

namespace IPA.Cores.Basic;

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

    public static readonly DateTime DnsDtStartDay = new DateTime(2021, 1, 1);

    public static uint GenerateSoaSerialNumberFromDateTime(DateTime dt)
    {
        int days = (int)(dt.Date - DnsDtStartDay.Date).TotalDays;

        days = Math.Max(days, 1);
        days = (days % 32000) + 10000;

        string str = days.ToString("D5") + dt.ToString("HHmmss").Substring(0, 5);

        return str._ToUInt();
    }
}

public class DnsUdpPacket
{
    public IPEndPoint RemoteEndPoint { get; }
    public IPEndPoint LocalEndPoint { get; }
    public IPEndPoint OriginalRemoteEndPoint { get; } // プロキシを経由してきた場合の元の DNS クライアントの情報
    public DnsMessageBase Message { get; }

    public DnsUdpPacket(IPEndPoint remoteEndPoint, IPEndPoint localEndPoint, DnsMessageBase message, IPEndPoint? originalRemoteEndPoint = null)
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
    }
}




public delegate List<DnsUdpPacket> EasyDnsServerProcessPacketsCallback(EasyDnsServer dnsServer, List<DnsUdpPacket> requestList);

public class EasyDnsServerDynOptions : INormalizable
{
    public int UdpRecvLoopPollingIntervalMsecs { get; set; } = 0;
    public int UdpDelayedProcessTaskQueueLength { get; set; } = 0;
    public int UdpDelayedResponsePacketQueueLength { get; set; } = 0;

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
    }
}

public class EasyDnsServerSetting
{
    public int UdpPort { get; }
    public EasyDnsServerProcessPacketsCallback Callback { get; }

    public EasyDnsServerSetting(EasyDnsServerProcessPacketsCallback callback, int udpPort = Consts.Ports.Dns)
    {
        this.Callback = callback;
        this.UdpPort = udpPort;
    }
}

public class EasyDnsServer : AsyncServiceWithMainLoop
{
    public EasyDnsServerSetting Setting { get; }

    public EasyDnsServerDynOptions DynOptions { get => this._DynOptions._CloneDeepWithNormalize(); set => this._DynOptions = value._CloneDeepWithNormalize(); }
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
                                var request = DnsUtil.ParsePacket(item.Data.Span);

                                DnsUdpPacket pkt = new DnsUdpPacket(item.RemoteIPEndPoint!, item.LocalIPEndPoint!, request);

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
                //if (delayedUdpPacketsArray.Length >= 1)
                //{
                    //delayedUdpPacketsArray.Length._Print();
                //}
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


#endif
