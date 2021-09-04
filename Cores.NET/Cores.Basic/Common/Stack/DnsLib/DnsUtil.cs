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

#nullable disable

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

namespace IPA.Cores.Basic
{
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

        static readonly DateTime DnsDtStartDay = new DateTime(2021, 1, 1);

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
        public DnsMessageBase Message { get; }

        public DnsUdpPacket(IPEndPoint remoteEndPoint, IPEndPoint localEndPoint, DnsMessageBase message)
        {
            if (remoteEndPoint.AddressFamily != localEndPoint.AddressFamily)
            {
                throw new CoresException("remoteEndPoint.AddressFamily != localEndPoint.AddressFamily");
            }

            this.RemoteEndPoint = remoteEndPoint;
            this.LocalEndPoint = localEndPoint;
            this.Message = message;
        }
    }




    public delegate Span<DnsUdpPacket> EasyDnsServerProcessPacketsCallback(Span<DnsUdpPacket> requestList);

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

        public EasyDnsServer(EasyDnsServerSetting setting)
        {
            try
            {
                this.Setting = setting;

                this.StartMainLoop(this.MainLoopAsync);
            }
            catch
            {
                this._DisposeSafe();
                throw;
            }
        }

#pragma warning disable CS1998 // 非同期メソッドは、'await' 演算子がないため、同期的に実行されます
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
                    var allRecvList = await udpSock.ReceiveDatagramsListAsync(cancel: cancel);

                    var allSendList = await allRecvList._ProcessDatagramWithMultiTasksAsync(async (perTaskRecvList) =>
                    {
                        var ret = Sync(() =>
                        {
                            Span<DnsUdpPacket> perTaskRequestPacketsList = new DnsUdpPacket[perTaskRecvList.Count];
                            int perTaskRequestPacketsListCount = 0;

                            //double start = Time.NowHighResDouble;

                            //Con.WriteDebug($"{Time.NowHighResDouble - start:F3} -- Start loop 1: perTaskRecvList.Count = {perTaskRecvList.Count}");

                            foreach (var item in perTaskRecvList)
                            {
                                try
                                {
                                    var request = DnsUtil.ParsePacket(item.Data.Span);

                                    DnsUdpPacket pkt = new DnsUdpPacket(item.RemoteIPEndPoint, item.LocalIPEndPoint, request);

                                    perTaskRequestPacketsList[perTaskRequestPacketsListCount++] = pkt;
                                }
                                catch { }
                            }

                            //Con.WriteDebug($"{Time.NowHighResDouble - start:F3} -- End loop 1: perTaskRequestPacketsListCount = {perTaskRequestPacketsListCount}");

                            Span<DnsUdpPacket> perTaskReaponsePacketsList = Setting.Callback(perTaskRequestPacketsList);

                            Span<Datagram> perTaskSendList = new Datagram[perTaskRequestPacketsListCount];
                            int perTaskSendListCount = 0;

                            //Con.WriteDebug($"{Time.NowHighResDouble - start:F3} -- Start loop 2: perTaskReaponsePacketsList.Count = {perTaskReaponsePacketsList.Length}");

                            foreach (var responsePkt in perTaskReaponsePacketsList)
                            {
                                try
                                {
                                    Memory<byte> packetData = DnsUtil.BuildPacket(responsePkt.Message).ToArray();

                                    var datagram = new Datagram(packetData, responsePkt.RemoteEndPoint, responsePkt.LocalEndPoint);

                                    perTaskSendList[perTaskSendListCount++] = datagram;
                                }
                                catch { }
                            }

                            //Con.WriteDebug($"{Time.NowHighResDouble - start:F3} -- End loop 2: perTaskSendListCount = {perTaskSendListCount}");

                            return perTaskSendList.Slice(0, perTaskSendListCount).ToArray().ToList();
                        });

                        return ret;
                    },
                    operation: MultitaskDivideOperation.RoundRobin,
                    cancel: cancel);

                    await udpSock.SendDatagramsListAsync(allSendList.ToArray());
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }
            }
        }
#pragma warning restore CS1998 // 非同期メソッドは、'await' 演算子がないため、同期的に実行されます

        protected override async Task CleanupImplAsync(Exception ex)
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
}


#endif
