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
    [Flags]
    enum PCapNgBlockType : uint
    {
        SectionHeader = 0x0A0D0D0A,
        InterfaceDescription = 0x00000001,
        EnhancedPacket = 0x00000006,
    }

    [Flags]
    enum PCapNgLinkType : ushort
    {
        Loopback = 0,
        Ethernet = 1,
    }

    [Flags]
    enum PCapNgOptionCode : ushort
    {
        EndOfOption = 0,
        Comment = 1,
        CustomUtf8 = 2988,
        CustomBinary = 2989,
        CustomUtf8NonCopy = 19372,
        CustomBinaryNonCopy = 19373,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    unsafe struct PCapNgGenericBlock
    {
        public PCapNgBlockType BlockType;
        public int BlockTotalLength;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    unsafe struct PCapNgSectionHeaderBlock
    {
        public PCapNgBlockType BlockType;
        public int BlockTotalLength;
        public uint ByteOrderMagic;
        public ushort MajorVersion;
        public ushort MinorVersion;
        public ulong SectionLength;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    unsafe struct PCapNgInterfaceDescriptionBlock
    {
        public PCapNgBlockType BlockType;
        public int BlockTotalLength;
        public PCapNgLinkType LinkType;
        public ushort Reserved;
        public uint SnapLen;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    unsafe struct PCapNgEnhancedPacketBlock
    {
        public PCapNgBlockType BlockType;
        public int BlockTotalLength;
        public int InterfaceId;
        public uint TimeStampHigh;
        public uint TimeStampLow;
        public int CapturePacketLength;
        public int OriginalPacketLength;
    }

    static class PCapNgUtil
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static ref T _PCapNgEncapsulateHeader<T>(this ref Packet pkt, out PacketSpan<T> retSpan, PCapNgBlockType blockType, ReadOnlySpan<byte> options = default) where T : unmanaged
        {
            int currentSize = pkt.Span.Length;
            int mod32 = currentSize % 4;

            if (mod32 != 0)
            {
                // Padding for data
                pkt.AppendSpan(4 - mod32);
            }

            int optionsSize = options.Length;
            if (optionsSize >= 1)
            {
                // Append the options
                pkt.AppendSpanWithData(options);

                mod32 = optionsSize % 4;
                if (mod32 != 0)
                {
                    // Padding for options
                    pkt.AppendSpan(4 - mod32);
                }
            }

            // Padding for Block Total Length
            int blockTotalLength = (sizeof(T) + pkt.Length + 4);

            // Append the footer
            pkt.AppendSpanWithData<int>(in blockTotalLength);

            // Prepend the header
            ref T ret = ref pkt.PrependSpan<T>(out retSpan);

            ref PCapNgGenericBlock generic = ref Unsafe.As<T, PCapNgGenericBlock>(ref ret);

            generic.BlockType = blockType;
            generic.BlockTotalLength = blockTotalLength;

            return ref ret;
        }

        public static PacketSpan<PCapNgEnhancedPacketBlock> _PCapNgEncapsulateEnhancedPacketBlock(this ref Packet pkt, int interfaceId, ReadOnlySpan<byte> options = default)
        {
            int packetDataSize = pkt.Length;

            ref PCapNgEnhancedPacketBlock header = ref pkt._PCapNgEncapsulateHeader<PCapNgEnhancedPacketBlock>(out PacketSpan<PCapNgEnhancedPacketBlock> retSpan, PCapNgBlockType.EnhancedPacket, options);

            header.InterfaceId = interfaceId;

            header.CapturePacketLength = packetDataSize;
            header.OriginalPacketLength = packetDataSize;

            return retSpan;
        }

        public static SpanBuffer<byte> GeneratePCapNgHeader()
        {
            Packet sectionHeaderPacket = new Packet();
            ref var section = ref sectionHeaderPacket._PCapNgEncapsulateHeader<PCapNgSectionHeaderBlock>(out _, PCapNgBlockType.SectionHeader);

            section.ByteOrderMagic = 0x1A2B3C4D;
            section.MajorVersion = 1;
            section.MinorVersion = 0;
            section.SectionLength = 0xffffffffffffffff;

            Packet interfaceDescriptionPacket = new Packet();
            ref var inf = ref interfaceDescriptionPacket._PCapNgEncapsulateHeader<PCapNgInterfaceDescriptionBlock>(out _, PCapNgBlockType.InterfaceDescription);
            inf.LinkType = PCapNgLinkType.Ethernet;

            SpanBuffer<byte> ret = new SpanBuffer<byte>();
            ret.Write(sectionHeaderPacket.Span);
            ret.Write(interfaceDescriptionPacket.Span);

            return ret;
        }
    }
}

