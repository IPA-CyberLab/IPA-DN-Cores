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
using Microsoft.AspNetCore.Server.IIS.Core;
using Microsoft.EntityFrameworkCore.Query.Internal;

namespace IPA.Cores.Basic
{
    public static partial class CoresConfigx
    {
        public static partial class SectorBasedRandomAccessSettings
        {
            public static readonly Copenhagen<int> DefaultMetadataFlushIntervalMsecs = 1 * 1000;
        }
    }

    // セクタベースのランダムアクセスを提供するシンプルなクラス。先頭部分に論理ファイルサイズが書いてある。
    public class SectorBasedRandomAccessSimple : SectorBasedRandomAccessBase<byte>
    {
        public SectorBasedRandomAccessSimple(IRandomAccess<byte> physical, int sectorSize, bool disposeObject = false)
            : base(physical, sectorSize, sectorSize, disposeObject)
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

        protected override async Task<long> GetVirtualSizeImplAsync(CancellationToken cancel = default)
        {
            using (MemoryHelper.FastAllocMemoryWithUsing(this.PhysicalPrePaddingSize, out Memory<byte> tmp))
            {
                int r = await this.PhysicalReadAsync(0, tmp, cancel);
                if (r < this.PhysicalPrePaddingSize) return 0;

                return tmp._GetString_Ascii(untilNullByte: true)._ToInt();
            }
        }

        protected override async Task SetVirtualSizeImplAsync(long logicalSize, CancellationToken cancel = default)
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
    public abstract class SectorBasedRandomAccessBase<T> : IRandomAccess<T>, IAsyncDisposable
    {
        public int SectorSize { get; }
        IRandomAccess<T> Physical { get; }
        public int PhysicalPrePaddingSize { get; }

        int MetadataFlushInterval { get; }

        bool DisposeObject { get; }

        public AsyncLock SharedAsyncLock { get; } = new AsyncLock();

        protected abstract Task<long> GetVirtualSizeImplAsync(CancellationToken cancel = default);
        protected abstract Task SetVirtualSizeImplAsync(long virtualSize, CancellationToken cancel = default);

        long VirtualSizeCache = -1;
        long PhysicalSizeCache = -1;
        long VirtualSizeLastWritten = -1;
        long LastMetaDataFlushTick = 0;

