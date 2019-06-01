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
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Runtime.InteropServices;

namespace IPA.Cores.Basic
{
    [Flags]
    enum PacketL2Type
    {
        Unknown = 0,
        Ethernet = 1,
    }

    [StructLayout(LayoutKind.Explicit, Pack = 8)]
    readonly struct L2
    {
        [FieldOffset(0)]
        public readonly PacketL2Type Type;

        [FieldOffset(8)]
        public readonly PacketPin<GenericHeader> Generic;
        [FieldOffset(8)]
        public readonly PacketPin<EthernetHeader> Ethernet;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public L2(PacketPin<EthernetHeader> pin)
        {
            this.Ethernet = default;

            this.Type = PacketL2Type.Ethernet;
            this.Generic = pin.ToGenericHeader();
        }
    }

    [StructLayout(LayoutKind.Explicit, Pack = 8)]
    readonly struct L2_TagVLan
    {
        [FieldOffset(0)]
        public readonly EthernetProtocolId Type;

        [FieldOffset(8)]
        public readonly PacketPin<GenericHeader> Generic;
        [FieldOffset(8)]
        public readonly PacketPin<TagVLanHeader> TagVlan;

        public L2_TagVLan(PacketPin<TagVLanHeader> pin, EthernetProtocolId tpid)
        {
            this.TagVlan = default;

            this.Type = tpid;
            this.Generic = pin.ToGenericHeader();
        }
    }

