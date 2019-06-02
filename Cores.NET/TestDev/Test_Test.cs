﻿// IPA Cores.NET
// 
// Copyright (c) 2018-2019 IPA CyberLab.
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

using System;
using System.IO;
using System.IO.Enumeration;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Json;
using System.Security.AccessControl;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Diagnostics;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Runtime.InteropServices;



#pragma warning disable CS0219
#pragma warning disable CS0162


namespace IPA.TestDev
{
    [Serializable]
    [DataContract]
    class TestData
    {
        [DataMember]
        public int A;
        [DataMember]
        public string B;
        [DataMember]
        public int C;
    }

    class CapTest : FastStreamBufferCaptureBase<byte>
    {
        public CapTest(FastStreamBuffer<byte> target, CancellationToken cancel = default) : base(target, cancel)
        {
        }

        protected override void CaptureCallbackImpl(long tick, FastBufferSegment<ReadOnlyMemory<byte>>[] segments, long totalSize)
        {
            Con.WriteLine($"Captured: {totalSize}");
        }
    }

    static class EnumTestClass
    {
        public static int GetValue<TKey>(TKey src) where TKey : Enum
        {
            return src.GetHashCode();
        }
        public static unsafe int GetValue2<TKey>(TKey src) where TKey : Enum
        {
            void* ptr = Unsafe.AsPointer(ref src);
            return *((int*)ptr);
        }
    }

    class TestHiveData1
    {
        public string Str;
        public string Date;
        public List<string> StrList = new List<string>();
    }

    static class TestClass
    {
        public static unsafe void Test()
        {
            PacketBuilder p = new PacketBuilder("Hello"._GetBytes_Ascii());

            var tcpHeader = new TCPHeader()
            {
                AckNumber = 123U._Endian32(),
                SeqNumber = 456U._Endian32(),
                Checksum = 0x1234U._Endian16(),
                SrcPort = 80U._Endian16(),
                DstPort = 443U._Endian16(),
                Flag = TCPFlags.Ack | TCPFlags.Fin | TCPFlags.Psh | TCPFlags.Rst,
                HeaderSize = (byte)((sizeof(TCPHeader) + 4) / 4),
                WindowSize = 1234U._Endian16(),
            };

            int tcp = p.PrependHeader<TCPHeader>(
                &tcpHeader,
                sizeof(TCPHeader) + 4);

            var v4Header = new IPv4Header()
            {
                SrcIP = 0x12345678,
                DstIP = 0xdeadbeef,
                Checksum = 0x1234U._Endian16(),
                Flags = IPv4Flags.DontFragment | IPv4Flags.MoreFragments,
                HeaderLen = (byte)(sizeof(IPv4Header) / 4),
                Identification = 0x1234U._Endian16(),
                Protocol = IPProtocolNumber.TCP,
                TimeToLive = 12,
                TotalLength = (ushort)(sizeof(IPv4Header) + sizeof(TCPHeader) + 4),
                Version = 4,
            };

            int ip = p.PrependHeader<IPv4Header>(
                &v4Header
                );

            var vlanHeader = new VLanHeader()
            {
                VLanId = 12345U._Endian16(),
                Protocol = EthernetProtocolId.IPv4._Endian16(),
            };

            int vlan = p.PrependHeader<VLanHeader>(
                &vlanHeader
                );

            EthernetHeader etherHeaderData = new EthernetHeader()
            {
                Protocol = EthernetProtocolId.VLan._Endian16(),
            };

            unsafe
            {
                etherHeaderData.SrcAddress[0] = 0x00; etherHeaderData.SrcAddress[1] = 0xAC; etherHeaderData.SrcAddress[2] = 0x01;
                etherHeaderData.SrcAddress[3] = 0x23; etherHeaderData.SrcAddress[4] = 0x45; etherHeaderData.SrcAddress[5] = 0x47;

                etherHeaderData.DestAddress[0] = 0x00; etherHeaderData.DestAddress[1] = 0x98; etherHeaderData.DestAddress[2] = 0x21;
                etherHeaderData.DestAddress[3] = 0x33; etherHeaderData.DestAddress[4] = 0x89; etherHeaderData.DestAddress[5] = 0x01;
            }

            int ether = p.PrependHeader<EthernetHeader>(&etherHeaderData);

            var d = p.Span.ToArray();

            var packet2 = new Packet(d);
            PacketParsed parsed = new PacketParsed(packet2);

            //Con.WriteLine(packet.Parsed.L2_TagVLan1.TagVlan.RefValueRead.VLanId);

            NoOp();
        }