        public SectorBasedRandomAccessBase(IRandomAccess<T> physical, int sectorSize, int physicalPrePaddingSize = 0, bool disposeObject = false, int metaDataFlushInterval = 0)
        {
            try
            {
                if (sectorSize <= 0) throw new ArgumentOutOfRangeException(nameof(sectorSize));
                if (physicalPrePaddingSize < 0) throw new ArgumentOutOfRangeException(nameof(physicalPrePaddingSize));
                if (metaDataFlushInterval == 0) metaDataFlushInterval = CoresConfigx.SectorBasedRandomAccessSettings.DefaultMetadataFlushIntervalMsecs;

                if ((physicalPrePaddingSize % sectorSize) != 0) throw new ArgumentOutOfRangeException("(physicalPrePaddingSize % sectorSize) != 0");

                this.SectorSize = sectorSize;
                this.DisposeObject = disposeObject;
                this.Physical = physical;
                this.PhysicalPrePaddingSize = physicalPrePaddingSize;
                this.MetadataFlushInterval = metaDataFlushInterval;
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
            DisposeAsync()._TryGetResult();
        }

        public async ValueTask DisposeAsync()
        {
            if (DisposeFlag.IsFirstCall() == false) return;

            try
            {
                await FlushAsync();
            }
            catch (Exception ex)
            {
                ex._Debug();
            }

            if (this.DisposeObject)
                await Physical._DisposeSafeAsync2();
        }

        async Task<long> GetVirtualSizeAsync(CancellationToken cancel = default)
        {
            if (this.VirtualSizeCache < 0)
            {
                long v = await GetVirtualSizeImplAsync(cancel);

                if (v < 0) throw new CoresException("GetLogicalSizeImplAsync() returned < 0");

                this.VirtualSizeCache = v;
            }
            return this.VirtualSizeCache;
        }

        async Task SetVirtualSizeAsync(long virtualSize, bool flush, CancellationToken cancel = default)
        {
            if (virtualSize < 0) throw new ArgumentOutOfRangeException(nameof(virtualSize));

            bool doFlush = flush;

            if (virtualSize < this.VirtualSizeLastWritten)
            {
                doFlush = true;
            }

            if (VirtualSizeLastWritten == -1)
            {
                doFlush = true;
            }

            if (this.MetadataFlushInterval >= 0)
            {
                long now = Time.Tick64;

                if (now >= (this.LastMetaDataFlushTick + this.MetadataFlushInterval))
                {
                    this.LastMetaDataFlushTick = now;

                    doFlush = true;
                }
            }

            if (doFlush)
            {
                if (VirtualSizeLastWritten != virtualSize)
                {
                    await SetVirtualSizeImplAsync(virtualSize, cancel);
                    VirtualSizeLastWritten = virtualSize;
                }
            }

            this.VirtualSizeCache = virtualSize;
        }

        protected async Task<long> PhysicalGetSizeAsync(CancellationToken cancel = default)
        {
            checked
            {
                if (this.PhysicalSizeCache >= 0) return this.PhysicalSizeCache;

                long size = await this.Physical.GetFileSizeAsync(false, cancel);

                if (size < 0) throw new CoresException("this.Physical.GetFileSizeAsync() returned < 0");

                if ((size % SectorSize) != 0)
                {
                    size = (size / SectorSize + 1) * SectorSize;

                    await this.Physical.SetFileSizeAsync(size, cancel);
                }

                this.PhysicalSizeCache = size;

                return size;
            }
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

                long physicalSize = await PhysicalGetSizeAsync(cancel);

                long availableSize = Math.Max(physicalSize - physicalPosition, 0);
                if (data.Length > availableSize)
                {
                    data = data.Slice(0, (int)availableSize);
                }

                if ((data.Length % this.SectorSize) != 0)
                    throw new ArgumentOutOfRangeException("(data.Length % this.SectorSize) != 0");

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

        void PhysicalSetSizeCache(long physicalSize)
        {
            if ((physicalSize % SectorSize) != 0)
                throw new ArgumentOutOfRangeException("(physicalSize % SectorSize) != 0");

            this.PhysicalSizeCache = physicalSize;
        }

        void LogicalSetSizeCache(long logicalSize)
        {
            checked
            {
                PhysicalSetSizeCache(logicalSize + this.PhysicalPrePaddingSize);
            }
        }

        protected async Task PhysicalSetSizeAsync(long physicalSize, CancellationToken cancel = default)
        {
            checked
            {
                if ((physicalSize % this.SectorSize) != 0)
                    throw new ArgumentOutOfRangeException("(physicalPosition % this.SectorSize) != 0");

                await this.Physical.SetFileSizeAsync(physicalSize, cancel);

                PhysicalSetSizeCache(physicalSize);
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

                long currentLogicalSize = await GetVirtualSizeAsync(cancel);
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
                    tmp.Span.Clear();

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

                int endPadding = (int)(endPosition - position - data.Length);

                int rawWriteSize = (int)(endPosition - startPosition);

                using (MemoryHelper.FastAllocMemoryWithUsing(rawWriteSize, out Memory<T> tmp))
                {
                    tmp.Span.Clear();

                    if (startPadding != 0)
                    {
                        await LogicalReadAsync(startPosition, tmp.Slice(0, SectorSize), cancel);
                    }

                    if (endPadding != 0)
                    {
                        await LogicalReadAsync(endPosition - SectorSize, tmp.Slice(tmp.Length - SectorSize, SectorSize), cancel);
                    }

                    data.CopyTo(tmp.Slice(startPadding));

                    long currentLogicalSize = await GetVirtualSizeAsync(cancel);

                    await LogicalWriteAsync(startPosition, tmp, cancel);

                    long newVirtualSize = Math.Max(currentLogicalSize, position + data.Length);

                    long newLogicalSize = (newVirtualSize + SectorSize - 1) / SectorSize * SectorSize;

                    LogicalSetSizeCache(newLogicalSize);

                    await SetVirtualSizeAsync(newVirtualSize, false, cancel);
                }
            }
        }

        public async Task AppendAsync(ReadOnlyMemory<T> data, CancellationToken cancel = default)
        {
            long currentSize = await GetVirtualSizeAsync(cancel);

            await WriteRandomAsync(currentSize, data, cancel);
        }

        public async Task<long> GetFileSizeAsync(bool refresh = false, CancellationToken cancel = default)
        {
            return await GetVirtualSizeAsync(cancel);
        }

        public async Task<long> GetPhysicalSizeAsync(CancellationToken cancel = default)
        {
            return await PhysicalGetSizeAsync(cancel);
        }

        public async Task SetFileSizeAsync(long newSize, CancellationToken cancel = default)
        {
            checked
            {
                if (newSize < 0) throw new ArgumentOutOfRangeException(nameof(newSize));

                await SetVirtualSizeAsync(newSize, false, cancel);

                int lastSectorPaddingSize = 0;

                long lastSectorStartPosition = 0;

                if ((newSize % SectorSize) != 0)
                {
                    lastSectorPaddingSize = SectorSize - (int)(newSize % SectorSize);

                    lastSectorStartPosition = (newSize / SectorSize) * SectorSize;

                    newSize = (newSize / SectorSize + 1) * SectorSize;
                }

                if (lastSectorPaddingSize != 0)
                {
                    using (MemoryHelper.FastAllocMemoryWithUsing(SectorSize, out Memory<T> tmp))
                    {
                        tmp.Span.Clear();

                        if (await LogicalReadAsync(lastSectorStartPosition / SectorSize * SectorSize, tmp, cancel) == SectorSize)
                        {
                            tmp.Span.Slice(SectorSize - lastSectorPaddingSize).Clear();

                            await LogicalWriteAsync(lastSectorStartPosition / SectorSize * SectorSize, tmp, cancel);

                            await FlushAsync();
                        }
                    }
                }

                await LogicalSetSizeAsync(newSize, cancel);
            }
        }

        public async Task FlushAsync(CancellationToken cancel = default)
        {
            if (this.Physical != null)
            {
                await Physical.FlushAsync(cancel);
            }
        }

        public class RandomAccessBasedStream : StreamImplBase
        {
            public bool DisposeTarget { get; }
            public IRandomAccess<byte> Target { get; }

            long CurrentPosition = 0;

            public RandomAccessBasedStream(IRandomAccess<byte> target, bool disposeTarget = false, StreamImplBaseOptions? options = null) : base(options ?? new StreamImplBaseOptions(true, true, true))
            {
                this.DisposeTarget = disposeTarget;
                this.Target = target;
                this.CurrentPosition = 0;
            }

            public override bool DataAvailable => true;

            Once DisposeFlag;
            protected override void Dispose(bool disposing)
            {
                try
                {
                    if (!disposing || DisposeFlag.IsFirstCall() == false) return;
                    if (this.DisposeTarget)
                    {
                        this.Target._DisposeSafe();
                    }
                }
                finally { base.Dispose(disposing); }
            }

            protected override async Task FlushImplAsync(CancellationToken cancellationToken = default)
            {
                await Target.FlushAsync(cancellationToken);
            }

            protected override long GetLengthImpl()
            {
                return Target.GetFileSize();
            }

            protected override long GetPositionImpl()
            {
                return this.CurrentPosition;
            }

            protected override long SeekImpl(long offset, SeekOrigin origin)
            {
                checked
                {
                    if (origin == SeekOrigin.Begin)
                    {
                        this.CurrentPosition = 0;
                    }
                    else if (origin == SeekOrigin.Current)
                    {
                        long newPosition = this.CurrentPosition + offset;

                        if (newPosition < 0) throw new ArgumentOutOfRangeException(nameof(offset));

                        this.CurrentPosition = newPosition;
                    }
                    else if (origin == SeekOrigin.End)
                    {
                        long newPosition = GetLengthImpl() - offset;

                        if (newPosition < 0) throw new ArgumentOutOfRangeException(nameof(offset));

                        this.CurrentPosition = newPosition;
                    }
                    else
                    {
                        throw new ArgumentOutOfRangeException(nameof(origin));
                    }

                    return this.CurrentPosition;
                }
            }

            protected override void SetLengthImpl(long length)
            {
                checked
                {
                    Target.SetFileSize(length);
                }
            }

            protected override void SetPositionImpl(long position)
            {
                this.SeekImpl(position, SeekOrigin.Begin);
            }

            protected override async ValueTask<int> ReadImplAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                checked
                {
                    int ret = await Target.ReadRandomAsync(this.CurrentPosition, buffer, cancellationToken);

                    Debug.Assert(buffer.Length >= ret);

                    this.CurrentPosition += ret;

                    return ret;
                }
            }

            protected override async ValueTask WriteImplAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                checked
                {
                    await Target.WriteRandomAsync(this.CurrentPosition, buffer, cancellationToken);

                    this.CurrentPosition += buffer.Length;
                }
            }
        }
    }

    namespace Tests
    {
        public static class SectorBasedRandomAccessTest
        {
            public static void Test()
            {
                Async(async () =>
                {
                    using var file = await Lfs.CreateAsync(@"c:\tmp\test.dat");

                    using var t = new SectorBasedRandomAccessSimple(file, 10, true);

                    await t.WriteRandomAsync(0, "012345678901234567890"._GetBytes());
                    await t.WriteRandomAsync(5, "Hello World    x"._GetBytes());
                    await t.SetFileSizeAsync(31);
                });
                Async(async () =>
                {
                    using var file = await Lfs.OpenAsync(@"c:\tmp\test.dat", writeMode: true);

                    using var t = new SectorBasedRandomAccessSimple(file, 10, true);

                    long size = await t.GetFileSizeAsync();
                    size._Print();

                    await t.WriteRandomAsync(12, "0"._GetBytes());

                    //await t.SetFileSizeAsync(size - 1);
                });
            }
        }
    }
}

#endif

