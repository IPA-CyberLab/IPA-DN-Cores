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
using System.IO;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    static partial class CoresConfig
    {
        public static partial class LargeMemoryBuffer
        {
            public static readonly Copenhagen<int> DefaultSegmentSize = 10_000_000;
            //public static readonly Copenhagen<int> DefaultSegmentSize = 10;
        }
    }

    ref struct SpanBuffer<T>
    {
        Span<T> InternalSpan;
        public int CurrentPosition { get; private set; }
        public int Length { get; private set; }
        public bool Empty => Length == 0;
        public int InternalBufferSize => InternalSpan.Length;

        public Span<T> Span { get => InternalSpan.Slice(0, Length); }
        public Span<T> SpanBefore { get => Span.Slice(0, CurrentPosition); }
        public Span<T> SpanAfter { get => Span.Slice(CurrentPosition); }

        public SpanBuffer(int size = 0) : this(new T[size]) { }

        public SpanBuffer(Span<T> baseSpan)
        {
            InternalSpan = baseSpan;
            CurrentPosition = 0;
            Length = baseSpan.Length;
        }

        public static SpanBuffer<byte> FromStruct<TStruct>(TStruct src)
        {
            Memory<byte> baseMemory = new byte[Util.SizeOfStruct<TStruct>()];
            ref TStruct dst = ref baseMemory._AsStruct<TStruct>();
            dst = src;
            return new SpanBuffer<byte>(baseMemory.Span);
        }

        static T dummyRefValue = default;
        public ref T GetRefForFixedPtr(int position = 0)
        {
            var span = this.Span.Slice(position);
            if (span.IsEmpty)
                return ref dummyRefValue;
            return ref span[0];
        }

        public static implicit operator SpanBuffer<T>(Span<T> span) => new SpanBuffer<T>(span);
        public static implicit operator SpanBuffer<T>(Memory<T> memory) => new SpanBuffer<T>(memory.Span);
        public static implicit operator SpanBuffer<T>(T[] array) => new SpanBuffer<T>(array.AsSpan());
        public static implicit operator Span<T>(SpanBuffer<T> buf) => buf.Span;
        public static implicit operator ReadOnlySpan<T>(SpanBuffer<T> buf) => buf.Span;
        public static implicit operator ReadOnlySpanBuffer<T>(SpanBuffer<T> buf) => buf.AsReadOnly();

        public SpanBuffer<T> SliceAfter() => Slice(CurrentPosition);
        public SpanBuffer<T> SliceBefore() => Slice(0, CurrentPosition);
        public SpanBuffer<T> Slice(int start) => Slice(start, this.Length - start);
        public SpanBuffer<T> Slice(int start, int length)
        {
            if (start < 0) throw new ArgumentOutOfRangeException("start < 0");
            if (length < 0) throw new ArgumentOutOfRangeException("length < 0");
            if (start > Length) throw new ArgumentOutOfRangeException("start > Size");
            if (checked(start + length) > Length) throw new ArgumentOutOfRangeException("length > Size");
            SpanBuffer<T> ret = new SpanBuffer<T>(this.InternalSpan.Slice(start, length));
            ret.Length = length;
            ret.CurrentPosition = Math.Max(checked(CurrentPosition - start), 0);
            return ret;
        }

        public SpanBuffer<T> Clone()
        {
            SpanBuffer<T> ret = new SpanBuffer<T>(InternalSpan.ToArray());
            ret.Length = Length;
            ret.CurrentPosition = CurrentPosition;
            return ret;
        }

        public ReadOnlySpanBuffer<T> AsReadOnly()
        {
            ReadOnlySpanBuffer<T> ret = new ReadOnlySpanBuffer<T>(Span);
            ret.Seek(CurrentPosition, SeekOrigin.Begin);
            return ret;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Span<T> Walk(int size, bool noMove = false)
        {
            int newSize = checked(CurrentPosition + size);

            if (InternalSpan.Length < newSize)
            {
                EnsureInternalBufferReserved(newSize);
            }
            var ret = InternalSpan.Slice(CurrentPosition, size);
            Length = Math.Max(newSize, Length);
            if (noMove == false) CurrentPosition += size;
            return ret;
        }

        public void Write(Memory<T> data) => Write(data.Span);
        public void Write(ReadOnlyMemory<T> data) => Write(data.Span);
        public void Write(T[] data, int offset = 0, int? length = null) => Write(data.AsSpan(offset, length ?? data.Length - offset));

        public void Write(Span<T> data)
        {
            var span = Walk(data.Length);
            data.CopyTo(span);
        }

        public void Write(ReadOnlySpan<T> data)
        {
            var span = Walk(data.Length);
            data.CopyTo(span);
        }

        public void WriteZero(int length)
        {
            var zeroSpan = ZeroedSharedMemory<T>.Memory.Span;
            while (length >= 1)
            {
                int currentSize = Math.Min(length, zeroSpan.Length);
                this.Write(zeroSpan.Slice(0, currentSize));
                length -= currentSize;
            }
        }

        public ReadOnlySpan<T> Read(int size, bool allowPartial = false)
        {
            int sizeRead = size;
            if (checked(CurrentPosition + size) > Length)
            {
                if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
                sizeRead = Length - CurrentPosition;
            }

            Span<T> ret = InternalSpan.Slice(CurrentPosition, sizeRead);
            CurrentPosition += sizeRead;
            return ret;
        }

        public ReadOnlySpan<T> Peek(int size, bool allowPartial = false)
        {
            int sizeRead = size;
            if (checked(CurrentPosition + size) > Length)
            {
                if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
                sizeRead = Length - CurrentPosition;
            }

            Span<T> ret = InternalSpan.Slice(CurrentPosition, sizeRead);
            return ret;
        }

        public void Seek(int offset, SeekOrigin mode, bool allocate = false)
        {
            int newPosition = 0;
            if (mode == SeekOrigin.Current)
                newPosition = checked(CurrentPosition + offset);
            else if (mode == SeekOrigin.End)
                newPosition = checked(Length + offset);
            else
                newPosition = offset;

            if (newPosition < 0) throw new ArgumentOutOfRangeException("newPosition < 0");
            if (allocate == false)
            {
                if (newPosition > Length) throw new ArgumentOutOfRangeException("newPosition > Size");
            }
            else
            {
                EnsureInternalBufferReserved(newPosition);
                Length = Math.Max(newPosition, Length);
            }

            CurrentPosition = newPosition;
        }

        public SpanBuffer<T> SeekToBegin()
        {
            Seek(0, SeekOrigin.Begin);
            return this;
        }

        public SpanBuffer<T> SeekToEnd()
        {
            Seek(0, SeekOrigin.End);
            return this;
        }

        public int Read(Span<T> dest, int? size = null, bool allowPartial = false)
        {
            int size2 = size ?? dest.Length;
            if (dest.Length < size2) throw new ArgumentException("dest.Length < size");
            var span = Read(size2, allowPartial);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Peek(Span<T> dest, int? size = null, bool allowPartial = false)
        {
            int size2 = size ?? dest.Length;
            if (dest.Length < size2) throw new ArgumentException("dest.Length < size");
            var span = Peek(size2, allowPartial);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Read(T[] dest, int offset, int size, bool allowPartial = false) => Read(dest.AsSpan(offset, size), size, allowPartial);
        public int Peek(T[] dest, int offset, int size, bool allowPartial = false) => Peek(dest.AsSpan(offset, size), size, allowPartial);

        public void EnsureInternalBufferReserved(int newSize)
        {
            if (InternalSpan.Length >= newSize) return;

            int newInternalSize = InternalSpan.Length;
            while (newInternalSize < newSize)
                newInternalSize = checked(Math.Max(newInternalSize, 128) * 2);

            InternalSpan = InternalSpan._ReAlloc(newInternalSize, this.Length);
        }

        public void OptimizeInternalBufferSize()
        {
            int newInternalSize = 128;
            while (newInternalSize < this.Length)
                newInternalSize = checked(newInternalSize * 2);

            if (this.InternalSpan.Length > newInternalSize)
                InternalSpan = InternalSpan._ReAlloc(newInternalSize, this.Length);
        }

        public void SetLength(int size)
        {
            if (size < 0) throw new ArgumentOutOfRangeException("size < 0");

            EnsureInternalBufferReserved(size);
            this.Length = size;
            if (this.CurrentPosition > this.Length)
                this.CurrentPosition = this.Length;
        }

        public void Clear()
        {
            InternalSpan = new Span<T>();
            CurrentPosition = 0;
            Length = 0;
        }

        public void WriteOne(T data)
        {
            var span = Walk(1);
            span[0] = data;
        }

        public T ReadOne()
        {
            var span = Read(1, false);
            return span[0];
        }

        public T PeekOne()
        {
            var span = Peek(1, false);
            return span[0];
        }
    }

    ref struct ReadOnlySpanBuffer<T>
    {
        ReadOnlySpan<T> InternalSpan;
        public int CurrentPosition { get; private set; }
        public int Length { get; private set; }
        public bool Empty => Length == 0;
        public int InternalBufferSize => InternalSpan.Length;

        public ReadOnlySpan<T> Span { get => InternalSpan.Slice(0, Length); }
        public ReadOnlySpan<T> SpanBefore { get => Span.Slice(0, CurrentPosition); }
        public ReadOnlySpan<T> SpanAfter { get => Span.Slice(CurrentPosition); }

        public static implicit operator ReadOnlySpanBuffer<T>(ReadOnlySpan<T> span) => new ReadOnlySpanBuffer<T>(span);
        public static implicit operator ReadOnlySpanBuffer<T>(ReadOnlyMemory<T> memory) => new ReadOnlySpanBuffer<T>(memory.Span);
        public static implicit operator ReadOnlySpanBuffer<T>(T[] array) => new ReadOnlySpanBuffer<T>(array.AsSpan());
        public static implicit operator ReadOnlySpan<T>(ReadOnlySpanBuffer<T> buf) => buf.Span;

        public ReadOnlySpanBuffer<T> SliceAfter() => Slice(CurrentPosition);
        public ReadOnlySpanBuffer<T> SliceBefore() => Slice(0, CurrentPosition);
        public ReadOnlySpanBuffer<T> Slice(int start) => Slice(start, this.Length - start);
        public ReadOnlySpanBuffer<T> Slice(int start, int length)
        {
            if (start < 0) throw new ArgumentOutOfRangeException("start < 0");
            if (length < 0) throw new ArgumentOutOfRangeException("length < 0");
            if (start > Length) throw new ArgumentOutOfRangeException("start > Size");
            if (checked(start + length) > Length) throw new ArgumentOutOfRangeException("length > Size");
            ReadOnlySpanBuffer<T> ret = new ReadOnlySpanBuffer<T>(this.InternalSpan.Slice(start, length));
            ret.Length = length;
            ret.CurrentPosition = Math.Max(checked(CurrentPosition - start), 0);
            return ret;
        }

        public ReadOnlySpanBuffer<T> Clone()
        {
            ReadOnlySpanBuffer<T> ret = new ReadOnlySpanBuffer<T>(InternalSpan.ToArray());
            ret.Length = Length;
            ret.CurrentPosition = CurrentPosition;
            return ret;
        }

        public SpanBuffer<T> CloneAsWritable()
        {
            SpanBuffer<T> ret = new SpanBuffer<T>(Span.ToArray());
            ret.Seek(CurrentPosition, SeekOrigin.Begin);
            return ret;
        }

        public ReadOnlySpanBuffer(int size = 0) : this(new T[size]) { }

        public ReadOnlySpanBuffer(ReadOnlySpan<T> baseSpan)
        {
            InternalSpan = baseSpan;
            CurrentPosition = 0;
            Length = baseSpan.Length;
        }

        public static ReadOnlySpanBuffer<byte> FromStruct<TStruct>(TStruct src)
        {
            Memory<byte> baseMemory = new byte[Util.SizeOfStruct<TStruct>()];
            ref TStruct dst = ref baseMemory._AsStruct<TStruct>();
            dst = src;
            return new ReadOnlySpanBuffer<byte>(baseMemory.Span);
        }

        static T dummyRefValue = default;
        public ref readonly T GetRefForFixedPtr(int position = 0)
        {
            var span = this.Span.Slice(position);
            if (span.IsEmpty)
                return ref dummyRefValue;
            return ref span[0];
        }

        public ReadOnlySpan<T> Read(int size, bool allowPartial = false)
        {
            int sizeRead = size;
            if (checked(CurrentPosition + size) > Length)
            {
                if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
                sizeRead = Length - CurrentPosition;
            }

            ReadOnlySpan<T> ret = InternalSpan.Slice(CurrentPosition, sizeRead);
            CurrentPosition += sizeRead;
            return ret;
        }

        public ReadOnlySpan<T> Peek(int size, bool allowPartial = false)
        {
            int sizeRead = size;
            if (checked(CurrentPosition + size) > Length)
            {
                if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
                sizeRead = Length - CurrentPosition;
            }

            ReadOnlySpan<T> ret = InternalSpan.Slice(CurrentPosition, sizeRead);
            return ret;
        }

        public void Seek(int offset, SeekOrigin mode, bool allocate = false)
        {
            int newPosition = 0;
            if (mode == SeekOrigin.Current)
                newPosition = checked(CurrentPosition + offset);
            else if (mode == SeekOrigin.End)
                newPosition = checked(Length + offset);
            else
                newPosition = offset;

            if (newPosition < 0) throw new ArgumentOutOfRangeException("newPosition < 0");
            if (newPosition > Length) throw new ArgumentOutOfRangeException("newPosition > Size");

            CurrentPosition = newPosition;
        }

        public ReadOnlySpanBuffer<T> SeekToBegin()
        {
            Seek(0, SeekOrigin.Begin);
            return this;
        }

        public ReadOnlySpanBuffer<T> SeekToEnd()
        {
            Seek(0, SeekOrigin.End);
            return this;
        }

        public int Read(Span<T> dest, int? size = null, bool allowPartial = false)
        {
            int size2 = size ?? dest.Length;
            if (dest.Length < size2) throw new ArgumentException("dest.Length < size");
            var span = Read(size2, allowPartial);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Peek(Span<T> dest, int? size = null, bool allowPartial = false)
        {
            int size2 = size ?? dest.Length;
            if (dest.Length < size2) throw new ArgumentException("dest.Length < size");
            var span = Peek(size2, allowPartial);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Read(T[] dest, int offset, int size, bool allowPartial = false) => Read(dest.AsSpan(offset, size), size, allowPartial);
        public int Peek(T[] dest, int offset, int size, bool allowPartial = false) => Peek(dest.AsSpan(offset, size), size, allowPartial);


        public void Clear()
        {
            InternalSpan = new ReadOnlySpan<T>();
            CurrentPosition = 0;
            Length = 0;
        }

        public T ReadOne()
        {
            var span = Read(1, false);
            return span[0];
        }

        public T PeekOne()
        {
            var span = Peek(1, false);
            return span[0];
        }
    }

    ref struct FastMemoryBuffer<T>
    {
        Memory<T> InternalBuffer;
        Span<T> InternalSpan;
        public int CurrentPosition { get; private set; }
        public int Length { get; private set; }
        public bool Empty => Length == 0;
        public int InternalBufferSize => InternalBuffer.Length;

        public Memory<T> Memory { get => InternalBuffer.Slice(0, Length); }
        public Memory<T> MemoryBefore { get => Memory.Slice(0, CurrentPosition); }
        public Memory<T> MemoryAfter { get => Memory.Slice(CurrentPosition); }

        public Span<T> Span { get => InternalBuffer.Slice(0, Length).Span; }
        public Span<T> SpanBefore { get => Memory.Slice(0, CurrentPosition).Span; }
        public Span<T> SpanAfter { get => Memory.Slice(CurrentPosition).Span; }

        public FastMemoryBuffer(int size = 0) : this(new T[size]) { }

        public FastMemoryBuffer(Memory<T> baseMemory)
        {
            InternalBuffer = baseMemory;
            CurrentPosition = 0;
            Length = baseMemory.Length;
            InternalSpan = InternalBuffer.Span;
        }

        public static FastMemoryBuffer<byte> FromStruct<TStruct>(TStruct src)
        {
            Memory<byte> baseMemory = new byte[Util.SizeOfStruct<TStruct>()];
            ref TStruct dst = ref baseMemory._AsStruct<TStruct>();
            dst = src;
            return new FastMemoryBuffer<byte>(baseMemory);
        }

        static T dummyRefValue = default;
        public ref T GetRefForFixedPtr(int position = 0)
        {
            var span = this.Span.Slice(position);
            if (span.IsEmpty)
                return ref dummyRefValue;
            return ref span[0];
        }

        public static implicit operator FastMemoryBuffer<T>(Memory<T> memory) => new FastMemoryBuffer<T>(memory);
        public static implicit operator FastMemoryBuffer<T>(T[] array) => new FastMemoryBuffer<T>(array.AsMemory());
        public static implicit operator Memory<T>(FastMemoryBuffer<T> buf) => buf.Memory;
        public static implicit operator Span<T>(FastMemoryBuffer<T> buf) => buf.Span;
        public static implicit operator ReadOnlyMemory<T>(FastMemoryBuffer<T> buf) => buf.Memory;
        public static implicit operator ReadOnlySpan<T>(FastMemoryBuffer<T> buf) => buf.Span;
        public static implicit operator FastReadOnlyMemoryBuffer<T>(FastMemoryBuffer<T> buf) => buf.AsReadOnly();
        public static implicit operator SpanBuffer<T>(FastMemoryBuffer<T> buf) => buf.AsSpanBuffer();
        public static implicit operator ReadOnlySpanBuffer<T>(FastMemoryBuffer<T> buf) => buf.AsReadOnlySpanBuffer();

        public FastMemoryBuffer<T> SliceAfter() => Slice(CurrentPosition);
        public FastMemoryBuffer<T> SliceBefore() => Slice(0, CurrentPosition);
        public FastMemoryBuffer<T> Slice(int start) => Slice(start, this.Length - start);
        public FastMemoryBuffer<T> Slice(int start, int length)
        {
            if (start < 0) throw new ArgumentOutOfRangeException("start < 0");
            if (length < 0) throw new ArgumentOutOfRangeException("length < 0");
            if (start > Length) throw new ArgumentOutOfRangeException("start > Size");
            if (checked(start + length) > Length) throw new ArgumentOutOfRangeException("length > Size");
            FastMemoryBuffer<T> ret = new FastMemoryBuffer<T>(this.InternalBuffer.Slice(start, length));
            ret.Length = length;
            ret.CurrentPosition = Math.Max(checked(CurrentPosition - start), 0);
            return ret;
        }

        public FastMemoryBuffer<T> Clone()
        {
            FastMemoryBuffer<T> ret = new FastMemoryBuffer<T>(InternalSpan.ToArray());
            ret.Length = Length;
            ret.CurrentPosition = CurrentPosition;
            return ret;
        }

        public FastReadOnlyMemoryBuffer<T> AsReadOnly()
        {
            FastReadOnlyMemoryBuffer<T> ret = new FastReadOnlyMemoryBuffer<T>(Memory);
            ret.Seek(CurrentPosition, SeekOrigin.Begin);
            return ret;
        }

        public SpanBuffer<T> AsSpanBuffer()
        {
            SpanBuffer<T> ret = new SpanBuffer<T>(Span);
            ret.Seek(CurrentPosition, SeekOrigin.Begin);
            return ret;
        }

        public ReadOnlySpanBuffer<T> AsReadOnlySpanBuffer()
        {
            ReadOnlySpanBuffer<T> ret = new ReadOnlySpanBuffer<T>(Span);
            ret.Seek(CurrentPosition, SeekOrigin.Begin);
            return ret;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Span<T> Walk(int size, bool noMove = false)
        {
            int newSize = checked(CurrentPosition + size);
            if (InternalBuffer.Length < newSize)
            {
                EnsureInternalBufferReserved(newSize);
            }
            var ret = InternalSpan.Slice(CurrentPosition, size);
            Length = Math.Max(newSize, Length);
            if (noMove == false) CurrentPosition += size;
            return ret;
        }

        public void Write(T[] data, int offset = 0, int? length = null) => Write(data.AsSpan(offset, length ?? data.Length - offset));
        public void Write(Memory<T> data) => Write(data.Span);
        public void Write(ReadOnlyMemory<T> data) => Write(data.Span);

        public void Write(Span<T> data)
        {
            var span = Walk(data.Length);
            data.CopyTo(span);
        }

        public void Write(ReadOnlySpan<T> data)
        {
            var span = Walk(data.Length);
            data.CopyTo(span);
        }

        public void WriteZero(int length)
        {
            var zeroSpan = ZeroedSharedMemory<T>.Memory.Span;
            while (length >= 1)
            {
                int currentSize = Math.Min(length, zeroSpan.Length);
                this.Write(zeroSpan.Slice(0, currentSize));
                length -= currentSize;
            }
        }

        public ReadOnlySpan<T> Read(int size, bool allowPartial = false)
        {
            int sizeRead = size;
            if (checked(CurrentPosition + size) > Length)
            {
                if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
                sizeRead = Length - CurrentPosition;
            }

            ReadOnlySpan<T> ret = InternalSpan.Slice(CurrentPosition, sizeRead);
            CurrentPosition += sizeRead;
            return ret;
        }

        public ReadOnlySpan<T> Peek(int size, bool allowPartial = false)
        {
            int sizeRead = size;
            if (checked(CurrentPosition + size) > Length)
            {
                if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
                sizeRead = Length - CurrentPosition;
            }

            Span<T> ret = InternalSpan.Slice(CurrentPosition, sizeRead);
            return ret;
        }

        public ReadOnlyMemory<T> ReadAsMemory(int size, bool allowPartial = false)
        {
            int sizeRead = size;
            if (checked(CurrentPosition + size) > Length)
            {
                if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
                sizeRead = Length - CurrentPosition;
            }

            ReadOnlyMemory<T> ret = InternalBuffer.Slice(CurrentPosition, sizeRead);
            CurrentPosition += sizeRead;
            return ret;
        }

        public ReadOnlyMemory<T> PeekAsMemory(int size, bool allowPartial = false)
        {
            int sizeRead = size;
            if (checked(CurrentPosition + size) > Length)
            {
                if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
                sizeRead = Length - CurrentPosition;
            }

            ReadOnlyMemory<T> ret = InternalBuffer.Slice(CurrentPosition, sizeRead);
            return ret;
        }

        public void Seek(int offset, SeekOrigin mode, bool allocate = false)
        {
            int newPosition = 0;
            if (mode == SeekOrigin.Current)
                newPosition = checked(CurrentPosition + offset);
            else if (mode == SeekOrigin.End)
                newPosition = checked(Length + offset);
            else
                newPosition = offset;

            if (newPosition < 0) throw new ArgumentOutOfRangeException("newPosition < 0");

            if (allocate == false)
            {
                if (newPosition > Length) throw new ArgumentOutOfRangeException("newPosition > Size");
            }
            else
            {
                EnsureInternalBufferReserved(newPosition);
                Length = Math.Max(newPosition, Length);
            }

            CurrentPosition = newPosition;
        }

        public FastMemoryBuffer<T> SeekToBegin()
        {
            Seek(0, SeekOrigin.Begin);
            return this;
        }

        public FastMemoryBuffer<T> SeekToEnd()
        {
            Seek(0, SeekOrigin.End);
            return this;
        }

        public int Read(Span<T> dest, int? size = null, bool allowPartial = false)
        {
            int size2 = size ?? dest.Length;
            if (dest.Length < size2) throw new ArgumentException("dest.Length < size");
            var span = Read(size2, allowPartial);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Peek(Span<T> dest, int? size = null, bool allowPartial = false)
        {
            int size2 = size ?? dest.Length;
            if (dest.Length < size2) throw new ArgumentException("dest.Length < size");
            var span = Peek(size2, allowPartial);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Read(T[] dest, int offset, int size, bool allowPartial = false) => Read(dest.AsSpan(offset, size), size, allowPartial);
        public int Peek(T[] dest, int offset, int size, bool allowPartial = false) => Peek(dest.AsSpan(offset, size), size, allowPartial);

        public int Read(Memory<T> dest, int? size = null, bool allowPartial = false)
        {
            int size2 = size ?? dest.Length;
            if (dest.Length < size2) throw new ArgumentException("dest.Length < size");
            var span = PeekAsMemory(size2, allowPartial);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Peek(Memory<T> dest, int? size = null, bool allowPartial = false)
        {
            int size2 = size ?? dest.Length;
            if (dest.Length < size2) throw new ArgumentException("dest.Length < size");
            var span = PeekAsMemory(size2, allowPartial);
            span.CopyTo(dest);
            return span.Length;
        }

        public void EnsureInternalBufferReserved(int newSize)
        {
            if (InternalBuffer.Length >= newSize) return;

            int newInternalSize = InternalBuffer.Length;
            while (newInternalSize < newSize)
                newInternalSize = checked(Math.Max(newInternalSize, 128) * 2);

            InternalBuffer = InternalBuffer._ReAlloc(newInternalSize, this.Length);
            InternalSpan = InternalBuffer.Span;
        }

        public void OptimizeInternalBufferSize()
        {
            int newInternalSize = 128;
            while (newInternalSize < this.Length)
                newInternalSize = checked(newInternalSize * 2);

            if (this.InternalBuffer.Length > newInternalSize)
            {
                InternalBuffer = InternalBuffer._ReAlloc(newInternalSize, this.Length);
                InternalSpan = InternalBuffer.Span;
            }
        }

        public void SetLength(int size)
        {
            if (size < 0) throw new ArgumentOutOfRangeException("size < 0");

            EnsureInternalBufferReserved(size);
            this.Length = size;
            if (this.CurrentPosition > this.Length)
                this.CurrentPosition = this.Length;
        }

        public void Clear()
        {
            InternalBuffer = new Memory<T>();
            CurrentPosition = 0;
            Length = 0;
            InternalSpan = new Span<T>();
        }

        public void WriteOne(T data)
        {
            var span = Walk(1);
            span[0] = data;
        }

        public T ReadOne()
        {
            var span = Read(1, false);
            return span[0];
        }

        public T PeekOne()
        {
            var span = Peek(1, false);
            return span[0];
        }
    }

    ref struct FastReadOnlyMemoryBuffer<T>
    {
        ReadOnlyMemory<T> InternalBuffer;
        ReadOnlySpan<T> InternalSpan;
        public int CurrentPosition { get; private set; }
        public int Length { get; private set; }
        public bool Empty => Length == 0;
        public int InternalBufferSize => InternalBuffer.Length;

        public ReadOnlyMemory<T> Memory { get => InternalBuffer.Slice(0, Length); }
        public ReadOnlyMemory<T> MemoryBefore { get => Memory.Slice(0, CurrentPosition); }
        public ReadOnlyMemory<T> MemoryAfter { get => Memory.Slice(CurrentPosition); }

        public ReadOnlySpan<T> Span { get => InternalBuffer.Slice(0, Length).Span; }
        public ReadOnlySpan<T> SpanBefore { get => Memory.Slice(0, CurrentPosition).Span; }
        public ReadOnlySpan<T> SpanAfter { get => Memory.Slice(CurrentPosition).Span; }

        public FastReadOnlyMemoryBuffer(int size = 0) : this(new T[size]) { }

        public FastReadOnlyMemoryBuffer(ReadOnlyMemory<T> baseMemory)
        {
            InternalBuffer = baseMemory;
            CurrentPosition = 0;
            Length = baseMemory.Length;
            InternalSpan = InternalBuffer.Span;
        }

        public static FastReadOnlyMemoryBuffer<byte> FromStruct<TStruct>(TStruct src)
        {
            Memory<byte> baseMemory = new byte[Util.SizeOfStruct<TStruct>()];
            ref TStruct dst = ref baseMemory._AsStruct<TStruct>();
            dst = src;
            return new FastReadOnlyMemoryBuffer<byte>(baseMemory);
        }

        static T dummyRefValue = default;
        public ref readonly T GetRefForFixedPtr(int position = 0)
        {
            var span = this.Span.Slice(position);
            if (span.IsEmpty)
                return ref dummyRefValue;
            return ref span[0];
        }

        public static implicit operator FastReadOnlyMemoryBuffer<T>(ReadOnlyMemory<T> memory) => new FastReadOnlyMemoryBuffer<T>(memory);
        public static implicit operator FastReadOnlyMemoryBuffer<T>(T[] array) => new FastReadOnlyMemoryBuffer<T>(array.AsMemory());
        public static implicit operator ReadOnlyMemory<T>(FastReadOnlyMemoryBuffer<T> buf) => buf.Memory;
        public static implicit operator ReadOnlySpan<T>(FastReadOnlyMemoryBuffer<T> buf) => buf.Span;
        public static implicit operator ReadOnlySpanBuffer<T>(FastReadOnlyMemoryBuffer<T> buf) => buf.AsReadOnlySpanBuffer();

        public FastReadOnlyMemoryBuffer<T> SliceAfter() => Slice(CurrentPosition);
        public FastReadOnlyMemoryBuffer<T> SliceBefore() => Slice(0, CurrentPosition);
        public FastReadOnlyMemoryBuffer<T> Slice(int start) => Slice(start, this.Length - start);
        public FastReadOnlyMemoryBuffer<T> Slice(int start, int length)
        {
            if (start < 0) throw new ArgumentOutOfRangeException("start < 0");
            if (length < 0) throw new ArgumentOutOfRangeException("length < 0");
            if (start > Length) throw new ArgumentOutOfRangeException("start > Size");
            if (checked(start + length) > Length) throw new ArgumentOutOfRangeException("length > Size");
            FastReadOnlyMemoryBuffer<T> ret = new FastReadOnlyMemoryBuffer<T>(this.InternalBuffer.Slice(start, length));
            ret.Length = length;
            ret.CurrentPosition = Math.Max(checked(CurrentPosition - start), 0);
            return ret;
        }

        public FastReadOnlyMemoryBuffer<T> Clone()
        {
            FastReadOnlyMemoryBuffer<T> ret = new FastReadOnlyMemoryBuffer<T>(InternalSpan.ToArray());
            ret.Length = Length;
            ret.CurrentPosition = CurrentPosition;
            return ret;
        }

        public FastMemoryBuffer<T> CloneAsWritable()
        {
            FastMemoryBuffer<T> ret = new FastMemoryBuffer<T>(Span.ToArray());
            ret.Seek(CurrentPosition, SeekOrigin.Begin);
            return ret;
        }

        public ReadOnlySpanBuffer<T> AsReadOnlySpanBuffer()
        {
            ReadOnlySpanBuffer<T> ret = new ReadOnlySpanBuffer<T>(Span);
            ret.Seek(CurrentPosition, SeekOrigin.Begin);
            return ret;
        }

        public ReadOnlySpan<T> Read(int size, bool allowPartial = false)
        {
            int sizeRead = size;
            if (checked(CurrentPosition + size) > Length)
            {
                if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
                sizeRead = Length - CurrentPosition;
            }

            ReadOnlySpan<T> ret = InternalSpan.Slice(CurrentPosition, sizeRead);
            CurrentPosition += sizeRead;
            return ret;
        }

        public ReadOnlySpan<T> Peek(int size, bool allowPartial = false)
        {
            int sizeRead = size;
            if (checked(CurrentPosition + size) > Length)
            {
                if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
                sizeRead = Length - CurrentPosition;
            }

            ReadOnlySpan<T> ret = InternalSpan.Slice(CurrentPosition, sizeRead);
            return ret;
        }

        public ReadOnlyMemory<T> ReadAsMemory(int size, bool allowPartial = false)
        {
            int sizeRead = size;
            if (checked(CurrentPosition + size) > Length)
            {
                if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
                sizeRead = Length - CurrentPosition;
            }

            ReadOnlyMemory<T> ret = InternalBuffer.Slice(CurrentPosition, sizeRead);
            CurrentPosition += sizeRead;
            return ret;
        }

        public ReadOnlyMemory<T> PeekAsMemory(int size, bool allowPartial = false)
        {
            int sizeRead = size;
            if (checked(CurrentPosition + size) > Length)
            {
                if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
                sizeRead = Length - CurrentPosition;
            }

            ReadOnlyMemory<T> ret = InternalBuffer.Slice(CurrentPosition, sizeRead);
            return ret;
        }

        public void Seek(int offset, SeekOrigin mode, bool allocate = false)
        {
            int newPosition = 0;
            if (mode == SeekOrigin.Current)
                newPosition = checked(CurrentPosition + offset);
            else if (mode == SeekOrigin.End)
                newPosition = checked(Length + offset);
            else
                newPosition = offset;

            if (newPosition < 0) throw new ArgumentOutOfRangeException("newPosition < 0");
            if (newPosition > Length) throw new ArgumentOutOfRangeException("newPosition > Size");

            CurrentPosition = newPosition;
        }

        public FastReadOnlyMemoryBuffer<T> SeekToBegin()
        {
            Seek(0, SeekOrigin.Begin);
            return this;
        }

        public FastReadOnlyMemoryBuffer<T> SeekToEnd()
        {
            Seek(0, SeekOrigin.End);
            return this;
        }

        public int Read(Span<T> dest, int? size = null, bool allowPartial = false)
        {
            int size2 = size ?? dest.Length;
            if (dest.Length < size2) throw new ArgumentException("dest.Length < size");
            var span = Read(size2, allowPartial);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Peek(Span<T> dest, int? size = null, bool allowPartial = false)
        {
            int size2 = size ?? dest.Length;
            if (dest.Length < size2) throw new ArgumentException("dest.Length < size");
            var span = Peek(size2, allowPartial);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Read(T[] dest, int offset, int size, bool allowPartial = false) => Read(dest.AsSpan(offset, size), size, allowPartial);
        public int Peek(T[] dest, int offset, int size, bool allowPartial = false) => Peek(dest.AsSpan(offset, size), size, allowPartial);

        public int Read(Memory<T> dest, int? size = null, bool allowPartial = false)
        {
            int size2 = size ?? dest.Length;
            if (dest.Length < size2) throw new ArgumentException("dest.Length < size");
            var span = ReadAsMemory(size2, allowPartial);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Peek(Memory<T> dest, int? size = null, bool allowPartial = false)
        {
            int size2 = size ?? dest.Length;
            if (dest.Length < size2) throw new ArgumentException("dest.Length < size");
            var span = PeekAsMemory(size2, allowPartial);
            span.CopyTo(dest);
            return span.Length;
        }

        public void Clear()
        {
            InternalBuffer = new ReadOnlyMemory<T>();
            CurrentPosition = 0;
            Length = 0;
            InternalSpan = new Span<T>();
        }
    }

    interface IBuffer<T> : IEmptyChecker
    {
        long LongCurrentPosition { get; }
        long LongLength { get; }
        long LongInternalBufferSize { get; }
        void Write(ReadOnlySpan<T> data);
        ReadOnlySpan<T> Read(long size, bool allowPartial = false);
        ReadOnlySpan<T> Peek(long size, bool allowPartial = false);
        T ReadOne();
        T PeekOne();
        void Seek(long offset, SeekOrigin mode, bool allocate = false);
        void SetLength(long size);
        void Clear();
    }

#pragma warning disable CS1998
    class BufferDirectStream : Stream
    {
        public IBuffer<byte> BaseBuffer { get; }

        public BufferDirectStream(IBuffer<byte> baseBuffer)
        {
            BaseBuffer = baseBuffer;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => true;
        public override long Length => BaseBuffer.LongLength;

        public override long Position
        {
            get => BaseBuffer.LongCurrentPosition;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin)
        {
            BaseBuffer.Seek(offset, origin, true);
            return BaseBuffer.LongCurrentPosition;
        }

        public override void SetLength(long value)
            => BaseBuffer.SetLength(value);

        public override Task FlushAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public override int Read(Span<byte> buffer)
        {
            var readSpan = BaseBuffer.Read(buffer.Length, true);
            readSpan.CopyTo(buffer);
            return readSpan.Length;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            BaseBuffer.Write(buffer);
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(this.Read(buffer, offset, count));
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return this.Read(buffer.Span);
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.Write(buffer, offset, count);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.Write(buffer.Span);
        }
    }
#pragma warning restore CS1998

    class MemoryBuffer<T> : IBuffer<T>
    {
        Memory<T> InternalBuffer;
        public int CurrentPosition { get; private set; }
        public int Length { get; private set; }
        public bool IsThisEmpty() => Length == 0;
        public int InternalBufferSize => InternalBuffer.Length;

        public Memory<T> Memory { get => InternalBuffer.Slice(0, Length); }
        public Memory<T> MemoryBefore { get => Memory.Slice(0, CurrentPosition); }
        public Memory<T> MemoryAfter { get => Memory.Slice(CurrentPosition); }

        public Span<T> Span { get => InternalBuffer.Slice(0, Length).Span; }
        public Span<T> SpanBefore { get => Memory.Slice(0, CurrentPosition).Span; }
        public Span<T> SpanAfter { get => Memory.Slice(CurrentPosition).Span; }

        public long LongCurrentPosition => this.CurrentPosition;

        public long LongLength => this.Length;

        public long LongInternalBufferSize => this.InternalBufferSize;

        public MemoryBuffer(int size = 0) : this(new T[size]) { }

        public MemoryBuffer(Memory<T> baseMemory)
        {
            InternalBuffer = baseMemory;
            CurrentPosition = 0;
            Length = baseMemory.Length;
        }

        public static MemoryBuffer<byte> FromStruct<TStruct>(TStruct src)
        {
            Memory<byte> baseMemory = new byte[Util.SizeOfStruct<TStruct>()];
            ref TStruct dst = ref baseMemory._AsStruct<TStruct>();
            dst = src;
            return new MemoryBuffer<byte>(baseMemory);
        }

        CriticalSection PinLockObj = new CriticalSection();
        int PinLockedCounter = 0;
        MemoryHandle PinHandle;
        public bool IsPinLocked() => (PinLockedCounter != 0);
        public ValueHolder PinLock()
        {
            lock (PinLockObj)
            {
                if (PinLockedCounter == 0)
                    PinHandle = InternalBuffer.Pin();
                PinLockedCounter++;
            }

            return new ValueHolder(() =>
            {
                lock (PinLockObj)
                {
                    Debug.Assert(PinLockedCounter >= 1);
                    PinLockedCounter--;
                    if (PinLockedCounter == 0)
                    {
                        PinHandle.Dispose();
                        PinHandle = default;
                    }
                }
            },
            LeakCounterKind.PinnedMemory);
        }

        static T dummyRefValue = default;
        public ref T GetRefForFixedPtr(int position = 0)
        {
            var span = this.Span.Slice(position);
            if (span.IsEmpty)
                return ref dummyRefValue;
            return ref span[0];
        }

        public static implicit operator MemoryBuffer<T>(Memory<T> memory) => new MemoryBuffer<T>(memory);
        public static implicit operator MemoryBuffer<T>(T[] array) => new MemoryBuffer<T>(array.AsMemory());
        public static implicit operator Memory<T>(MemoryBuffer<T> buf) => buf.Memory;
        public static implicit operator Span<T>(MemoryBuffer<T> buf) => buf.Span;
        public static implicit operator ReadOnlyMemory<T>(MemoryBuffer<T> buf) => buf.Memory;
        public static implicit operator ReadOnlySpan<T>(MemoryBuffer<T> buf) => buf.Span;
        public static implicit operator ReadOnlyMemoryBuffer<T>(MemoryBuffer<T> buf) => buf.AsReadOnly();
        public static implicit operator SpanBuffer<T>(MemoryBuffer<T> buf) => buf.AsSpanBuffer();
        public static implicit operator ReadOnlySpanBuffer<T>(MemoryBuffer<T> buf) => buf.AsReadOnlySpanBuffer();

        public MemoryBuffer<T> SliceAfter() => Slice(CurrentPosition);
        public MemoryBuffer<T> SliceBefore() => Slice(0, CurrentPosition);
        public MemoryBuffer<T> Slice(int start) => Slice(start, this.Length - start);
        public MemoryBuffer<T> Slice(int start, int length)
        {
            if (start < 0) throw new ArgumentOutOfRangeException("start < 0");
            if (length < 0) throw new ArgumentOutOfRangeException("length < 0");
            if (start > Length) throw new ArgumentOutOfRangeException("start > Size");
            if (checked(start + length) > Length) throw new ArgumentOutOfRangeException("length > Size");
            MemoryBuffer<T> ret = new MemoryBuffer<T>(this.InternalBuffer.Slice(start, length));
            ret.Length = length;
            ret.CurrentPosition = Math.Max(checked(CurrentPosition - start), 0);
            return ret;
        }

        public MemoryBuffer<T> Clone()
        {
            MemoryBuffer<T> ret = new MemoryBuffer<T>(InternalBuffer.ToArray());
            ret.Length = Length;
            ret.CurrentPosition = CurrentPosition;
            return ret;
        }

        public ReadOnlyMemoryBuffer<T> AsReadOnly()
        {
            ReadOnlyMemoryBuffer<T> ret = new ReadOnlyMemoryBuffer<T>(Memory);
            ret.Seek(CurrentPosition, SeekOrigin.Begin);
            return ret;
        }

        public SpanBuffer<T> AsSpanBuffer()
        {
            SpanBuffer<T> ret = new SpanBuffer<T>(Span);
            ret.Seek(CurrentPosition, SeekOrigin.Begin);
            return ret;
        }

        public ReadOnlySpanBuffer<T> AsReadOnlySpanBuffer()
        {
            ReadOnlySpanBuffer<T> ret = new ReadOnlySpanBuffer<T>(Span);
            ret.Seek(CurrentPosition, SeekOrigin.Begin);
            return ret;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Span<T> Walk(int size, bool noMove = false)
        {
            int newSize = checked(CurrentPosition + size);
            if (InternalBuffer.Length < newSize)
            {
                EnsureInternalBufferReserved(newSize);
            }
            var ret = InternalBuffer.Span.Slice(CurrentPosition, size);
            Length = Math.Max(newSize, Length);
            if (noMove == false) CurrentPosition += size;
            return ret;
        }

        public void Write(T[] data, int offset = 0, int? length = null) => Write(data.AsSpan(offset, length ?? data.Length - offset));
        public void Write(Memory<T> data) => Write(data.Span);
        public void Write(ReadOnlyMemory<T> data) => Write(data.Span);

        public void Write(Span<T> data)
        {
            var span = Walk(data.Length);
            data.CopyTo(span);
        }

        public void Write(ReadOnlySpan<T> data)
        {
            var span = Walk(data.Length);
            data.CopyTo(span);
        }

        public void WriteOne(T data)
        {
            var span = Walk(1);
            span[0] = data;
        }

        public T ReadOne()
        {
            var span = Read(1, false);
            return span[0];
        }

        public T PeekOne()
        {
            var span = Peek(1, false);
            return span[0];
        }

        public void WriteZero(int length)
        {
            var zeroSpan = ZeroedSharedMemory<T>.Memory.Span;
            while (length >= 1)
            {
                int currentSize = Math.Min(length, zeroSpan.Length);
                this.Write(zeroSpan.Slice(0, currentSize));
                length -= currentSize;
            }
        }

        public ReadOnlySpan<T> Read(int size, bool allowPartial = false)
        {
            int sizeRead = size;
            if (checked(CurrentPosition + size) > Length)
            {
                if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
                sizeRead = Length - CurrentPosition;
            }

            ReadOnlySpan<T> ret = InternalBuffer.Span.Slice(CurrentPosition, sizeRead);
            CurrentPosition += sizeRead;
            return ret;
        }

        public ReadOnlySpan<T> Peek(int size, bool allowPartial = false)
        {
            int sizeRead = size;
            if (checked(CurrentPosition + size) > Length)
            {
                if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
                sizeRead = Length - CurrentPosition;
            }

            Span<T> ret = InternalBuffer.Span.Slice(CurrentPosition, sizeRead);
            return ret;
        }

        public ReadOnlyMemory<T> ReadAsMemory(int size, bool allowPartial = false)
        {
            int sizeRead = size;
            if (checked(CurrentPosition + size) > Length)
            {
                if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
                sizeRead = Length - CurrentPosition;
            }

            ReadOnlyMemory<T> ret = InternalBuffer.Slice(CurrentPosition, sizeRead);
            CurrentPosition += sizeRead;
            return ret;
        }

        public ReadOnlyMemory<T> PeekAsMemory(int size, bool allowPartial = false)
        {
            int sizeRead = size;
            if (checked(CurrentPosition + size) > Length)
            {
                if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
                sizeRead = Length - CurrentPosition;
            }

            ReadOnlyMemory<T> ret = InternalBuffer.Slice(CurrentPosition, sizeRead);
            return ret;
        }

        public void Seek(int offset, SeekOrigin mode, bool allocate = false)
        {
            int newPosition = 0;
            if (mode == SeekOrigin.Current)
                newPosition = checked(CurrentPosition + offset);
            else if (mode == SeekOrigin.End)
                newPosition = checked(Length + offset);
            else
                newPosition = offset;

            if (newPosition < 0) throw new ArgumentOutOfRangeException("newPosition < 0");

            if (allocate == false)
            {
                if (newPosition > Length) throw new ArgumentOutOfRangeException("newPosition > Size");
            }
            else
            {
                EnsureInternalBufferReserved(newPosition);
                Length = Math.Max(newPosition, Length);
            }

            CurrentPosition = newPosition;
        }

        public void SetLength(int size)
        {
            if (size < 0) throw new ArgumentOutOfRangeException("size < 0");

            EnsureInternalBufferReserved(size);
            this.Length = size;
            if (this.CurrentPosition > this.Length)
                this.CurrentPosition = this.Length;
        }

        public MemoryBuffer<T> SeekToBegin()
        {
            Seek(0, SeekOrigin.Begin);
            return this;
        }

        public MemoryBuffer<T> SeekToEnd()
        {
            Seek(0, SeekOrigin.End);
            return this;
        }

        public int Read(Span<T> dest, int? size = null, bool allowPartial = false)
        {
            int size2 = size ?? dest.Length;
            if (dest.Length < size2) throw new ArgumentException("dest.Length < size");
            var span = Read(size2, allowPartial);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Peek(Span<T> dest, int? size = null, bool allowPartial = false)
        {
            int size2 = size ?? dest.Length;
            if (dest.Length < size2) throw new ArgumentException("dest.Length < size");
            var span = Peek(size2, allowPartial);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Read(T[] dest, int offset, int size, bool allowPartial = false) => Read(dest.AsSpan(offset, size), size, allowPartial);
        public int Peek(T[] dest, int offset, int size, bool allowPartial = false) => Peek(dest.AsSpan(offset, size), size, allowPartial);

        public int Read(Memory<T> dest, int? size = null, bool allowPartial = false)
        {
            int size2 = size ?? dest.Length;
            if (dest.Length < size2) throw new ArgumentException("dest.Length < size");
            var span = PeekAsMemory(size2, allowPartial);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Peek(Memory<T> dest, int? size = null, bool allowPartial = false)
        {
            int size2 = size ?? dest.Length;
            if (dest.Length < size2) throw new ArgumentException("dest.Length < size");
            var span = PeekAsMemory(size2, allowPartial);
            span.CopyTo(dest);
            return span.Length;
        }

        public void EnsureInternalBufferReserved(int newSize)
        {
            if (InternalBuffer.Length >= newSize) return;

            int newInternalSize = InternalBuffer.Length;
            while (newInternalSize < newSize)
                newInternalSize = checked(Math.Max(newInternalSize, 128) * 2);

            if (IsPinLocked()) throw new ApplicationException("Memory pin is locked.");

            InternalBuffer = InternalBuffer._ReAlloc(newInternalSize, this.Length);
        }

        public void OptimizeInternalBufferSize()
        {
            if (IsPinLocked()) throw new ApplicationException("Memory pin is locked.");

            int newInternalSize = 128;
            while (newInternalSize < this.Length)
                newInternalSize = checked(newInternalSize * 2);

            if (this.InternalBuffer.Length > newInternalSize)
                InternalBuffer = InternalBuffer._ReAlloc(newInternalSize, this.Length);
        }

        public void Clear()
        {
            if (IsPinLocked()) throw new ApplicationException("Memory pin is locked.");

            InternalBuffer = new Memory<T>();
            CurrentPosition = 0;
            Length = 0;
        }

        public ReadOnlySpan<T> Read(long size, bool allowPartial = false)
            => Read(checked((int)size), allowPartial);

        public ReadOnlySpan<T> Peek(long size, bool allowPartial = false)
            => Peek(checked((int)size), allowPartial);

        public void Seek(long offset, SeekOrigin mode, bool allocate = false)
            => Seek(checked((int)offset), mode, allocate);

        public void SetLength(long size)
            => SetLength(checked((int)size));
    }

    class ReadOnlyMemoryBuffer<T> : IBuffer<T>
    {
        ReadOnlyMemory<T> InternalBuffer;
        public int CurrentPosition { get; private set; }
        public int Length { get; private set; }
        public bool IsThisEmpty() => Length == 0;
        public int InternalBufferSize => InternalBuffer.Length;

        public ReadOnlyMemory<T> Memory { get => InternalBuffer.Slice(0, Length); }
        public ReadOnlyMemory<T> MemoryBefore { get => Memory.Slice(0, CurrentPosition); }
        public ReadOnlyMemory<T> MemoryAfter { get => Memory.Slice(CurrentPosition); }

        public ReadOnlySpan<T> Span { get => InternalBuffer.Slice(0, Length).Span; }
        public ReadOnlySpan<T> SpanBefore { get => Memory.Slice(0, CurrentPosition).Span; }
        public ReadOnlySpan<T> SpanAfter { get => Memory.Slice(CurrentPosition).Span; }

        public long LongCurrentPosition => this.CurrentPosition;

        public long LongLength => this.Length;

        public long LongInternalBufferSize => this.InternalBufferSize;

        public ReadOnlyMemoryBuffer(int size = 0) : this(new T[size]) { }

        public ReadOnlyMemoryBuffer(ReadOnlyMemory<T> baseMemory)
        {
            InternalBuffer = baseMemory;
            CurrentPosition = 0;
            Length = baseMemory.Length;
        }

        public static ReadOnlyMemoryBuffer<byte> FromStruct<TStruct>(TStruct src)
        {
            Memory<byte> baseMemory = new byte[Util.SizeOfStruct<TStruct>()];
            ref TStruct dst = ref baseMemory._AsStruct<TStruct>();
            dst = src;
            return new ReadOnlyMemoryBuffer<byte>(baseMemory);
        }

        CriticalSection PinLockObj = new CriticalSection();
        int PinLockedCounter = 0;
        MemoryHandle PinHandle;
        public bool IsPinLocked() => (PinLockedCounter != 0);
        public ValueHolder PinLock()
        {
            lock (PinLockObj)
            {
                if (PinLockedCounter == 0)
                    PinHandle = InternalBuffer.Pin();
                PinLockedCounter++;
            }

            return new ValueHolder(() =>
            {
                lock (PinLockObj)
                {
                    Debug.Assert(PinLockedCounter >= 1);
                    PinLockedCounter--;
                    if (PinLockedCounter == 0)
                    {
                        PinHandle.Dispose();
                        PinHandle = default;
                    }
                }
            },
            LeakCounterKind.PinnedMemory);
        }

        static T dummyRefValue = default;
        public ref readonly T GetRefForFixedPtr(int position = 0)
        {
            var span = this.Span.Slice(position);
            if (span.IsEmpty)
                return ref dummyRefValue;
            return ref span[0];
        }

        public static implicit operator ReadOnlyMemoryBuffer<T>(ReadOnlyMemory<T> memory) => new ReadOnlyMemoryBuffer<T>(memory);
        public static implicit operator ReadOnlyMemoryBuffer<T>(T[] array) => new ReadOnlyMemoryBuffer<T>(array.AsMemory());
        public static implicit operator ReadOnlyMemory<T>(ReadOnlyMemoryBuffer<T> buf) => buf.Memory;
        public static implicit operator ReadOnlySpan<T>(ReadOnlyMemoryBuffer<T> buf) => buf.Span;
        public static implicit operator ReadOnlySpanBuffer<T>(ReadOnlyMemoryBuffer<T> buf) => buf.AsReadOnlySpanBuffer();

        public ReadOnlyMemoryBuffer<T> SliceAfter() => Slice(CurrentPosition);
        public ReadOnlyMemoryBuffer<T> SliceBefore() => Slice(0, CurrentPosition);
        public ReadOnlyMemoryBuffer<T> Slice(int start) => Slice(start, this.Length - start);
        public ReadOnlyMemoryBuffer<T> Slice(int start, int length)
        {
            if (start < 0) throw new ArgumentOutOfRangeException("start < 0");
            if (length < 0) throw new ArgumentOutOfRangeException("length < 0");
            if (start > Length) throw new ArgumentOutOfRangeException("start > Size");
            if (checked(start + length) > Length) throw new ArgumentOutOfRangeException("length > Size");
            ReadOnlyMemoryBuffer<T> ret = new ReadOnlyMemoryBuffer<T>(this.InternalBuffer.Slice(start, length));
            ret.Length = length;
            ret.CurrentPosition = Math.Max(checked(CurrentPosition - start), 0);
            return ret;
        }

        public ReadOnlyMemoryBuffer<T> Clone()
        {
            ReadOnlyMemoryBuffer<T> ret = new ReadOnlyMemoryBuffer<T>(InternalBuffer.ToArray());
            ret.Length = Length;
            ret.CurrentPosition = CurrentPosition;
            return ret;
        }

        public MemoryBuffer<T> CloneAsWritable()
        {
            MemoryBuffer<T> ret = new MemoryBuffer<T>(Span.ToArray());
            ret.Seek(CurrentPosition, SeekOrigin.Begin);
            return ret;
        }

        public ReadOnlySpanBuffer<T> AsReadOnlySpanBuffer()
        {
            ReadOnlySpanBuffer<T> ret = new ReadOnlySpanBuffer<T>(Span);
            ret.Seek(CurrentPosition, SeekOrigin.Begin);
            return ret;
        }

        public ReadOnlySpan<T> Read(int size, bool allowPartial = false)
        {
            int sizeRead = size;
            if (checked(CurrentPosition + size) > Length)
            {
                if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
                sizeRead = Length - CurrentPosition;
            }

            ReadOnlySpan<T> ret = InternalBuffer.Span.Slice(CurrentPosition, sizeRead);
            CurrentPosition += sizeRead;
            return ret;
        }

        public ReadOnlySpan<T> Peek(int size, bool allowPartial = false)
        {
            int sizeRead = size;
            if (checked(CurrentPosition + size) > Length)
            {
                if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
                sizeRead = Length - CurrentPosition;
            }

            ReadOnlySpan<T> ret = InternalBuffer.Span.Slice(CurrentPosition, sizeRead);
            return ret;
        }

        public ReadOnlyMemory<T> ReadAsMemory(int size, bool allowPartial = false)
        {
            int sizeRead = size;
            if (checked(CurrentPosition + size) > Length)
            {
                if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
                sizeRead = Length - CurrentPosition;
            }

            ReadOnlyMemory<T> ret = InternalBuffer.Slice(CurrentPosition, sizeRead);
            CurrentPosition += sizeRead;
            return ret;
        }

        public ReadOnlyMemory<T> PeekAsMemory(int size, bool allowPartial = false)
        {
            int sizeRead = size;
            if (checked(CurrentPosition + size) > Length)
            {
                if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
                sizeRead = Length - CurrentPosition;
            }

            ReadOnlyMemory<T> ret = InternalBuffer.Slice(CurrentPosition, sizeRead);
            return ret;
        }

        public void Seek(int offset, SeekOrigin mode, bool allocate = false)
        {
            int newPosition = 0;
            if (mode == SeekOrigin.Current)
                newPosition = checked(CurrentPosition + offset);
            else if (mode == SeekOrigin.End)
                newPosition = checked(Length + offset);
            else
                newPosition = offset;

            if (newPosition < 0) throw new ArgumentOutOfRangeException("newPosition < 0");
            if (newPosition > Length) throw new ArgumentOutOfRangeException("newPosition > Size");

            CurrentPosition = newPosition;
        }

        public ReadOnlySpanBuffer<T> SeekToBegin()
        {
            Seek(0, SeekOrigin.Begin);
            return this;
        }

        public ReadOnlySpanBuffer<T> SeekToEnd()
        {
            Seek(0, SeekOrigin.End);
            return this;
        }

        public int Read(Span<T> dest, int? size = null, bool allowPartial = false)
        {
            int size2 = size ?? dest.Length;
            if (dest.Length < size2) throw new ArgumentException("dest.Length < size");
            var span = Read(size2, allowPartial);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Peek(Span<T> dest, int? size = null, bool allowPartial = false)
        {
            int size2 = size ?? dest.Length;
            if (dest.Length < size2) throw new ArgumentException("dest.Length < size");
            var span = Peek(size2, allowPartial);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Read(T[] dest, int offset, int size, bool allowPartial = false) => Read(dest.AsSpan(offset, size), size, allowPartial);
        public int Peek(T[] dest, int offset, int size, bool allowPartial = false) => Peek(dest.AsSpan(offset, size), size, allowPartial);

        public int Read(Memory<T> dest, int? size = null, bool allowPartial = false)
        {
            int size2 = size ?? dest.Length;
            if (dest.Length < size2) throw new ArgumentException("dest.Length < size");
            var span = ReadAsMemory(size2, allowPartial);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Peek(Memory<T> dest, int? size = null, bool allowPartial = false)
        {
            int size2 = size ?? dest.Length;
            if (dest.Length < size2) throw new ArgumentException("dest.Length < size");
            var span = PeekAsMemory(size2, allowPartial);
            span.CopyTo(dest);
            return span.Length;
        }

        void IBuffer<T>.Clear() => throw new NotSupportedException();

        void IBuffer<T>.Write(ReadOnlySpan<T> data) => throw new NotSupportedException();

        public ReadOnlySpan<T> Read(long size, bool allowPartial = false)
            => Read(checked((int)size), allowPartial);

        public ReadOnlySpan<T> Peek(long size, bool allowPartial = false)
            => Peek(checked((int)size), allowPartial);

        public void Seek(long offset, SeekOrigin mode, bool allocate = false)
            => Seek(checked((int)offset), mode, allocate);

        public void SetLength(long size)
            => SetLength(checked((int)size));

        public T ReadOne()
        {
            var span = Read(1, false);
            return span[0];
        }

        public T PeekOne()
        {
            var span = Peek(1, false);
            return span[0];
        }

    }

    class HugeMemoryBufferOptions
    {
        public readonly long SegmentSize;

        public HugeMemoryBufferOptions(long? segmentSize = null)
        {
            this.SegmentSize = Math.Max(1, segmentSize ?? CoresConfig.LargeMemoryBuffer.DefaultSegmentSize.Value);
        }
    }

    class HugeMemoryBuffer<T> : IEmptyChecker, IBuffer<T>, IRandomAccess<T>
    {
        public readonly HugeMemoryBufferOptions Options;

        Dictionary<long, MemoryBuffer<T>> Segments = new Dictionary<long, MemoryBuffer<T>>();
        public long CurrentPosition { get; private set; } = 0;
        public long Length { get; private set; } = 0;
        public bool IsThisEmpty() => Length == 0;

        public long PhysicalSize => this.Segments.Values.Select(x => (long)x.InternalBufferSize).Sum();

        public long LongCurrentPosition => this.CurrentPosition;
        public long LongLength => this.Length;
        public long LongInternalBufferSize => PhysicalSize;

        public AsyncLock SharedAsyncLock { get; } = new AsyncLock();

        public HugeMemoryBuffer(HugeMemoryBufferOptions options = null, Memory<T> initialContents = default)
        {
            checked
            {
                this.Options = options ?? new HugeMemoryBufferOptions();

                if (this.Options.SegmentSize <= 0) throw new ArgumentOutOfRangeException("Options.SegmentSize <= 0");

                if (initialContents.IsEmpty == false)
                {
                    Debug.Assert(Segments.Count == 0);

                    long totalSize = 0;
                    DividedSegmentList segList = new DividedSegmentList(0, initialContents.Length, this.Options.SegmentSize);
                    foreach (var seg in segList.SegmentList)
                    {
                        totalSize += seg.Size;
                        Segments.Add(seg.SegmentIndex, new MemoryBuffer<T>(initialContents.Slice((int)seg.RelativePosition, (int)seg.Size)));
                    }
                    Debug.Assert(totalSize == initialContents.Length);
                    Length = initialContents.Length;
                }
            }
        }

        public static implicit operator HugeMemoryBuffer<T>(ReadOnlyMemory<T> readOnlyMemory) => new HugeMemoryBuffer<T>(initialContents: readOnlyMemory.ToArray());
        public static implicit operator HugeMemoryBuffer<T>(Memory<T> memory) => new HugeMemoryBuffer<T>(initialContents: memory);
        public static implicit operator HugeMemoryBuffer<T>(T[] array) => new HugeMemoryBuffer<T>(initialContents: array);

        public void Write(T[] data, int offset = 0, int? length = null) => Write(data.AsMemory(offset, length ?? data.Length - offset));
        public void Write(ReadOnlyMemory<T> data) => Write(data.Span);

        public void WriteRandom(long position, ReadOnlySpan<T> data)
        {
            if (position < 0) throw new ArgumentOutOfRangeException("position < 0");
            checked
            {
                long start = position;

                DividedSegmentList segList = new DividedSegmentList(start, data.Length, this.Options.SegmentSize);
                foreach (DividedSegment seg in segList.SegmentList)
                {
                    var srcSpan = data.Slice((int)seg.RelativePosition, (int)seg.Size);
                    var destSpan = PrepareSegment(in seg);

                    Debug.Assert(srcSpan.Length == destSpan.Length);
                    Debug.Assert(srcSpan.Length >= 1);

                    srcSpan.CopyTo(destSpan);
                }

                this.Length = Math.Max(this.Length, start + data.Length);
            }
        }

        public void Write(ReadOnlySpan<T> data)
        {
            checked
            {
                WriteRandom(this.CurrentPosition, data);

                this.CurrentPosition += data.Length;
            }
        }

        Span<T> PrepareSegment(in DividedSegment seg)
        {
            checked
            {
                long segIndex = seg.AbsolutePosition / this.Options.SegmentSize;
                if (this.Segments.TryGetValue(segIndex, out MemoryBuffer<T> segment))
                {
                    if (segment.Length < (seg.InSegmentOffset + seg.Size))
                        segment.SetLength((int)(seg.InSegmentOffset + seg.Size));
                }
                else
                {
                    segment = new MemoryBuffer<T>((int)(seg.InSegmentOffset + seg.Size));
                    this.Segments.Add(segIndex, segment);
                }
                return segment.Span.Slice((int)seg.InSegmentOffset, (int)seg.Size);
            }
        }

        public void WriteZeroRandom(long position, long length)
        {
            checked
            {
                if (position < 0) throw new ArgumentOutOfRangeException("position < 0");
                if (length < 0) throw new ArgumentOutOfRangeException("length");
                if (length == 0) return;

                long start = position;

                DividedSegmentList segList = new DividedSegmentList(start, length, this.Options.SegmentSize);
                foreach (DividedSegment seg in segList.SegmentList)
                {
                    var destSpan = PrepareSegment(in seg);

                    Debug.Assert(destSpan.Length >= 1);

                    destSpan.Fill(default);
                }

                this.Length = Math.Max(this.Length, start + length);
            }
        }

        public void WriteZero(long length)
        {
            checked
            {
                if (length < 0) throw new ArgumentOutOfRangeException("length");
                if (length == 0) return;

                WriteZeroRandom(this.CurrentPosition, length);

                this.CurrentPosition += length;
            }
        }

        public IReadOnlyList<SparseChunk<T>> ReadRandomFast(long start, long size, out long readSize, bool allowPartial = false)
        {
            checked
            {
                if (start < 0) throw new ArgumentOutOfRangeException("start");
                if (size < 0) throw new ArgumentOutOfRangeException("size");
                if (start > this.Length) throw new ArgumentException("start > length");

                long end = start + size;

                if (end > this.Length)
                {
                    if (allowPartial == false)
                        throw new ArgumentException("start + size > length");
                    size = this.Length - start;
                    end = this.Length;
                }

                List<SparseChunk<T>> ret = new List<SparseChunk<T>>();

                DividedSegmentList segList = new DividedSegmentList(start, size, this.Options.SegmentSize);
                foreach (DividedSegment seg in segList.SegmentList)
                {
                    long segIndex = seg.AbsolutePosition / this.Options.SegmentSize;
                    if (this.Segments.TryGetValue(segIndex, out MemoryBuffer<T> segment))
                    {
                        if (seg.InSegmentOffset >= segment.Length)
                        {
                            ret.Add(new SparseChunk<T>(seg.RelativePosition, (int)seg.Size));
                        }
                        else
                        {
                            if (segment.Length >= (seg.InSegmentOffset + seg.Size))
                            {
                                ret.Add(new SparseChunk<T>(seg.RelativePosition, segment.Memory.Slice((int)seg.InSegmentOffset, (int)seg.Size)));
                            }
                            else
                            {
                                ret.Add(new SparseChunk<T>(seg.RelativePosition, segment.Memory.Slice((int)seg.InSegmentOffset, (int)(segment.Length - seg.InSegmentOffset))));
                                ret.Add(new SparseChunk<T>(seg.RelativePosition, (int)(seg.Size - (segment.Length - seg.InSegmentOffset))));
                            }
                        }
                    }
                    else
                    {
                        ret.Add(new SparseChunk<T>(seg.RelativePosition, (int)seg.Size));
                    }
                }

                Debug.Assert(ret.Select(x => x.Size).Sum() == size);

                readSize = size;

                return ret;
            }
        }

        public IReadOnlyList<SparseChunk<T>> ReadFast(long size, out long readSize, bool allowPartial = false)
        {
            long sizeToRead = size;
            if (checked(CurrentPosition + size) > Length)
            {
                if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
                sizeToRead = Length - CurrentPosition;
            }

            var ret = this.ReadRandomFast(CurrentPosition, size, out readSize, allowPartial);

            Debug.Assert(readSize == sizeToRead);

            CurrentPosition += sizeToRead;

            return ret;
        }

        public IReadOnlyList<SparseChunk<T>> PeekFast(long size, out long readSize, bool allowPartial = false)
        {
            long sizeToRead = size;
            if (checked(CurrentPosition + size) > Length)
            {
                if (allowPartial == false) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
                sizeToRead = Length - CurrentPosition;
            }

            var ret = this.ReadRandomFast(CurrentPosition, size, out readSize, allowPartial);

            Debug.Assert(readSize == sizeToRead);

            return ret;
        }

        public int Read(Span<T> dest, bool allowPartial = false)
        {
            checked
            {
                long readSize2 = Util.CopySparseChunkListToSpan(ReadFast(dest.Length, out long readSize1, allowPartial), dest);
                Debug.Assert(readSize1 == readSize2);
                return (int)readSize1;
            }
        }

        public int Peek(Span<T> dest, bool allowPartial = false)
        {
            checked
            {
                long readSize2 = Util.CopySparseChunkListToSpan(PeekFast(dest.Length, out long readSize1, allowPartial), dest);
                Debug.Assert(readSize1 == readSize2);
                return (int)readSize1;
            }
        }

        public ReadOnlySpan<T> Read(int size, bool allowPartial = false)
        {
            checked
            {
                long remainSize = this.Length - this.CurrentPosition;
                if (allowPartial == false && remainSize < size) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
                int sizeToRead = (int)Math.Min(remainSize, size);

                var chunkList = ReadFast(sizeToRead, out long readSize1, allowPartial);
                Debug.Assert(readSize1 == sizeToRead);

                if (chunkList.Count == 1)
                {
                    // No need to copy
                    return chunkList[0].GetMemoryOrGenerateSparse().Span;
                }

                Span<T> ret = new T[sizeToRead];
                long retSize = Util.CopySparseChunkListToSpan(chunkList, ret);
                Debug.Assert(retSize == sizeToRead);
                return ret;
            }
        }

        public ReadOnlySpan<T> Peek(int size, bool allowPartial = false)
        {
            checked
            {
                long remainSize = this.Length - this.CurrentPosition;
                if (allowPartial == false && remainSize < size) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
                int sizeToRead = (int)Math.Min(remainSize, size);

                var chunkList = PeekFast(sizeToRead, out long readSize1, allowPartial);
                Debug.Assert(readSize1 == sizeToRead);

                if (chunkList.Count == 1)
                {
                    // No need to copy
                    return chunkList[0].GetMemoryOrGenerateSparse().Span;
                }

                Span<T> ret = new T[sizeToRead];
                long retSize = Util.CopySparseChunkListToSpan(chunkList, ret);
                Debug.Assert(retSize == sizeToRead);
                return ret;
            }
        }

        public ReadOnlyMemory<T> ReadAsMemory(int size, bool allowPartial = false)
        {
            checked
            {
                long remainSize = this.Length - this.CurrentPosition;
                if (allowPartial == false && remainSize < size) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
                int sizeToRead = (int)Math.Min(remainSize, size);

                var chunkList = ReadFast(sizeToRead, out long readSize1, allowPartial);
                Debug.Assert(readSize1 == sizeToRead);

                if (chunkList.Count == 1)
                {
                    // No need to copy
                    return chunkList[0].GetMemoryOrGenerateSparse();
                }

                Memory<T> ret = new T[sizeToRead];
                long retSize = Util.CopySparseChunkListToSpan(chunkList, ret.Span);
                Debug.Assert(retSize == sizeToRead);
                return ret;
            }
        }

        public ReadOnlyMemory<T> PeekAsMemory(int size, bool allowPartial = false)
        {
            checked
            {
                long remainSize = this.Length - this.CurrentPosition;
                if (allowPartial == false && remainSize < size) throw new ArgumentOutOfRangeException("(CurrentPosition + size) > Size");
                int sizeToRead = (int)Math.Min(remainSize, size);

                var chunkList = PeekFast(sizeToRead, out long readSize1, allowPartial);
                Debug.Assert(readSize1 == sizeToRead);

                if (chunkList.Count == 1)
                {
                    // No need to copy
                    return chunkList[0].GetMemoryOrGenerateSparse();
                }

                Memory<T> ret = new T[sizeToRead];
                long retSize = Util.CopySparseChunkListToSpan(chunkList, ret.Span);
                Debug.Assert(retSize == sizeToRead);
                return ret;
            }
        }

        public void Seek(long offset, SeekOrigin mode, bool allocate = false)
        {
            checked
            {
                long newPosition = 0;
                if (mode == SeekOrigin.Current)
                    newPosition = checked(CurrentPosition + offset);
                else if (mode == SeekOrigin.End)
                    newPosition = checked(Length + offset);
                else
                    newPosition = offset;

                if (newPosition < 0) throw new ArgumentOutOfRangeException("newPosition < 0");

                if (allocate == false)
                {
                    if (newPosition > Length) throw new ArgumentOutOfRangeException("newPosition > Size");
                }
                else
                {
                    Length = Math.Max(newPosition, Length);
                }

                CurrentPosition = newPosition;
            }
        }
        public HugeMemoryBuffer<T> SeekToBegin()
        {
            Seek(0, SeekOrigin.Begin);
            return this;
        }
        public HugeMemoryBuffer<T> SeekToEnd()
        {
            Seek(0, SeekOrigin.End);
            return this;
        }

        public void Clear() => SetLength(0);

        public void SetLength(long size)
        {
            checked
            {
                if (size < 0) throw new ArgumentOutOfRangeException("size");

                if (this.Length > size)
                {
                    // Shrinking
                    if (size == 0)
                    {
                        this.Segments.Clear();
                    }
                    else
                    {
                        // Get the last segment information
                        DividedSegmentList segList = new DividedSegmentList(size - 1, 1, this.Options.SegmentSize);
                        Debug.Assert(segList.SegmentList.Length == 1);
                        DividedSegment lastSegInfo = segList.SegmentList[0];

                        // delete all unnecessary segments following the last segment
                        var deleteList = this.Segments.Keys.Where(x => x > lastSegInfo.SegmentIndex).ToArray();
                        foreach (var index in deleteList)
                            this.Segments.Remove(index);

                        // shrink the last segment if necessary
                        if (this.Segments.TryGetValue(lastSegInfo.SegmentIndex, out MemoryBuffer<T> lastSegment))
                        {
                            if (lastSegment.Length > lastSegInfo.Size)
                            {
                                lastSegment.SetLength((int)lastSegInfo.Size);
                                lastSegment.OptimizeInternalBufferSize();
                            }
                        }
                    }
                }

                this.Length = size;
                if (this.CurrentPosition > this.Length)
                    this.CurrentPosition = this.Length;
            }
        }

        public ReadOnlySpan<T> Read(long size, bool allowPartial = false)
            => Read(checked((int)size), allowPartial);

        public ReadOnlySpan<T> Peek(long size, bool allowPartial = false)
            => Peek(checked((int)size), allowPartial);

        public Task<int> ReadRandomAsync(long position, Memory<T> data, CancellationToken cancel = default)
            => Task.FromResult(ReadRandom(position, data, cancel));

        public int ReadRandom(long position, Memory<T> data, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            long readSize2 = Util.CopySparseChunkListToSpan(ReadRandomFast(position, data.Length, out long readSize1, true), data.Span);
            Debug.Assert(readSize1 == readSize2);
            return (int)readSize1;
        }

        public Task WriteRandomAsync(long position, ReadOnlyMemory<T> data, CancellationToken cancel = default)
        {
            WriteRandom(position, data, cancel);
            return Task.CompletedTask;
        }

        public void WriteRandom(long position, ReadOnlyMemory<T> data, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            WriteRandom(position, data.Span);
        }

        public Task AppendAsync(ReadOnlyMemory<T> data, CancellationToken cancel = default)
        {
            Append(data, cancel);
            return Task.CompletedTask;
        }

        public void Append(ReadOnlyMemory<T> data, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            WriteRandom(this.CurrentPosition, data.Span);
        }

        public Task<long> GetFileSizeAsync(bool refresh = false, CancellationToken cancel = default)
            => Task.FromResult(GetFileSize(refresh, cancel));

        public long GetFileSize(bool refresh = false, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            return this.Length;
        }

        public Task SetFileSizeAsync(long size, CancellationToken cancel = default)
        {
            SetFileSize(size, cancel);
            return Task.CompletedTask;
        }

        public void SetFileSize(long size, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            this.SetLength(size);
        }

        public Task FlushAsync(CancellationToken cancel = default)
        {
            Flush(cancel);
            return Task.CompletedTask;
        }

        public void Flush(CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
        }

        public void Dispose() { }

        public Task<long> GetPhysicalSizeAsync(CancellationToken cancel = default)
        {
            return Task.FromResult(GetPhysicalSize(cancel));
        }

        public long GetPhysicalSize(CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            return this.PhysicalSize;
        }

        Memory<T> tmpArray = new T[1];

        public void WriteOne(T data)
        {
            tmpArray.Span[0] = data;

            Write(tmpArray);
        }

        public T ReadOne()
        {
            var span = Read(1, false);
            return span[0];
        }

        public T PeekOne()
        {
            var span = Peek(1, false);
            return span[0];
        }
    }

    static class SpanMemoryBufferHelper
    {
        public static void WriteBool8(this ref SpanBuffer<byte> buf, bool value) => value._SetBool8(buf.Walk(1, false));
        public static void WriteUInt8(this ref SpanBuffer<byte> buf, byte value) => value._SetUInt8(buf.Walk(1, false));
        public static void WriteByte(this ref SpanBuffer<byte> buf, byte value) => value._SetUInt8(buf.Walk(1, false));
        public static void WriteUInt16(this ref SpanBuffer<byte> buf, ushort value) => value._SetUInt16(buf.Walk(2, false));
        public static void WriteUInt32(this ref SpanBuffer<byte> buf, uint value) => value._SetUInt32(buf.Walk(4, false));
        public static void WriteUInt64(this ref SpanBuffer<byte> buf, ulong value) => value._SetUInt64(buf.Walk(8, false));
        public static void WriteSInt8(this ref SpanBuffer<byte> buf, sbyte value) => value._SetSInt8(buf.Walk(1, false));
        public static void WriteSInt16(this ref SpanBuffer<byte> buf, short value) => value._SetSInt16(buf.Walk(2, false));
        public static void WriteSInt32(this ref SpanBuffer<byte> buf, int value) => value._SetSInt32(buf.Walk(4, false));
        public static void WriteSInt64(this ref SpanBuffer<byte> buf, long value) => value._SetSInt64(buf.Walk(8, false));

        public static void SetBool8(this ref SpanBuffer<byte> buf, bool value) => value._SetBool8(buf.Walk(1, true));
        public static void SetUInt8(this ref SpanBuffer<byte> buf, byte value) => value._SetUInt8(buf.Walk(1, true));
        public static void SetUInt16(this ref SpanBuffer<byte> buf, ushort value) => value._SetUInt16(buf.Walk(2, true));
        public static void SetUInt32(this ref SpanBuffer<byte> buf, uint value) => value._SetUInt32(buf.Walk(4, true));
        public static void SetUInt64(this ref SpanBuffer<byte> buf, ulong value) => value._SetUInt64(buf.Walk(8, true));
        public static void SetSInt8(this ref SpanBuffer<byte> buf, sbyte value) => value._SetSInt8(buf.Walk(1, true));
        public static void SetSInt16(this ref SpanBuffer<byte> buf, short value) => value._SetSInt16(buf.Walk(2, true));
        public static void SetSInt32(this ref SpanBuffer<byte> buf, int value) => value._SetSInt32(buf.Walk(4, true));
        public static void SetSInt64(this ref SpanBuffer<byte> buf, long value) => value._SetSInt64(buf.Walk(8, true));

        public static bool ReadBool8(ref this SpanBuffer<byte> buf) => buf.Read(1)._GetBool8();
        public static byte ReadUInt8(ref this SpanBuffer<byte> buf) => buf.Read(1)._GetUInt8();
        public static byte ReadByte(ref this SpanBuffer<byte> buf) => buf.Read(1)._GetUInt8();
        public static ushort ReadUInt16(ref this SpanBuffer<byte> buf) => buf.Read(2)._GetUInt16();
        public static uint ReadUInt32(ref this SpanBuffer<byte> buf) => buf.Read(4)._GetUInt32();
        public static ulong ReadUInt64(ref this SpanBuffer<byte> buf) => buf.Read(8)._GetUInt64();
        public static sbyte ReadSInt8(ref this SpanBuffer<byte> buf) => buf.Read(1)._GetSInt8();
        public static short ReadSInt16(ref this SpanBuffer<byte> buf) => buf.Read(2)._GetSInt16();
        public static int ReadSInt32(ref this SpanBuffer<byte> buf) => buf.Read(4)._GetSInt32();
        public static long ReadSInt64(ref this SpanBuffer<byte> buf) => buf.Read(8)._GetSInt64();

        public static bool PeekBool8(ref this SpanBuffer<byte> buf) => buf.Peek(1)._GetBool8();
        public static byte PeekUInt8(ref this SpanBuffer<byte> buf) => buf.Peek(1)._GetUInt8();
        public static ushort PeekUInt16(ref this SpanBuffer<byte> buf) => buf.Peek(2)._GetUInt16();
        public static uint PeekUInt32(ref this SpanBuffer<byte> buf) => buf.Peek(4)._GetUInt32();
        public static ulong PeekUInt64(ref this SpanBuffer<byte> buf) => buf.Peek(8)._GetUInt64();
        public static sbyte PeekSInt8(ref this SpanBuffer<byte> buf) => buf.Peek(1)._GetSInt8();
        public static short PeekSInt16(ref this SpanBuffer<byte> buf) => buf.Peek(2)._GetSInt16();
        public static int PeekSInt32(ref this SpanBuffer<byte> buf) => buf.Peek(4)._GetSInt32();
        public static long PeekSInt64(ref this SpanBuffer<byte> buf) => buf.Peek(8)._GetSInt64();


        public static bool ReadBool8(ref this ReadOnlySpanBuffer<byte> buf) => buf.Read(1)._GetBool8();
        public static byte ReadUInt8(ref this ReadOnlySpanBuffer<byte> buf) => buf.Read(1)._GetUInt8();
        public static byte ReadByte(ref this ReadOnlySpanBuffer<byte> buf) => buf.Read(1)._GetUInt8();
        public static ushort ReadUInt16(ref this ReadOnlySpanBuffer<byte> buf) => buf.Read(2)._GetUInt16();
        public static uint ReadUInt32(ref this ReadOnlySpanBuffer<byte> buf) => buf.Read(4)._GetUInt32();
        public static ulong ReadUInt64(ref this ReadOnlySpanBuffer<byte> buf) => buf.Read(8)._GetUInt64();
        public static sbyte ReadSInt8(ref this ReadOnlySpanBuffer<byte> buf) => buf.Read(1)._GetSInt8();
        public static short ReadSInt16(ref this ReadOnlySpanBuffer<byte> buf) => buf.Read(2)._GetSInt16();
        public static int ReadSInt32(ref this ReadOnlySpanBuffer<byte> buf) => buf.Read(4)._GetSInt32();
        public static long ReadSInt64(ref this ReadOnlySpanBuffer<byte> buf) => buf.Read(8)._GetSInt64();

        public static bool PeekBool8(ref this ReadOnlySpanBuffer<byte> buf) => buf.Peek(1)._GetBool8();
        public static byte PeekUInt8(ref this ReadOnlySpanBuffer<byte> buf) => buf.Peek(1)._GetUInt8();
        public static ushort PeekUInt16(ref this ReadOnlySpanBuffer<byte> buf) => buf.Peek(2)._GetUInt16();
        public static uint PeekUInt32(ref this ReadOnlySpanBuffer<byte> buf) => buf.Peek(4)._GetUInt32();
        public static ulong PeekUInt64(ref this ReadOnlySpanBuffer<byte> buf) => buf.Peek(8)._GetUInt64();
        public static sbyte PeekSInt8(ref this ReadOnlySpanBuffer<byte> buf) => buf.Peek(1)._GetSInt8();
        public static short PeekSInt16(ref this ReadOnlySpanBuffer<byte> buf) => buf.Peek(2)._GetSInt16();
        public static int PeekSInt32(ref this ReadOnlySpanBuffer<byte> buf) => buf.Peek(4)._GetSInt32();
        public static long PeekSInt64(ref this ReadOnlySpanBuffer<byte> buf) => buf.Peek(8)._GetSInt64();


        public static void WriteBool8(this ref FastMemoryBuffer<byte> buf, bool value) => value._SetBool8(buf.Walk(1, false));
        public static void WriteUInt8(this ref FastMemoryBuffer<byte> buf, byte value) => value._SetUInt8(buf.Walk(1, false));
        public static void WriteByte(this ref FastMemoryBuffer<byte> buf, byte value) => value._SetUInt8(buf.Walk(1, false));
        public static void WriteUInt16(this ref FastMemoryBuffer<byte> buf, ushort value) => value._SetUInt16(buf.Walk(2, false));
        public static void WriteUInt32(this ref FastMemoryBuffer<byte> buf, uint value) => value._SetUInt32(buf.Walk(4, false));
        public static void WriteUInt64(this ref FastMemoryBuffer<byte> buf, ulong value) => value._SetUInt64(buf.Walk(8, false));
        public static void WriteSInt8(this ref FastMemoryBuffer<byte> buf, sbyte value) => value._SetSInt8(buf.Walk(1, false));
        public static void WriteSInt16(this ref FastMemoryBuffer<byte> buf, short value) => value._SetSInt16(buf.Walk(2, false));
        public static void WriteSInt32(this ref FastMemoryBuffer<byte> buf, int value) => value._SetSInt32(buf.Walk(4, false));
        public static void WriteSInt64(this ref FastMemoryBuffer<byte> buf, long value) => value._SetSInt64(buf.Walk(8, false));

        public static void SetBool8(this ref FastMemoryBuffer<byte> buf, bool value) => value._SetBool8(buf.Walk(1, true));
        public static void SetUInt8(this ref FastMemoryBuffer<byte> buf, byte value) => value._SetUInt8(buf.Walk(1, true));
        public static void SetUInt16(this ref FastMemoryBuffer<byte> buf, ushort value) => value._SetUInt16(buf.Walk(2, true));
        public static void SetUInt32(this ref FastMemoryBuffer<byte> buf, uint value) => value._SetUInt32(buf.Walk(4, true));
        public static void SetUInt64(this ref FastMemoryBuffer<byte> buf, ulong value) => value._SetUInt64(buf.Walk(8, true));
        public static void SetSInt8(this ref FastMemoryBuffer<byte> buf, sbyte value) => value._SetSInt8(buf.Walk(1, true));
        public static void SetSInt16(this ref FastMemoryBuffer<byte> buf, short value) => value._SetSInt16(buf.Walk(2, true));
        public static void SetSInt32(this ref FastMemoryBuffer<byte> buf, int value) => value._SetSInt32(buf.Walk(4, true));
        public static void SetSInt64(this ref FastMemoryBuffer<byte> buf, long value) => value._SetSInt64(buf.Walk(8, true));

        public static bool ReadBool8(ref this FastMemoryBuffer<byte> buf) => buf.Read(1)._GetBool8();
        public static byte ReadUInt8(ref this FastMemoryBuffer<byte> buf) => buf.Read(1)._GetUInt8();
        public static byte ReadByte(ref this FastMemoryBuffer<byte> buf) => buf.Read(1)._GetUInt8();
        public static ushort ReadUInt16(ref this FastMemoryBuffer<byte> buf) => buf.Read(2)._GetUInt16();
        public static uint ReadUInt32(ref this FastMemoryBuffer<byte> buf) => buf.Read(4)._GetUInt32();
        public static ulong ReadUInt64(ref this FastMemoryBuffer<byte> buf) => buf.Read(8)._GetUInt64();
        public static sbyte ReadSInt8(ref this FastMemoryBuffer<byte> buf) => buf.Read(1)._GetSInt8();
        public static short ReadSInt16(ref this FastMemoryBuffer<byte> buf) => buf.Read(2)._GetSInt16();
        public static int ReadSInt32(ref this FastMemoryBuffer<byte> buf) => buf.Read(4)._GetSInt32();
        public static long ReadSInt64(ref this FastMemoryBuffer<byte> buf) => buf.Read(8)._GetSInt64();

        public static bool PeekBool8(ref this FastMemoryBuffer<byte> buf) => buf.Peek(1)._GetBool8();
        public static byte PeekUInt8(ref this FastMemoryBuffer<byte> buf) => buf.Peek(1)._GetUInt8();
        public static ushort PeekUInt16(ref this FastMemoryBuffer<byte> buf) => buf.Peek(2)._GetUInt16();
        public static uint PeekUInt32(ref this FastMemoryBuffer<byte> buf) => buf.Peek(4)._GetUInt32();
        public static ulong PeekUInt64(ref this FastMemoryBuffer<byte> buf) => buf.Peek(8)._GetUInt64();
        public static sbyte PeekSInt8(ref this FastMemoryBuffer<byte> buf) => buf.Peek(1)._GetSInt8();
        public static short PeekSInt16(ref this FastMemoryBuffer<byte> buf) => buf.Peek(2)._GetSInt16();
        public static int PeekSInt32(ref this FastMemoryBuffer<byte> buf) => buf.Peek(4)._GetSInt32();
        public static long PeekSInt64(ref this FastMemoryBuffer<byte> buf) => buf.Peek(8)._GetSInt64();


        public static bool ReadBool8(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Read(1)._GetBool8();
        public static byte ReadUInt8(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Read(1)._GetUInt8();
        public static byte ReadByte(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Read(1)._GetUInt8();
        public static ushort ReadUInt16(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Read(2)._GetUInt16();
        public static uint ReadUInt32(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Read(4)._GetUInt32();
        public static ulong ReadUInt64(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Read(8)._GetUInt64();
        public static sbyte ReadSInt8(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Read(1)._GetSInt8();
        public static short ReadSInt16(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Read(2)._GetSInt16();
        public static int ReadSInt32(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Read(4)._GetSInt32();
        public static long ReadSInt64(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Read(8)._GetSInt64();

        public static bool PeekBool8(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Peek(1)._GetBool8();
        public static byte PeekUInt8(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Peek(1)._GetUInt8();
        public static ushort PeekUInt16(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Peek(2)._GetUInt16();
        public static uint PeekUInt32(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Peek(4)._GetUInt32();
        public static ulong PeekUInt64(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Peek(8)._GetUInt64();
        public static sbyte PeekSInt8(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Peek(1)._GetSInt8();
        public static short PeekSInt16(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Peek(2)._GetSInt16();
        public static int PeekSInt32(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Peek(4)._GetSInt32();
        public static long PeekSInt64(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Peek(8)._GetSInt64();


        public static void WriteBool8(this MemoryBuffer<byte> buf, bool value) => value._SetBool8(buf.Walk(1, false));
        public static void WriteUInt8(this MemoryBuffer<byte> buf, byte value) => value._SetUInt8(buf.Walk(1, false));
        public static void WriteByte(this MemoryBuffer<byte> buf, byte value) => value._SetUInt8(buf.Walk(1, false));
        public static void WriteUInt16(this MemoryBuffer<byte> buf, ushort value) => value._SetUInt16(buf.Walk(2, false));
        public static void WriteUInt32(this MemoryBuffer<byte> buf, uint value) => value._SetUInt32(buf.Walk(4, false));
        public static void WriteUInt64(this MemoryBuffer<byte> buf, ulong value) => value._SetUInt64(buf.Walk(8, false));
        public static void WriteSInt8(this MemoryBuffer<byte> buf, sbyte value) => value._SetSInt8(buf.Walk(1, false));
        public static void WriteSInt16(this MemoryBuffer<byte> buf, short value) => value._SetSInt16(buf.Walk(2, false));
        public static void WriteSInt32(this MemoryBuffer<byte> buf, int value) => value._SetSInt32(buf.Walk(4, false));
        public static void WriteSInt64(this MemoryBuffer<byte> buf, long value) => value._SetSInt64(buf.Walk(8, false));

        public static void SetBool8(this MemoryBuffer<byte> buf, bool value) => value._SetBool8(buf.Walk(1, true));
        public static void SetUInt8(this MemoryBuffer<byte> buf, byte value) => value._SetUInt8(buf.Walk(1, true));
        public static void SetUInt16(this MemoryBuffer<byte> buf, ushort value) => value._SetUInt16(buf.Walk(2, true));
        public static void SetUInt32(this MemoryBuffer<byte> buf, uint value) => value._SetUInt32(buf.Walk(4, true));
        public static void SetUInt64(this MemoryBuffer<byte> buf, ulong value) => value._SetUInt64(buf.Walk(8, true));
        public static void SetSInt8(this MemoryBuffer<byte> buf, sbyte value) => value._SetSInt8(buf.Walk(1, true));
        public static void SetSInt16(this MemoryBuffer<byte> buf, short value) => value._SetSInt16(buf.Walk(2, true));
        public static void SetSInt32(this MemoryBuffer<byte> buf, int value) => value._SetSInt32(buf.Walk(4, true));
        public static void SetSInt64(this MemoryBuffer<byte> buf, long value) => value._SetSInt64(buf.Walk(8, true));

        public static bool ReadBool8(this MemoryBuffer<byte> buf) => buf.Read(1)._GetBool8();
        public static byte ReadUInt8(this MemoryBuffer<byte> buf) => buf.Read(1)._GetUInt8();
        public static byte ReadByte(this MemoryBuffer<byte> buf) => buf.Read(1)._GetUInt8();
        public static ushort ReadUInt16(this MemoryBuffer<byte> buf) => buf.Read(2)._GetUInt16();
        public static uint ReadUInt32(this MemoryBuffer<byte> buf) => buf.Read(4)._GetUInt32();
        public static ulong ReadUInt64(this MemoryBuffer<byte> buf) => buf.Read(8)._GetUInt64();
        public static sbyte ReadSInt8(this MemoryBuffer<byte> buf) => buf.Read(1)._GetSInt8();
        public static short ReadSInt16(this MemoryBuffer<byte> buf) => buf.Read(2)._GetSInt16();
        public static int ReadSInt32(this MemoryBuffer<byte> buf) => buf.Read(4)._GetSInt32();
        public static long ReadSInt64(this MemoryBuffer<byte> buf) => buf.Read(8)._GetSInt64();

        public static bool PeekBool8(this MemoryBuffer<byte> buf) => buf.Peek(1)._GetBool8();
        public static byte PeekUInt8(this MemoryBuffer<byte> buf) => buf.Peek(1)._GetUInt8();
        public static ushort PeekUInt16(this MemoryBuffer<byte> buf) => buf.Peek(2)._GetUInt16();
        public static uint PeekUInt32(this MemoryBuffer<byte> buf) => buf.Peek(4)._GetUInt32();
        public static ulong PeekUInt64(this MemoryBuffer<byte> buf) => buf.Peek(8)._GetUInt64();
        public static sbyte PeekSInt8(this MemoryBuffer<byte> buf) => buf.Peek(1)._GetSInt8();
        public static short PeekSInt16(this MemoryBuffer<byte> buf) => buf.Peek(2)._GetSInt16();
        public static int PeekSInt32(this MemoryBuffer<byte> buf) => buf.Peek(4)._GetSInt32();
        public static long PeekSInt64(this MemoryBuffer<byte> buf) => buf.Peek(8)._GetSInt64();

        public static bool ReadBool8(this ReadOnlyMemoryBuffer<byte> buf) => buf.Read(1)._GetBool8();
        public static byte ReadUInt8(this ReadOnlyMemoryBuffer<byte> buf) => buf.Read(1)._GetUInt8();
        public static byte ReadByte(this ReadOnlyMemoryBuffer<byte> buf) => buf.Read(1)._GetUInt8();
        public static ushort ReadUInt16(this ReadOnlyMemoryBuffer<byte> buf) => buf.Read(2)._GetUInt16();
        public static uint ReadUInt32(this ReadOnlyMemoryBuffer<byte> buf) => buf.Read(4)._GetUInt32();
        public static ulong ReadUInt64(this ReadOnlyMemoryBuffer<byte> buf) => buf.Read(8)._GetUInt64();
        public static sbyte ReadSInt8(this ReadOnlyMemoryBuffer<byte> buf) => buf.Read(1)._GetSInt8();
        public static short ReadSInt16(this ReadOnlyMemoryBuffer<byte> buf) => buf.Read(2)._GetSInt16();
        public static int ReadSInt32(this ReadOnlyMemoryBuffer<byte> buf) => buf.Read(4)._GetSInt32();
        public static long ReadSInt64(this ReadOnlyMemoryBuffer<byte> buf) => buf.Read(8)._GetSInt64();

        public static bool PeekBool8(this ReadOnlyMemoryBuffer<byte> buf) => buf.Peek(1)._GetBool8();
        public static byte PeekUInt8(this ReadOnlyMemoryBuffer<byte> buf) => buf.Peek(1)._GetUInt8();
        public static ushort PeekUInt16(this ReadOnlyMemoryBuffer<byte> buf) => buf.Peek(2)._GetUInt16();
        public static uint PeekUInt32(this ReadOnlyMemoryBuffer<byte> buf) => buf.Peek(4)._GetUInt32();
        public static ulong PeekUInt64(this ReadOnlyMemoryBuffer<byte> buf) => buf.Peek(8)._GetUInt64();
        public static sbyte PeekSInt8(this ReadOnlyMemoryBuffer<byte> buf) => buf.Peek(1)._GetSInt8();
        public static short PeekSInt16(this ReadOnlyMemoryBuffer<byte> buf) => buf.Peek(2)._GetSInt16();
        public static int PeekSInt32(this ReadOnlyMemoryBuffer<byte> buf) => buf.Peek(4)._GetSInt32();
        public static long PeekSInt64(this ReadOnlyMemoryBuffer<byte> buf) => buf.Peek(8)._GetSInt64();
    }

    static class MemoryHelper
    {
        public static readonly long _MemoryObjectOffset;
        public static readonly long _MemoryIndexOffset;
        public static readonly long _MemoryLengthOffset;
        public static readonly bool _UseFast = false;

        static unsafe MemoryHelper()
        {
            try
            {
                _MemoryObjectOffset = Marshal.OffsetOf<Memory<byte>>("_object").ToInt64();
                _MemoryIndexOffset = Marshal.OffsetOf<Memory<byte>>("_index").ToInt64();
                _MemoryLengthOffset = Marshal.OffsetOf<Memory<byte>>("_length").ToInt64();

                if (_MemoryObjectOffset != Marshal.OffsetOf<ReadOnlyMemory<DummyValueType>>("_object").ToInt64() ||
                    _MemoryIndexOffset != Marshal.OffsetOf<ReadOnlyMemory<DummyValueType>>("_index").ToInt64() ||
                    _MemoryLengthOffset != Marshal.OffsetOf<ReadOnlyMemory<DummyValueType>>("_length").ToInt64())
                {
                    throw new Exception();
                }

                _UseFast = true;
            }
            catch
            {
                Random r = new Random();
                bool ok = true;

                for (int i = 0; i < 32; i++)
                {
                    int a = r.Next(96) + 32;
                    int b = r.Next(a / 2);
                    int c = r.Next(a / 2);
                    if (ValidateMemoryStructureLayoutForSecurity(a, b, c) == false ||
                        ValidateReadOnlyMemoryStructureLayoutForSecurity(a, b, c) == false)
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                {
                    _MemoryObjectOffset = 0;
                    _MemoryIndexOffset = sizeof(void*);
                    _MemoryLengthOffset = _MemoryIndexOffset + sizeof(int);

                    _UseFast = true;
                }
            }
        }

        public const int MemoryUsePoolThreshold = 1024;

        public static T[] FastAllocMoreThan<T>(int size)
        {
            if (size < MemoryUsePoolThreshold)
                return new T[size];
            else
                return ArrayPool<T>.Shared.Rent(size);
        }

        public static Memory<T> FastAllocMemory<T>(int size)
        {
            if (size < MemoryUsePoolThreshold)
                return new T[size];
            else
                return new Memory<T>(FastAllocMoreThan<T>(size)).Slice(0, size);
        }

        public static void FastFree<T>(T[] array)
        {
            if (array.Length >= MemoryUsePoolThreshold)
                ArrayPool<T>.Shared.Return(array);
        }

        public static void FastFree<T>(Memory<T> memory) => memory._GetInternalArray()._FastFree();

        public static ValueHolder FastAllocMemoryWithUsing<T>(int size, out Memory<T> memory)
        {
            T[] allocatedArray = FastAllocMoreThan<T>(size);

            if (allocatedArray.Length == size)
                memory = allocatedArray;
            else
                memory = allocatedArray.AsMemory(0, size);

            return new ValueHolder(() =>
            {
                FastFree(allocatedArray);
            },
            LeakCounterKind.FastAllocMemoryWithUsing);
        }

        unsafe struct DummyValueType
        {
            public fixed char fixedBuffer[96];
        }

        static unsafe bool ValidateMemoryStructureLayoutForSecurity(int a, int b, int c)
        {
            try
            {
                DummyValueType[] obj = new DummyValueType[a];
                Memory<DummyValueType> mem = new Memory<DummyValueType>(obj, b, c);

                void* memPtr = Unsafe.AsPointer(ref mem);

                byte* p = (byte*)memPtr;
                DummyValueType[] array = Unsafe.Read<DummyValueType[]>(p);
                if (array == obj)
                {
                    p += sizeof(void*);
                    if (Unsafe.Read<int>(p) == b)
                    {
                        p += sizeof(int);
                        if (Unsafe.Read<int>(p) == c)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        static unsafe bool ValidateReadOnlyMemoryStructureLayoutForSecurity(int a, int b, int c)
        {
            try
            {
                DummyValueType[] obj = new DummyValueType[a];
                ReadOnlyMemory<DummyValueType> mem = new ReadOnlyMemory<DummyValueType>(obj, b, c);

                void* memPtr = Unsafe.AsPointer(ref mem);

                byte* p = (byte*)memPtr;
                DummyValueType[] array = Unsafe.Read<DummyValueType[]>(p);
                if (array == obj)
                {
                    p += sizeof(void*);
                    if (Unsafe.Read<int>(p) == b)
                    {
                        p += sizeof(int);
                        if (Unsafe.Read<int>(p) == c)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public static List<List<Memory<T>>> SplitMemoryArray<T>(IEnumerable<Memory<T>> src, int elementMaxSize)
        {
            elementMaxSize = Math.Max(1, elementMaxSize);

            int currentSize = 0;
            List<List<Memory<T>>> ret = new List<List<Memory<T>>>();
            List<Memory<T>> currentList = new List<Memory<T>>();

            foreach (Memory<T> mSrc in src)
            {
                Memory<T> m = mSrc;

                LABEL_START:

                if (m.Length >= 1)
                {
                    int overSize = (currentSize + m.Length) - elementMaxSize;
                    if (overSize >= 0)
                    {
                        Memory<T> mAdd = m.Slice(0, m.Length - overSize);

                        currentList.Add(mAdd);
                        ret.Add(currentList);
                        currentList = new List<Memory<T>>();
                        currentSize = 0;

                        m = m.Slice(mAdd.Length);

                        goto LABEL_START;
                    }
                    else
                    {
                        currentList.Add(m);
                        currentSize += m.Length;
                    }
                }
            }

            if (currentList.Count >= 1)
                ret.Add(currentList);

            return ret;
        }

        public static List<List<ArraySegment<T>>> SplitMemoryArrayToArraySegment<T>(IEnumerable<Memory<T>> src, int elementMaxSize)
        {
            elementMaxSize = Math.Max(1, elementMaxSize);

            int currentSize = 0;
            List<List<ArraySegment<T>>> ret = new List<List<ArraySegment<T>>>();
            List<ArraySegment<T>> currentList = new List<ArraySegment<T>>();

            foreach (Memory<T> mSrc in src)
            {
                Memory<T> m = mSrc;

                LABEL_START:

                if (m.Length >= 1)
                {
                    int overSize = (currentSize + m.Length) - elementMaxSize;
                    if (overSize >= 0)
                    {
                        Memory<T> mAdd = m.Slice(0, m.Length - overSize);

                        currentList.Add(mAdd._AsSegment());
                        ret.Add(currentList);
                        currentList = new List<ArraySegment<T>>();
                        currentSize = 0;

                        m = m.Slice(mAdd.Length);

                        goto LABEL_START;
                    }
                    else
                    {
                        currentList.Add(m._AsSegment());
                        currentSize += m.Length;
                    }
                }
            }

            if (currentList.Count >= 1)
                ret.Add(currentList);

            return ret;
        }
    }

    class FastMemoryPool<T>
    {
        Memory<T> Pool;
        int CurrentPos;
        int MinReserveSize;

        public FastMemoryPool(int initialSize = 0)
        {
            initialSize = Math.Max(initialSize, 1);
            Pool = new T[initialSize];
            MinReserveSize = initialSize;
        }

        public Memory<T> Reserve(int maxSize)
        {
            checked
            {
                if (maxSize < 0) throw new ArgumentOutOfRangeException("size");
                if (maxSize == 0) return Memory<T>.Empty;

                Debug.Assert((Pool.Length - CurrentPos) >= 0);

                if ((Pool.Length - CurrentPos) < maxSize)
                {
                    MinReserveSize = Math.Max(MinReserveSize, maxSize * 5);
                    Pool = new T[MinReserveSize];
                    CurrentPos = 0;
                }

                var ret = Pool.Slice(CurrentPos, maxSize);
                CurrentPos += maxSize;
                return ret;
            }
        }

        public void Commit(ref Memory<T> reservedMemory, int commitSize)
        {
            reservedMemory = Commit(reservedMemory, commitSize);
        }

        public Memory<T> Commit(Memory<T> reservedMemory, int commitSize)
        {
            checked
            {
                int returnSize = reservedMemory.Length - commitSize;
                Debug.Assert(returnSize >= 0);
                if (returnSize == 0) return reservedMemory;

                CurrentPos -= returnSize;
                Debug.Assert(CurrentPos >= 0);

                if (commitSize >= 1)
                    return reservedMemory.Slice(0, commitSize);
                else
                    return Memory<T>.Empty;
            }
        }
    }
}

