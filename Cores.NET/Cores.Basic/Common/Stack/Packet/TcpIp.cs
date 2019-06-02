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
using System.Runtime.InteropServices;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    unsafe struct GenericHeader
    {
    }

    [Flags]
    enum TCPWellknownPorts : ushort
    {
        L2TP = 1701,
    }

    [Flags]
    enum PPPProtocolId : ushort
    {
        Unknown = 0,
        LCP = 0xc021,
        PAP = 0xc023,
        IPCP = 0x8021,
        CHAP = 0xc223,
        IPv4 = 0x0021,
        IPv6 = 0x0057,
    }

    [Flags]
    enum EthernetProtocolId : ushort
    {
        Unknown = 0,
        ARPv4 = 0x0806,
        IPv4 = 0x0800,
        IPv6 = 0x86dd,
        VLan = 0x8100,
        PPPoE_Discovery = 0x8863,
        PPPoE_Session = 0x8864,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    unsafe struct EthernetHeader
    {
        public fixed byte DestAddress[6];
        public fixed byte SrcAddress[6];
        public EthernetProtocolId Protocol;
    }

    [Flags]
    enum PPPoECode : byte
    {
        Data = 0x00,
        ActiveDiscoveryInitiation = 0x09,
        ActiveDiscoveryOffer = 0x07,
        ActiveDiscoveryRequest = 0x19,
        ActiveDiscoverySessionConfirmation = 0x65,
        ActiveDiscoveryTerminate = 0xa7,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct PPPoESessionHeader
    {
        public byte VersionAndType;
        public PPPoECode Code;
        public ushort SessionId;
        public ushort PayloadLength;
        public PPPProtocolId PPPProtocolId;

        public byte Version
        {
            get => (byte)(this.VersionAndType._GetBitsUInt8(0xf0) >> 4);
            set => this.VersionAndType._UpdateBitsUInt8(0xf0, (byte)(value << 4));
        }

        public byte Type
        {
            get => this.VersionAndType._GetBitsUInt8(0x0f);
            set => this.VersionAndType._UpdateBitsUInt8(0x0f, (byte)value);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct VLanHeader
    {
        public ushort TagAndVLanId;
        public EthernetProtocolId Protocol;

        public ushort VLanId
        {
            get => this.TagAndVLanId._GetBitsUInt16Endian(0xfff);
            set => this.TagAndVLanId._UpdateBitsUInt16Endian(0xfff, (ushort)value);
        }
    }

    [Flags]
    enum IPProtocolNumber : byte
    {
        Unknown = 0,
        TCP = 0x06,
        UDP = 0x11,
        ESP = 50,
        EtherIP = 97,
        L2TPv3 = 115,
    }

    [Flags]
    enum IPv4Flags : byte
    {
        MoreFragments = 1,
        DontFragment = 2,
        Reserved = 4,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    unsafe struct IPv4Header
    {
        public byte VersionAndHeaderLength;
        public byte TypeOfService;
        public ushort TotalLength;
        public ushort Identification;
        public fixed byte FlagsAndFlagmentOffset[2];
        public byte TimeToLive;
        public IPProtocolNumber Protocol;
        public ushort Checksum;
        public uint SrcIP;
        public uint DstIP;

        public byte Version
        {
            get => (byte)(this.VersionAndHeaderLength >> 4 & 0x0f);
            set => VersionAndHeaderLength |= (byte)(((value) & 0x0f) << 4);
        }

        public byte HeaderLen
        {
            get => (byte)(VersionAndHeaderLength & 0x0f);
            set => VersionAndHeaderLength |= (byte)((value) & 0x0f);
        }

        public IPv4Flags Flags
        {
            get => (IPv4Flags)((FlagsAndFlagmentOffset[0] >> 5) & 0x07);
            set => FlagsAndFlagmentOffset[0] |= (byte)((((byte)(value)) & 0x07) << 5);
        }

        public ushort Offset
        {
            get => (ushort)(((FlagsAndFlagmentOffset[0] & 0x1f) * 256 + (FlagsAndFlagmentOffset[1])));
            set { FlagsAndFlagmentOffset[0] |= (byte)((value) / 256); FlagsAndFlagmentOffset[1] = (byte)((value) % 256); }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct UDPHeader
    {
        public ushort SrcPort;
        public ushort DstPort;
        public ushort PacketLength;
        public ushort Checksum;
    }

    [Flags]
    enum TCPFlags : byte
    {
        None = 0,
        Fin = 1,
        Syn = 2,
        Rst = 4,
        Psh = 8,
        Ack = 16,
        Urg = 32,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct TCPHeader
    {
        public ushort SrcPort;
        public ushort DstPort;
        public uint SeqNumber;
        public uint AckNumber;
        public byte HeaderSizeAndReserved;
        public TCPFlags Flag;
        public ushort WindowSize;
        public ushort Checksum;
        public ushort UrgentPointer;

        public byte HeaderSize
        {
            get => (byte)((this.HeaderSizeAndReserved >> 4) & 0x0f);
            set => this.HeaderSizeAndReserved = (byte)((value & 0x0f) << 4);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct IPv4PseudoHeader
    {
        public uint SrcIP;
        public uint DstIP;
        public byte Reserved;
        public byte Protocol;
        public ushort PacketLength;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct UDPv4PseudoHeader
    {
        public uint SrcIP;
        public uint DstIP;
        public byte Reserved;
        public byte Protocol;
        public ushort PacketLength1;
        public ushort SrcPort;
        public ushort DstPort;
        public ushort PacketLength2;
        public ushort Checksum;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct PPPDataHeader
    {
        public byte Address;
        public byte Control;
        public PPPProtocolId Protocol;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct L2TPHeaderForStdData
    {
        public L2TPPacketFlag Flag;
        public byte ReservedAndVersion;
        public ushort Length;
        public ushort TunnelId;
        public ushort SessionId;
    }

    [Flags]
    enum L2TPPacketType
    {
        Data = 0,
        Control,
    }

    [Flags]
    enum L2TPPacketFlag : byte
    {
        None = 0,
        Priority = 0x01,
        Offset = 0x02,
        Sequence = 0x08,
        Length = 0x40,
        ControlMessage = 0x80,
    }

    class L2TPPacketParsed
    {
        public int Version;
        public L2TPPacketFlag Flag;
        public bool IsZLB;
        public bool IsYamahaV3;
        public int Length;
        public uint TunnelId;
        public uint SessionId;
        public ushort Ns, Nr;
        public int OffsetSize;
        public PacketPin<GenericHeader> Data;

        public bool IsControlMessage => Flag.Bit(L2TPPacketFlag.ControlMessage);
        public bool IsDataMessage => !IsControlMessage;
    }


    static class TcpIpPacketUtil
    {
        public static EthernetProtocolId ConvertPPPToEthernetProtocolId(this PPPProtocolId id)
        {
            switch (id)
            {
                case PPPProtocolId.IPv4:
                    return EthernetProtocolId.IPv4;

                case PPPProtocolId.IPv6:
                    return EthernetProtocolId.IPv6;

                default:
                    return EthernetProtocolId.Unknown;
            }
        }
    }
}