        public static unsafe void Test___()
        {
            Packet p = new Packet("Hello"._GetBytes_Ascii());

            PacketPin<TCPHeader> tcp = p.PrependHeader<TCPHeader>(
                new TCPHeader()
                {
                    AckNumber = 123U._Endian32(),
                    SeqNumber = 456U._Endian32(),
                    Checksum = 0x1234U._Endian16(),
                    SrcPort = 80U._Endian16(),
                    DstPort = 443U._Endian16(),
                    Flag = TCPFlags.Ack | TCPFlags.Fin | TCPFlags.Psh | TCPFlags.Rst,
                    HeaderSize = (byte)((sizeof(TCPHeader) + 4) / 4),
                    WindowSize = 1234U._Endian16(),
                },
                sizeof(TCPHeader) + 4);

            PacketPin<IPv4Header> ip = tcp.PrependHeader<IPv4Header>(
                new IPv4Header()
                {
                    SrcIP = 0x12345678,
                    DstIP = 0xdeadbeef,
                    Checksum = 0x1234U._Endian16(),
                    Flags = IPv4Flags.DontFragment | IPv4Flags.MoreFragments,
                    HeaderLen = (byte)(sizeof(IPv4Header) / 4),
                    Identification = 0x1234U._Endian16(),
                    Protocol = IPProtocolNumber.TCP,
                    TimeToLive = 12,
                    TotalLength = (ushort)(sizeof(IPv4Header) + tcp.HeaderSize),
                    Version = 4,
                });

            PacketPin<VLanHeader> vlan = ip.PrependHeader<VLanHeader>(
                new VLanHeader()
                {
                    VLanId = 12345U._Endian16(),
                    Protocol = EthernetProtocolId.IPv4._Endian16(),
                });

            EthernetHeader etherHeaderData = new EthernetHeader()
            {
                Protocol = EthernetProtocolId.VLan._Endian16(),
            };

            etherHeaderData.SrcAddress[0] = 0x00; etherHeaderData.SrcAddress[1] = 0xAC; etherHeaderData.SrcAddress[2] = 0x01;
            etherHeaderData.SrcAddress[3] = 0x23; etherHeaderData.SrcAddress[4] = 0x45; etherHeaderData.SrcAddress[5] = 0x47;

            etherHeaderData.DestAddress[0] = 0x00; etherHeaderData.DestAddress[1] = 0x98; etherHeaderData.DestAddress[2] = 0x21;
            etherHeaderData.DestAddress[3] = 0x33; etherHeaderData.DestAddress[4] = 0x89; etherHeaderData.DestAddress[5] = 0x01;

            PacketPin<EthernetHeader> ether = vlan.PrependHeader<EthernetHeader>(in etherHeaderData);

            var d = p.Memory.ToArray();

            var packet2 = new Packet(d);
            PacketParsed parsed = new PacketParsed(packet2);

            //Con.WriteLine(packet.Parsed.L2_TagVLan1.TagVlan.RefValueRead.VLanId);

            NoOp();
        }

        public static unsafe void Test_()
        {
            Con.WriteLine(Unsafe.SizeOf<PacketParsed>());

            //var packetMem = Res.AppRoot["190527_novlan_simple_tcp.txt"].HexParsedBinary;
            //var packetMem = Res.AppRoot["190527_novlan_simple_udp.txt"].HexParsedBinary;
            //var packetMem = Res.AppRoot["190527_vlan_simple_tcp.txt"].HexParsedBinary;
            //var packetMem = Res.AppRoot["190527_vlan_simple_udp.txt"].HexParsedBinary;
            //var packetMem = Res.AppRoot["190531_vlan_pppoe_tcp.txt"].HexParsedBinary;
            //var packetMem = Res.AppRoot["190531_vlan_pppoe_udp.txt"].HexParsedBinary;
            var packetMem = Res.AppRoot["190531_vlan_pppoe_l2tp_tcp.txt"].HexParsedBinary;

            Packet packet = new Packet(packetMem._CloneMemory());

            PacketParsed parsed = new PacketParsed(packet);

            //Con.WriteLine(packet.Parsed.L2_TagVLan1.TagVlan.RefValueRead.VLanId);

            NoOp();
        }
    }
}


