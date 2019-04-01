﻿// IPA Cores.NET
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
using static IPA.Cores.GlobalFunctions.Basic;

#pragma warning disable CS0162

namespace IPA.Cores.Basic
{
    class DisconnectedException : Exception { }
    class FastBufferDisconnectedException : DisconnectedException { }
    class SocketDisconnectedException : DisconnectedException { }
    class BaseStreamDisconnectedException : DisconnectedException { }

    class Datagram
    {
        public Memory<byte> Data;
        public EndPoint EndPoint;
        public byte Flag;

        public IPEndPoint IPEndPoint { get => (IPEndPoint)EndPoint; set => EndPoint = value; }

        public Datagram(Memory<byte> data, EndPoint endPoint, byte flag = 0)
        {
            Data = data;
            EndPoint = endPoint;
            Flag = flag;
        }
    }

    class FastLinkedListNode<T>
    {
        public T Value;
        public FastLinkedListNode<T> Next, Previous;
    }

    class FastLinkedList<T>
    {
        public int Count;
        public FastLinkedListNode<T> First, Last;

        public void Clear()
        {
            Count = 0;
            First = Last = null;
        }

        public FastLinkedListNode<T> AddFirst(T value)
        {
            if (First == null)
            {
                Debug.Assert(Last == null);
                Debug.Assert(Count == 0);
                First = Last = new FastLinkedListNode<T>() { Value = value, Next = null, Previous = null };
                Count++;
                return First;
            }
            else
            {
                Debug.Assert(Last != null);
                Debug.Assert(Count >= 1);
                var oldFirst = First;
                var nn = new FastLinkedListNode<T>() { Value = value, Next = oldFirst, Previous = null };
                Debug.Assert(oldFirst.Previous == null);
                oldFirst.Previous = nn;
                First = nn;
                Count++;
                return nn;
            }
        }

        public void AddFirst(FastLinkedListNode<T> chainFirst, FastLinkedListNode<T> chainLast, int chainedCount)
        {
            if (First == null)
            {
                Debug.Assert(Last == null);
                Debug.Assert(Count == 0);
                First = chainFirst;
                Last = chainLast;
                chainFirst.Previous = null;
                chainLast.Next = null;
                Count = chainedCount;
            }
            else
            {
                Debug.Assert(Last != null);
                Debug.Assert(Count >= 1);
                var oldFirst = First;
                Debug.Assert(oldFirst.Previous == null);
                oldFirst.Previous = chainLast;
                First = chainFirst;
                Count += chainedCount;
            }
        }

        public FastLinkedListNode<T> AddLast(T value)
        {
            if (Last == null)
            {
                Debug.Assert(First == null);
                Debug.Assert(Count == 0);
                First = Last = new FastLinkedListNode<T>() { Value = value, Next = null, Previous = null };
                Count++;
                return Last;
            }
            else
            {
                Debug.Assert(First != null);
                Debug.Assert(Count >= 1);
                var oldLast = Last;
                var nn = new FastLinkedListNode<T>() { Value = value, Next = null, Previous = oldLast };
                Debug.Assert(oldLast.Next == null);
                oldLast.Next = nn;
                Last = nn;
                Count++;
                return nn;
            }
        }

        public void AddLast(FastLinkedListNode<T> chainFirst, FastLinkedListNode<T> chainLast, int chainedCount)
        {
            if (Last == null)
            {
                Debug.Assert(First == null);
                Debug.Assert(Count == 0);
                First = chainFirst;
                Last = chainLast;
                chainFirst.Previous = null;
                chainLast.Next = null;
                Count = chainedCount;
            }
            else
            {
                Debug.Assert(First != null);
                Debug.Assert(Count >= 1);
                var oldLast = Last;
                Debug.Assert(oldLast.Next == null);
                oldLast.Next = chainFirst;
                Last = chainLast;
                Count += chainedCount;
            }
        }

