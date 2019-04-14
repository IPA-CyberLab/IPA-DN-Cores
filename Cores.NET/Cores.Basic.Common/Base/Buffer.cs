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

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    ref struct SpanBuffer<T>
    {
        Span<T> InternalSpan;
        public int CurrentPosition { get; private set; }
        public int Length { get; private set; }

        public Span<T> Span { get => InternalSpan.Slice(0, Length); }
        public Span<T> SpanBefore { get => Span.Slice(0, CurrentPosition); }
        public Span<T> SpanAfter { get => Span.Slice(CurrentPosition); }

        public SpanBuffer(Span<T> baseSpan)
        {
            InternalSpan = baseSpan;
            CurrentPosition = 0;
            Length = baseSpan.Length;
        }

        public static SpanBuffer<byte> FromStruct<TStruct>(TStruct src)
        {
            Memory<byte> baseMemory = new byte[Util.SizeOfStruct<TStruct>()];
            ref TStruct dst = ref baseMemory.AsStruct<TStruct>();
            dst = src;
            return new SpanBuffer<byte>(baseMemory.Span);
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

        public void Seek(int offset, SeekOrigin mode)
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

        public int Read(Span<T> dest, int size)
        {
            if (dest.Length < size) throw new ArgumentException("dest.Length < size");
            var span = Read(size);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Peek(Span<T> dest, int size)
        {
            if (dest.Length < size) throw new ArgumentException("dest.Length < size");
            var span = Peek(size);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Read(T[] dest, int offset, int size) => Read(dest.AsSpan(offset, size), size);
        public int Peek(T[] dest, int offset, int size) => Peek(dest.AsSpan(offset, size), size);

        public void EnsureInternalBufferReserved(int newSize)
        {
            if (InternalSpan.Length >= newSize) return;

            int newInternalSize = InternalSpan.Length;
            while (newInternalSize < newSize)
                newInternalSize = checked(Math.Max(newInternalSize, 128) * 2);

            InternalSpan = InternalSpan.ReAlloc(newInternalSize);
        }

        public void Clear()
        {
            InternalSpan = new Span<T>();
            CurrentPosition = 0;
            Length = 0;
        }
    }

    ref struct ReadOnlySpanBuffer<T>
    {
        ReadOnlySpan<T> InternalSpan;
        public int CurrentPosition { get; private set; }
        public int Length { get; private set; }

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

        public ReadOnlySpanBuffer(ReadOnlySpan<T> baseSpan)
        {
            InternalSpan = baseSpan;
            CurrentPosition = 0;
            Length = baseSpan.Length;
        }

        public static ReadOnlySpanBuffer<byte> FromStruct<TStruct>(TStruct src)
        {
            Memory<byte> baseMemory = new byte[Util.SizeOfStruct<TStruct>()];
            ref TStruct dst = ref baseMemory.AsStruct<TStruct>();
            dst = src;
            return new ReadOnlySpanBuffer<byte>(baseMemory.Span);
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

        public void Seek(int offset, SeekOrigin mode)
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

        public int Read(Span<T> dest, int size)
        {
            if (dest.Length < size) throw new ArgumentException("dest.Length < size");
            var span = Read(size);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Peek(Span<T> dest, int size)
        {
            if (dest.Length < size) throw new ArgumentException("dest.Length < size");
            var span = Peek(size);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Read(T[] dest, int offset, int size) => Read(dest.AsSpan(offset, size), size);
        public int Peek(T[] dest, int offset, int size) => Peek(dest.AsSpan(offset, size), size);


        public void Clear()
        {
            InternalSpan = new ReadOnlySpan<T>();
            CurrentPosition = 0;
            Length = 0;
        }
    }

    ref struct FastMemoryBuffer<T>
    {
        Memory<T> InternalBuffer;
        Span<T> InternalSpan;
        public int CurrentPosition { get; private set; }
        public int Length { get; private set; }

        public Memory<T> Memory { get => InternalBuffer.Slice(0, Length); }
        public Memory<T> MemoryBefore { get => Memory.Slice(0, CurrentPosition); }
        public Memory<T> MemoryAfter { get => Memory.Slice(CurrentPosition); }

        public Span<T> Span { get => InternalBuffer.Slice(0, Length).Span; }
        public Span<T> SpanBefore { get => Memory.Slice(0, CurrentPosition).Span; }
        public Span<T> SpanAfter { get => Memory.Slice(CurrentPosition).Span; }

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
            ref TStruct dst = ref baseMemory.AsStruct<TStruct>();
            dst = src;
            return new FastMemoryBuffer<byte>(baseMemory);
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

        public void Seek(int offset, SeekOrigin mode)
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

        public int Read(Span<T> dest, int size)
        {
            if (dest.Length < size) throw new ArgumentException("dest.Length < size");
            var span = Read(size);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Peek(Span<T> dest, int size)
        {
            if (dest.Length < size) throw new ArgumentException("dest.Length < size");
            var span = Peek(size);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Read(T[] dest, int offset, int size) => Read(dest.AsSpan(offset, size), size);
        public int Peek(T[] dest, int offset, int size) => Peek(dest.AsSpan(offset, size), size);

        public int Read(Memory<T> dest, int size)
        {
            if (dest.Length < size) throw new ArgumentException("dest.Length < size");
            var span = PeekAsMemory(size);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Peek(Memory<T> dest, int size)
        {
            if (dest.Length < size) throw new ArgumentException("dest.Length < size");
            var span = PeekAsMemory(size);
            span.CopyTo(dest);
            return span.Length;
        }

        public void EnsureInternalBufferReserved(int newSize)
        {
            if (InternalBuffer.Length >= newSize) return;

            int newInternalSize = InternalBuffer.Length;
            while (newInternalSize < newSize)
                newInternalSize = checked(Math.Max(newInternalSize, 128) * 2);

            InternalBuffer = InternalBuffer.ReAlloc(newInternalSize);
            InternalSpan = InternalBuffer.Span;
        }

        public void Clear()
        {
            InternalBuffer = new Memory<T>();
            CurrentPosition = 0;
            Length = 0;
            InternalSpan = new Span<T>();
        }
    }

    ref struct FastReadOnlyMemoryBuffer<T>
    {
        ReadOnlyMemory<T> InternalBuffer;
        ReadOnlySpan<T> InternalSpan;
        public int CurrentPosition { get; private set; }
        public int Length { get; private set; }

        public ReadOnlyMemory<T> Memory { get => InternalBuffer.Slice(0, Length); }
        public ReadOnlyMemory<T> MemoryBefore { get => Memory.Slice(0, CurrentPosition); }
        public ReadOnlyMemory<T> MemoryAfter { get => Memory.Slice(CurrentPosition); }

        public ReadOnlySpan<T> Span { get => InternalBuffer.Slice(0, Length).Span; }
        public ReadOnlySpan<T> SpanBefore { get => Memory.Slice(0, CurrentPosition).Span; }
        public ReadOnlySpan<T> SpanAfter { get => Memory.Slice(CurrentPosition).Span; }

        public FastReadOnlyMemoryBuffer(ReadOnlyMemory<T> baseMemory)
        {
            InternalBuffer = baseMemory;
            CurrentPosition = 0;
            Length = baseMemory.Length;
            InternalSpan = InternalBuffer.Span;
        }

        public static FastReadOnlyMemoryBuffer<byte> FromStruct<TStruct>(TStruct src)
        {
            ReadOnlyMemory<byte> baseMemory = new byte[Util.SizeOfStruct<TStruct>()];
            ref TStruct dst = ref baseMemory.AsStruct<TStruct>();
            dst = src;
            return new FastReadOnlyMemoryBuffer<byte>(baseMemory);
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

        public void Seek(int offset, SeekOrigin mode)
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

        public int Read(Span<T> dest, int size)
        {
            if (dest.Length < size) throw new ArgumentException("dest.Length < size");
            var span = Read(size);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Peek(Span<T> dest, int size)
        {
            if (dest.Length < size) throw new ArgumentException("dest.Length < size");
            var span = Peek(size);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Read(T[] dest, int offset, int size) => Read(dest.AsSpan(offset, size), size);
        public int Peek(T[] dest, int offset, int size) => Peek(dest.AsSpan(offset, size), size);

        public int Read(Memory<T> dest, int size)
        {
            if (dest.Length < size) throw new ArgumentException("dest.Length < size");
            var span = ReadAsMemory(size);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Peek(Memory<T> dest, int size)
        {
            if (dest.Length < size) throw new ArgumentException("dest.Length < size");
            var span = PeekAsMemory(size);
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

    interface IBuffer<T>
    {
        int CurrentPosition { get; }
        int Length { get; }
        void Write(ReadOnlySpan<T> data);
        ReadOnlySpan<T> Read(int size, bool allowPartial = false);
        ReadOnlySpan<T> Peek(int size, bool allowPartial = false);
        void Seek(int offset, SeekOrigin mode);
        void Clear();
        Holder PinLock();
        bool IsPinLocked();
    }

    class BufferStream : Stream
    {
        public IBuffer<byte> BaseBuffer { get; }

        public BufferStream(IBuffer<byte> baseBuffer)
        {
            BaseBuffer = baseBuffer;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => true;
        public override long Length => BaseBuffer.Length;

        public override long Position
        {
            get => BaseBuffer.CurrentPosition;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin)
        {
            checked
            {
                BaseBuffer.Seek((int)offset, origin);
                return BaseBuffer.CurrentPosition;
            }
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return base.FlushAsync(cancellationToken);
        }

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

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(this.Read(buffer, offset, count));
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            return this.Read(buffer.Span);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.Write(buffer, offset, count);
            return Task.CompletedTask;
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            this.Write(buffer.Span);
        }
    }

    class MemoryBuffer<T> : IBuffer<T>
    {
        Memory<T> InternalBuffer;
        public int CurrentPosition { get; private set; }
        public int Length { get; private set; }

        public Memory<T> Memory { get => InternalBuffer.Slice(0, Length); }
        public Memory<T> MemoryBefore { get => Memory.Slice(0, CurrentPosition); }
        public Memory<T> MemoryAfter { get => Memory.Slice(CurrentPosition); }

        public Span<T> Span { get => InternalBuffer.Slice(0, Length).Span; }
        public Span<T> SpanBefore { get => Memory.Slice(0, CurrentPosition).Span; }
        public Span<T> SpanAfter { get => Memory.Slice(CurrentPosition).Span; }

        public MemoryBuffer() : this(Memory<T>.Empty) { }

        public MemoryBuffer(Memory<T> baseMemory)
        {
            InternalBuffer = baseMemory;
            CurrentPosition = 0;
            Length = baseMemory.Length;
        }

        public static MemoryBuffer<byte> FromStruct<TStruct>(TStruct src)
        {
            Memory<byte> baseMemory = new byte[Util.SizeOfStruct<TStruct>()];
            ref TStruct dst = ref baseMemory.AsStruct<TStruct>();
            dst = src;
            return new MemoryBuffer<byte>(baseMemory);
        }

        CriticalSection PinLockObj = new CriticalSection();
        int PinLockedCounter = 0;
        MemoryHandle PinHandle;
        public bool IsPinLocked() => (PinLockedCounter != 0);
        public Holder PinLock()
        {
            lock (PinLockObj)
            {
                if (PinLockedCounter == 0)
                    PinHandle = InternalBuffer.Pin();
                PinLockedCounter++;
            }

            return new Holder(() =>
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

        public void Seek(int offset, SeekOrigin mode)
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

        public int Read(Span<T> dest, int size)
        {
            if (dest.Length < size) throw new ArgumentException("dest.Length < size");
            var span = Read(size);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Peek(Span<T> dest, int size)
        {
            if (dest.Length < size) throw new ArgumentException("dest.Length < size");
            var span = Peek(size);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Read(T[] dest, int offset, int size) => Read(dest.AsSpan(offset, size), size);
        public int Peek(T[] dest, int offset, int size) => Peek(dest.AsSpan(offset, size), size);

        public int Read(Memory<T> dest, int size)
        {
            if (dest.Length < size) throw new ArgumentException("dest.Length < size");
            var span = PeekAsMemory(size);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Peek(Memory<T> dest, int size)
        {
            if (dest.Length < size) throw new ArgumentException("dest.Length < size");
            var span = PeekAsMemory(size);
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

            InternalBuffer = InternalBuffer.ReAlloc(newInternalSize);
        }

        public void Clear()
        {
            if (IsPinLocked()) throw new ApplicationException("Memory pin is locked.");

            InternalBuffer = new Memory<T>();
            CurrentPosition = 0;
            Length = 0;
        }
    }

    class ReadOnlyMemoryBuffer<T> : IBuffer<T>
    {
        ReadOnlyMemory<T> InternalBuffer;
        public int CurrentPosition { get; private set; }
        public int Length { get; private set; }

        public ReadOnlyMemory<T> Memory { get => InternalBuffer.Slice(0, Length); }
        public ReadOnlyMemory<T> MemoryBefore { get => Memory.Slice(0, CurrentPosition); }
        public ReadOnlyMemory<T> MemoryAfter { get => Memory.Slice(CurrentPosition); }

        public ReadOnlySpan<T> Span { get => InternalBuffer.Slice(0, Length).Span; }
        public ReadOnlySpan<T> SpanBefore { get => Memory.Slice(0, CurrentPosition).Span; }
        public ReadOnlySpan<T> SpanAfter { get => Memory.Slice(CurrentPosition).Span; }

        public ReadOnlyMemoryBuffer() : this(ReadOnlyMemory<T>.Empty) { }

        public ReadOnlyMemoryBuffer(ReadOnlyMemory<T> baseMemory)
        {
            InternalBuffer = baseMemory;
            CurrentPosition = 0;
            Length = baseMemory.Length;
        }

        public static ReadOnlyMemoryBuffer<byte> FromStruct<TStruct>(TStruct src)
        {
            ReadOnlyMemory<byte> baseMemory = new byte[Util.SizeOfStruct<TStruct>()];
            ref TStruct dst = ref baseMemory.AsStruct<TStruct>();
            dst = src;
            return new ReadOnlyMemoryBuffer<byte>(baseMemory);
        }

        CriticalSection PinLockObj = new CriticalSection();
        int PinLockedCounter = 0;
        MemoryHandle PinHandle;
        public bool IsPinLocked() => (PinLockedCounter != 0);
        public Holder PinLock()
        {
            lock (PinLockObj)
            {
                if (PinLockedCounter == 0)
                    PinHandle = InternalBuffer.Pin();
                PinLockedCounter++;
            }

            return new Holder(() =>
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

        public void Seek(int offset, SeekOrigin mode)
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

        public int Read(Span<T> dest, int size)
        {
            if (dest.Length < size) throw new ArgumentException("dest.Length < size");
            var span = Read(size);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Peek(Span<T> dest, int size)
        {
            if (dest.Length < size) throw new ArgumentException("dest.Length < size");
            var span = Peek(size);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Read(T[] dest, int offset, int size) => Read(dest.AsSpan(offset, size), size);
        public int Peek(T[] dest, int offset, int size) => Peek(dest.AsSpan(offset, size), size);

        public int Read(Memory<T> dest, int size)
        {
            if (dest.Length < size) throw new ArgumentException("dest.Length < size");
            var span = ReadAsMemory(size);
            span.CopyTo(dest);
            return span.Length;
        }

        public int Peek(Memory<T> dest, int size)
        {
            if (dest.Length < size) throw new ArgumentException("dest.Length < size");
            var span = PeekAsMemory(size);
            span.CopyTo(dest);
            return span.Length;
        }

        void IBuffer<T>.Clear() => throw new NotSupportedException();

        void IBuffer<T>.Write(ReadOnlySpan<T> data) => throw new NotSupportedException();
    }

    static class SpanMemoryBufferHelper
    {
        public static void WriteBool8(this ref SpanBuffer<byte> buf, bool value) => value.SetBool8(buf.Walk(1, false));
        public static void WriteUInt8(this ref SpanBuffer<byte> buf, byte value) => value.SetUInt8(buf.Walk(1, false));
        public static void WriteUInt16(this ref SpanBuffer<byte> buf, ushort value) => value.SetUInt16(buf.Walk(2, false));
        public static void WriteUInt32(this ref SpanBuffer<byte> buf, uint value) => value.SetUInt32(buf.Walk(4, false));
        public static void WriteUInt64(this ref SpanBuffer<byte> buf, ulong value) => value.SetUInt64(buf.Walk(8, false));
        public static void WriteSInt8(this ref SpanBuffer<byte> buf, sbyte value) => value.SetSInt8(buf.Walk(1, false));
        public static void WriteSInt16(this ref SpanBuffer<byte> buf, short value) => value.SetSInt16(buf.Walk(2, false));
        public static void WriteSInt32(this ref SpanBuffer<byte> buf, int value) => value.SetSInt32(buf.Walk(4, false));
        public static void WriteSInt64(this ref SpanBuffer<byte> buf, long value) => value.SetSInt64(buf.Walk(8, false));

        public static void SetBool8(this ref SpanBuffer<byte> buf, bool value) => value.SetBool8(buf.Walk(1, true));
        public static void SetUInt8(this ref SpanBuffer<byte> buf, byte value) => value.SetUInt8(buf.Walk(1, true));
        public static void SetUInt16(this ref SpanBuffer<byte> buf, ushort value) => value.SetUInt16(buf.Walk(2, true));
        public static void SetUInt32(this ref SpanBuffer<byte> buf, uint value) => value.SetUInt32(buf.Walk(4, true));
        public static void SetUInt64(this ref SpanBuffer<byte> buf, ulong value) => value.SetUInt64(buf.Walk(8, true));
        public static void SetSInt8(this ref SpanBuffer<byte> buf, sbyte value) => value.SetSInt8(buf.Walk(1, true));
        public static void SetSInt16(this ref SpanBuffer<byte> buf, short value) => value.SetSInt16(buf.Walk(2, true));
        public static void SetSInt32(this ref SpanBuffer<byte> buf, int value) => value.SetSInt32(buf.Walk(4, true));
        public static void SetSInt64(this ref SpanBuffer<byte> buf, long value) => value.SetSInt64(buf.Walk(8, true));

        public static bool ReadBool8(ref this SpanBuffer<byte> buf) => buf.Read(1).GetBool8();
        public static byte ReadUInt8(ref this SpanBuffer<byte> buf) => buf.Read(1).GetUInt8();
        public static ushort ReadUInt16(ref this SpanBuffer<byte> buf) => buf.Read(2).GetUInt16();
        public static uint ReadUInt32(ref this SpanBuffer<byte> buf) => buf.Read(4).GetUInt32();
        public static ulong ReadUInt64(ref this SpanBuffer<byte> buf) => buf.Read(8).GetUInt64();
        public static sbyte ReadSInt8(ref this SpanBuffer<byte> buf) => buf.Read(1).GetSInt8();
        public static short ReadSInt16(ref this SpanBuffer<byte> buf) => buf.Read(2).GetSInt16();
        public static int ReadSInt32(ref this SpanBuffer<byte> buf) => buf.Read(4).GetSInt32();
        public static long ReadSInt64(ref this SpanBuffer<byte> buf) => buf.Read(8).GetSInt64();

        public static bool PeekBool8(ref this SpanBuffer<byte> buf) => buf.Peek(1).GetBool8();
        public static byte PeekUInt8(ref this SpanBuffer<byte> buf) => buf.Peek(1).GetUInt8();
        public static ushort PeekUInt16(ref this SpanBuffer<byte> buf) => buf.Peek(2).GetUInt16();
        public static uint PeekUInt32(ref this SpanBuffer<byte> buf) => buf.Peek(4).GetUInt32();
        public static ulong PeekUInt64(ref this SpanBuffer<byte> buf) => buf.Peek(8).GetUInt64();
        public static sbyte PeekSInt8(ref this SpanBuffer<byte> buf) => buf.Peek(1).GetSInt8();
        public static short PeekSInt16(ref this SpanBuffer<byte> buf) => buf.Peek(2).GetSInt16();
        public static int PeekSInt32(ref this SpanBuffer<byte> buf) => buf.Peek(4).GetSInt32();
        public static long PeekSInt64(ref this SpanBuffer<byte> buf) => buf.Peek(8).GetSInt64();


        public static bool ReadBool8(ref this ReadOnlySpanBuffer<byte> buf) => buf.Read(1).GetBool8();
        public static byte ReadUInt8(ref this ReadOnlySpanBuffer<byte> buf) => buf.Read(1).GetUInt8();
        public static ushort ReadUInt16(ref this ReadOnlySpanBuffer<byte> buf) => buf.Read(2).GetUInt16();
        public static uint ReadUInt32(ref this ReadOnlySpanBuffer<byte> buf) => buf.Read(4).GetUInt32();
        public static ulong ReadUInt64(ref this ReadOnlySpanBuffer<byte> buf) => buf.Read(8).GetUInt64();
        public static sbyte ReadSInt8(ref this ReadOnlySpanBuffer<byte> buf) => buf.Read(1).GetSInt8();
        public static short ReadSInt16(ref this ReadOnlySpanBuffer<byte> buf) => buf.Read(2).GetSInt16();
        public static int ReadSInt32(ref this ReadOnlySpanBuffer<byte> buf) => buf.Read(4).GetSInt32();
        public static long ReadSInt64(ref this ReadOnlySpanBuffer<byte> buf) => buf.Read(8).GetSInt64();

        public static bool PeekBool8(ref this ReadOnlySpanBuffer<byte> buf) => buf.Peek(1).GetBool8();
        public static byte PeekUInt8(ref this ReadOnlySpanBuffer<byte> buf) => buf.Peek(1).GetUInt8();
        public static ushort PeekUInt16(ref this ReadOnlySpanBuffer<byte> buf) => buf.Peek(2).GetUInt16();
        public static uint PeekUInt32(ref this ReadOnlySpanBuffer<byte> buf) => buf.Peek(4).GetUInt32();
        public static ulong PeekUInt64(ref this ReadOnlySpanBuffer<byte> buf) => buf.Peek(8).GetUInt64();
        public static sbyte PeekSInt8(ref this ReadOnlySpanBuffer<byte> buf) => buf.Peek(1).GetSInt8();
        public static short PeekSInt16(ref this ReadOnlySpanBuffer<byte> buf) => buf.Peek(2).GetSInt16();
        public static int PeekSInt32(ref this ReadOnlySpanBuffer<byte> buf) => buf.Peek(4).GetSInt32();
        public static long PeekSInt64(ref this ReadOnlySpanBuffer<byte> buf) => buf.Peek(8).GetSInt64();


        public static void WriteBool8(this ref FastMemoryBuffer<byte> buf, bool value) => value.SetBool8(buf.Walk(1, false));
        public static void WriteUInt8(this ref FastMemoryBuffer<byte> buf, byte value) => value.SetUInt8(buf.Walk(1, false));
        public static void WriteUInt16(this ref FastMemoryBuffer<byte> buf, ushort value) => value.SetUInt16(buf.Walk(2, false));
        public static void WriteUInt32(this ref FastMemoryBuffer<byte> buf, uint value) => value.SetUInt32(buf.Walk(4, false));
        public static void WriteUInt64(this ref FastMemoryBuffer<byte> buf, ulong value) => value.SetUInt64(buf.Walk(8, false));
        public static void WriteSInt8(this ref FastMemoryBuffer<byte> buf, sbyte value) => value.SetSInt8(buf.Walk(1, false));
        public static void WriteSInt16(this ref FastMemoryBuffer<byte> buf, short value) => value.SetSInt16(buf.Walk(2, false));
        public static void WriteSInt32(this ref FastMemoryBuffer<byte> buf, int value) => value.SetSInt32(buf.Walk(4, false));
        public static void WriteSInt64(this ref FastMemoryBuffer<byte> buf, long value) => value.SetSInt64(buf.Walk(8, false));

        public static void SetBool8(this ref FastMemoryBuffer<byte> buf, bool value) => value.SetBool8(buf.Walk(1, true));
        public static void SetUInt8(this ref FastMemoryBuffer<byte> buf, byte value) => value.SetUInt8(buf.Walk(1, true));
        public static void SetUInt16(this ref FastMemoryBuffer<byte> buf, ushort value) => value.SetUInt16(buf.Walk(2, true));
        public static void SetUInt32(this ref FastMemoryBuffer<byte> buf, uint value) => value.SetUInt32(buf.Walk(4, true));
        public static void SetUInt64(this ref FastMemoryBuffer<byte> buf, ulong value) => value.SetUInt64(buf.Walk(8, true));
        public static void SetSInt8(this ref FastMemoryBuffer<byte> buf, sbyte value) => value.SetSInt8(buf.Walk(1, true));
        public static void SetSInt16(this ref FastMemoryBuffer<byte> buf, short value) => value.SetSInt16(buf.Walk(2, true));
        public static void SetSInt32(this ref FastMemoryBuffer<byte> buf, int value) => value.SetSInt32(buf.Walk(4, true));
        public static void SetSInt64(this ref FastMemoryBuffer<byte> buf, long value) => value.SetSInt64(buf.Walk(8, true));

        public static bool ReadBool8(ref this FastMemoryBuffer<byte> buf) => buf.Read(1).GetBool8();
        public static byte ReadUInt8(ref this FastMemoryBuffer<byte> buf) => buf.Read(1).GetUInt8();
        public static ushort ReadUInt16(ref this FastMemoryBuffer<byte> buf) => buf.Read(2).GetUInt16();
        public static uint ReadUInt32(ref this FastMemoryBuffer<byte> buf) => buf.Read(4).GetUInt32();
        public static ulong ReadUInt64(ref this FastMemoryBuffer<byte> buf) => buf.Read(8).GetUInt64();
        public static sbyte ReadSInt8(ref this FastMemoryBuffer<byte> buf) => buf.Read(1).GetSInt8();
        public static short ReadSInt16(ref this FastMemoryBuffer<byte> buf) => buf.Read(2).GetSInt16();
        public static int ReadSInt32(ref this FastMemoryBuffer<byte> buf) => buf.Read(4).GetSInt32();
        public static long ReadSInt64(ref this FastMemoryBuffer<byte> buf) => buf.Read(8).GetSInt64();

        public static bool PeekBool8(ref this FastMemoryBuffer<byte> buf) => buf.Peek(1).GetBool8();
        public static byte PeekUInt8(ref this FastMemoryBuffer<byte> buf) => buf.Peek(1).GetUInt8();
        public static ushort PeekUInt16(ref this FastMemoryBuffer<byte> buf) => buf.Peek(2).GetUInt16();
        public static uint PeekUInt32(ref this FastMemoryBuffer<byte> buf) => buf.Peek(4).GetUInt32();
        public static ulong PeekUInt64(ref this FastMemoryBuffer<byte> buf) => buf.Peek(8).GetUInt64();
        public static sbyte PeekSInt8(ref this FastMemoryBuffer<byte> buf) => buf.Peek(1).GetSInt8();
        public static short PeekSInt16(ref this FastMemoryBuffer<byte> buf) => buf.Peek(2).GetSInt16();
        public static int PeekSInt32(ref this FastMemoryBuffer<byte> buf) => buf.Peek(4).GetSInt32();
        public static long PeekSInt64(ref this FastMemoryBuffer<byte> buf) => buf.Peek(8).GetSInt64();


        public static bool ReadBool8(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Read(1).GetBool8();
        public static byte ReadUInt8(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Read(1).GetUInt8();
        public static ushort ReadUInt16(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Read(2).GetUInt16();
        public static uint ReadUInt32(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Read(4).GetUInt32();
        public static ulong ReadUInt64(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Read(8).GetUInt64();
        public static sbyte ReadSInt8(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Read(1).GetSInt8();
        public static short ReadSInt16(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Read(2).GetSInt16();
        public static int ReadSInt32(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Read(4).GetSInt32();
        public static long ReadSInt64(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Read(8).GetSInt64();

        public static bool PeekBool8(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Peek(1).GetBool8();
        public static byte PeekUInt8(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Peek(1).GetUInt8();
        public static ushort PeekUInt16(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Peek(2).GetUInt16();
        public static uint PeekUInt32(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Peek(4).GetUInt32();
        public static ulong PeekUInt64(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Peek(8).GetUInt64();
        public static sbyte PeekSInt8(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Peek(1).GetSInt8();
        public static short PeekSInt16(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Peek(2).GetSInt16();
        public static int PeekSInt32(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Peek(4).GetSInt32();
        public static long PeekSInt64(ref this FastReadOnlyMemoryBuffer<byte> buf) => buf.Peek(8).GetSInt64();


        public static void WriteBool8(this MemoryBuffer<byte> buf, bool value) => value.SetBool8(buf.Walk(1, false));
        public static void WriteUInt8(this MemoryBuffer<byte> buf, byte value) => value.SetUInt8(buf.Walk(1, false));
        public static void WriteUInt16(this MemoryBuffer<byte> buf, ushort value) => value.SetUInt16(buf.Walk(2, false));
        public static void WriteUInt32(this MemoryBuffer<byte> buf, uint value) => value.SetUInt32(buf.Walk(4, false));
        public static void WriteUInt64(this MemoryBuffer<byte> buf, ulong value) => value.SetUInt64(buf.Walk(8, false));
        public static void WriteSInt8(this MemoryBuffer<byte> buf, sbyte value) => value.SetSInt8(buf.Walk(1, false));
        public static void WriteSInt16(this MemoryBuffer<byte> buf, short value) => value.SetSInt16(buf.Walk(2, false));
        public static void WriteSInt32(this MemoryBuffer<byte> buf, int value) => value.SetSInt32(buf.Walk(4, false));
        public static void WriteSInt64(this MemoryBuffer<byte> buf, long value) => value.SetSInt64(buf.Walk(8, false));

        public static void SetBool8(this MemoryBuffer<byte> buf, bool value) => value.SetBool8(buf.Walk(1, true));
        public static void SetUInt8(this MemoryBuffer<byte> buf, byte value) => value.SetUInt8(buf.Walk(1, true));
        public static void SetUInt16(this MemoryBuffer<byte> buf, ushort value) => value.SetUInt16(buf.Walk(2, true));
        public static void SetUInt32(this MemoryBuffer<byte> buf, uint value) => value.SetUInt32(buf.Walk(4, true));
        public static void SetUInt64(this MemoryBuffer<byte> buf, ulong value) => value.SetUInt64(buf.Walk(8, true));
        public static void SetSInt8(this MemoryBuffer<byte> buf, sbyte value) => value.SetSInt8(buf.Walk(1, true));
        public static void SetSInt16(this MemoryBuffer<byte> buf, short value) => value.SetSInt16(buf.Walk(2, true));
        public static void SetSInt32(this MemoryBuffer<byte> buf, int value) => value.SetSInt32(buf.Walk(4, true));
        public static void SetSInt64(this MemoryBuffer<byte> buf, long value) => value.SetSInt64(buf.Walk(8, true));

        public static bool ReadBool8(this MemoryBuffer<byte> buf) => buf.Read(1).GetBool8();
        public static byte ReadUInt8(this MemoryBuffer<byte> buf) => buf.Read(1).GetUInt8();
        public static ushort ReadUInt16(this MemoryBuffer<byte> buf) => buf.Read(2).GetUInt16();
        public static uint ReadUInt32(this MemoryBuffer<byte> buf) => buf.Read(4).GetUInt32();
        public static ulong ReadUInt64(this MemoryBuffer<byte> buf) => buf.Read(8).GetUInt64();
        public static sbyte ReadSInt8(this MemoryBuffer<byte> buf) => buf.Read(1).GetSInt8();
        public static short ReadSInt16(this MemoryBuffer<byte> buf) => buf.Read(2).GetSInt16();
        public static int ReadSInt32(this MemoryBuffer<byte> buf) => buf.Read(4).GetSInt32();
        public static long ReadSInt64(this MemoryBuffer<byte> buf) => buf.Read(8).GetSInt64();

        public static bool PeekBool8(this MemoryBuffer<byte> buf) => buf.Peek(1).GetBool8();
        public static byte PeekUInt8(this MemoryBuffer<byte> buf) => buf.Peek(1).GetUInt8();
        public static ushort PeekUInt16(this MemoryBuffer<byte> buf) => buf.Peek(2).GetUInt16();
        public static uint PeekUInt32(this MemoryBuffer<byte> buf) => buf.Peek(4).GetUInt32();
        public static ulong PeekUInt64(this MemoryBuffer<byte> buf) => buf.Peek(8).GetUInt64();
        public static sbyte PeekSInt8(this MemoryBuffer<byte> buf) => buf.Peek(1).GetSInt8();
        public static short PeekSInt16(this MemoryBuffer<byte> buf) => buf.Peek(2).GetSInt16();
        public static int PeekSInt32(this MemoryBuffer<byte> buf) => buf.Peek(4).GetSInt32();
        public static long PeekSInt64(this MemoryBuffer<byte> buf) => buf.Peek(8).GetSInt64();

        public static bool ReadBool8(this ReadOnlyMemoryBuffer<byte> buf) => buf.Read(1).GetBool8();
        public static byte ReadUInt8(this ReadOnlyMemoryBuffer<byte> buf) => buf.Read(1).GetUInt8();
        public static ushort ReadUInt16(this ReadOnlyMemoryBuffer<byte> buf) => buf.Read(2).GetUInt16();
        public static uint ReadUInt32(this ReadOnlyMemoryBuffer<byte> buf) => buf.Read(4).GetUInt32();
        public static ulong ReadUInt64(this ReadOnlyMemoryBuffer<byte> buf) => buf.Read(8).GetUInt64();
        public static sbyte ReadSInt8(this ReadOnlyMemoryBuffer<byte> buf) => buf.Read(1).GetSInt8();
        public static short ReadSInt16(this ReadOnlyMemoryBuffer<byte> buf) => buf.Read(2).GetSInt16();
        public static int ReadSInt32(this ReadOnlyMemoryBuffer<byte> buf) => buf.Read(4).GetSInt32();
        public static long ReadSInt64(this ReadOnlyMemoryBuffer<byte> buf) => buf.Read(8).GetSInt64();

        public static bool PeekBool8(this ReadOnlyMemoryBuffer<byte> buf) => buf.Peek(1).GetBool8();
        public static byte PeekUInt8(this ReadOnlyMemoryBuffer<byte> buf) => buf.Peek(1).GetUInt8();
        public static ushort PeekUInt16(this ReadOnlyMemoryBuffer<byte> buf) => buf.Peek(2).GetUInt16();
        public static uint PeekUInt32(this ReadOnlyMemoryBuffer<byte> buf) => buf.Peek(4).GetUInt32();
        public static ulong PeekUInt64(this ReadOnlyMemoryBuffer<byte> buf) => buf.Peek(8).GetUInt64();
        public static sbyte PeekSInt8(this ReadOnlyMemoryBuffer<byte> buf) => buf.Peek(1).GetSInt8();
        public static short PeekSInt16(this ReadOnlyMemoryBuffer<byte> buf) => buf.Peek(2).GetSInt16();
        public static int PeekSInt32(this ReadOnlyMemoryBuffer<byte> buf) => buf.Peek(4).GetSInt32();
        public static long PeekSInt64(this ReadOnlyMemoryBuffer<byte> buf) => buf.Peek(8).GetSInt64();
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

        public static T[] FastAlloc<T>(int size)
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
                return new Memory<T>(FastAlloc<T>(size)).Slice(0, size);
        }

        public static void FastFree<T>(T[] array)
        {
            if (array.Length >= MemoryUsePoolThreshold)
                ArrayPool<T>.Shared.Return(array);
        }

        public static void FastFree<T>(Memory<T> memory) => memory.GetInternalArray().FastFree();

        public static Holder FastAllocMemoryWithUsing<T>(int size, out Memory<T> memory)
        {
            var ret = FastAllocMemory<T>(size);

            memory = ret;

            return new Holder(() =>
            {
                FastFree(ret);
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

                        currentList.Add(mAdd.AsSegment());
                        ret.Add(currentList);
                        currentList = new List<ArraySegment<T>>();
                        currentSize = 0;

                        m = m.Slice(mAdd.Length);

                        goto LABEL_START;
                    }
                    else
                    {
                        currentList.Add(m.AsSegment());
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
            initialSize = Math.Min(initialSize, 1);
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

