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
    enum FastBufferCallbackEventType
    {
        Init,
        Written,
        Read,
        PartialProcessReadData,
        EmptyToNonEmpty,
        NonEmptyToEmpty,
        Disconnected,
    }

    interface IFastBufferState
    {
        long Id { get; }

        long PinHead { get; }
        long PinTail { get; }
        long Length { get; }

        ExceptionQueue ExceptionQueue { get; }
        LayerInfo Info { get; }

        CriticalSection LockObj { get; }

        bool IsReadyToWrite { get; }
        bool IsReadyToRead { get; }
        bool IsEventsEnabled { get; }
        AsyncAutoResetEvent EventWriteReady { get; }
        AsyncAutoResetEvent EventReadReady { get; }

        FastEventListenerList<IFastBufferState, FastBufferCallbackEventType> EventListeners { get; }

        void CompleteRead();
        void CompleteWrite(bool checkDisconnect = true);
    }

    static class IFastBufferStateHelper
    {
        static readonly int PollingTimeout = CoresConfig.FastPipeConfig.PollingTimeout;

        public static async Task WaitForReadyToWriteAsync(this IFastBufferState writer, CancellationToken cancel, int timeout)
        {
            LocalTimer timer = new LocalTimer();

            timer.AddTimeout(PollingTimeout);
            long timeoutTick = timer.AddTimeout(timeout);

            while (writer.IsReadyToWrite == false)
            {
                if (FastTick64.Now >= timeoutTick) throw new TimeoutException();
                cancel.ThrowIfCancellationRequested();

                await TaskUtil.WaitObjectsAsync(
                    cancels: new CancellationToken[] { cancel },
                    events: new AsyncAutoResetEvent[] { writer.EventWriteReady },
                    timeout: timer.GetNextInterval()
                    );
            }

            cancel.ThrowIfCancellationRequested();
        }

        public static async Task WaitForReadyToReadAsync(this IFastBufferState reader, CancellationToken cancel, int timeout)
        {
            LocalTimer timer = new LocalTimer();

            timer.AddTimeout(PollingTimeout);
            long timeoutTick = timer.AddTimeout(timeout);

            while (reader.IsReadyToRead == false)
            {
                if (FastTick64.Now >= timeoutTick) throw new TimeoutException();
                cancel.ThrowIfCancellationRequested();

                await TaskUtil.WaitObjectsAsync(
                    cancels: new CancellationToken[] { cancel },
                    events: new AsyncAutoResetEvent[] { reader.EventReadReady },
                    timeout: timer.GetNextInterval()
                    );
            }

            cancel.ThrowIfCancellationRequested();
        }
    }

    interface IFastBuffer<T> : IFastBufferState
    {
        void Clear();
        void Enqueue(T item);
        void EnqueueAll(Span<T> itemList);
        void EnqueueAllWithLock(Span<T> itemList);
        IReadOnlyList<T> Dequeue(long minReadSize, out long totalReadSize, bool allowSplitSegments = true);
        IReadOnlyList<T> DequeueWithLock(long minReadSize, out long totalReadSize, bool allowSplitSegments = true);
        IReadOnlyList<T> DequeueAll(out long totalReadSize);
        IReadOnlyList<T> DequeueAllWithLock(out long totalReadSize);
        long DequeueAllAndEnqueueToOther(IFastBuffer<T> other);
    }

    readonly struct FastBufferSegment<T>
    {
        public readonly T Item;
        public readonly long Pin;
        public readonly long RelativeOffset;

        public FastBufferSegment(T item, long pin, long relativeOffset)
        {
            Item = item;
            Pin = pin;
            RelativeOffset = relativeOffset;
        }
    }

    static internal class FastBufferGlobalIdCounter
    {
        static long Id = 0;
        public static long NewId() => Interlocked.Increment(ref Id);
    }

    class FastStreamBuffer<T> : IFastBuffer<Memory<T>>
    {
        FastLinkedList<Memory<T>> List = new FastLinkedList<Memory<T>>();
        public long PinHead { get; private set; } = 0;
        public long PinTail { get; private set; } = 0;
        public long Length { get { long ret = checked(PinTail - PinHead); Debug.Assert(ret >= 0); return ret; } }
        public long Threshold { get; set; }
        public long Id { get; }

        public FastEventListenerList<IFastBufferState, FastBufferCallbackEventType> EventListeners { get; }
            = new FastEventListenerList<IFastBufferState, FastBufferCallbackEventType>();

        public bool IsReadyToWrite
        {
            get
            {
                if (IsDisconnected) return true;
                if (Length <= Threshold) return true;
                CompleteWrite(false);
                return false;
            }
        }

        public bool IsReadyToRead
        {
            get
            {
                if (IsDisconnected) return true;
                if (Length >= 1) return true;
                CompleteRead();
                return false;
            }
        }
        public bool IsEventsEnabled { get; }

        Once internalDisconnectedFlag;
        public bool IsDisconnected { get => internalDisconnectedFlag.IsSet; }

        public AsyncAutoResetEvent EventWriteReady { get; } = null;
        public AsyncAutoResetEvent EventReadReady { get; } = null;

        public const long DefaultThreshold = 524288;

        public List<Action> OnDisconnected { get; } = new List<Action>();

        public CriticalSection LockObj { get; } = new CriticalSection();

        public ExceptionQueue ExceptionQueue { get; } = new ExceptionQueue();
        public LayerInfo Info { get; } = new LayerInfo();

        public FastStreamBuffer(bool enableEvents = false, long? thresholdLength = null)
        {
            if (thresholdLength < 0) throw new ArgumentOutOfRangeException("thresholdLength < 0");

            Threshold = thresholdLength ?? DefaultThreshold;
            IsEventsEnabled = enableEvents;
            if (IsEventsEnabled)
            {
                EventWriteReady = new AsyncAutoResetEvent();
                EventReadReady = new AsyncAutoResetEvent();
            }

            Id = FastBufferGlobalIdCounter.NewId();

            EventListeners.Fire(this, FastBufferCallbackEventType.Init);
        }

        Once checkDisconnectFlag;

        public void CheckDisconnected()
        {
            if (IsDisconnected)
            {
                if (checkDisconnectFlag.IsFirstCall())
                {
                    ExceptionQueue.Raise(new FastBufferDisconnectedException());
                }
                else
                {
                    ExceptionQueue.ThrowFirstExceptionIfExists();
                    throw new FastBufferDisconnectedException();
                }
            }
        }

        public void Disconnect()
        {
            if (internalDisconnectedFlag.IsFirstCall())
            {
                foreach (var ev in OnDisconnected)
                {
                    try
                    {
                        ev();
                    }
                    catch { }
                }
                EventReadReady.Set();
                EventWriteReady.Set();

                EventListeners.Fire(this, FastBufferCallbackEventType.Disconnected);
            }
        }

        long LastHeadPin = long.MinValue;

        public void CompleteRead()
        {
            if (IsEventsEnabled)
            {
                bool setFlag = false;

                lock (LockObj)
                {
                    long current = PinHead;
                    if (LastHeadPin != current)
                    {
                        LastHeadPin = current;
                        if (IsReadyToWrite)
                            setFlag = true;
                    }
                    if (IsDisconnected)
                        setFlag = true;
                }

                if (setFlag)
                {
                    EventWriteReady.Set();
                }
            }
        }

        long LastTailPin = long.MinValue;

        public void CompleteWrite(bool checkDisconnect = true)
        {
            if (IsEventsEnabled)
            {
                bool setFlag = false;
                lock (LockObj)
                {
                    long current = PinTail;
                    if (LastTailPin != current)
                    {
                        LastTailPin = current;
                        setFlag = true;
                    }
                    if (IsDisconnected)
                        setFlag = true;
                }
                if (setFlag)
                {
                    EventReadReady.Set();
                }
            }

            if (checkDisconnect)
                CheckDisconnected();
        }

        public void Clear()
        {
            checked
            {
                List.Clear();
                PinTail = PinHead;
            }
        }

        public void InsertBefore(Memory<T> item)
        {
            CheckDisconnected();
            checked
            {
                if (item.IsEmpty) return;
                List.AddFirst(item);
                PinHead -= item.Length;
            }
        }

        public void InsertHead(Memory<T> item)
        {
            CheckDisconnected();
            checked
            {
                if (item.IsEmpty) return;
                List.AddFirst(item);
                PinTail += item.Length;
            }
        }

        public void InsertTail(Memory<T> item)
        {
            CheckDisconnected();
            checked
            {
                if (item.IsEmpty) return;
                List.AddLast(item);
                PinTail += item.Length;
            }
        }

        public void Insert(long pin, Memory<T> item, bool appendIfOverrun = false)
        {
            CheckDisconnected();
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
                        InsertBefore(new T[PinHead - pin]);

                    if (pin > PinTail)
                        InsertTail(new T[pin - PinTail]);
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
                    Memory<T> sliceBefore = node.Value.Slice(0, offsetInSegment);
                    Memory<T> sliceAfter = node.Value.Slice(offsetInSegment);

                    node.Value = sliceBefore;
                    var newNode = List.AddAfter(node, item);
                    List.AddAfter(newNode, sliceAfter);
                    PinTail += item.Length;
                }
            }
        }

        FastLinkedListNode<Memory<T>> GetNodeWithPin(long pin, out int offsetInSegment, out long nodePin)
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
                FastLinkedListNode<Memory<T>> node = List.First;
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

        void GetOverlappedNodes(long pinStart, long pinEnd,
            out FastLinkedListNode<Memory<T>> firstNode, out int firstNodeOffsetInSegment, out long firstNodePin,
            out FastLinkedListNode<Memory<T>> lastNode, out int lastNodeOffsetInSegment, out long lastNodePin,
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

                FastLinkedListNode<Memory<T>> node = firstNode;
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

        public FastBufferSegment<Memory<T>>[] GetSegmentsFast(long pin, long size, out long readSize, bool allowPartial = false)
        {
            checked
            {
                if (size < 0) throw new ArgumentOutOfRangeException("size < 0");
                if (size == 0)
                {
                    readSize = 0;
                    return new FastBufferSegment<Memory<T>>[0];
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

                FastBufferSegment<Memory<T>>[] ret = GetUncontiguousSegments(pin, pin + size, false);
                readSize = size;
                return ret;
            }
        }

        public FastBufferSegment<Memory<T>>[] ReadForwardFast(ref long pin, long size, out long readSize, bool allowPartial = false)
        {
            checked
            {
                FastBufferSegment<Memory<T>>[] ret = GetSegmentsFast(pin, size, out readSize, allowPartial);
                pin += readSize;
                return ret;
            }
        }

        public Memory<T> GetContiguous(long pin, long size, bool allowPartial = false)
        {
            checked
            {
                if (size < 0) throw new ArgumentOutOfRangeException("size < 0");
                if (size == 0)
                {
                    return new Memory<T>();
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
                Memory<T> ret = GetContiguousMemory(pin, pin + size, false, false);
                return ret;
            }
        }

        public Memory<T> ReadForwardContiguous(ref long pin, long size, bool allowPartial = false)
        {
            checked
            {
                Memory<T> ret = GetContiguous(pin, size, allowPartial);
                pin += ret.Length;
                return ret;
            }
        }

        public Memory<T> PutContiguous(long pin, long size, bool appendIfOverrun = false)
        {
            checked
            {
                if (size < 0) throw new ArgumentOutOfRangeException("size < 0");
                if (size == 0)
                {
                    return new Memory<T>();
                }
                Memory<T> ret = GetContiguousMemory(pin, pin + size, appendIfOverrun, false);
                return ret;
            }
        }

        public Memory<T> WriteForwardContiguous(ref long pin, long size, bool appendIfOverrun = false)
        {
            checked
            {
                Memory<T> ret = PutContiguous(pin, size, appendIfOverrun);
                pin += ret.Length;
                return ret;
            }
        }

        public void Enqueue(Memory<T> item)
        {
            CheckDisconnected();
            long oldLen = Length;
            if (item.Length == 0) return;
            InsertTail(item);
            EventListeners.Fire(this, FastBufferCallbackEventType.Written);
            if (Length != 0 && oldLen == 0)
                EventListeners.Fire(this, FastBufferCallbackEventType.EmptyToNonEmpty);
        }

        public void EnqueueAllWithLock(Span<Memory<T>> itemList)
        {
            lock (LockObj)
                EnqueueAll(itemList);
        }

        public void EnqueueAll(Span<Memory<T>> itemList)
        {
            CheckDisconnected();
            checked
            {
                int num = 0;
                long oldLen = Length;
                foreach (Memory<T> t in itemList)
                {
                    if (t.Length != 0)
                    {
                        List.AddLast(t);
                        PinTail += t.Length;
                        num++;
                    }
                }
                if (num >= 1)
                {
                    EventListeners.Fire(this, FastBufferCallbackEventType.Written);

                    if (Length != 0 && oldLen == 0)
                        EventListeners.Fire(this, FastBufferCallbackEventType.EmptyToNonEmpty);
                }
            }
        }

        public int DequeueContiguousSlowWithLock(Memory<T> dest, int size = int.MaxValue)
        {
            lock (this.LockObj)
                return DequeueContiguousSlow(dest, size);
        }

        public int DequeueContiguousSlow(Memory<T> dest, int size = int.MaxValue)
        {
            if (IsDisconnected && this.Length == 0) CheckDisconnected();
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
                EventListeners.Fire(this, FastBufferCallbackEventType.Read);
                if (Length == 0 && oldLen != 0)
                    EventListeners.Fire(this, FastBufferCallbackEventType.NonEmptyToEmpty);
                return (int)totalSize;
            }
        }

        public Memory<T> DequeueContiguousSlowWithLock(int size = int.MaxValue)
        {
            lock (this.LockObj)
                return DequeueContiguousSlow(size);
        }

        public Memory<T> DequeueContiguousSlow(int size = int.MaxValue)
        {
            if (IsDisconnected && this.Length == 0) CheckDisconnected();
            checked
            {
                long oldLen = Length;
                if (size < 0) throw new ArgumentOutOfRangeException("size < 0");
                if (size == 0) return Memory<T>.Empty;
                int readSize = (int)Math.Min(size, Length);
                Memory<T> ret = new T[readSize];
                int r = DequeueContiguousSlow(ret, readSize);
                Debug.Assert(r <= readSize);
                ret = ret.Slice(0, r);
                EventListeners.Fire(this, FastBufferCallbackEventType.Read);
                if (Length == 0 && oldLen != 0)
                    EventListeners.Fire(this, FastBufferCallbackEventType.NonEmptyToEmpty);
                return ret;
            }
        }

        public IReadOnlyList<Memory<T>> DequeueAllWithLock(out long totalReadSize)
        {
            lock (this.LockObj)
                return DequeueAll(out totalReadSize);
        }
        public IReadOnlyList<Memory<T>> DequeueAll(out long totalReadSize) => Dequeue(long.MaxValue, out totalReadSize);

        public IReadOnlyList<Memory<T>> DequeueWithLock(long minReadSize, out long totalReadSize, bool allowSplitSegments = true)
        {
            lock (this.LockObj)
                return Dequeue(minReadSize, out totalReadSize, allowSplitSegments);
        }

        public IReadOnlyList<Memory<T>> Dequeue(long minReadSize, out long totalReadSize, bool allowSplitSegments = true)
        {
            if (IsDisconnected && this.Length == 0) CheckDisconnected();
            checked
            {
                if (minReadSize < 1) throw new ArgumentOutOfRangeException("size < 1");

                totalReadSize = 0;
                if (List.First == null)
                {
                    return new List<Memory<T>>();
                }

                long oldLen = Length;

                FastLinkedListNode<Memory<T>> node = List.First;
                List<Memory<T>> ret = new List<Memory<T>>();
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
                            EventListeners.Fire(this, FastBufferCallbackEventType.Read);
                            if (Length == 0 && oldLen != 0)
                                EventListeners.Fire(this, FastBufferCallbackEventType.NonEmptyToEmpty);
                            return ret;
                        }
                        else
                        {
                            ret.Add(node.Value);
                            totalReadSize += node.Value.Length;
                            List.Remove(node);
                            PinHead += totalReadSize;
                            Debug.Assert(minReadSize <= totalReadSize);
                            EventListeners.Fire(this, FastBufferCallbackEventType.Read);
                            if (Length == 0 && oldLen != 0)
                                EventListeners.Fire(this, FastBufferCallbackEventType.NonEmptyToEmpty);
                            return ret;
                        }
                    }
                    else
                    {
                        ret.Add(node.Value);
                        totalReadSize += node.Value.Length;

                        FastLinkedListNode<Memory<T>> deleteNode = node;
                        node = node.Next;

                        List.Remove(deleteNode);

                        if (node == null)
                        {
                            PinHead += totalReadSize;
                            EventListeners.Fire(this, FastBufferCallbackEventType.Read);
                            if (Length == 0 && oldLen != 0)
                                EventListeners.Fire(this, FastBufferCallbackEventType.NonEmptyToEmpty);
                            return ret;
                        }
                    }
                }
            }
        }

        public long DequeueAllAndEnqueueToOther(IFastBuffer<Memory<T>> other) => DequeueAllAndEnqueueToOther((FastStreamBuffer<T>)other);

        public long DequeueAllAndEnqueueToOther(FastStreamBuffer<T> other)
        {
            if (IsDisconnected && this.Length == 0) CheckDisconnected();
            other.CheckDisconnected();
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
                    this.List = new FastLinkedList<Memory<T>>();
                    this.PinHead = this.PinTail;
                    other.PinTail += length;
                    EventListeners.Fire(this, FastBufferCallbackEventType.Read);
                    if (Length == 0 && oldLen != 0)
                        EventListeners.Fire(this, FastBufferCallbackEventType.NonEmptyToEmpty);
                    other.EventListeners.Fire(other, FastBufferCallbackEventType.Written);
                    if (other.Length != 0 && otherOldLen == 0)
                        other.EventListeners.Fire(other, FastBufferCallbackEventType.EmptyToNonEmpty);
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
                    EventListeners.Fire(this, FastBufferCallbackEventType.Read);
                    if (Length == 0 && oldLen != 0)
                        EventListeners.Fire(this, FastBufferCallbackEventType.NonEmptyToEmpty);
                    other.EventListeners.Fire(other, FastBufferCallbackEventType.Written);
                    if (other.Length != 0 && otherOldLen == 0)
                        other.EventListeners.Fire(other, FastBufferCallbackEventType.EmptyToNonEmpty);
                    return length;
                }
            }
        }

        FastBufferSegment<Memory<T>>[] GetUncontiguousSegments(long pinStart, long pinEnd, bool appendIfOverrun)
        {
            checked
            {
                if (pinStart == pinEnd) return new FastBufferSegment<Memory<T>>[0];
                if (pinStart > pinEnd) throw new ArgumentOutOfRangeException("pinStart > pinEnd");

                if (appendIfOverrun)
                {
                    if (List.First == null)
                    {
                        InsertHead(new T[pinEnd - pinStart]);
                        PinHead = pinStart;
                        PinTail = pinEnd;
                    }

                    if (pinStart < PinHead)
                        InsertBefore(new T[PinHead - pinStart]);

                    if (pinEnd > PinTail)
                        InsertTail(new T[pinEnd - PinTail]);
                }
                else
                {
                    if (List.First == null) throw new ArgumentOutOfRangeException("Buffer is empty.");
                    if (pinStart < PinHead) throw new ArgumentOutOfRangeException("pinStart < PinHead");
                    if (pinEnd > PinTail) throw new ArgumentOutOfRangeException("pinEnd > PinTail");
                }

                GetOverlappedNodes(pinStart, pinEnd,
                    out FastLinkedListNode<Memory<T>> firstNode, out int firstNodeOffsetInSegment, out long firstNodePin,
                    out FastLinkedListNode<Memory<T>> lastNode, out int lastNodeOffsetInSegment, out long lastNodePin,
                    out int nodeCounts, out int lackRemainLength);

                Debug.Assert(lackRemainLength == 0, "lackRemainLength != 0");

                if (firstNode == lastNode)
                    return new FastBufferSegment<Memory<T>>[1]{ new FastBufferSegment<Memory<T>>(
                    firstNode.Value.Slice(firstNodeOffsetInSegment, lastNodeOffsetInSegment - firstNodeOffsetInSegment), pinStart, 0) };

                FastBufferSegment<Memory<T>>[] ret = new FastBufferSegment<Memory<T>>[nodeCounts];

                FastLinkedListNode<Memory<T>> prevNode = firstNode.Previous;
                FastLinkedListNode<Memory<T>> nextNode = lastNode.Next;

                FastLinkedListNode<Memory<T>> node = firstNode;
                int count = 0;
                long currentOffset = 0;

                while (true)
                {
                    Debug.Assert(node != null, "node == null");

                    int sliceStart = (node == firstNode) ? firstNodeOffsetInSegment : 0;
                    int sliceLength = (node == lastNode) ? lastNodeOffsetInSegment : node.Value.Length - sliceStart;

                    ret[count] = new FastBufferSegment<Memory<T>>(node.Value.Slice(sliceStart, sliceLength), currentOffset + pinStart, currentOffset);
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
                    out FastLinkedListNode<Memory<T>> firstNode, out int firstNodeOffsetInSegment, out long firstNodePin,
                    out FastLinkedListNode<Memory<T>> lastNode, out int lastNodeOffsetInSegment, out long lastNodePin,
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
                        Memory<T> slice1 = firstNode.Value.Slice(0, firstNodeOffsetInSegment);
                        Memory<T> slice2 = firstNode.Value.Slice(lastNodeOffsetInSegment);
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

        public T[] ToArray() => GetContiguousMemory(PinHead, PinTail, false, true).ToArray();

        public T[] ItemsSlow { get => ToArray(); }

        Memory<T> GetContiguousMemory(long pinStart, long pinEnd, bool appendIfOverrun, bool noReplace)
        {
            checked
            {
                if (pinStart == pinEnd) return new Memory<T>();
                if (pinStart > pinEnd) throw new ArgumentOutOfRangeException("pinStart > pinEnd");

                if (appendIfOverrun)
                {
                    if (List.First == null)
                    {
                        InsertHead(new T[pinEnd - pinStart]);
                        PinHead = pinStart;
                        PinTail = pinEnd;
                    }

                    if (pinStart < PinHead)
                        InsertBefore(new T[PinHead - pinStart]);

                    if (pinEnd > PinTail)
                        InsertTail(new T[pinEnd - PinTail]);
                }
                else
                {
                    if (List.First == null) throw new ArgumentOutOfRangeException("Buffer is empty.");
                    if (pinStart < PinHead) throw new ArgumentOutOfRangeException("pinStart < PinHead");
                    if (pinEnd > PinTail) throw new ArgumentOutOfRangeException("pinEnd > PinTail");
                }

                GetOverlappedNodes(pinStart, pinEnd,
                    out FastLinkedListNode<Memory<T>> firstNode, out int firstNodeOffsetInSegment, out long firstNodePin,
                    out FastLinkedListNode<Memory<T>> lastNode, out int lastNodeOffsetInSegment, out long lastNodePin,
                    out int nodeCounts, out int lackRemainLength);

                Debug.Assert(lackRemainLength == 0, "lackRemainLength != 0");

                if (firstNode == lastNode)
                    return firstNode.Value.Slice(firstNodeOffsetInSegment, lastNodeOffsetInSegment - firstNodeOffsetInSegment);

                FastLinkedListNode<Memory<T>> prevNode = firstNode.Previous;
                FastLinkedListNode<Memory<T>> nextNode = lastNode.Next;

                Memory<T> newMemory = new T[lastNodePin + lastNode.Value.Length - firstNodePin];
                FastLinkedListNode<Memory<T>> node = firstNode;
                int currentWritePointer = 0;

                while (true)
                {
                    Debug.Assert(node != null, "node == null");

                    bool finish = false;
                    node.Value.CopyTo(newMemory.Slice(currentWritePointer));

                    if (node == lastNode) finish = true;

                    FastLinkedListNode<Memory<T>> nodeToDelete = node;
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

        public static implicit operator FastStreamBuffer<T>(Memory<T> memory)
        {
            FastStreamBuffer<T> ret = new FastStreamBuffer<T>(false, null);
            ret.Enqueue(memory);
            return ret;
        }

        public static implicit operator FastStreamBuffer<T>(Span<T> span) => span.ToArray().AsMemory();
        public static implicit operator FastStreamBuffer<T>(T[] data) => data.AsMemory();
    }

    class FastDatagramBuffer<T> : IFastBuffer<T>
    {
        Fifo<T> Fifo = new Fifo<T>();

        public long PinHead { get; private set; } = 0;
        public long PinTail { get; private set; } = 0;
        public long Length { get { long ret = checked(PinTail - PinHead); Debug.Assert(ret >= 0); return ret; } }
        public long Threshold { get; set; }
        public long Id { get; }

        public FastEventListenerList<IFastBufferState, FastBufferCallbackEventType> EventListeners { get; }
            = new FastEventListenerList<IFastBufferState, FastBufferCallbackEventType>();

        public bool IsReadyToWrite
        {
            get
            {
                if (IsDisconnected) return true;
                if (Length <= Threshold) return true;
                CompleteWrite(false);
                return false;
            }
        }

        public bool IsReadyToRead
        {
            get
            {
                if (IsDisconnected) return true;
                if (Length >= 1) return true;
                CompleteRead();
                return false;
            }
        }

        public bool IsEventsEnabled { get; }

        public AsyncAutoResetEvent EventWriteReady { get; } = null;
        public AsyncAutoResetEvent EventReadReady { get; } = null;

        Once internalDisconnectedFlag;
        public bool IsDisconnected { get => internalDisconnectedFlag.IsSet; }

        public List<Action> OnDisconnected { get; } = new List<Action>();

        public const long DefaultThreshold = 65536;

        public CriticalSection LockObj { get; } = new CriticalSection();

        public ExceptionQueue ExceptionQueue { get; } = new ExceptionQueue();
        public LayerInfo Info { get; } = new LayerInfo();

        public FastDatagramBuffer(bool enableEvents = false, long? thresholdLength = null)
        {
            if (thresholdLength < 0) throw new ArgumentOutOfRangeException("thresholdLength < 0");

            Threshold = thresholdLength ?? DefaultThreshold;
            IsEventsEnabled = enableEvents;
            if (IsEventsEnabled)
            {
                EventWriteReady = new AsyncAutoResetEvent();
                EventReadReady = new AsyncAutoResetEvent();
            }

            Id = FastBufferGlobalIdCounter.NewId();

            EventListeners.Fire(this, FastBufferCallbackEventType.Init);
        }

        Once checkDisconnectFlag;

        public void CheckDisconnected()
        {
            if (IsDisconnected)
            {
                if (checkDisconnectFlag.IsFirstCall())
                    ExceptionQueue.Raise(new FastBufferDisconnectedException());
                else
                {
                    ExceptionQueue.ThrowFirstExceptionIfExists();
                    throw new FastBufferDisconnectedException();
                }
            }
        }

        public void Disconnect()
        {
            if (internalDisconnectedFlag.IsFirstCall())
            {
                foreach (var ev in OnDisconnected)
                {
                    try
                    {
                        ev();
                    }
                    catch { }
                }
                EventReadReady.Set();
                EventWriteReady.Set();

                EventListeners.Fire(this, FastBufferCallbackEventType.Disconnected);
            }
        }

        long LastHeadPin = long.MinValue;

        public void CompleteRead()
        {
            if (IsEventsEnabled)
            {
                bool setFlag = false;

                lock (LockObj)
                {
                    long current = PinHead;
                    if (LastHeadPin != current)
                    {
                        LastHeadPin = current;
                        if (IsReadyToWrite)
                            setFlag = true;
                    }
                    if (IsDisconnected)
                        setFlag = true;
                }

                if (setFlag)
                {
                    EventWriteReady.Set();
                }
            }
        }

        long LastTailPin = long.MinValue;

        public void CompleteWrite(bool checkDisconnect = true)
        {
            if (IsEventsEnabled)
            {
                bool setFlag = false;

                lock (LockObj)
                {
                    long current = PinTail;
                    if (LastTailPin != current)
                    {
                        LastTailPin = current;
                        setFlag = true;
                    }
                }

                if (setFlag)
                {
                    EventReadReady.Set();
                }
            }
            if (checkDisconnect)
                CheckDisconnected();
        }

        public void Clear()
        {
            checked
            {
                Fifo.Clear();
                PinTail = PinHead;
            }
        }

        public void Enqueue(T item)
        {
            CheckDisconnected();
            checked
            {
                long oldLen = Length;
                Fifo.Write(item);
                PinTail++;
                EventListeners.Fire(this, FastBufferCallbackEventType.Written);
                if (Length != 0 && oldLen == 0)
                    EventListeners.Fire(this, FastBufferCallbackEventType.EmptyToNonEmpty);
            }
        }

        public void EnqueueAllWithLock(Span<T> itemList)
        {
            lock (LockObj)
                EnqueueAll(itemList);
        }

        public void EnqueueAll(Span<T> itemList)
        {
            CheckDisconnected();
            checked
            {
                long oldLen = Length;
                Fifo.Write(itemList);
                PinTail += itemList.Length;
                EventListeners.Fire(this, FastBufferCallbackEventType.Written);
                if (Length != 0 && oldLen == 0)
                    EventListeners.Fire(this, FastBufferCallbackEventType.EmptyToNonEmpty);
            }
        }

        public IReadOnlyList<T> DequeueWithLock(long minReadSize, out long totalReadSize, bool allowSplitSegments = true)
        {
            lock (this.LockObj)
                return Dequeue(minReadSize, out totalReadSize, allowSplitSegments);
        }

        public IReadOnlyList<T> Dequeue(long minReadSize, out long totalReadSize, bool allowSplitSegments = true)
        {
            if (IsDisconnected && this.Length == 0) CheckDisconnected();
            checked
            {
                if (minReadSize < 1) throw new ArgumentOutOfRangeException("size < 1");
                if (minReadSize >= int.MaxValue) minReadSize = int.MaxValue;

                long oldLen = Length;

                totalReadSize = 0;
                if (Fifo.Size == 0)
                {
                    return new List<T>();
                }

                T[] tmp = Fifo.Read((int)minReadSize);

                totalReadSize = tmp.Length;
                List<T> ret = new List<T>(tmp);

                PinHead += totalReadSize;

                EventListeners.Fire(this, FastBufferCallbackEventType.Read);

                if (Length == 0 && oldLen != 0)
                    EventListeners.Fire(this, FastBufferCallbackEventType.NonEmptyToEmpty);

                return ret;
            }
        }

        public IReadOnlyList<T> DequeueAll(out long totalReadSize) => Dequeue(long.MaxValue, out totalReadSize);

        public IReadOnlyList<T> DequeueAllWithLock(out long totalReadSize)
        {
            lock (LockObj)
                return DequeueAll(out totalReadSize);
        }

        public long DequeueAllAndEnqueueToOther(IFastBuffer<T> other) => DequeueAllAndEnqueueToOther((FastDatagramBuffer<T>)other);

        public long DequeueAllAndEnqueueToOther(FastDatagramBuffer<T> other)
        {
            if (IsDisconnected && this.Length == 0) CheckDisconnected();
            other.CheckDisconnected();
            checked
            {
                if (this == other) throw new ArgumentException("this == other");

                if (this.Length == 0)
                {
                    Debug.Assert(this.Fifo.Size == 0);
                    return 0;
                }

                if (other.Length == 0)
                {
                    long oldLen = Length;
                    long length = this.Length;
                    Debug.Assert(other.Fifo.Size == 0);
                    other.Fifo = this.Fifo;
                    this.Fifo = new Fifo<T>();
                    this.PinHead = this.PinTail;
                    other.PinTail += length;
                    EventListeners.Fire(this, FastBufferCallbackEventType.Read);
                    other.EventListeners.Fire(other, FastBufferCallbackEventType.Written);
                    if (Length != 0 && oldLen == 0)
                        EventListeners.Fire(this, FastBufferCallbackEventType.EmptyToNonEmpty);
                    return length;
                }
                else
                {
                    long oldLen = Length;
                    long length = this.Length;
                    var data = this.Fifo.Read();
                    other.Fifo.Write(data);
                    this.PinHead = this.PinTail;
                    other.PinTail += length;
                    EventListeners.Fire(this, FastBufferCallbackEventType.Read);
                    other.EventListeners.Fire(other, FastBufferCallbackEventType.Written);
                    if (Length != 0 && oldLen == 0)
                        EventListeners.Fire(this, FastBufferCallbackEventType.EmptyToNonEmpty);
                    return length;
                }
            }
        }

        public T[] ToArray() => Fifo.Span.ToArray();

        public T[] ItemsSlow { get => ToArray(); }
    }

    class FastStreamBuffer : FastStreamBuffer<byte>
    {
        public FastStreamBuffer(bool enableEvents = false, long? thresholdLength = null)
            : base(enableEvents, thresholdLength) { }
    }

    class FastDatagramBuffer : FastDatagramBuffer<Datagram>
    {
        public FastDatagramBuffer(bool enableEvents = false, long? thresholdLength = null)
            : base(enableEvents, thresholdLength) { }
    }
}