        public FastLinkedListNode<T> AddAfter(FastLinkedListNode<T> prevNode, T value)
        {
            var nextNode = prevNode.Next;
            Debug.Assert(First != null && Last != null);
            Debug.Assert(nextNode != null || Last == prevNode);
            Debug.Assert(nextNode == null || nextNode.Previous == prevNode);
            var nn = new FastLinkedListNode<T>() { Value = value, Next = nextNode, Previous = prevNode };
            prevNode.Next = nn;
            if (nextNode != null) nextNode.Previous = nn;
            if (Last == prevNode) Last = nn;
            Count++;
            return nn;
        }

        public void AddAfter(FastLinkedListNode<T> prevNode, FastLinkedListNode<T> chainFirst, FastLinkedListNode<T> chainLast, int chainedCount)
        {
            var nextNode = prevNode.Next;
            Debug.Assert(First != null && Last != null);
            Debug.Assert(nextNode != null || Last == prevNode);
            Debug.Assert(nextNode == null || nextNode.Previous == prevNode);
            prevNode.Next = chainFirst;
            chainFirst.Previous = prevNode;
            if (nextNode != null) nextNode.Previous = chainLast;
            chainLast.Previous = nextNode;
            if (Last == prevNode) Last = chainLast;
            Count += chainedCount;
        }

        public FastLinkedListNode<T> AddBefore(FastLinkedListNode<T> nextNode, T value)
        {
            var prevNode = nextNode.Previous;
            Debug.Assert(First != null && Last != null);
            Debug.Assert(prevNode != null || First == nextNode);
            Debug.Assert(prevNode == null || prevNode.Next == nextNode);
            var nn = new FastLinkedListNode<T>() { Value = value, Next = nextNode, Previous = prevNode };
            nextNode.Previous = nn;
            if (prevNode != null) prevNode.Next = nn;
            if (First == nextNode) First = nn;
            Count++;
            return nn;
        }

        public void AddBefore(FastLinkedListNode<T> nextNode, FastLinkedListNode<T> chainFirst, FastLinkedListNode<T> chainLast, int chainedCount)
        {
            var prevNode = nextNode.Previous;
            Debug.Assert(First != null && Last != null);
            Debug.Assert(prevNode != null || First == nextNode);
            Debug.Assert(prevNode == null || prevNode.Next == nextNode);
            nextNode.Previous = chainLast;
            chainLast.Next = nextNode;
            if (prevNode != null) prevNode.Next = chainFirst;
            chainFirst.Previous = prevNode;
            if (First == nextNode) First = chainFirst;
            Count += chainedCount;
        }

        public void Remove(FastLinkedListNode<T> node)
        {
            Debug.Assert(First != null && Last != null);

            if (node.Previous != null && node.Next != null)
            {
                Debug.Assert(First != null);
                Debug.Assert(Last != null);
                Debug.Assert(First != node);
                Debug.Assert(Last != node);

                node.Previous.Next = node.Next;
                node.Next.Previous = node.Previous;

                Count--;
            }
            else if (node.Previous == null && node.Next == null)
            {
                Debug.Assert(First == node);
                Debug.Assert(Last == node);

                First = Last = null;

                Count--;
            }
            else if (node.Previous != null)
            {
                Debug.Assert(First != null);
                Debug.Assert(First != node);
                Debug.Assert(Last == node);

                node.Previous.Next = null;
                Last = node.Previous;

                Count--;
            }
            else
            {
                Debug.Assert(Last != null);
                Debug.Assert(Last != node);
                Debug.Assert(First == node);

                node.Next.Previous = null;
                First = node.Next;

                Count--;
            }
        }

        public T[] ItemsReadOnly
        {
            get
            {
                List<T> ret = new List<T>();
                var node = First;
                while (node != null)
                {
                    ret.Add(node.Value);
                    node = node.Next;
                }
                return ret.ToArray();
            }
        }
    }

}

