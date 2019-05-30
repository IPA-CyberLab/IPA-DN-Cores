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
    readonly struct L3
    {
        [FieldOffset(0)]
        public readonly EthernetTpid Type;

        [FieldOffset(8)]
        public readonly PacketPin<GenericHeader> Generic;
        [FieldOffset(8)]
        public readonly PacketPin<IPv4Header> IPv4;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public L3(PacketPin<IPv4Header> pin)
        {
            this.IPv4 = default;

            this.Type = EthernetTpid.IPv4;
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

    class PacketParseOption
    {
    }

    struct PacketParsed
    {
        public Packet Packet { get; private set; }
        public PacketParseOption ParseOption { get; private set; }

        public string ErrorStr { get; private set; }
        public bool IsOk => ErrorStr == null;
        public bool IsError => !IsOk;

        public L2 L2 { get; private set; }
        public L3 L3 { get; private set; }
        public L4 L4 { get; private set; }

        static readonly PacketParseOption DefaultOption = new PacketParseOption();

        public void ParsePacket(Packet packet, int? startPin, PacketParseOption options = null)
        {
            this.Packet = packet;
            this.ParseOption = options ?? DefaultOption;

            this.ErrorStr = null;
            this.L2 = default;
            this.L3 = default;
            this.L4 = default;

            ParseL2_Ethernet(startPin ?? packet.PinHead);
        }

        void SetError(string err, [CallerMemberName] string caller = null)
        {
            caller = caller._NonNullTrim();
            err = err._NonNullTrim();
            this.ErrorStr = $"{caller}: {err}";
        }

        bool ParseL2_Ethernet(int pin)
        {
            PacketPin<EthernetHeader> ether = Packet.GetHeader<EthernetHeader>(pin);

            if (ether.IsEmpty)
            {
                SetError("Insufficient header data");
                return false;
            }

            this.L2 = new L2(ether);

            switch (ether.RefValueRead.Protocol._Endian16())
            {
                case EthernetTpid.IPv4:
                    return ParseL3_IPv4();
            }

            return true;
        }

        public bool ParseL3_IPv4()
        {
            PacketPin<IPv4Header> ipv4 = this.L2.Generic.GetNextHeader<IPv4Header>();
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

            PacketPin<IPv4Header> ipv4full = this.L2.Generic.GetNextHeader<IPv4Header>(headerLen);
            if (ipv4full.IsEmpty)
            {
                SetError($"Insufficient header data. HeaderLen: {headerLen}");
                return false;
            }

            this.L3 = new L3(ipv4full);

            switch (ipv4full.RefValueRead.Protocol)
            {
                case IPProtocolNumber.TCP:
                    return ParseL4_TCP();

                case IPProtocolNumber.UDP:
                    return ParseL4_UDP();
            }

            return true;
        }

        public bool ParseL4_UDP()
        {
            PacketPin<UDPHeader> udp = this.L3.Generic.GetNextHeader<UDPHeader>();
            if (udp.IsEmpty)
            {
                SetError("Insufficient header data");
                return false;
            }

            this.L4 = new L4(udp);

            return true;
        }

        public bool ParseL4_TCP()
        {
            PacketPin<TCPHeader> tcp = this.L3.Generic.GetNextHeader<TCPHeader>();
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

            PacketPin<TCPHeader> tcpfull = this.L3.Generic.GetNextHeader<TCPHeader>(headerLen);
            if (tcpfull.IsEmpty)
            {
                SetError($"Insufficient header data. HeaderLen: {headerLen}");
                return false;
            }

            this.L4 = new L4(tcpfull);

            return true;
        }
    }

    partial class Packet
    {
        public PacketParsed Parsed;

        public void ParsePacket(int? startPin = null, PacketParseOption options = null)
        {
            Parsed.ParsePacket(this, startPin, options);
        }
    }
}
