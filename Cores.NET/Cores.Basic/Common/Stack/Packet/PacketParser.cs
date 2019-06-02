// IPA Cores.NET
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
        public readonly PacketSpan<GenericHeader> Generic;
        [FieldOffset(8)]
        public readonly PacketSpan<EthernetHeader> Ethernet;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public L2(PacketSpan<EthernetHeader> pin)
        {
            this.Ethernet = default;

            this.Type = PacketL2Type.Ethernet;
            this.Generic = pin.ToGenericHeader();
        }
    }

    [StructLayout(LayoutKind.Explicit, Pack = 8)]
    readonly struct L2_VLan
    {
        [FieldOffset(0)]
        public readonly EthernetProtocolId Type;

        [FieldOffset(8)]
        public readonly PacketSpan<GenericHeader> Generic;
        [FieldOffset(8)]
        public readonly PacketSpan<VLanHeader> TagVlan;

        public L2_VLan(PacketSpan<VLanHeader> pin, EthernetProtocolId tpid)
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
        public readonly PacketSpan<GenericHeader> Generic;
        [FieldOffset(8)]
        public readonly PacketSpan<IPv4Header> IPv4;
        [FieldOffset(8)]
        public readonly PacketSpan<PPPoESessionHeader> PPPoE;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public L3(PacketSpan<IPv4Header> pin)
        {
            this.IPv4 = default;
            this.PPPoE = default;

            this.Type = EthernetProtocolId.IPv4;
            this.Generic = pin.ToGenericHeader();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public L3(PacketSpan<PPPoESessionHeader> pin)
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
        public readonly PacketSpan<GenericHeader> Generic;
        [FieldOffset(8)]
        public readonly PacketSpan<TCPHeader> TCP;
        [FieldOffset(8)]
        public readonly PacketSpan<UDPHeader> UDP;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public L4(PacketSpan<TCPHeader> pin)
        {
            this.TCP = default;
            this.UDP = default;

            this.Type = IPProtocolNumber.TCP;
            this.Generic = pin.ToGenericHeader();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public L4(PacketSpan<UDPHeader> pin)
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
        public readonly PacketSpan<GenericHeader> Generic;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public L7(PacketSpan<GenericHeader> pin, L7Type type)
        {
            this.L2TPPacketParsed = null;

            this.Generic = pin;
            this.Type = type;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public L7(PacketSpan<GenericHeader> pin, L2TPPacketParsed l2tpPacketParsed)
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

    unsafe class PacketParsed
    {
        static readonly PacketParseOption DefaultOption = new PacketParseOption();

        public PacketParseOption ParseOption { get; private set; }

        public string ErrorStr { get; private set; }
        public bool IsOk => ErrorStr == null;
        public bool IsError => !IsOk;

        public L2 L2 { get; private set; }
        public L2_VLan L2_VLan1 { get; private set; }
        public L2_VLan L2_VLan2 { get; private set; }
        public L2_VLan L2_VLan3 { get; private set; }

        public L3 L3 { get; private set; }

        public L4 L4 { get; private set; }

        public L7 L7 { get; private set; }

        public PacketInfo Info;

        public PacketParsed InnerPacket { get; private set; }

        public PacketParsed(ref Packet pkt, int startPin = DefaultSize, PacketParseOption options = null, int maxPacketSize = DefaultSize, PacketParseMode mode = PacketParseMode.Layer2, EthernetProtocolId layer3ProtocolId = EthernetProtocolId.Unknown)
        {
            this.ParseOption = options ?? DefaultOption;

            //this.ErrorStr = null;
            //this.L2 = default;
            //this.L2_TagVLan1 = default;
            //this.L2_TagVLan2 = default;
            //this.L2_TagVLan3 = default;
            //this.L3 = default;
            //this.L4 = default;
            //this.L7 = default;
            //this.Info = default;

            //this.InnerPacket = null;

            switch (mode)
            {
                case PacketParseMode.Layer2:
                    PacketSpan<EthernetHeader> ether = pkt.GetHeader<EthernetHeader>(startPin._DefaultSize(pkt.PinHead), maxPacketSize: maxPacketSize._DefaultSize(pkt.Length));
                    ParseL2_Ethernet(ref pkt, ether);
                    break;

                case PacketParseMode.Layer3:
                    PacketSpan<GenericHeader> generic = pkt.GetHeader<GenericHeader>(startPin._DefaultSize(pkt.PinHead), size: 0, maxPacketSize: maxPacketSize._DefaultSize(pkt.Length));
                    ParseL3(ref pkt, generic, layer3ProtocolId);
                    break;
            }
        }

        void SetError(string err, [CallerMemberName] string caller = null)
        {
            caller = caller._NonNullTrim();
            err = err._NonNullTrim();
            this.ErrorStr = $"{caller}: {err}";
        }

        bool ParseL2_Ethernet(ref Packet pkt, PacketSpan<EthernetHeader> ether)
        {
            if (ether.IsEmpty(ref pkt))
            {
                SetError("Insufficient header data");
                return false;
            }

            this.L2 = new L2(ether);

            EthernetProtocolId tpid = ether.GetRefValue(ref pkt).Protocol._Endian16();

            if (tpid == EthernetProtocolId.VLan)
                return ParseL2_VLan1(ref pkt, this.L2.Ethernet, tpid);
            else
                return ParseL3(ref pkt, this.L2.Generic, tpid);
        }

        bool ParseL2_VLan1(ref Packet pkt, PacketSpan<EthernetHeader> prevHeader, EthernetProtocolId thisTpid)
        {
            PacketSpan<VLanHeader> vlan = prevHeader.GetNextHeader<VLanHeader>(ref pkt);
            if (vlan.IsEmpty(ref pkt))
            {
                SetError("Insufficient header data");
                return false;
            }

            this.L2_VLan1 = new L2_VLan(vlan, thisTpid);

            EthernetProtocolId tpid = vlan.GetRefValue(ref pkt).Protocol._Endian16();

            if (tpid == EthernetProtocolId.VLan)
                return ParseL2_VLan2(ref pkt, this.L2.Ethernet, tpid);
            else
                return ParseL3(ref pkt, this.L2_VLan1.Generic, tpid);
        }

        bool ParseL2_VLan2(ref Packet pkt, PacketSpan<EthernetHeader> prevHeader, EthernetProtocolId thisTpid)
        {
            PacketSpan<VLanHeader> vlan = prevHeader.GetNextHeader<VLanHeader>(ref pkt);
            if (vlan.IsEmpty(ref pkt))
            {
                SetError("Insufficient header data");
                return false;
            }

            this.L2_VLan2 = new L2_VLan(vlan, thisTpid);

            EthernetProtocolId tpid = vlan.GetRefValue(ref pkt).Protocol._Endian16();

            if (tpid == EthernetProtocolId.VLan)
                return ParseL2_VLan3(ref pkt, this.L2.Ethernet, tpid);
            else
                return ParseL3(ref pkt, this.L2_VLan2.Generic, tpid);
        }

        bool ParseL2_VLan3(ref Packet pkt, PacketSpan<EthernetHeader> prevHeader, EthernetProtocolId thisTpid)
        {
            PacketSpan<VLanHeader> vlan = prevHeader.GetNextHeader<VLanHeader>(ref pkt);
            if (vlan.IsEmpty(ref pkt))
            {
                SetError("Insufficient header data");
                return false;
            }

            this.L2_VLan3 = new L2_VLan(vlan, thisTpid);

            EthernetProtocolId tpid = vlan.GetRefValue(ref pkt).Protocol._Endian16();

            if (tpid == EthernetProtocolId.VLan)
            {
                SetError("Too many tagged VLAN headers stacking");
                return false;
            }
            else
            {
                return ParseL3(ref pkt, this.L2_VLan3.Generic, tpid);
            }
        }

        bool ParseL3(ref Packet pkt, PacketSpan<GenericHeader> prevHeader, EthernetProtocolId tpid)
        {
            switch (tpid)
            {
                case EthernetProtocolId.IPv4:
                    return ParseL3_IPv4(ref pkt, prevHeader);

                case EthernetProtocolId.PPPoE_Session:
                    return ParseL3_PPPoESession(ref pkt, prevHeader);
            }

            return true;
        }

        bool ParseL3_PPPoESession(ref Packet pkt, PacketSpan<GenericHeader> prevHeader)
        {
            PacketSpan<PPPoESessionHeader> pppoe = prevHeader.GetNextHeader<PPPoESessionHeader>(ref pkt);
            if (pppoe.IsEmpty(ref pkt))
            {
                SetError("Insufficient header data");
                return false;
            }

            ref PPPoESessionHeader data = ref pppoe.GetRefValue(ref pkt);

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

            ParsePPP_AsOverlay(ref pkt, this.L3.Generic, data.PPPProtocolId._Endian16(), payloadSize);

            return true;
        }

        void ParsePPP_AsOverlay(ref Packet pkt, PacketSpan<GenericHeader> prevHeader, PPPProtocolId pppProtocolId, int size)
        {
            EthernetProtocolId etherProtocolId = pppProtocolId.ConvertPPPToEthernetProtocolId();

            PacketSpan<GenericHeader> innerPacket = prevHeader.GetNextHeader<GenericHeader>(ref pkt, size);

            PacketParsed innerPacketParsed = new PacketParsed(ref pkt, innerPacket.Pin, this.ParseOption, innerPacket.HeaderSize, PacketParseMode.Layer3, etherProtocolId);

            this.InnerPacket = innerPacketParsed;
        }

        bool ParseL3_IPv4(ref Packet pkt, PacketSpan<GenericHeader> prevHeader)
        {
            PacketSpan<IPv4Header> ipv4 = prevHeader.GetNextHeader<IPv4Header>(ref pkt);
            if (ipv4.IsEmpty(ref pkt))
            {
                SetError("Insufficient header data");
                return false;
            }

            ref IPv4Header data = ref ipv4.GetRefValue(ref pkt);

            if (data.Version != 4)
            {
                SetError($"Invalid version: {data.Version}");
                return false;
            }

            int headerLen = data.HeaderLen * 4;
            if (headerLen < sizeof(IPv4Header))
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

            PacketSpan<IPv4Header> ipv4full = prevHeader.GetNextHeader<IPv4Header>(ref pkt, headerLen, totalLen);
            if (ipv4full.IsEmpty(ref pkt))
            {
                SetError($"Insufficient header data. HeaderLen: {headerLen}");
                return false;
            }

            this.L3 = new L3(ipv4full);
            this.Info.L3_SrcIPv4 = data.SrcIP;
            this.Info.L3_DestIPv4 = data.DstIP;

            switch (ipv4full.GetRefValue(ref pkt).Protocol)
            {
                case IPProtocolNumber.TCP:
                    return ParseL4_TCP(ref pkt, this.L3.Generic);

                case IPProtocolNumber.UDP:
                    return ParseL4_UDP(ref pkt, this.L3.Generic);
            }

            return true;
        }

        bool ParseL4_UDP(ref Packet pkt, PacketSpan<GenericHeader> prevHeader)
        {
            PacketSpan<UDPHeader> udp = prevHeader.GetNextHeader<UDPHeader>(ref pkt);
            if (udp.IsEmpty(ref pkt))
            {
                SetError("Insufficient header data");
                return false;
            }

            ref UDPHeader data = ref udp.GetRefValue(ref pkt);

            this.L4 = new L4(udp);

            this.Info.L4_SrcPort = data.SrcPort._Endian16();
            this.Info.L4_DestPort = data.DstPort._Endian16();

            PacketSpan<GenericHeader> payload = udp.GetNextHeader<GenericHeader>(ref pkt, size: udp.GetPayloadSize(ref pkt));

            return ParseL7_UDP(ref pkt, payload, in data);
        }

        bool ParseL4_TCP(ref Packet pkt, PacketSpan<GenericHeader> prevHeader)
        {
            PacketSpan<TCPHeader> tcp = prevHeader.GetNextHeader<TCPHeader>(ref pkt);
            if (tcp.IsEmpty(ref pkt))
            {
                SetError("Insufficient header data");
                return false;
            }

            ref TCPHeader data = ref tcp.GetRefValue(ref pkt);

            int headerLen = data.HeaderSize * 4;
            if (headerLen < sizeof(TCPHeader))
            {
                SetError($"Invalid HeaderLen: {headerLen}");
                return false;
            }

            PacketSpan<TCPHeader> tcpfull = prevHeader.GetNextHeader<TCPHeader>(ref pkt, headerLen);
            if (tcpfull.IsEmpty(ref pkt))
            {
                SetError($"Insufficient header data. HeaderLen: {headerLen}");
                return false;
            }

            this.L4 = new L4(tcpfull);

            this.Info.L4_SrcPort = data.SrcPort._Endian16();
            this.Info.L4_DestPort = data.DstPort._Endian16();

            PacketSpan<GenericHeader> payload = tcpfull.GetNextHeader<GenericHeader>(ref pkt, size: tcpfull.GetPayloadSize(ref pkt));

            this.L7 = new L7(payload, L7Type.GenericTCP);

            return true;
        }

        bool ParseL7_UDP(ref Packet pkt, PacketSpan<GenericHeader> payload, in UDPHeader udpHeader)
        {
            if (this.Info.L4_SrcPort == (ushort)TCPWellknownPorts.L2TP || this.Info.L4_DestPort == (ushort)TCPWellknownPorts.L2TP)
            {
                // L2TP
                if (ParseL7_L2TP(ref pkt, payload, udpHeader))
                {
                    return true;
                }
            }

            // Generic
            this.L7 = new L7(payload, L7Type.GenericUDP);
            return true;
        }

        bool ParseL7_L2TP(ref Packet pkt, PacketSpan<GenericHeader> payload, in UDPHeader udpHeader)
        {
            Span<byte> span = payload.GetSpan(ref pkt);

            if (span.Length < 6) return false;

            ref L2TPHeaderForStdData std = ref span._AsStruct<L2TPHeaderForStdData>();

            L2TPPacketFlag flags = std.Flag;
            byte version = (byte)(std.ReservedAndVersion & 0x0F);
            if (version != 2)
            {
                return false;
            }

            L2TPPacketParsed parsed = new L2TPPacketParsed();

            parsed.Version = version;
            parsed.Flag = flags;

            if (flags.Bit(L2TPPacketFlag.Length) && flags.Bit(L2TPPacketFlag.ControlMessage) == false && flags.Bit(L2TPPacketFlag.Offset) == false &&
                flags.Bit(L2TPPacketFlag.Sequence) == false)
            {
                // Suitable for standard data packet: parse with the structure for faster processing
                parsed.Length = std.Length._Endian16();

                if (parsed.Length > span.Length || parsed.Length < sizeof(L2TPHeaderForStdData))
                {
                    return false;
                }

                parsed.TunnelId = std.TunnelId._Endian16();

                parsed.SessionId = std.SessionId._Endian16();

                parsed.Data = payload.GetInnerHeader<GenericHeader>(ref pkt, sizeof(L2TPHeaderForStdData), parsed.Length - sizeof(L2TPHeaderForStdData));
            }
            else
            {
                // Other packets: parse normally
                SpanBuffer<byte> buf = span;

                buf.Walk(2);

                if (flags.Bit(L2TPPacketFlag.Length))
                {
                    parsed.Length = buf.ReadUInt16();

                    if (parsed.Length > buf.Length || parsed.Length < buf.CurrentPosition)
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

                parsed.Data = payload.GetInnerHeader<GenericHeader>(ref pkt, buf.CurrentPosition, buf.Length - buf.CurrentPosition);
            }

            this.L7 = new L7(payload, parsed);

            if (parsed.IsControlMessage)
            {
                return true;
            }
            else
            {
                return ParseL7_L2TP_PPPData(ref pkt, payload, parsed);
            }
        }

        bool ParseL7_L2TP_PPPData(ref Packet pkt, PacketSpan<GenericHeader> payload, L2TPPacketParsed l2tp)
        {
            PacketSpan<PPPDataHeader> pppHeader = l2tp.Data.GetInnerHeader<PPPDataHeader>(ref pkt, 0);
            ref PPPDataHeader h = ref pppHeader.GetRefValue(ref pkt);

            if (h.Address != 0xff) return false;
            if (h.Control != 0x03) return false;

            ParsePPP_AsOverlay(ref pkt, pppHeader.ToGenericHeader(), h.Protocol._Endian16(), pppHeader.GetPayloadSize(ref pkt));

            return true;
        }
    }
}
