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

    ref struct Packet
    {
        ElasticSpan<byte> Elastic;
        public int PinHead { get; private set; }
        public int PinTail { get; private set; }
        public int Length { get { int ret = checked(PinTail - PinHead); Debug.Assert(ret >= 0); return ret; } }

        public Packet(Span<byte> initialContents, bool copyInitialContents = true)
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PacketSpan<T> GetSpan<T>(int pin, int size = DefaultSize, int maxPacketSize = int.MaxValue) where T : unmanaged
            => new PacketSpan<T>(pin, size, maxPacketSize);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe ref T PrependSpan<T>(out PacketSpan<T> retPacketPin, int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));

            ref byte b = ref this.Elastic.Prepend(size);
            this.PinHead -= size;

            retPacketPin = new PacketSpan<T>(this.PinHead, size);

            return ref Unsafe.As<byte, T>(ref b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe ref T PrependSpan<T>(int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));

            ref byte b = ref this.Elastic.Prepend(size);
            this.PinHead -= size;

            return ref Unsafe.As<byte, T>(ref b);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public unsafe PacketSpan<T> PrependSpan<T>(in T data, int size = DefaultSize) where T : unmanaged
        {
            fixed (T* ptr = &data)
                return PrependSpan(ptr, size);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public unsafe PacketSpan<T> PrependSpan<T>(T* data, int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));
            
            this.Elastic.Prepend((byte*)data, sizeof(T), size);

            this.PinHead -= size;

            return new PacketSpan<T>(this.PinHead, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe PacketSpan<T> PrependSpan<T>(ReadOnlySpan<byte> data, int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));

            this.Elastic.Prepend(data, size);
            this.PinHead -= size;

            return new PacketSpan<T>(this.PinHead, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe ref T AppendSpan<T>(out PacketSpan<T> retPacketPin, int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));

            int oldPinTail = this.PinTail;

            ref byte b = ref this.Elastic.Append(size);
            this.PinTail += size;

            retPacketPin = new PacketSpan<T>(oldPinTail, size);

            return ref Unsafe.As<byte, T>(ref b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe ref T AppendSpan<T>(int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));

            int oldPinTail = this.PinTail;

            ref byte b = ref this.Elastic.Append(size);
            this.PinTail += size;

            return ref Unsafe.As<byte, T>(ref b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe PacketSpan<T> AppendSpan<T>(in T data, int size = DefaultSize) where T : unmanaged
        {
            fixed (T* ptr = &data)
                return AppendSpan(ptr, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe PacketSpan<T> AppendSpan<T>(T* ptr, int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));

            int oldPinTail = this.PinTail;

            this.Elastic.Append((byte*)ptr, sizeof(T), size);

            this.PinTail += size;

            return new PacketSpan<T>(oldPinTail, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe PacketSpan<T> AppendSpan<T>(ReadOnlySpan<byte> data, int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));

            int oldPinTail = this.PinTail;

            this.Elastic.Append(data, size);
            this.PinTail += size;

            return new PacketSpan<T>(oldPinTail, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe ref T InsertSpan<T>(out PacketSpan<T> retPacketPin, PacketInsertMode mode, int pos, int size = DefaultSize) where T : unmanaged
        {
            if (pos == this.PinHead && mode == PacketInsertMode.MoveHead)
            {
                return ref PrependSpan<T>(out retPacketPin, size);
            }
            else if (pos == this.PinTail && mode == PacketInsertMode.MoveTail)
            {
                return ref AppendSpan<T>(out retPacketPin, size);
            }

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

            retPacketPin = new PacketSpan<T>(pos, size);

            return ref Unsafe.As<byte, T>(ref b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe PacketSpan<T> InsertSpan<T>(PacketInsertMode mode, int pos, in T data, int size = DefaultSize) where T : unmanaged
        {
            fixed (T* ptr = &data)
                return InsertSpan(mode, pos, ptr, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe PacketSpan<T> InsertSpan<T>(PacketInsertMode mode, int pos, T *ptr, int size = DefaultSize) where T : unmanaged
        {
            if (pos == this.PinHead && mode == PacketInsertMode.MoveHead)
            {
                return PrependSpan<T>(ptr, size);
            }
            else if (pos == this.PinTail && mode == PacketInsertMode.MoveTail)
            {
                return AppendSpan<T>(ptr, size);
            }

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

            return new PacketSpan<T>(pos, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe PacketSpan<T> InsertSpan<T>(PacketInsertMode mode, int pos, ReadOnlySpan<byte> data, int size = DefaultSize) where T : unmanaged
        {
            if (pos == this.PinHead && mode == PacketInsertMode.MoveHead)
            {
                return PrependSpan<T>(data, size);
            }
            else if (pos == this.PinTail && mode == PacketInsertMode.MoveTail)
            {
                return AppendSpan<T>(data, size);
            }

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

            return new PacketSpan<T>(pos, size);
        }
    }

    readonly unsafe struct PacketSpan<T> where T : unmanaged
    {
        public int Pin { get; }
        public int HeaderSize { get; }

        readonly int MaxTotalSize;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe bool IsEmpty(ref Packet pkt)
        {
            if (pkt.IsSafeToRead(this.Pin, this.HeaderSize) == false) return true;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe PacketSpan(int pin, int headerSize = DefaultSize, int maxPacketSize = int.MaxValue)
        {
            this.Pin = pin;
            this.HeaderSize = headerSize._DefaultSize(sizeof(T));
            this.MaxTotalSize = Math.Max(maxPacketSize, 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetTotalPacketSizeRaw(ref Packet pkt)
        {
            return Math.Max(pkt.PinTail - this.Pin, 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetTotalPacketSize(ref Packet pkt)
        {
            return Math.Min(Math.Max(pkt.PinTail - this.Pin, 0), this.MaxTotalSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetPayloadSize(ref Packet pkt)
        {
            return Math.Max(0, GetTotalPacketSize(ref pkt) - this.HeaderSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe ref T GetRefValue(ref Packet pkt)
        {
            return ref pkt.AsStruct<T>(this.Pin);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe ref TOther GetRefValue<TOther>(ref Packet pkt) where TOther: unmanaged
        {
            return ref pkt.AsStruct<TOther>(this.Pin);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe ref TOther GetNextHeaderRefValue<TOther>(ref Packet pkt) where TOther : unmanaged
        {
            return ref pkt.AsStruct<TOther>(this.Pin + this.HeaderSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<byte> GetSpan(ref Packet pkt, int size)
        {
            return pkt.GetContiguousSpan(this.Pin, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<byte> GetSpan(ref Packet pkt)
        {
            return pkt.GetContiguousSpan(this.Pin, this.HeaderSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<byte> GetSpan(ref Packet pkt, int pin, int size)
        {
            return pkt.GetContiguousSpan(pin, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PacketSpan<TNext> GetNextSpan<TNext>(ref Packet pkt, int size = DefaultSize, int maxPacketSize = int.MaxValue) where TNext : unmanaged
        {
            return pkt.GetSpan<TNext>(this.Pin + this.HeaderSize, size, Math.Min(this.GetPayloadSize(ref pkt), maxPacketSize));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PacketSpan<TNext> GetInnerSpan<TNext>(ref Packet pkt, int offset, int size = DefaultSize, int maxPacketSize = int.MaxValue) where TNext : unmanaged
        {
            if (offset > GetTotalPacketSize(ref pkt)) throw new ArgumentOutOfRangeException("offset > TotalPacketSize");
            return pkt.GetSpan<TNext>(this.Pin + offset, size, Math.Min(GetTotalPacketSize(ref pkt) - offset, maxPacketSize));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PacketSpan<TNext> PrependSpan<TNext>(ref Packet pkt, ReadOnlySpan<byte> data, int size = DefaultSize) where TNext : unmanaged
            => pkt.InsertSpan<TNext>(PacketInsertMode.MoveHead, this.Pin, data, size);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PacketSpan<TNext> PrependSpan<TNext>(ref Packet pkt, in TNext data, int size = DefaultSize) where TNext : unmanaged
            => pkt.InsertSpan<TNext>(PacketInsertMode.MoveHead, this.Pin, data, size);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PacketSpan<TNext> PrependSpan<TNext>(ref Packet pkt, TNext* ptr, int size = DefaultSize) where TNext : unmanaged
            => pkt.InsertSpan<TNext>(PacketInsertMode.MoveHead, this.Pin, ptr, size);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref TNext PrependSpan<TNext>(ref Packet pkt, out PacketSpan<TNext> retPacketPin, int size = DefaultSize) where TNext : unmanaged
            => ref pkt.InsertSpan<TNext>(out retPacketPin, PacketInsertMode.MoveHead, this.Pin, size);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PacketSpan<TNext> AppendSpan<TNext>(ref Packet pkt, ReadOnlySpan<byte> data, int size = DefaultSize) where TNext : unmanaged
            => pkt.InsertSpan<TNext>(PacketInsertMode.MoveTail, this.Pin + this.HeaderSize, data, size);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PacketSpan<TNext> AppendSpan<TNext>(ref Packet pkt, in TNext data, int size = DefaultSize) where TNext : unmanaged
            => pkt.InsertSpan<TNext>(PacketInsertMode.MoveTail, this.Pin + this.HeaderSize, data, size);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PacketSpan<TNext> AppendSpan<TNext>(ref Packet pkt, TNext* ptr, int size = DefaultSize) where TNext : unmanaged
            => pkt.InsertSpan<TNext>(PacketInsertMode.MoveTail, this.Pin + this.HeaderSize, ptr, size);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref TNext AppendSpan<TNext>(ref Packet pkt, out PacketSpan<TNext> retPacketPin, int size = DefaultSize) where TNext : unmanaged
            => ref pkt.InsertSpan<TNext>(out retPacketPin, PacketInsertMode.MoveTail, this.Pin + this.HeaderSize, size);
    }

    static class PacketPinHelper
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref readonly PacketSpan<GenericHeader> ToGenericSpan<TFrom>(this ref PacketSpan<TFrom> src) where TFrom : unmanaged
            => ref Unsafe.As<PacketSpan<TFrom>, PacketSpan<GenericHeader>>(ref src);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref readonly PacketSpan<TTo> ToOtherTypeSpan<TTo>(this ref PacketSpan<GenericHeader> src) where TTo : unmanaged
            => ref Unsafe.As<PacketSpan<GenericHeader>, PacketSpan<TTo>>(ref src);
    }
}

