// IPA Cores.NET
// 
// Copyright (c) 2019- IPA CyberLab.
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

// Author: Daiyuu Nobori
// 開発中のクラスの一時置き場

#if true

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    // 任意の Stream 型を IBuffer<byte> 型に変換するクラス
    public class StreamDirectBuffer : IBuffer<byte>
    {
        public Stream BaseStream { get; }

        public StreamDirectBuffer(Stream baseStream)
        {
            if (baseStream.CanSeek == false) throw new ArgumentException("Cannot seek", nameof(baseStream));

            this.BaseStream = baseStream;
        }

        public long LongCurrentPosition => BaseStream.Position;

        public long LongLength => BaseStream.Length;

        public long LongInternalBufferSize => BaseStream.Length;

        public void Clear()
        {
            BaseStream.Seek(0, SeekOrigin.Begin);
            BaseStream.SetLength(0);
        }

        public bool IsThisEmpty()
        {
            return BaseStream.Length == 0;
        }

        public ReadOnlySpan<byte> Peek(long size, bool allowPartial = false)
        {
            checked
            {
                int sizeToRead = (int)size;

                long currentPosition = LongCurrentPosition;

                try
                {
                    return Read(size, allowPartial);
                }
                finally
                {
                    Seek(currentPosition, SeekOrigin.Begin, false);
                }
            }
        }

        public byte PeekOne()
        {
            var tmp = Peek(1, false);
            Debug.Assert(tmp.Length == 1);
            return tmp[0];
        }

        public ReadOnlySpan<byte> Read(long size, bool allowPartial = false)
        {
            checked
            {
                int sizeToRead = (int)size;

                Span<byte> buf = new byte[sizeToRead];

                int resultSize = BaseStream.Read(buf);

                if (resultSize == 0)
                {
                    // 最後に到達した
                    if (allowPartial == false)
                        throw new CoresException("End of stream");
                    else
                        return ReadOnlySpan<byte>.Empty;
                }

                Debug.Assert(resultSize <= sizeToRead);

                if (allowPartial == false)
                {
                    if (resultSize < sizeToRead)
                    {
                        // 巻き戻す
                        if (resultSize >= 1)
                        {
                            BaseStream.Seek(-resultSize, SeekOrigin.Current);
                        }
                        throw new CoresException($"resultSize ({resultSize}) < sizeToRead ({sizeToRead})");
                    }
                }

                if (resultSize != sizeToRead)
                {
                    buf = buf.Slice(0, resultSize);
                }

                return buf;
            }
        }

        public byte ReadOne()
        {
            var tmp = Read(1, false);
            Debug.Assert(tmp.Length == 1);
            return tmp[0];
        }

        public void Seek(long offset, SeekOrigin mode, bool allocate = false)
        {
            checked
            {
                long currentPosition = LongCurrentPosition;
                long newPosition;
                long currentLength = LongLength;
                long newLength = currentLength;

                if (mode == SeekOrigin.Current)
                    newPosition = checked(currentPosition + offset);
                else if (mode == SeekOrigin.End)
                    newPosition = checked(currentLength + offset);
                else
                    newPosition = offset;

                if (newPosition < 0) throw new ArgumentOutOfRangeException("newPosition < 0");

                if (allocate == false)
                {
                    if (newPosition > currentLength) throw new ArgumentOutOfRangeException("newPosition > Size");
                }
                else
                {
                    newLength = Math.Max(newPosition, currentLength);
                }

                if (currentLength != newLength)
                {
                    BaseStream.SetLength(newLength);
                }

                if (currentPosition != newPosition)
                {
                    long ret = BaseStream.Seek(newPosition, SeekOrigin.Begin);

                    if (ret != newPosition)
                    {
                        throw new CoresException($"ret {ret} != newPosition {newPosition}");
                    }
                }
            }
        }

        public void SetLength(long size)
        {
            BaseStream.SetLength(size);
        }

        public void Write(ReadOnlySpan<byte> data)
        {
            BaseStream.Write(data);
        }

        public void WriteOne(byte data)
        {
            Write(data._SingleArray());
        }
    }

    // 一定サイズまではメモリ上、それを超えた場合はストレージ上に保存されるバッファ (非同期アクセスはサポートしていない。将来遠隔ストレージに置くなどして非同期が必要になった場合は実装を追加すること)
    //public class MemoryAndStorageStream<T> : IBuffer<T>
    //{
    //}
}

#endif

