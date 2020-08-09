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
    // セクタベースのランダムアクセスを提供するシンプルなクラス。先頭部分に論理ファイルサイズが書いてある。
    public class SectorBasedRandomAccessSimple : SectorBasedRandomAccessBase<byte>
    {
        public SectorBasedRandomAccessSimple(IRandomAccess<byte> physical, int sectorSize, bool disposeObject = false)
            : base(physical, sectorSize, 8, disposeObject)
        {
            try
            {
            }
            catch
            {
                this._DisposeSafe();
                throw;
            }
        }

        protected override async Task<long> GetLogicalSizeImplAsync(CancellationToken cancel = default)
        {
            using (MemoryHelper.FastAllocMemoryWithUsing(this.PhysicalPrePaddingSize, out Memory<byte> tmp))
            {
                int r = await this.PhysicalReadAsync(0, tmp, cancel);
                if (r < this.PhysicalPrePaddingSize) return 0;

                return tmp._GetString_Ascii(untilNullByte: true)._ToInt();
            }
        }

        protected override async Task SetLogicalSizeImplAsync(long logicalSize, CancellationToken cancel = default)
        {
            using (MemoryHelper.FastAllocMemoryWithUsing(this.PhysicalPrePaddingSize, out Memory<byte> tmp))
            {
                tmp.Span.Clear();

                byte[] strTmp = logicalSize.ToString()._GetBytes_Ascii();

                strTmp.AsMemory().CopyTo(tmp);

                await this.PhysicalWriteAsync(0, tmp, cancel);
            }
        }
    }

    // セクタベースのランダムアクセスを提供する抽象クラス。ベースの IRandomAccess に対する読み書きは必ずセクタサイズの倍数となる。
    public abstract class SectorBasedRandomAccessBase<T> : IRandomAccess<T>
    {
        public int SectorSize { get; }
        IRandomAccess<T> Physical { get; }
        public int PhysicalPrePaddingSize { get; }

        bool DisposeObject { get; }

        public AsyncLock SharedAsyncLock { get; } = new AsyncLock();

        protected abstract Task<long> GetLogicalSizeImplAsync(CancellationToken cancel = default);
        protected abstract Task SetLogicalSizeImplAsync(long logicalSize, CancellationToken cancel = default);

        long LogicalSizeCache = -1;

        public SectorBasedRandomAccessBase(IRandomAccess<T> physical, int sectorSize, int physicalPrePaddingSize = 0, bool disposeObject = false)
        {
            try
            {
                if (sectorSize <= 0) throw new ArgumentOutOfRangeException(nameof(sectorSize));
                if (physicalPrePaddingSize < 0) throw new ArgumentOutOfRangeException(nameof(physicalPrePaddingSize));

                if ((physicalPrePaddingSize % sectorSize) != 0) throw new ArgumentOutOfRangeException("(physicalPrePaddingSize % sectorSize) != 0");

                this.SectorSize = sectorSize;
                this.DisposeObject = disposeObject;
                this.Physical = physical;
            }
            catch
            {
                this._DisposeSafe();
                throw;
            }
        }

        public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;

            if (this.DisposeObject)
                Physical._DisposeSafe();
        }

        async Task<long> GetLogicalSize(CancellationToken cancel = default)
        {
            if (this.LogicalSizeCache < 0)
            {
                long v = await GetLogicalSizeImplAsync(cancel);

                if (v < 0) throw new CoresException("GetLogicalSizeImplAsync() returned < 0");

                this.LogicalSizeCache = v;
            }
            return this.LogicalSizeCache;
        }

        async Task SetLogicalSize(long logicalSize, CancellationToken cancel = default)
        {
            if (logicalSize < 0) throw new ArgumentOutOfRangeException(nameof(logicalSize));

            await SetLogicalSizeImplAsync(logicalSize, cancel);
        }

        protected async Task<int> PhysicalReadAsync(long physicalPosition, Memory<T> data, CancellationToken cancel = default)
        {
            checked
            {
                if (data.Length == 0) return 0;

                if ((data.Length % this.SectorSize) != 0)
                    throw new ArgumentOutOfRangeException("(data.Length % this.SectorSize) != 0");

                if ((physicalPosition % this.SectorSize) != 0)
                    throw new ArgumentOutOfRangeException("(physicalPosition % this.SectorSize) != 0");

                int readSize = await this.Physical.ReadRandomAsync(physicalPosition, data, cancel);

                if (readSize > data.Length)
                    throw new CoresException("readSize > data.Length");

                int lastPartialSegmentSize = readSize % this.SectorSize;
                if (lastPartialSegmentSize != 0)
                {
                    data.Span.Slice(readSize / this.SectorSize * this.SectorSize, this.SectorSize - lastPartialSegmentSize);

                    readSize = (readSize / this.SectorSize + 1) * this.SectorSize;
                }

                return readSize;
            }
        }

        protected Task<int> LogicalReadAsync(long logicalPosition, Memory<T> data, CancellationToken cancel = default)
        {
            checked
            {
                if (logicalPosition < 0) throw new ArgumentOutOfRangeException(nameof(logicalPosition));
                return PhysicalReadAsync(logicalPosition + this.PhysicalPrePaddingSize, data, cancel);
            }
        }

        protected async Task PhysicalWriteAsync(long physicalPosition, ReadOnlyMemory<T> data, CancellationToken cancel = default)
        {
            checked
            {
                if (data.Length == 0) return;

                if ((data.Length % this.SectorSize) != 0)
                    throw new ArgumentOutOfRangeException("(data.Length % this.SectorSize) != 0");

                if ((physicalPosition % this.SectorSize) != 0)
                    throw new ArgumentOutOfRangeException("(physicalPosition % this.SectorSize) != 0");

                await this.Physical.WriteRandomAsync(physicalPosition, data, cancel);
            }
        }

        protected Task LogicalWriteAsync(long logicalPosition, ReadOnlyMemory<T> data, CancellationToken cancel = default)
        {
            checked
            {
                if (logicalPosition < 0) throw new ArgumentOutOfRangeException(nameof(logicalPosition));
                return PhysicalWriteAsync(logicalPosition + this.PhysicalPrePaddingSize, data, cancel);
            }
        }

        protected async Task PhysicalSetSizeAsync(long physicalSize, CancellationToken cancel = default)
        {
            checked
            {
                if ((physicalSize % this.SectorSize) != 0)
                    throw new ArgumentOutOfRangeException("(physicalPosition % this.SectorSize) != 0");

                await this.Physical.SetFileSizeAsync(physicalSize, cancel);
            }
        }

        protected Task LogicalSetSizeAsync(long logicalSize, CancellationToken cancel = default)
        {
            checked
            {
                return PhysicalSetSizeAsync(logicalSize + this.PhysicalPrePaddingSize, cancel);
            }
        }

        public async Task<int> ReadRandomAsync(long position, Memory<T> data, CancellationToken cancel = default)
        {
            checked
            {
                if (position < 0) throw new ArgumentOutOfRangeException(nameof(position));

                long currentLogicalSize = await GetLogicalSize(cancel);
                if (position + data.Length > currentLogicalSize)
                {
                    int newDataLength = Math.Max((int)(currentLogicalSize - position), 0);

                    data = data.Slice(0, newDataLength);
                }

                if (data.Length == 0) return 0;

                long startPosition = position;
                if ((startPosition % SectorSize) != 0)
                {
                    startPosition = startPosition / SectorSize * SectorSize;
                }

                int startPadding = (int)(position - startPosition);

                long endPosition = position + data.Length;
                if ((endPosition % SectorSize) != 0)
                {
                    endPosition = (endPosition / SectorSize + 1) * SectorSize;
                }

                int endPadding = (int)(endPosition - position);

                int rawReadSize = (int)(endPosition - startPosition);

                using (MemoryHelper.FastAllocMemoryWithUsing(rawReadSize, out Memory<T> tmp))
                {
                    int rawReadResultSize = await LogicalReadAsync(startPosition, tmp, cancel);

                    int readLogicalSize = Math.Min(rawReadResultSize - startPadding, data.Length);

                    tmp.Slice(startPadding, readLogicalSize).CopyTo(data);

                    return readLogicalSize;
                }
            }
        }

        public async Task WriteRandomAsync(long position, ReadOnlyMemory<T> data, CancellationToken cancel = default)
        {
            checked
            {
                if (position < 0) throw new ArgumentOutOfRangeException(nameof(position));

                if (data.Length == 0) return;

                long startPosition = position;
                if ((startPosition % SectorSize) != 0)
                {
                    startPosition = startPosition / SectorSize * SectorSize;
                }

                int startPadding = (int)(position - startPosition);

                long endPosition = position + data.Length;
                if ((endPosition % SectorSize) != 0)
                {
                    endPosition = (endPosition / SectorSize + 1) * SectorSize;
                }

                int endPadding = (int)(endPosition - position);

                int rawWriteSize = (int)(endPosition - startPosition);

                using (MemoryHelper.FastAllocMemoryWithUsing(rawWriteSize, out Memory<T> tmp))
                {
                    if (startPadding != 0)
                    {
                        await LogicalReadAsync(startPosition, tmp.Slice(0, SectorSize), cancel);
                    }

                    if (endPadding != 0)
                    {
                        await LogicalReadAsync(endPosition - SectorSize, tmp.Slice(tmp.Length - SectorSize, SectorSize), cancel);
                    }

                    data.CopyTo(tmp.Slice(startPadding));

                    long currentLogicalSize = await GetLogicalSize(cancel);

                    await LogicalWriteAsync(startPosition, tmp, cancel);

                    long newLogicalSize = Math.Max(currentLogicalSize, position + data.Length);

                    await SetLogicalSize(newLogicalSize, cancel);
                }
            }
        }

        public async Task AppendAsync(ReadOnlyMemory<T> data, CancellationToken cancel = default)
        {
            long currentSize = await GetLogicalSize(cancel);

            await WriteRandomAsync(currentSize, data, cancel);
        }

        public async Task<long> GetFileSizeAsync(bool refresh = false, CancellationToken cancel = default)
        {
            return await GetLogicalSize(cancel);
        }

        public async Task<long> GetPhysicalSizeAsync(CancellationToken cancel = default)
        {
            return await Physical.GetFileSizeAsync(false, cancel);
        }

        public async Task SetFileSizeAsync(long size, CancellationToken cancel = default)
        {
            checked
            {
                if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));

                await SetLogicalSize(size, cancel);

                if ((size % SectorSize) != 0)
                {
                    size = (size / SectorSize + 1) * SectorSize;
                }

                await LogicalSetSizeAsync(size, cancel);
            }
        }

        public async Task FlushAsync(CancellationToken cancel = default)
        {
            await Physical.FlushAsync(cancel);
        }
    }
}

#endif

