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
    partial class Packet
    {
        ByteLinkedList List = new ByteLinkedList();
        public int PinHead { get; private set; } = 0;
        public int PinTail { get; private set; } = 0;
        public int Length { get { int ret = checked(PinTail - PinHead); Debug.Assert(ret >= 0); return ret; } }

        public Packet() { }

        public Packet(Memory<byte> baseMemory)
        {
            InsertHeadInternal(baseMemory);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            checked
            {
                List.Clear();
                PinTail = PinHead;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void InsertBefore(Memory<byte> item)
        {
            checked
            {
                if (item.IsEmpty) return;
                List.AddFirst(item);
                PinHead -= item.Length;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void InsertHeadInternal(Memory<byte> item)
        {
            checked
            {
                if (item.IsEmpty) return;
                List.AddFirst(item);
                PinTail += item.Length;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void InsertTail(Memory<byte> item)
        {
            checked
            {
                if (item.IsEmpty) return;
                List.AddLast(item);
                PinTail += item.Length;
            }
        }

        void Insert(int pin, Memory<byte> item, bool appendIfOverrun = false)
        {
            checked
            {
                if (item.IsEmpty) return;

                if (List.First == null)
                {
                    InsertHeadInternal(item);
                    return;
                }

                if (appendIfOverrun)
                {
                    if (pin < PinHead)
                        InsertBefore(new byte[PinHead - pin]);

                    if (pin > PinTail)
                        InsertTail(new byte[pin - PinTail]);
                }
                else
                {
                    if (List.First == null) throw new ArgumentOutOfRangeException("Buffer is empty.");
                    if (pin < PinHead) throw new ArgumentOutOfRangeException("pin < PinHead");
                    if (pin > PinTail) throw new ArgumentOutOfRangeException("pin > PinTail");
                }

                var node = GetNodeWithPin(pin, out int offsetInSegment, out _);
                Debug.Assert(node != null);
                if (offsetInSegment == 0)
                {
                    var newNode = List.AddBefore(node, item);
                    PinTail += item.Length;
                }
                else if (node.Value.Length == offsetInSegment)
                {
                    var newNode = List.AddAfter(node, item);
                    PinTail += item.Length;
                }
                else
                {
                    Memory<byte> sliceBefore = node.Value.Slice(0, offsetInSegment);
                    Memory<byte> sliceAfter = node.Value.Slice(offsetInSegment);

                    node.Value = sliceBefore;
                    var newNode = List.AddAfter(node, item);
                    List.AddAfter(newNode, sliceAfter);
                    PinTail += item.Length;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        ByteLinkedListNode GetNodeWithPin(int pin, out int offsetInSegment, out int nodePin)
        {
            checked
            {
                offsetInSegment = 0;
                nodePin = 0;
                if (List.First == null)
                {
                    if (pin != PinHead) throw new ArgumentOutOfRangeException("List.First == null, but pin != PinHead");
                    return null;
                }
                if (pin < PinHead) throw new ArgumentOutOfRangeException("pin < PinHead");
                if (pin == PinHead)
                {
                    nodePin = pin;
                    return List.First;
                }
                if (pin > PinTail) throw new ArgumentOutOfRangeException("pin > PinTail");
                if (pin == PinTail)
                {
                    var last = List.Last;
                    if (last != null)
                    {
                        offsetInSegment = last.Value.Length;
                        nodePin = PinTail - last.Value.Length;
                    }
                    else
                    {
                        nodePin = PinTail;
                    }
                    return last;
                }
                int currentPin = PinHead;
                ByteLinkedListNode node = List.First;
                while (node != null)
                {
                    if (pin >= currentPin && pin < (currentPin + node.Value.Length))
                    {
                        offsetInSegment = (int)(pin - currentPin);
                        nodePin = currentPin;
                        return node;
                    }
                    currentPin += node.Value.Length;
                    node = node.Next;
                }
                throw new ApplicationException("GetNodeWithPin: Bug!");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void GetOverlappedNodes(int pinStart, int pinEnd,
            out ByteLinkedListNode firstNode, out int firstNodeOffsetInSegment, out int firstNodePin,
            out ByteLinkedListNode lastNode, out int lastNodeOffsetInSegment, out int lastNodePin,
            out int nodeCounts, out int lackRemainLength)
        {
            checked
            {
                if (pinStart > pinEnd) throw new ArgumentOutOfRangeException("pinStart > pinEnd");

                firstNode = GetNodeWithPin(pinStart, out firstNodeOffsetInSegment, out firstNodePin);

                if (pinEnd > PinTail)
                {
                    lackRemainLength = (int)checked(pinEnd - PinTail);
                    pinEnd = PinTail;
                }

                ByteLinkedListNode node = firstNode;
                int currentPin = pinStart - firstNodeOffsetInSegment;
                nodeCounts = 0;
                while (true)
                {
                    Debug.Assert(node != null, "node == null");

                    nodeCounts++;
                    if (pinEnd <= (currentPin + node.Value.Length))
                    {
                        lastNodeOffsetInSegment = (int)(pinEnd - currentPin);
                        lastNode = node;
                        lackRemainLength = 0;
                        lastNodePin = currentPin;

                        Debug.Assert(firstNodeOffsetInSegment != firstNode.Value.Length);
                        Debug.Assert(lastNodeOffsetInSegment != 0);

                        return;
                    }
                    currentPin += node.Value.Length;
                    node = node.Next;
                }
            }
        }

        public ReadOnlySpan<Memory<byte>> GetAllFast()
        {
            PacketSegment[] segments = GetSegmentsFast(this.PinHead, this.Length, out int readSize, true);

            Span<Memory<byte>> ret = new Memory<byte>[segments.Length];

            for (int i = 0; i < segments.Length; i++)
            {
                ret[i] = segments[i].Item;
            }

            return ret;
        }

        public PacketSegment[] GetSegmentsFast(int pin, int size, out int readSize, bool allowPartial = false)
        {
            checked
            {
                if (size < 0) throw new ArgumentOutOfRangeException("size < 0");
                if (size == 0)
                {
                    readSize = 0;
                    return new PacketSegment[0];
                }
                if (pin > PinTail)
                {
                    throw new ArgumentOutOfRangeException("pin > PinTail");
                }
                if ((pin + size) > PinTail)
                {
                    if (allowPartial == false)
                        throw new ArgumentOutOfRangeException("(pin + size) > PinTail");
                    size = PinTail - pin;
                }

                PacketSegment[] ret = GetUncontiguousSegments(pin, pin + size, false);
                readSize = size;
                return ret;
            }
        }

        public Memory<byte> GetContiguous(int pin, int size, bool allowPartial = false)
        {
            checked
            {
                if (size < 0) throw new ArgumentOutOfRangeException("size < 0");
                if (size == 0)
                {
                    return new Memory<byte>();
                }
                if (pin > PinTail)
                {
                    throw new ArgumentOutOfRangeException("pin > PinTail");
                }
                if ((pin + size) > PinTail)
                {
                    if (allowPartial == false)
                        throw new ArgumentOutOfRangeException("(pin + size) > PinTail");
                    size = PinTail - pin;
                }
                Memory<byte> ret = GetContiguousMemory(pin, pin + size, false, false);
                return ret;
            }
        }

        public Memory<byte> PutContiguous(int pin, int size, bool appendIfOverrun = false)
        {
            checked
            {
                if (size < 0) throw new ArgumentOutOfRangeException("size < 0");
                if (size == 0)
                {
                    return new Memory<byte>();
                }
                Memory<byte> ret = GetContiguousMemory(pin, pin + size, appendIfOverrun, false);
                return ret;
            }
        }

        PacketSegment[] GetUncontiguousSegments(int pinStart, int pinEnd, bool appendIfOverrun)
        {
            checked
            {
                if (pinStart == pinEnd) return new PacketSegment[0];
                if (pinStart > pinEnd) throw new ArgumentOutOfRangeException("pinStart > pinEnd");

                if (appendIfOverrun)
                {
                    if (List.First == null)
                    {
                        InsertHeadInternal(new byte[pinEnd - pinStart]);
                        PinHead = pinStart;
                        PinTail = pinEnd;
                    }

                    if (pinStart < PinHead)
                        InsertBefore(new byte[PinHead - pinStart]);

                    if (pinEnd > PinTail)
                        InsertTail(new byte[pinEnd - PinTail]);
                }
                else
                {
                    if (List.First == null) throw new ArgumentOutOfRangeException("Buffer is empty.");
                    if (pinStart < PinHead) throw new ArgumentOutOfRangeException("pinStart < PinHead");
                    if (pinEnd > PinTail) throw new ArgumentOutOfRangeException("pinEnd > PinTail");
                }

                GetOverlappedNodes(pinStart, pinEnd,
                    out ByteLinkedListNode firstNode, out int firstNodeOffsetInSegment, out int firstNodePin,
                    out ByteLinkedListNode lastNode, out int lastNodeOffsetInSegment, out int lastNodePin,
                    out int nodeCounts, out int lackRemainLength);

                Debug.Assert(lackRemainLength == 0, "lackRemainLength != 0");

                if (firstNode == lastNode)
                    return new PacketSegment[1]{ new PacketSegment(
                    firstNode.Value.Slice(firstNodeOffsetInSegment, lastNodeOffsetInSegment - firstNodeOffsetInSegment), pinStart, 0) };

                PacketSegment[] ret = new PacketSegment[nodeCounts];

                ByteLinkedListNode prevNode = firstNode.Previous;
                ByteLinkedListNode nextNode = lastNode.Next;

                ByteLinkedListNode node = firstNode;
                int count = 0;
                int currentOffset = 0;

                while (true)
                {
                    Debug.Assert(node != null, "node == null");

                    int sliceStart = (node == firstNode) ? firstNodeOffsetInSegment : 0;
                    int sliceLength = (node == lastNode) ? lastNodeOffsetInSegment : node.Value.Length - sliceStart;

                    ret[count] = new PacketSegment(node.Value.Slice(sliceStart, sliceLength), currentOffset + pinStart, currentOffset);
                    count++;

                    Debug.Assert(count <= nodeCounts, "count > nodeCounts");

                    currentOffset += sliceLength;

                    if (node == lastNode)
                    {
                        Debug.Assert(count == ret.Length, "count != ret.Length");
                        break;
                    }

                    node = node.Next;
                }

                return ret;
            }
        }

        public void Remove(int pinStart, int length)
        {
            checked
            {
                if (length == 0) return;
                if (length < 0) throw new ArgumentOutOfRangeException("length < 0");
                int pinEnd = checked(pinStart + length);
                if (List.First == null) throw new ArgumentOutOfRangeException("Buffer is empty.");
                if (pinStart < PinHead) throw new ArgumentOutOfRangeException("pinStart < PinHead");
                if (pinEnd > PinTail) throw new ArgumentOutOfRangeException("pinEnd > PinTail");

                GetOverlappedNodes(pinStart, pinEnd,
                    out ByteLinkedListNode firstNode, out int firstNodeOffsetInSegment, out int firstNodePin,
                    out ByteLinkedListNode lastNode, out int lastNodeOffsetInSegment, out int lastNodePin,
                    out int nodeCounts, out int lackRemainLength);

                Debug.Assert(lackRemainLength == 0, "lackRemainLength != 0");

                if (firstNode == lastNode)
                {
                    Debug.Assert(firstNodeOffsetInSegment < lastNodeOffsetInSegment);
                    if (firstNodeOffsetInSegment == 0 && lastNodeOffsetInSegment == lastNode.Value.Length)
                    {
                        Debug.Assert(firstNode.Value.Length == length, "firstNode.Value.Length != length");
                        List.Remove(firstNode);
                        PinTail -= length;
                        return;
                    }
                    else
                    {
                        Debug.Assert((lastNodeOffsetInSegment - firstNodeOffsetInSegment) == length);
                        Memory<byte> slice1 = firstNode.Value.Slice(0, firstNodeOffsetInSegment);
                        Memory<byte> slice2 = firstNode.Value.Slice(lastNodeOffsetInSegment);
                        Debug.Assert(slice1.Length != 0 || slice2.Length != 0);
                        if (slice1.Length == 0)
                        {
                            firstNode.Value = slice2;
                        }
                        else if (slice2.Length == 0)
                        {
                            firstNode.Value = slice1;
                        }
                        else
                        {
                            firstNode.Value = slice1;
                            List.AddAfter(firstNode, slice2);
                        }
                        PinTail -= length;
                        return;
                    }
                }
                else
                {
                    firstNode.Value = firstNode.Value.Slice(0, firstNodeOffsetInSegment);
                    lastNode.Value = lastNode.Value.Slice(lastNodeOffsetInSegment);

                    var node = firstNode.Next;
                    while (node != lastNode)
                    {
                        var nodeToDelete = node;

                        Debug.Assert(node.Next != null);
                        node = node.Next;

                        List.Remove(nodeToDelete);
                    }

                    if (lastNode.Value.Length == 0)
                        List.Remove(lastNode);

                    if (firstNode.Value.Length == 0)
                        List.Remove(firstNode);

                    PinTail -= length;
                    return;
                }
            }
        }

        public byte[] ToArray() => GetContiguousMemory(PinHead, PinTail, false, true).ToArray();

        public byte[] ItemsSlow { get => ToArray(); }

        Memory<byte> GetContiguousMemory(int pinStart, int pinEnd, bool appendIfOverrun, bool noReplace)
        {
            checked
            {
                if (pinStart == pinEnd) return new Memory<byte>();
                if (pinStart > pinEnd) throw new ArgumentOutOfRangeException("pinStart > pinEnd");

                if (appendIfOverrun)
                {
                    if (List.First == null)
                    {
                        InsertHeadInternal(new byte[pinEnd - pinStart]);
                        PinHead = pinStart;
                        PinTail = pinEnd;
                    }

                    if (pinStart < PinHead)
                        InsertBefore(new byte[PinHead - pinStart]);

                    if (pinEnd > PinTail)
                        InsertTail(new byte[pinEnd - PinTail]);
                }
                else
                {
                    ByteLinkedListNode firstList = List.First;
                    if (firstList == null) throw new ArgumentOutOfRangeException("Buffer is empty.");
                    if (pinStart < PinHead) throw new ArgumentOutOfRangeException("pinStart < PinHead");
                    if (pinEnd > PinTail) throw new ArgumentOutOfRangeException("pinEnd > PinTail");

                    if (firstList.Next == null)
                    {
                        return firstList.Value.Slice(pinStart - PinHead, pinEnd - pinStart);
                    }
                }

                GetOverlappedNodes(pinStart, pinEnd,
                    out ByteLinkedListNode firstNode, out int firstNodeOffsetInSegment, out int firstNodePin,
                    out ByteLinkedListNode lastNode, out int lastNodeOffsetInSegment, out int lastNodePin,
                    out int nodeCounts, out int lackRemainLength);

                Debug.Assert(lackRemainLength == 0, "lackRemainLength != 0");

                if (firstNode == lastNode)
                    return firstNode.Value.Slice(firstNodeOffsetInSegment, lastNodeOffsetInSegment - firstNodeOffsetInSegment);

                ByteLinkedListNode prevNode = firstNode.Previous;
                ByteLinkedListNode nextNode = lastNode.Next;

                Memory<byte> newMemory = new byte[lastNodePin + lastNode.Value.Length - firstNodePin];
                ByteLinkedListNode node = firstNode;
                int currentWritePointer = 0;

                while (true)
                {
                    Debug.Assert(node != null, "node == null");

                    bool finish = false;
                    node.Value.CopyTo(newMemory.Slice(currentWritePointer));

                    if (node == lastNode) finish = true;

                    ByteLinkedListNode nodeToDelete = node;
                    currentWritePointer += node.Value.Length;

                    node = node.Next;

                    if (noReplace == false)
                        List.Remove(nodeToDelete);

                    if (finish) break;
                }

                if (noReplace == false)
                {
                    if (prevNode != null)
                        List.AddAfter(prevNode, newMemory);
                    else if (nextNode != null)
                        List.AddBefore(nextNode, newMemory);
                    else
                        List.AddFirst(newMemory);
                }

                var ret = newMemory.Slice(firstNodeOffsetInSegment, newMemory.Length - (lastNode.Value.Length - lastNodeOffsetInSegment) - firstNodeOffsetInSegment);
                Debug.Assert(ret.Length == (pinEnd - pinStart), "ret.Length");
                return ret;
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
        public unsafe ref readonly T AsReadOnlyStruct<T>(int pin, int? size = null) where T : struct
        {
            int size2 = size ?? Unsafe.SizeOf<T>();

            Memory<byte> data = this.GetContiguous(pin, size2, false);
            fixed (void* ptr = &data.Span[0])
                return ref Unsafe.AsRef<T>(ptr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe ref T AsStruct<T>(int pin, int? size = null)
        {
            Memory<byte> data = this.PutContiguous(pin, size ?? Unsafe.SizeOf<T>(), true);
            fixed (void* ptr = &data.Span[0])
                return ref Unsafe.AsRef<T>(ptr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PacketPin<T> GetHeader<T>(int pin, int? size = null, int maxPacketSize = int.MaxValue) where T : struct
            => new PacketPin<T>(this, pin, size, maxPacketSize);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PacketPin<T> InsertHeaderHead<T>(Memory<byte> data) where T : struct
        {
            InsertBefore(data);
            return new PacketPin<T>(this, this.PinHead, data.Length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe PacketPin<T> InsertHeaderHead<T>(in T value, int ?size = null) where T : struct
        {
            int size2 = size ?? Unsafe.SizeOf<T>();
            byte[] data = new byte[size2];
            fixed (void* ptr = data)
                (Unsafe.AsRef<T>(ptr)) = value;

            return InsertHeaderHead<T>(data);
        }
    }

    interface IPacketPin : IEmptyChecker
    {
        Packet Packet { get; }
        int Pin { get; }
        int HeaderSize { get; }
        bool IsEmpty { get; }
        bool IsFilled { get; }
        PacketPin<TNext> GetNextHeader<TNext>(int? size = null) where TNext : struct;
        ReadOnlyMemory<byte> MemoryRead { get; }
        Memory<byte> Memory { get; }
    }

    readonly unsafe struct PacketPin<T> : IPacketPin where T : struct
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

        public unsafe ref T RefValue
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref Packet.AsStruct<T>(this.Pin, this.HeaderSize);
        }

        public unsafe T _ValueDebug
        {
            get => RefValueRead;
        }

        public ReadOnlyMemory<byte> MemoryRead
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Packet.GetContiguous(this.Pin, this.HeaderSize, false);
        }

        public Memory<byte> Memory
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Packet.PutContiguous(this.Pin, this.HeaderSize, true);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PacketPin<TNext> GetNextHeader<TNext>(int? size = null) where TNext : struct
        {
            return Packet.GetHeader<TNext>(this.Pin + this.HeaderSize, size, this.PayloadSize);
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

