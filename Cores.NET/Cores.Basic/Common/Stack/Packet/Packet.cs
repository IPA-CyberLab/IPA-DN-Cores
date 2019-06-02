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
    enum PacketInsertMode
    {
        MoveHead = 0,
        MoveTail,
    }

    ref struct PacketBuilder
    {
        ElasticSpan<byte> Elastic;
        public int PinHead { get; private set; }
        public int PinTail { get; private set; }
        public int Length { get { int ret = checked(PinTail - PinHead); Debug.Assert(ret >= 0); return ret; } }

        public PacketBuilder(Span<byte> initialContents, bool copyInitialContents = true)
        {
            this.Elastic = new ElasticSpan<byte>(initialContents, copyInitialContents);
            this.PinHead = 0;
            this.PinTail = this.Elastic.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            checked
            {
                Elastic = new ElasticSpan<byte>();
                PinTail = PinHead;
            }
        }

        public Span<byte> Span
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => this.Elastic.Span;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsSafeToRead(int pin, int size)
        {
            if ((pin + size) > PinTail) return false;
            if (pin < PinHead) return false;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<byte> GetContiguousSpan(int pin, int size)
        {
            return this.Elastic.Span.Slice(pin - PinHead, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe ref T AsStruct<T>(int pin, int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));

            Span<byte> data = this.GetContiguousSpan(pin, size);
            fixed (void* ptr = &data[0])
                return ref Unsafe.AsRef<T>(ptr);
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public PacketPin<T> GetHeader<T>(int pin, int size = DefaultSize, int maxPacketSize = int.MaxValue) where T : unmanaged
        //    => new PacketPin<T>(this, pin, size, maxPacketSize);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe ref T PrependHeader<T>(int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));

            ref byte b = ref this.Elastic.Prepend(size);
            this.PinHead -= size;

            return ref Unsafe.As<byte, T>(ref b);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public unsafe int PrependHeader<T>(in T data, int size = DefaultSize) where T : unmanaged
        {
            fixed (T* ptr = &data)
                return PrependHeader(ptr, size);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public unsafe int PrependHeader<T>(T* data, int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));

            this.Elastic.Prepend((byte*)data, sizeof(T), size);

            this.PinHead -= size;

            return this.PinHead;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe int PrependHeader<T>(ReadOnlySpan<byte> data, int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));

            this.Elastic.Prepend(data, size);
            this.PinHead -= size;

            return this.PinHead;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe ref T AppendHeader<T>(int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));

            int oldPinTail = this.PinTail;

            ref byte b = ref this.Elastic.Append(size);
            this.PinTail += size;

            return ref Unsafe.As<byte, T>(ref b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe int AppendHeader<T>(in T data, int size = DefaultSize) where T : unmanaged
        {
            fixed (T* ptr = &data)
                return AppendHeader(ptr, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe int AppendHeader<T>(T* ptr, int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));

            int oldPinTail = this.PinTail;

            this.Elastic.Append((byte*)ptr, sizeof(T), size);

            this.PinTail += size;

            return oldPinTail;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe int AppendHeader<T>(ReadOnlySpan<byte> data, int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));

            int oldPinTail = this.PinTail;

            this.Elastic.Append(data, size);
            this.PinTail += size;

            return oldPinTail;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe ref T InsertHeader<T>(PacketInsertMode mode, int pos, int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));

            ref byte b = ref this.Elastic.Insert(size, pos - this.PinHead);

            if (mode == PacketInsertMode.MoveHead)
            {
                this.PinHead -= size;
                pos -= size;
            }
            else
            {
                this.PinTail += size;
            }

            return ref Unsafe.As<byte, T>(ref b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe int InsertHeader<T>(PacketInsertMode mode, int pos, in T data, int size = DefaultSize) where T : unmanaged
        {
            fixed (T* ptr = &data)
                return InsertHeader(mode, pos, ptr, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe int InsertHeader<T>(PacketInsertMode mode, int pos, T* ptr, int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));

            this.Elastic.Insert((byte*)ptr, sizeof(T), pos - this.PinHead, size);

            if (mode == PacketInsertMode.MoveHead)
            {
                this.PinHead -= size;
                pos -= size;
            }
            else
            {
                this.PinTail += size;
            }

            return pos;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe int InsertHeader<T>(PacketInsertMode mode, int pos, ReadOnlySpan<byte> data, int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));

            this.Elastic.Insert(data, pos - this.PinHead, size);

            if (mode == PacketInsertMode.MoveHead)
            {
                this.PinHead -= size;
                pos -= size;
            }
            else
            {
                this.PinTail += size;
            }

            return pos;
        }
    }

    class Packet
    {
        ElasticMemory<byte> Elastic;
        public int PinHead { get; private set; } = 0;
        public int PinTail { get; private set; } = 0;
        public int Length { get { int ret = checked(PinTail - PinHead); Debug.Assert(ret >= 0); return ret; } }

        public Packet() { }

        public Packet(Memory<byte> initialContents, bool copyInitialContents = true)
        {
            this.Elastic = new ElasticMemory<byte>(initialContents, copyInitialContents);
            this.PinHead = 0;
            this.PinTail = this.Elastic.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            checked
            {
                Elastic = new ElasticMemory<byte>();
                PinTail = PinHead;
            }
        }

        public Memory<byte> Memory
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => this.Elastic.Memory;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsSafeToRead(int pin, int size)
        {
            if ((pin + size) > PinTail) return false;
            if (pin < PinHead) return false;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<byte> GetContiguousSpan(int pin, int size)
        {
            return this.Elastic.Span.Slice(pin - PinHead, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Memory<byte> GetContiguousMemory(int pin, int size)
        {
            return this.Elastic.Memory.Slice(pin - PinHead, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe ref T AsStruct<T>(int pin, int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));

            Span<byte> data = this.GetContiguousSpan(pin, size);
            fixed (void* ptr = &data[0])
                return ref Unsafe.AsRef<T>(ptr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PacketPin<T> GetHeader<T>(int pin, int size = DefaultSize, int maxPacketSize = int.MaxValue) where T : unmanaged
            => new PacketPin<T>(this, pin, size, maxPacketSize);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe PacketPin<T> PrependHeader<T>(int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));

            this.Elastic.Prepend(size);
            this.PinHead -= size;

            return new PacketPin<T>(this, this.PinHead, size);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public unsafe PacketPin<T> PrependHeader<T>(in T data, int size = DefaultSize) where T : unmanaged
        {
            fixed (T* ptr = &data)
                return PrependHeader(ptr, size);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public unsafe PacketPin<T> PrependHeader<T>(T* data, int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));
            
            this.Elastic.Prepend((byte*)data, sizeof(T), size);

            this.PinHead -= size;

            return new PacketPin<T>(this, this.PinHead, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe PacketPin<T> PrependHeader<T>(ReadOnlySpan<byte> data, int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));

            this.Elastic.Prepend(data, size);
            this.PinHead -= size;

            return new PacketPin<T>(this, this.PinHead, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe PacketPin<T> AppendHeader<T>(int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));

            int oldPinTail = this.PinTail;

            this.Elastic.Append(size);
            this.PinTail += size;

            return new PacketPin<T>(this, oldPinTail, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe PacketPin<T> AppendHeader<T>(in T data, int size = DefaultSize) where T : unmanaged
        {
            fixed (T* ptr = &data)
                return AppendHeader(ptr, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe PacketPin<T> AppendHeader<T>(T* ptr, int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));

            int oldPinTail = this.PinTail;

            this.Elastic.Append((byte*)ptr, sizeof(T), size);

            this.PinTail += size;

            return new PacketPin<T>(this, oldPinTail, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe PacketPin<T> AppendHeader<T>(ReadOnlySpan<byte> data, int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));

            int oldPinTail = this.PinTail;

            this.Elastic.Append(data, size);
            this.PinTail += size;

            return new PacketPin<T>(this, oldPinTail, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe PacketPin<T> InsertHeader<T>(PacketInsertMode mode, int pos, int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));

            this.Elastic.Insert(size, pos - this.PinHead);

            if (mode == PacketInsertMode.MoveHead)
            {
                this.PinHead -= size;
                pos -= size;
            }
            else
            {
                this.PinTail += size;
            }

            return new PacketPin<T>(this, pos, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe PacketPin<T> InsertHeader<T>(PacketInsertMode mode, int pos, in T data, int size = DefaultSize) where T : unmanaged
        {
            fixed (T* ptr = &data)
                return InsertHeader(mode, pos, ptr, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe PacketPin<T> InsertHeader<T>(PacketInsertMode mode, int pos, T *ptr, int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));

            this.Elastic.Insert((byte *)ptr, sizeof(T), pos - this.PinHead, size);

            if (mode == PacketInsertMode.MoveHead)
            {
                this.PinHead -= size;
                pos -= size;
            }
            else
            {
                this.PinTail += size;
            }

            return new PacketPin<T>(this, pos, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe PacketPin<T> InsertHeader<T>(PacketInsertMode mode, int pos, ReadOnlySpan<byte> data, int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));

            this.Elastic.Insert(data, pos - this.PinHead, size);

            if (mode == PacketInsertMode.MoveHead)
            {
                this.PinHead -= size;
                pos -= size;
            }
            else
            {
                this.PinTail += size;
            }

            return new PacketPin<T>(this, pos, size);
        }
    }

    readonly unsafe struct PacketPin<T> where T : unmanaged
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsThisEmpty() => IsEmpty;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe PacketPin(Packet packet, int pin, int headerSize = DefaultSize, int maxPacketSize = int.MaxValue)
        {
            this.Packet = packet;
            this.Pin = pin;
            this.HeaderSize = headerSize._DefaultSize(sizeof(T));
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

        public unsafe ref T RefValue
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref Packet.AsStruct<T>(this.Pin, this.HeaderSize);
        }

        public unsafe T _ValueDebug
        {
            get => RefValue;
        }

        public Span<byte> Span
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Packet.GetContiguousSpan(this.Pin, this.HeaderSize);
        }

        public Memory<byte> Memory
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Packet.GetContiguousMemory(this.Pin, this.HeaderSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PacketPin<TNext> GetNextHeader<TNext>(int size = DefaultSize, int maxPacketSize = int.MaxValue) where TNext : unmanaged
        {
            return Packet.GetHeader<TNext>(this.Pin + this.HeaderSize, size, Math.Min(this.PayloadSize, maxPacketSize));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PacketPin<TNext> GetInnerHeader<TNext>(int offset, int size = DefaultSize, int maxPacketSize = int.MaxValue) where TNext : unmanaged
        {
            if (offset > TotalPacketSize) throw new ArgumentOutOfRangeException("offset > TotalPacketSize");
            return Packet.GetHeader<TNext>(this.Pin + offset, size, Math.Min(TotalPacketSize - offset, maxPacketSize));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PacketPin<TNext> PrependHeader<TNext>(ReadOnlySpan<byte> data, int size = DefaultSize) where TNext : unmanaged
            => this.Packet.InsertHeader<TNext>(PacketInsertMode.MoveHead, this.Pin, data, size);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PacketPin<TNext> PrependHeader<TNext>(in TNext data, int size = DefaultSize) where TNext : unmanaged
            => this.Packet.InsertHeader<TNext>(PacketInsertMode.MoveHead, this.Pin, data, size);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PacketPin<TNext> PrependHeader<TNext>(TNext *ptr, int size = DefaultSize) where TNext : unmanaged
            => this.Packet.InsertHeader<TNext>(PacketInsertMode.MoveHead, this.Pin, ptr, size);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PacketPin<TNext> PrependHeader<TNext>(int size = DefaultSize) where TNext : unmanaged
            => this.Packet.InsertHeader<TNext>(PacketInsertMode.MoveHead, this.Pin, size);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PacketPin<TNext> AppendHeader<TNext>(ReadOnlySpan<byte> data, int size = DefaultSize) where TNext : unmanaged
            => this.Packet.InsertHeader<TNext>(PacketInsertMode.MoveTail, this.Pin + this.HeaderSize, data, size);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PacketPin<TNext> AppendHeader<TNext>(in TNext data, int size = DefaultSize) where TNext : unmanaged
            => this.Packet.InsertHeader<TNext>(PacketInsertMode.MoveTail, this.Pin + this.HeaderSize, data, size);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PacketPin<TNext> AppendHeader<TNext>(TNext *ptr, int size = DefaultSize) where TNext : unmanaged
            => this.Packet.InsertHeader<TNext>(PacketInsertMode.MoveTail, this.Pin + this.HeaderSize, ptr, size);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PacketPin<TNext> AppendHeader<TNext>(int size = DefaultSize) where TNext : unmanaged
            => this.Packet.InsertHeader<TNext>(PacketInsertMode.MoveTail, this.Pin + this.HeaderSize, size);
    }

    static class PacketPinHelper
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref readonly PacketPin<GenericHeader> ToGenericHeader<TFrom>(this ref PacketPin<TFrom> src) where TFrom : unmanaged
            => ref Unsafe.As<PacketPin<TFrom>, PacketPin<GenericHeader>>(ref src);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref readonly PacketPin<TTo> ToOtherTypeHeader<TTo>(this ref PacketPin<GenericHeader> src) where TTo : unmanaged
            => ref Unsafe.As<PacketPin<GenericHeader>, PacketPin<TTo>>(ref src);
    }
}

