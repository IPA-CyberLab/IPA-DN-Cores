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

namespace IPA.Cores.Basic
{
    class Packet
    {
        ElasticMemory<byte> Memory;
        public int PinHead { get; private set; } = 0;
        public int PinTail { get; private set; } = 0;
        public int Length { get { int ret = checked(PinTail - PinHead); Debug.Assert(ret >= 0); return ret; } }

        public Packet() { }

        public Packet(Memory<byte> initialContents, bool copyInitialContents = true)
        {
            this.Memory = new ElasticMemory<byte>(initialContents, copyInitialContents);
            this.PinHead = 0;
            this.PinTail = this.Memory.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            checked
            {
                Memory = new ElasticMemory<byte>();
                PinTail = PinHead;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Packet(Memory<byte> memory)
            => new Packet(memory);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Packet(Span<byte> span) => span.ToArray().AsMemory();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Packet(ReadOnlySpan<byte> span) => span.ToArray().AsMemory();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Packet(byte[] data) => data.AsMemory();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsSafeToRead(int pin, int size)
        {
            if ((pin + size) > PinTail) return false;
            if (pin < PinHead) return false;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Memory<byte> GetContiguous(int pin, int size, bool allowPartial = false)
        {
            return this.Memory.Memory.Slice(pin - PinHead, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe ref readonly T AsReadOnlyStruct<T>(int pin, int? size = null) where T : struct
        {
            int size2 = size ?? Unsafe.SizeOf<T>();

            Memory<byte> data = this.GetContiguous(pin, size2, false);
            fixed (void* ptr = &data.Span[0])
                return ref Unsafe.AsRef<T>(ptr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PacketPin<T> GetHeader<T>(int pin, int? size = null, int maxPacketSize = int.MaxValue) where T : struct
            => new PacketPin<T>(this, pin, size, maxPacketSize);
    }

    readonly unsafe struct PacketPin<T> where T : struct
    {
        public Packet Packet { get; }
        public int Pin { get; }
        public int HeaderSize { get; }

        readonly int MaxTotalSize;

        public bool IsEmpty
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (Packet == null) return true;
                if (Packet.IsSafeToRead(this.Pin, this.HeaderSize) == false) return true;
                return false;
            }
        }

        public bool IsFilled
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => !IsEmpty;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsThisEmpty() => IsEmpty;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PacketPin(Packet packet, int pin, int? headerSize = null, int maxPacketSize = int.MaxValue)
        {
            this.Packet = packet;
            this.Pin = pin;
            this.HeaderSize = headerSize ?? Unsafe.SizeOf<T>();
            this.MaxTotalSize = Math.Max(maxPacketSize, 0);
        }

        public int TotalPacketSizeRaw
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Math.Max(Packet.PinTail - this.Pin, 0);
        }

        public int TotalPacketSize
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Math.Min(Math.Max(Packet.PinTail - this.Pin, 0), this.MaxTotalSize);
        }

        public int PayloadSize
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Math.Max(0, TotalPacketSize - this.HeaderSize);
        }

        public unsafe ref readonly T RefValueRead
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref Packet.AsReadOnlyStruct<T>(this.Pin, this.HeaderSize);
        }

        //public unsafe ref T RefValue
        //{
        //    [MethodImpl(MethodImplOptions.AggressiveInlining)]
        //    get => ref Packet.AsStruct<T>(this.Pin, this.HeaderSize);
        //}

        public unsafe T _ValueDebug
        {
            get => RefValueRead;
        }

        public ReadOnlyMemory<byte> MemoryRead
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Packet.GetContiguous(this.Pin, this.HeaderSize, false);
        }

        //public Memory<byte> Memory
        //{
        //    [MethodImpl(MethodImplOptions.AggressiveInlining)]
        //    get => Packet.PutContiguous(this.Pin, this.HeaderSize, true);
        //}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PacketPin<TNext> GetNextHeader<TNext>(int? size = null, int maxPacketSize = int.MaxValue) where TNext : struct
        {
            return Packet.GetHeader<TNext>(this.Pin + this.HeaderSize, size, Math.Min(this.PayloadSize, maxPacketSize));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PacketPin<TNext> GetInnerHeader<TNext>(int offset, int? size = null, int maxPacketSize = int.MaxValue) where TNext : struct
        {
            if (offset > TotalPacketSize) throw new ArgumentOutOfRangeException("offset > TotalPacketSize");
            return Packet.GetHeader<TNext>(this.Pin + offset, size, Math.Min(TotalPacketSize - offset, maxPacketSize));
        }
    }

    static class PacketPinHelper
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref readonly PacketPin<GenericHeader> ToGenericHeader<TFrom>(this ref PacketPin<TFrom> src) where TFrom : struct
            => ref Unsafe.As<PacketPin<TFrom>, PacketPin<GenericHeader>>(ref src);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref readonly PacketPin<TTo> ToOtherTypeHeader<TTo>(this ref PacketPin<GenericHeader> src) where TTo: struct
            => ref Unsafe.As<PacketPin<GenericHeader>, PacketPin<TTo>>(ref src);
    }
}