    [StructLayout(LayoutKind.Explicit, Pack = 8)]
    readonly struct L3
    {
        [FieldOffset(0)]
        public readonly EthernetProtocolId Type;

        [FieldOffset(8)]
        public readonly PacketPin<GenericHeader> Generic;
        [FieldOffset(8)]
        public readonly PacketPin<IPv4Header> IPv4;
        [FieldOffset(8)]
        public readonly PacketPin<PPPoESessionHeader> PPPoE;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public L3(PacketPin<IPv4Header> pin)
        {
            this.IPv4 = default;
            this.PPPoE = default;

            this.Type = EthernetProtocolId.IPv4;
            this.Generic = pin.ToGenericHeader();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public L3(PacketPin<PPPoESessionHeader> pin)
        {
            this.IPv4 = default;
            this.PPPoE = default;

            this.Type = EthernetProtocolId.PPPoE_Session;
            this.Generic = pin.ToGenericHeader();
        }
    }

    [StructLayout(LayoutKind.Explicit, Pack = 8)]
    readonly struct L4
    {
        [FieldOffset(0)]
        public readonly IPProtocolNumber Type;

        [FieldOffset(8)]
        public readonly PacketPin<GenericHeader> Generic;
        [FieldOffset(8)]
        public readonly PacketPin<TCPHeader> TCP;
        [FieldOffset(8)]
        public readonly PacketPin<UDPHeader> UDP;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public L4(PacketPin<TCPHeader> pin)
        {
            this.TCP = default;
            this.UDP = default;

            this.Type = IPProtocolNumber.TCP;
            this.Generic = pin.ToGenericHeader();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public L4(PacketPin<UDPHeader> pin)
        {
            this.TCP = default;
            this.UDP = default;

            this.Type = IPProtocolNumber.UDP;
            this.Generic = pin.ToGenericHeader();
        }
    }

    [Flags]
    enum L7Type
    {
        Unknown = 0,
        GenericTCP,
        GenericUDP,
        L2TP,
    }

    [StructLayout(LayoutKind.Explicit, Pack = 8)]
    readonly struct L7
    {
        [FieldOffset(0)]
        public readonly L7Type Type;

        [FieldOffset(8)]
        public readonly L2TPPacketParsed L2TPPacketParsed;

        [FieldOffset(16)]
        public readonly PacketPin<GenericHeader> Generic;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public L7(PacketPin<GenericHeader> pin, L7Type type)
        {
            this.L2TPPacketParsed = null;

            this.Generic = pin;
            this.Type = type;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public L7(PacketPin<GenericHeader> pin, L2TPPacketParsed l2tpPacketParsed)
        {
            this.L2TPPacketParsed = l2tpPacketParsed;

            this.Generic = pin;
            this.Type = L7Type.L2TP;
        }
    }

    class PacketParseOption
    {
    }

    [Flags]
    enum PacketParseMode
    {
        Layer2 = 0,
        Layer3,
    }

    struct PacketInfo
    {
        public uint L3_SrcIPv4;
        public uint L3_DestIPv4;

        public ushort L4_SrcPort;
        public ushort L4_DestPort;
    }

    struct PacketParsed
    {
        public Packet Packet { get; private set; }
        public PacketParseOption ParseOption { get; private set; }

        public string ErrorStr { get; private set; }
        public bool IsOk => ErrorStr == null;
        public bool IsError => !IsOk;

        public L2 L2 { get; private set; }
        public L2_TagVLan L2_TagVLan1 { get; private set; }
        public L2_TagVLan L2_TagVLan2 { get; private set; }
        public L2_TagVLan L2_TagVLan3 { get; private set; }

        public L3 L3 { get; private set; }

        public L4 L4 { get; private set; }

        public L7 L7 { get; private set; }

        public PacketInfo Info;

        public Ref<PacketParsed> InnerPacket { get; private set; }

        static readonly PacketParseOption DefaultOption = new PacketParseOption();

        public PacketParsed(Packet packet, int? startPin = null, PacketParseOption options = null, int? maxPacketSize = null, PacketParseMode mode = PacketParseMode.Layer2, EthernetProtocolId layer3ProtocolId = EthernetProtocolId.Unknown)
        {
            this.Packet = packet;
            this.ParseOption = options ?? DefaultOption;

            this.ErrorStr = null;
            this.L2 = default;
            this.L2_TagVLan1 = default;
            this.L2_TagVLan2 = default;
            this.L2_TagVLan3 = default;
            this.L3 = default;
            this.L4 = default;
            this.L7 = default;
            this.Info = default;

            this.InnerPacket = null;

            switch (mode)
            {
                case PacketParseMode.Layer2:
                    PacketPin<EthernetHeader> ether = Packet.GetHeader<EthernetHeader>(startPin ?? packet.PinHead, maxPacketSize: maxPacketSize ?? packet.Length);
                    ParseL2_Ethernet(ether);
                    break;

                case PacketParseMode.Layer3:
                    PacketPin<GenericHeader> generic = Packet.GetHeader<GenericHeader>(startPin ?? packet.PinHead, size: 0, maxPacketSize: maxPacketSize ?? packet.Length);
                    ParseL3(generic, layer3ProtocolId);
                    break;
            }
        }

        void SetError(string err, [CallerMemberName] string caller = null)
        {
            caller = caller._NonNullTrim();
            err = err._NonNullTrim();
            this.ErrorStr = $"{caller}: {err}";
        }

        bool ParseL2_Ethernet(PacketPin<EthernetHeader> ether)
        {
            if (ether.IsEmpty)
            {
                SetError("Insufficient header data");
                return false;
            }

            this.L2 = new L2(ether);

            EthernetProtocolId tpid = ether.RefValueRead.Protocol._Endian16();

            if (tpid == EthernetProtocolId.TagVlan)
                return ParseL2_TagVLan1(this.L2.Ethernet, tpid);
            else
                return ParseL3(this.L2.Generic, tpid);
        }

        bool ParseL2_TagVLan1(PacketPin<EthernetHeader> prevHeader, EthernetProtocolId thisTpid)
        {
            PacketPin<TagVLanHeader> tagVLan = prevHeader.GetNextHeader<TagVLanHeader>();
            if (tagVLan.IsEmpty)
            {
                SetError("Insufficient header data");
                return false;
            }

            this.L2_TagVLan1 = new L2_TagVLan(tagVLan, thisTpid);

            EthernetProtocolId tpid = tagVLan.RefValueRead.Protocol._Endian16();

            if (tpid == EthernetProtocolId.TagVlan)
                return ParseL2_TagVLan2(this.L2.Ethernet, tpid);
            else
                return ParseL3(this.L2_TagVLan1.Generic, tpid);
        }

        bool ParseL2_TagVLan2(PacketPin<EthernetHeader> prevHeader, EthernetProtocolId thisTpid)
        {
            PacketPin<TagVLanHeader> tagVLan = prevHeader.GetNextHeader<TagVLanHeader>();
            if (tagVLan.IsEmpty)
            {
                SetError("Insufficient header data");
                return false;
            }

            this.L2_TagVLan2 = new L2_TagVLan(tagVLan, thisTpid);

            EthernetProtocolId tpid = tagVLan.RefValueRead.Protocol._Endian16();

            if (tpid == EthernetProtocolId.TagVlan)
                return ParseL2_TagVLan3(this.L2.Ethernet, tpid);
            else
                return ParseL3(this.L2_TagVLan2.Generic, tpid);
        }

        bool ParseL2_TagVLan3(PacketPin<EthernetHeader> prevHeader, EthernetProtocolId thisTpid)
        {
            PacketPin<TagVLanHeader> tagVLan = prevHeader.GetNextHeader<TagVLanHeader>();
            if (tagVLan.IsEmpty)
            {
                SetError("Insufficient header data");
                return false;
            }

            this.L2_TagVLan3 = new L2_TagVLan(tagVLan, thisTpid);

            EthernetProtocolId tpid = tagVLan.RefValueRead.Protocol._Endian16();

            if (tpid == EthernetProtocolId.TagVlan)
            {
                SetError("Too many tagged VLAN headers stacking");
                return false;
            }
            else
            {
                return ParseL3(this.L2_TagVLan3.Generic, tpid);
            }
        }

        bool ParseL3(PacketPin<GenericHeader> prevHeader, EthernetProtocolId tpid)
        {
            switch (tpid)
            {
                case EthernetProtocolId.IPv4:
                    return ParseL3_IPv4(prevHeader);

                case EthernetProtocolId.PPPoE_Session:
                    return ParseL3_PPPoESession(prevHeader);
            }

            return true;
        }

        bool ParseL3_PPPoESession(PacketPin<GenericHeader> prevHeader)
        {
            PacketPin<PPPoESessionHeader> pppoe = prevHeader.GetNextHeader<PPPoESessionHeader>();
            if (pppoe.IsEmpty)
            {
                SetError("Insufficient header data");
                return false;
            }

            ref readonly PPPoESessionHeader data = ref pppoe.RefValueRead;

            if (data.Version != 1)
            {
                SetError($"Invalid version: {data.Version}");
                return false;
            }

            if (data.Type != 1)
            {
                SetError($"Invalid type: {data.Type}");
                return false;
            }

            if (data.Code != PPPoECode.Data)
            {
                SetError($"Invalid code: {data.Code}");
                return false;
            }

            int payloadSize = data.PayloadLength._Endian16();
            if (payloadSize < sizeof(PPPProtocolId))
            {
                SetError($"PayloadLength < {sizeof(PPPProtocolId)}");
                return false;
            }
            payloadSize -= sizeof(PPPProtocolId);

            this.L3 = new L3(pppoe);

            ParsePPP_AsOverlay(this.L3.Generic, data.PPPProtocolId._Endian16(), payloadSize);

            return true;
        }

        void ParsePPP_AsOverlay(PacketPin<GenericHeader> prevHeader, PPPProtocolId pppProtocolId, int size)
        {
            EthernetProtocolId etherProtocolId = pppProtocolId.ConvertPPPToEthernetProtocolId();

            PacketPin<GenericHeader> innerPacket = prevHeader.GetNextHeader<GenericHeader>(size);

            PacketParsed innerPacketParsed = new PacketParsed(this.Packet, innerPacket.Pin, this.ParseOption, innerPacket.HeaderSize, PacketParseMode.Layer3, etherProtocolId);

            this.InnerPacket = innerPacketParsed;
        }

        bool ParseL3_IPv4(PacketPin<GenericHeader> prevHeader)
        {
            PacketPin<IPv4Header> ipv4 = prevHeader.GetNextHeader<IPv4Header>();
            if (ipv4.IsEmpty)
            {
                SetError("Insufficient header data");
                return false;
            }

            ref readonly IPv4Header data = ref ipv4.RefValueRead;

            if (data.Version != 4)
            {
                SetError($"Invalid version: {data.Version}");
                return false;
            }

            int headerLen = data.HeaderLen * 4;
            if (headerLen < Util.SizeOfStruct<IPv4Header>())
            {
                SetError($"Invalid HeaderLen: {headerLen}");
                return false;
            }

            int totalLen = data.TotalLength._Endian16();
            if (totalLen < headerLen)
            {
                SetError($"Invalid TotalLength: {totalLen}");
                return false;
            }

            PacketPin<IPv4Header> ipv4full = prevHeader.GetNextHeader<IPv4Header>(headerLen, totalLen);
            if (ipv4full.IsEmpty)
            {
                SetError($"Insufficient header data. HeaderLen: {headerLen}");
                return false;
            }

            this.L3 = new L3(ipv4full);
            this.Info.L3_SrcIPv4 = data.SrcIP;
            this.Info.L3_DestIPv4 = data.DstIP;

            switch (ipv4full.RefValueRead.Protocol)
            {
                case IPProtocolNumber.TCP:
                    return ParseL4_TCP(this.L3.Generic);

                case IPProtocolNumber.UDP:
                    return ParseL4_UDP(this.L3.Generic);
            }

            return true;
        }

        bool ParseL4_UDP(PacketPin<GenericHeader> prevHeader)
        {
            PacketPin<UDPHeader> udp = prevHeader.GetNextHeader<UDPHeader>();
            if (udp.IsEmpty)
            {
                SetError("Insufficient header data");
                return false;
            }

            ref readonly UDPHeader data = ref udp.RefValueRead;

            this.L4 = new L4(udp);

            this.Info.L4_SrcPort = data.SrcPort._Endian16();
            this.Info.L4_DestPort = data.DstPort._Endian16();

            PacketPin<GenericHeader> payload = udp.GetNextHeader<GenericHeader>(size: udp.PayloadSize);

            return ParseL7_UDP(payload, in data);
        }

        bool ParseL4_TCP(PacketPin<GenericHeader> prevHeader)
        {
            PacketPin<TCPHeader> tcp = prevHeader.GetNextHeader<TCPHeader>();
            if (tcp.IsEmpty)
            {
                SetError("Insufficient header data");
                return false;
            }

            ref readonly TCPHeader data = ref tcp.RefValueRead;

            int headerLen = data.HeaderSize * 4;
            if (headerLen < Util.SizeOfStruct<TCPHeader>())
            {
                SetError($"Invalid HeaderLen: {headerLen}");
                return false;
            }

            PacketPin<TCPHeader> tcpfull = prevHeader.GetNextHeader<TCPHeader>(headerLen);
            if (tcpfull.IsEmpty)
            {
                SetError($"Insufficient header data. HeaderLen: {headerLen}");
                return false;
            }

            this.L4 = new L4(tcpfull);

            this.Info.L4_SrcPort = data.SrcPort._Endian16();
            this.Info.L4_DestPort = data.DstPort._Endian16();

            PacketPin<GenericHeader> payload = tcpfull.GetNextHeader<GenericHeader>(size: tcpfull.PayloadSize);

            this.L7 = new L7(payload, L7Type.GenericTCP);

            return true;
        }

        bool ParseL7_UDP(PacketPin<GenericHeader> payload, in UDPHeader udpHeader)
        {
            if (this.Info.L4_SrcPort == (ushort)TCPWellknownPorts.L2TP || this.Info.L4_DestPort == (ushort)TCPWellknownPorts.L2TP)
            {
                // L2TP
                if (ParseL7_L2TP(payload, udpHeader))
                {
                    return true;
                }
            }

            // Generic
            this.L7 = new L7(payload, L7Type.GenericUDP);
            return true;
        }

        bool ParseL7_L2TP(PacketPin<GenericHeader> payload, in UDPHeader udpHeader)
        {
            ReadOnlySpanBuffer<byte> buf = payload.MemoryRead.Span;

            L2TPPacketParsed parsed = new L2TPPacketParsed();

            L2TPPacketFlag flags = (L2TPPacketFlag)buf.ReadUInt8();
            byte version = buf.ReadUInt8();

            if (version != 2)
            {
                return false;
            }

            parsed.Version = version;
            parsed.Flag = flags;

            if (flags.Bit(L2TPPacketFlag.Length))
            {
                parsed.Length = buf.ReadUInt16();

                if (parsed.Length > buf.Length || parsed.Length <= buf.CurrentPosition)
                {
                    return false;
                }

                buf = buf.Slice(0, parsed.Length);
            }

            parsed.TunnelId = buf.ReadUInt16();

            parsed.SessionId = buf.ReadUInt16();

            if (flags.Bit(L2TPPacketFlag.Sequence))
            {
                parsed.Ns = buf.ReadUInt16();
                parsed.Nr = buf.ReadUInt16();
            }

            if (flags.Bit(L2TPPacketFlag.Offset))
            {
                parsed.OffsetSize = buf.ReadUInt16();

                buf.Read(parsed.OffsetSize);
            }

            parsed.Data = payload.GetInnerHeader<GenericHeader>(buf.CurrentPosition);

            this.L7 = new L7(payload, parsed);

            if (parsed.IsControlMessage)
            {
                return true;
            }
            else
            {
                return ParseL7_L2TP_PPPData(payload, parsed);
            }
        }

        bool ParseL7_L2TP_PPPData(PacketPin<GenericHeader> payload, L2TPPacketParsed l2tp)
        {
            PacketPin<PPPDataHeader> pppHeader = l2tp.Data.GetInnerHeader<PPPDataHeader>(0);
            ref readonly PPPDataHeader h = ref pppHeader.RefValueRead;

            if (h.Address != 0xff) return false;
            if (h.Control != 0x03) return false;

            ParsePPP_AsOverlay(pppHeader.ToGenericHeader(), h.Protocol._Endian16(), pppHeader.PayloadSize);

            return true;
        }
    }
}
