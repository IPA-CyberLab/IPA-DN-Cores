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
        ByteLinkedList List = new ByteLinkedList();
        public long PinHead { get; private set; } = 0;
        public long PinTail { get; private set; } = 0;
        public long Length { get { long ret = checked(PinTail - PinHead); Debug.Assert(ret >= 0); return ret; } }

        public Packet() { }

        public Packet(Memory<byte> baseMemory)
        {
            InsertHead(baseMemory);
        }

        public void Clear()
        {
            checked
            {
                List.Clear();
                PinTail = PinHead;
            }
        }

        public void InsertBefore(Memory<byte> item)
        {
            checked
            {
                if (item.IsEmpty) return;
                List.AddFirst(item);
                PinHead -= item.Length;
            }
        }

        public void InsertHead(Memory<byte> item)
        {
            checked
            {
                if (item.IsEmpty) return;
                List.AddFirst(item);
                PinTail += item.Length;
            }
        }

        public void InsertTail(Memory<byte> item)
        {
            checked
            {
                if (item.IsEmpty) return;
                List.AddLast(item);
                PinTail += item.Length;
            }
        }

        public void Insert(long pin, Memory<byte> item, bool appendIfOverrun = false)
        {
            checked
            {
                if (item.IsEmpty) return;

                if (List.First == null)
                {
                    InsertHead(item);
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
        ByteLinkedListNode GetNodeWithPin(long pin, out int offsetInSegment, out long nodePin)
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
                long currentPin = PinHead;
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
        void GetOverlappedNodes(long pinStart, long pinEnd,
            out ByteLinkedListNode firstNode, out int firstNodeOffsetInSegment, out long firstNodePin,
            out ByteLinkedListNode lastNode, out int lastNodeOffsetInSegment, out long lastNodePin,
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
                long currentPin = pinStart - firstNodeOffsetInSegment;
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
            ByteSegment[] segments = GetSegmentsFast(this.PinHead, this.Length, out long readSize, true);

            Span<Memory<byte>> ret = new Memory<byte>[segments.Length];

            for (int i = 0; i < segments.Length; i++)
            {
                ret[i] = segments[i].Item;
            }

            return ret;
        }

        public ByteSegment[] GetSegmentsFast(long pin, long size, out long readSize, bool allowPartial = false)
        {
            checked
            {
                if (size < 0) throw new ArgumentOutOfRangeException("size < 0");
                if (size == 0)
                {
                    readSize = 0;
                    return new ByteSegment[0];
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

                ByteSegment[] ret = GetUncontiguousSegments(pin, pin + size, false);
                readSize = size;
                return ret;
            }
        }

        public ByteSegment[] ReadForwardFast(ref long pin, long size, out long readSize, bool allowPartial = false)
        {
            checked
            {
                ByteSegment[] ret = GetSegmentsFast(pin, size, out readSize, allowPartial);
                pin += readSize;
                return ret;
            }
        }

        public Memory<byte> GetContiguous(long pin, long size, bool allowPartial = false)
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

        public Memory<byte> ReadForwardContiguous(ref long pin, long size, bool allowPartial = false)
        {
            checked
            {
                Memory<byte> ret = GetContiguous(pin, size, allowPartial);
                pin += ret.Length;
                return ret;
            }
        }

        public Memory<byte> PutContiguous(long pin, long size, bool appendIfOverrun = false)
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

        public Memory<byte> WriteForwardContiguous(ref long pin, long size, bool appendIfOverrun = false)
        {
            checked
            {
                Memory<byte> ret = PutContiguous(pin, size, appendIfOverrun);
                pin += ret.Length;
                return ret;
            }
        }

        public void Enqueue(Memory<byte> item)
        {
            long oldLen = Length;
            if (item.Length == 0) return;
            InsertTail(item);
        }

        public void EnqueueAll(ReadOnlySpan<Memory<byte>> itemList)
        {
            checked
            {
                int num = 0;
                long oldLen = Length;
                foreach (Memory<byte> t in itemList)
                {
                    if (t.Length != 0)
                    {
                        List.AddLast(t);
                        PinTail += t.Length;
                        num++;
                    }
                }
            }
        }

        public int DequeueContiguousSlow(Memory<byte> dest, int size = int.MaxValue)
        {
            checked
            {
                long oldLen = Length;
                if (size < 0) throw new ArgumentOutOfRangeException("size < 0");
                size = Math.Min(size, dest.Length);
                Debug.Assert(size >= 0);
                if (size == 0) return 0;
                var memarray = Dequeue(size, out long totalSize, true);
                Debug.Assert(totalSize <= size);
                if (totalSize > int.MaxValue) throw new IndexOutOfRangeException("totalSize > int.MaxValue");
                if (dest.Length < totalSize) throw new ArgumentOutOfRangeException("dest.Length < totalSize");
                int pos = 0;
                foreach (var mem in memarray)
                {
                    mem.CopyTo(dest.Slice(pos, mem.Length));
                    pos += mem.Length;
                }
                Debug.Assert(pos == totalSize);
                return (int)totalSize;
            }
        }

        public Memory<byte> DequeueContiguousSlow(int size = int.MaxValue)
        {
            checked
            {
                long oldLen = Length;
                if (size < 0) throw new ArgumentOutOfRangeException("size < 0");
                if (size == 0) return Memory<byte>.Empty;
                int readSize = (int)Math.Min(size, Length);
                Memory<byte> ret = new byte[readSize];
                int r = DequeueContiguousSlow(ret, readSize);
                Debug.Assert(r <= readSize);
                ret = ret.Slice(0, r);
                return ret;
            }
        }

        public IReadOnlyList<Memory<byte>> DequeueAll(out long totalReadSize) => Dequeue(long.MaxValue, out totalReadSize);

        public IReadOnlyList<Memory<byte>> Dequeue(long minReadSize, out long totalReadSize, bool allowSplitSegments = true)
        {
            checked
            {
                if (minReadSize < 1) throw new ArgumentOutOfRangeException("minReadSize < 1");

                totalReadSize = 0;
                if (List.First == null)
                {
                    return new List<Memory<byte>>();
                }

                long oldLen = Length;

                ByteLinkedListNode node = List.First;
                List<Memory<byte>> ret = new List<Memory<byte>>();
                while (true)
                {
                    if ((totalReadSize + node.Value.Length) >= minReadSize)
                    {
                        if (allowSplitSegments && (totalReadSize + node.Value.Length) > minReadSize)
                        {
                            int lastSegmentReadSize = (int)(minReadSize - totalReadSize);
                            Debug.Assert(lastSegmentReadSize <= node.Value.Length);
                            ret.Add(node.Value.Slice(0, lastSegmentReadSize));
                            if (lastSegmentReadSize == node.Value.Length)
                                List.Remove(node);
                            else
                                node.Value = node.Value.Slice(lastSegmentReadSize);
                            totalReadSize += lastSegmentReadSize;
                            PinHead += totalReadSize;
                            Debug.Assert(minReadSize >= totalReadSize);
                            return ret;
                        }
                        else
                        {
                            ret.Add(node.Value);
                            totalReadSize += node.Value.Length;
                            List.Remove(node);
                            PinHead += totalReadSize;
                            Debug.Assert(minReadSize <= totalReadSize);
                            return ret;
                        }
                    }
                    else
                    {
                        ret.Add(node.Value);
                        totalReadSize += node.Value.Length;

                        ByteLinkedListNode deleteNode = node;
                        node = node.Next;

                        List.Remove(deleteNode);

                        if (node == null)
                        {
                            PinHead += totalReadSize;
                            return ret;
                        }
                    }
                }
            }
        }

        public long DequeueAllAndEnqueueToOther(IFastBuffer<Memory<byte>> other) => DequeueAllAndEnqueueToOther((Packet)other);

        public long DequeueAllAndEnqueueToOther(Packet other)
        {
            checked
            {
                if (this == other) throw new ArgumentException("this == other");

                if (this.Length == 0)
                {
                    Debug.Assert(this.List.Count == 0);
                    return 0;
                }

                if (other.Length == 0)
                {
                    long length = this.Length;
                    long otherOldLen = other.Length;
                    long oldLen = Length;
                    Debug.Assert(other.List.Count == 0);
                    other.List = this.List;
                    this.List = new ByteLinkedList();
                    this.PinHead = this.PinTail;
                    other.PinTail += length;
                    return length;
                }
                else
                {
                    long length = this.Length;
                    long oldLen = Length;
                    long otherOldLen = other.Length;
                    var chainFirst = this.List.First;
                    var chainLast = this.List.Last;
                    other.List.AddLast(this.List.First, this.List.Last, this.List.Count);
                    this.List.Clear();
                    this.PinHead = this.PinTail;
                    other.PinTail += length;
                    return length;
                }
            }
        }

        ByteSegment[] GetUncontiguousSegments(long pinStart, long pinEnd, bool appendIfOverrun)
        {
            checked
            {
                if (pinStart == pinEnd) return new ByteSegment[0];
                if (pinStart > pinEnd) throw new ArgumentOutOfRangeException("pinStart > pinEnd");

                if (appendIfOverrun)
                {
                    if (List.First == null)
                    {
                        InsertHead(new byte[pinEnd - pinStart]);
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
                    out ByteLinkedListNode firstNode, out int firstNodeOffsetInSegment, out long firstNodePin,
                    out ByteLinkedListNode lastNode, out int lastNodeOffsetInSegment, out long lastNodePin,
                    out int nodeCounts, out int lackRemainLength);

                Debug.Assert(lackRemainLength == 0, "lackRemainLength != 0");

                if (firstNode == lastNode)
                    return new ByteSegment[1]{ new ByteSegment(
                    firstNode.Value.Slice(firstNodeOffsetInSegment, lastNodeOffsetInSegment - firstNodeOffsetInSegment), pinStart, 0) };

                ByteSegment[] ret = new ByteSegment[nodeCounts];

                ByteLinkedListNode prevNode = firstNode.Previous;
                ByteLinkedListNode nextNode = lastNode.Next;

                ByteLinkedListNode node = firstNode;
                int count = 0;
                long currentOffset = 0;

                while (true)
                {
                    Debug.Assert(node != null, "node == null");

                    int sliceStart = (node == firstNode) ? firstNodeOffsetInSegment : 0;
                    int sliceLength = (node == lastNode) ? lastNodeOffsetInSegment : node.Value.Length - sliceStart;

                    ret[count] = new ByteSegment(node.Value.Slice(sliceStart, sliceLength), currentOffset + pinStart, currentOffset);
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

        public void Remove(long pinStart, long length)
        {
            checked
            {
                if (length == 0) return;
                if (length < 0) throw new ArgumentOutOfRangeException("length < 0");
                long pinEnd = checked(pinStart + length);
                if (List.First == null) throw new ArgumentOutOfRangeException("Buffer is empty.");
                if (pinStart < PinHead) throw new ArgumentOutOfRangeException("pinStart < PinHead");
                if (pinEnd > PinTail) throw new ArgumentOutOfRangeException("pinEnd > PinTail");

                GetOverlappedNodes(pinStart, pinEnd,
                    out ByteLinkedListNode firstNode, out int firstNodeOffsetInSegment, out long firstNodePin,
                    out ByteLinkedListNode lastNode, out int lastNodeOffsetInSegment, out long lastNodePin,
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

        Memory<byte> GetContiguousMemory(long pinStart, long pinEnd, bool appendIfOverrun, bool noReplace)
        {
            checked
            {
                if (pinStart == pinEnd) return new Memory<byte>();
                if (pinStart > pinEnd) throw new ArgumentOutOfRangeException("pinStart > pinEnd");

                if (appendIfOverrun)
                {
                    if (List.First == null)
                    {
                        InsertHead(new byte[pinEnd - pinStart]);
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
                    out ByteLinkedListNode firstNode, out int firstNodeOffsetInSegment, out long firstNodePin,
                    out ByteLinkedListNode lastNode, out int lastNodeOffsetInSegment, out long lastNodePin,
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

        public static implicit operator Packet(Memory<byte> memory)
            => new Packet(memory);

        public static implicit operator Packet(Span<byte> span) => span.ToArray().AsMemory();
        public static implicit operator Packet(ReadOnlySpan<byte> span) => span.ToArray().AsMemory();
        public static implicit operator Packet(byte[] data) => data.AsMemory();
    }
}

