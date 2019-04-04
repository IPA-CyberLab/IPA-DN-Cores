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
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using System.Buffers;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.GlobalFunctions.Basic;

#pragma warning disable CS0162

namespace IPA.Cores.Basic
{
    class FileException : Exception
    {
        public FileException(string path, string message) : base($"File \"{path}\": {message}") { }
    }

    class FileSystemException : Exception
    {
        public FileSystemException(string message) : base(message) { }
    }

    class FileParameters
    {
        public string Path { get; }
        public FileMode Mode { get; }
        public FileShare Share { get; }
        public FileAccess Access { get; }
        public FileOperationFlags OperationFlags { get; }

        public FileParameters(string path, FileMode mode = FileMode.Open, FileAccess access = FileAccess.Read, FileShare share = FileShare.Read, FileOperationFlags operationFlags = FileOperationFlags.None)
        {
            this.Path = path;
            this.Mode = mode;
            this.Share = share;
            this.Access = access;
            this.OperationFlags = operationFlags;
        }
    }

    [Flags]
    enum FileOperationFlags
    {
        None = 0,
        PartialReadError = 1,
        BackupMode = 2,
    }

    class FileObjectStream : FileStream
    {
        FileObjectBase Obj;
        bool DisposeObject = false;

        private FileObjectStream() : base((SafeFileHandle)null, FileAccess.Read) { }

        private void _InternalInit(FileObjectBase obj, bool disposeObject)
        {
            Obj = obj;
            DisposeObject = disposeObject;
        }

        public static FileObjectStream CreateFromFileObject(FileObjectBase obj, bool disposeObject = false)
        {
            FileObjectStream ret = Util.NewWithoutConstructor<FileObjectStream>();

            ret._InternalInit(obj, disposeObject);

            return ret;
        }

        Once DisposeFlag;
        protected override void Dispose(bool disposing)
        {
            if (DisposeFlag.IsFirstCall() && disposing)
            {
                if (this.DisposeObject)
                    Obj.DisposeSafe();
            }
            base.Dispose(disposing);
        }

        public override bool CanRead => this.Obj.FileParams.Access.Bit(FileAccess.Read);
        public override bool CanWrite => this.Obj.FileParams.Access.Bit(FileAccess.Write);
        public override bool CanSeek => true;
        public override long Length => Obj.GetFileSize();
        public override long Position { get => Obj.Position; set => Obj.Position = value; }
        public override long Seek(long offset, SeekOrigin origin) => Obj.Seek(offset, origin);
        public override void SetLength(long value) => Obj.SetFileSize(value);

        public override bool CanTimeout => false;
        public override int ReadTimeout => throw new NotImplementedException();
        public override int WriteTimeout => throw new NotImplementedException();

        [Obsolete]
        public override IntPtr Handle => IntPtr.Zero;

        public override bool IsAsync => true;

        public override string Name => Obj.FileParams.Path;

        public override SafeFileHandle SafeFileHandle => null;

        public override void Flush() => Obj.FlushAsync().GetResult();
        public override async Task FlushAsync(CancellationToken cancellationToken = default)
        {
            using (cancellationToken.Register(() => Obj.Close()))
            {
                await Obj.FlushAsync();
            }
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            using (cancellationToken.Register(() => Obj.Close()))
            {
                return await Obj.ReadAsync(buffer.AsMemory(offset, count));
            }
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            using (cancellationToken.Register(() => Obj.Close()))
            {
                await Obj.WriteAsync(buffer.AsReadOnlyMemory(offset, count));
            }
        }

        public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer, offset, count, CancellationToken.None).GetResult();

        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count, CancellationToken.None).GetResult();

        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
            => ReadAsync(buffer, offset, count, default).AsApm(callback, state);

        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
            => WriteAsync(buffer, offset, count, default).AsApm(callback, state);

        public override int EndRead(IAsyncResult asyncResult) => ((Task<int>)asyncResult).GetResult();
        public override void EndWrite(IAsyncResult asyncResult) => ((Task)asyncResult).GetResult();

        public override bool Equals(object obj) => object.Equals(this, obj);
        public override int GetHashCode() => 0;
        public override string ToString() => this.Obj.ToString();
        public override object InitializeLifetimeService() => base.InitializeLifetimeService();
        public override void Close() => Dispose(true);

        public override void CopyTo(Stream destination, int bufferSize)
        {
            byte[] array = ArrayPool<byte>.Shared.Rent(bufferSize);
            try
            {
                int count;
                while ((count = this.Read(array, 0, array.Length)) != 0)
                {
                    destination.Write(array, 0, count);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(array, false);
            }
        }

        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            try
            {
                for (; ; )
                {
                    int num = await this.ReadAsync(new Memory<byte>(buffer), cancellationToken).ConfigureAwait(false);
                    int num2 = num;
                    if (num2 == 0)
                    {
                        break;
                    }
                    await destination.WriteAsync(new ReadOnlyMemory<byte>(buffer, 0, num2), cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, false);
            }
        }

        [Obsolete]
        protected override WaitHandle CreateWaitHandle() => new ManualResetEvent(false);

        [Obsolete]
        protected override void ObjectInvariant() { }

        public override int Read(Span<byte> buffer)
        {
            byte[] array = ArrayPool<byte>.Shared.Rent(buffer.Length);
            int result;
            try
            {
                int num = this.Read(array, 0, buffer.Length);
                if ((ulong)num > (ulong)((long)buffer.Length))
                {
                    throw new IOException("StreamTooLong");
                }
                new Span<byte>(array, 0, num).CopyTo(buffer);
                result = num;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(array, false);
            }
            return result;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            using (cancellationToken.Register(() => Obj.Close()))
            {
                return await Obj.ReadAsync(buffer);
            }
        }

        public override int ReadByte()
        {
            byte[] array = new byte[1];
            if (this.Read(array, 0, 1) == 0)
            {
                return -1;
            }
            return (int)array[0];
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            byte[] array = ArrayPool<byte>.Shared.Rent(buffer.Length);
            try
            {
                buffer.CopyTo(array);
                this.Write(array, 0, buffer.Length);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(array, false);
            }
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            using (cancellationToken.Register(() => Obj.Close()))
            {
                await Obj.WriteAsync(buffer);
            }
        }

        public override void WriteByte(byte value)
             => this.Write(new byte[] { value }, 0, 1);

        public override void Flush(bool flushToDisk) => this.Flush();

        public override void Lock(long position, long length) => throw new NotImplementedException();

        public override void Unlock(long position, long length) => throw new NotImplementedException();
    }

    enum FileObjectEventType
    {
        Read,
        Write,
        ReadRandom,
        WriteRandom,
        Seek,
        SetFileSize,
        Flush,
        Closed,
    }

    abstract class FileObjectBase : IDisposable, IAsyncClosable
    {
        public FileSystemBase FileSystem { get; }
        public FileParameters FileParams { get; }
        public bool IsOpened => !this.ClosedFlag.IsSet;
        public Exception LastError { get; private set; } = null;

        public int MicroOperationSize { get; set; } = 4096;

        long InternalPosition = 0;
        long InternalFileSize = 0;
        CancellationTokenSource CancelSource = new CancellationTokenSource();
        CancellationToken CancelToken => CancelSource.Token;

        public FastEventListenerList<FileObjectBase, FileObjectEventType> EventListeners { get; }
            = new FastEventListenerList<FileObjectBase, FileObjectEventType>();

        AsyncLock AsyncLockObj = new AsyncLock();

        protected FileObjectBase(FileSystemBase fileSystem, FileParameters fileParams)
        {
            this.FileSystem = fileSystem;
            this.FileParams = fileParams;
        }

        public override string ToString() => $"FileObject('{FileParams.Path}')";

        protected virtual async Task CreateAsync(CancellationToken cancel = default)
        {
            using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken operationCancel, this.CancelToken, cancel))
            {
                this.InternalPosition = await GetCurrentPositionImplAsync(operationCancel);
                this.InternalFileSize = await GetFileSizeImplAsync(operationCancel);

                if (this.InternalPosition > this.InternalFileSize)
                    throw new FileException(this.FileParams.Path, $"Current position is out of range. Current position: {this.InternalPosition}, File size: {this.InternalFileSize}.");

                if (this.InternalPosition < 0)
                    throw new FileException(this.FileParams.Path, $"Current position is invalid. Current position: {this.InternalPosition}.");

                if (this.InternalFileSize < 0)
                    throw new FileException(this.FileParams.Path, $"Current filesize is invalid. Current filesize: {this.InternalFileSize}.");

                this.LastError = null;
            }
        }

        protected abstract Task<int> ReadImplAsync(long position, Memory<byte> data, CancellationToken cancel = default);
        protected abstract Task WriteImplAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default);
        protected abstract Task<long> GetFileSizeImplAsync(CancellationToken cancel = default);
        protected abstract Task SetFileSizeImplAsync(long size, CancellationToken cancel = default);
        protected abstract Task<long> GetCurrentPositionImplAsync(CancellationToken cancel = default);
        protected abstract Task FlushImplAsync(CancellationToken cancel = default);
        protected abstract Task CloseImplAsync();

        public FileStream GetStream(bool disposeObject = false) => FileObjectStream.CreateFromFileObject(this, disposeObject);

        Once ClosedFlag;

        protected void CheckIsOpened()
        {
            if (IsOpened == false)
                throw new FileException(this.FileParams.Path, $"File is closed.");
        }

        protected void CheckAccessBit(FileAccess access)
        {
            if (FileParams.Access.Bit(access) == false)
                throw new FileException(this.FileParams.Path, $"The file handle has no '{access}' access right.");
        }

        public long Position
        {
            set => Seek(value, SeekOrigin.Begin);
            get => this.InternalPosition;
        }

        public async Task<int> ReadAsync(Memory<byte> data, CancellationToken cancel = default)
        {
            checked
            {
                try
                {
                    EventListeners.Fire(this, FileObjectEventType.Read);

                    if (data.IsEmpty) return 0;

                    using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken operationCancel, this.CancelToken, cancel))
                    {
                        using (await AsyncLockObj.LockWithAwait())
                        {
                            CheckIsOpened();

                            CheckAccessBit(FileAccess.Read);

                            if (this.InternalFileSize < this.InternalPosition)
                            {
                                await GetFileSizeInternalAsync(true, operationCancel);
                                if (this.InternalFileSize < this.InternalPosition)
                                    throw new FileException(this.FileParams.Path, $"Current position is out of range. Current position: {this.InternalPosition}, File size: {this.InternalFileSize}.");
                            }

                            long newPosition = this.InternalPosition + data.Length;
                            if (this.FileParams.OperationFlags.Bit(FileOperationFlags.PartialReadError))
                            {
                                if (this.InternalFileSize < newPosition)
                                {
                                    await GetFileSizeInternalAsync(true, operationCancel);
                                    if (this.InternalFileSize < newPosition)
                                        throw new FileException(this.FileParams.Path, $"New position is out of range. New position: {newPosition}, File size: {this.InternalFileSize}.");
                                }
                            }

                            long originalPosition = this.InternalPosition;

                            operationCancel.ThrowIfCancellationRequested();

                            try
                            {
                                int r = await TaskUtil.DoMicroReadOperations(async (target, pos, c) =>
                                {
                                    return await ReadImplAsync(pos, target, c);
                                },
                                data, MicroOperationSize, this.InternalPosition, operationCancel);

                                if (r < 0) throw new FileException(this.FileParams.Path, $"ReadImplAsync returned {r}.");

                                if (this.FileParams.OperationFlags.Bit(FileOperationFlags.PartialReadError))
                                    if (r != data.Length)
                                        throw new FileException(this.FileParams.Path, $"ReadImplAsync returned {r} while {data.Length} requested.");

                                this.InternalPosition += r;

                                return r;
                            }
                            catch
                            {
                                this.InternalPosition = originalPosition;
                                throw;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.LastError = ex;
                    throw;
                }
            }
        }

        public int Read(Memory<byte> data) => ReadAsync(data).GetResult();


        public async Task<int> ReadRandomAsync(long position, Memory<byte> data, CancellationToken cancel = default)
        {
            checked
            {
                try
                {
                    if (position < 0) throw new ArgumentOutOfRangeException("position < 0");
                    EventListeners.Fire(this, FileObjectEventType.ReadRandom);

                    if (data.IsEmpty) return 0;

                    using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken operationCancel, this.CancelToken, cancel))
                    {
                        using (await AsyncLockObj.LockWithAwait())
                        {
                            CheckIsOpened();

                            CheckAccessBit(FileAccess.Read);

                            if (this.InternalFileSize < position)
                            {
                                await GetFileSizeInternalAsync(true, operationCancel);
                                if (this.InternalFileSize < position)
                                    throw new FileException(this.FileParams.Path, $"The random position is out of range. Position: {position}, File size: {this.InternalFileSize}.");
                            }

                            long newPosition = position + data.Length;
                            if (this.FileParams.OperationFlags.Bit(FileOperationFlags.PartialReadError))
                            {
                                if (this.InternalFileSize < newPosition)
                                {
                                    await GetFileSizeInternalAsync(true, operationCancel);
                                    if (this.InternalFileSize < newPosition)
                                        throw new FileException(this.FileParams.Path, $"New position is out of range. New position: {newPosition}, File size: {this.InternalFileSize}.");
                                }
                            }

                            operationCancel.ThrowIfCancellationRequested();

                            try
                            {
                                int r = await TaskUtil.DoMicroReadOperations(async (target, pos, c) =>
                                {
                                    return await ReadImplAsync(pos, target, c);
                                },
                                data, MicroOperationSize, position, operationCancel);

                                if (r < 0) throw new FileException(this.FileParams.Path, $"ReadImplAsync returned {r}.");

                                if (this.FileParams.OperationFlags.Bit(FileOperationFlags.PartialReadError))
                                    if (r != data.Length)
                                        throw new FileException(this.FileParams.Path, $"ReadImplAsync returned {r} while {data.Length} requested.");

                                return r;
                            }
                            catch
                            {
                                throw;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.LastError = ex;
                    throw;
                }
            }
        }

        public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancel = default)
        {
            checked
            {
                try
                {
                    EventListeners.Fire(this, FileObjectEventType.Write);

                    if (data.IsEmpty) return;

                    using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken operationCancel, this.CancelToken, cancel))
                    {
                        using (await AsyncLockObj.LockWithAwait())
                        {
                            CheckIsOpened();

                            CheckAccessBit(FileAccess.Write);

                            if (this.InternalFileSize < this.InternalPosition)
                            {
                                await GetFileSizeInternalAsync(true, operationCancel);
                                if (this.InternalFileSize < this.InternalPosition)
                                    throw new FileException(this.FileParams.Path, $"Current position is out of range. Current position: {this.InternalPosition}, File size: {this.InternalFileSize}.");
                            }

                            operationCancel.ThrowIfCancellationRequested();

                            await TaskUtil.DoMicroWriteOperations(async (target, pos, c) =>
                            {
                                await WriteImplAsync(pos, target, c);
                            },
                            data, MicroOperationSize, this.InternalPosition, operationCancel);

                            this.InternalPosition += data.Length;

                            if (this.InternalFileSize < this.InternalPosition)
                            {
                                this.InternalFileSize = this.InternalPosition;
                            }

                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.LastError = ex;
                    throw;
                }
            }
        }

        public void Write(Memory<byte> data, CancellationToken cancel = default) => WriteAsync(data, cancel).GetResult();

        public async Task WriteRandomAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
        {
            checked
            {
                try
                {
                    if (position < 0) throw new ArgumentOutOfRangeException("position < 0");

                    EventListeners.Fire(this, FileObjectEventType.WriteRandom);

                    if (data.IsEmpty) return;

                    using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken operationCancel, this.CancelToken, cancel))
                    {
                        using (await AsyncLockObj.LockWithAwait())
                        {
                            CheckIsOpened();

                            CheckAccessBit(FileAccess.Write);

                            if (position < 0)
                                position = this.InternalFileSize;

                            if (this.InternalFileSize < position)
                            {
                                await GetFileSizeInternalAsync(true, operationCancel);

                                if (this.InternalFileSize < position)
                                {
                                    await SetFileSizeImplAsync(position, operationCancel);
                                }
                            }

                            operationCancel.ThrowIfCancellationRequested();

                            await TaskUtil.DoMicroWriteOperations(async (target, pos, c) =>
                            {
                                await WriteImplAsync(pos, target, c);
                            },
                            data, MicroOperationSize, position, operationCancel);

                            if (this.InternalFileSize < (position + data.Length))
                            {
                                this.InternalFileSize = (position + data.Length);
                            }

                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.LastError = ex;
                    throw;
                }
            }
        }

        public void WriteRandom(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
            => WriteRandomAsync(position, data, cancel).GetResult();

        public Task AppendAsync(ReadOnlyMemory<byte> data, CancellationToken cancel = default)
            => this.WriteRandomAsync(-1, data, cancel);

        public void Append(ReadOnlyMemory<byte> data, CancellationToken cancel = default)
            => this.AppendAsync(data, cancel).GetResult();

        public async Task<long> SeekAsync(long offset, SeekOrigin origin, CancellationToken cancel = default)
        {
            checked
            {
                try
                {
                    EventListeners.Fire(this, FileObjectEventType.Seek);

                    using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken operationCancel, this.CancelToken, cancel))
                    {
                        using (await AsyncLockObj.LockWithAwait())
                        {
                            CheckIsOpened();

                            long newPosition = 0;

                            if (origin == SeekOrigin.Begin)
                                newPosition = offset;
                            else if (origin == SeekOrigin.Current)
                                newPosition = this.InternalPosition + offset;
                            else if (origin == SeekOrigin.End)
                                newPosition = (await GetFileSizeInternalAsync(true, operationCancel)) + offset;
                            else
                                throw new FileException(this.FileParams.Path, $"Invalid origin value: {(int)origin}");

                            if (newPosition < 0)
                                throw new FileException(this.FileParams.Path, $"newPosition < 0");

                            if (this.InternalFileSize < newPosition)
                            {
                                await GetFileSizeInternalAsync(true, operationCancel);
                                if (this.InternalFileSize < newPosition)
                                    throw new FileException(this.FileParams.Path, $"New position is out of range. New position: {newPosition}, File size: {this.InternalFileSize}.");
                            }

                            if (this.InternalPosition != newPosition)
                            {
                                this.InternalPosition = newPosition;
                            }

                            return this.InternalPosition;
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.LastError = ex;
                    throw;
                }
            }
        }

        public Task<long> SeekToBeginAsync(CancellationToken cancel = default) => SeekAsync(0, SeekOrigin.Begin, cancel);
        public Task<long> SeekToEndAsync(CancellationToken cancel = default) => SeekAsync(0, SeekOrigin.End, cancel);

        public long Seek(long offset, SeekOrigin origin, CancellationToken cancel = default)
            => SeekAsync(offset, origin, cancel).GetResult();

        public long SeekToBegin(CancellationToken cancel = default) => SeekToBeginAsync(cancel).GetResult();
        public long SeekToEnd(CancellationToken cancel = default) => SeekToEndAsync(cancel).GetResult();

        async Task<long> GetFileSizeInternalAsync(bool refresh, CancellationToken cancel = default)
        {
            checked
            {
                try
                {
                    if (refresh == false)
                        return this.InternalFileSize;

                    cancel.ThrowIfCancellationRequested();

                    long r = await GetFileSizeImplAsync(cancel);

                    if (r < 0)
                        throw new FileException(this.FileParams.Path, $"GetFileSizeImplAsync returned {r}.");

                    this.InternalFileSize = r;

                    return r;
                }
                catch (Exception ex)
                {
                    this.LastError = ex;
                    throw;
                }
            }
        }

        public async Task<long> GetFileSizeAsync(bool refresh = false, CancellationToken cancel = default)
        {
            checked
            {
                try
                {
                    using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken operationCancel, this.CancelToken, cancel))
                    {
                        using (await AsyncLockObj.LockWithAwait())
                        {
                            CheckIsOpened();

                            return await GetFileSizeInternalAsync(refresh, cancel);
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.LastError = ex;
                    throw;
                }
            }
        }

        public async Task SetFileSizeAsync(long size, CancellationToken cancel = default)
        {
            checked
            {
                try
                {
                    EventListeners.Fire(this, FileObjectEventType.SetFileSize);

                    using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken operationCancel, this.CancelToken, cancel))
                    {
                        using (await AsyncLockObj.LockWithAwait())
                        {
                            CheckIsOpened();
                            CheckAccessBit(FileAccess.Write);

                            operationCancel.ThrowIfCancellationRequested();

                            await SetFileSizeImplAsync(size, operationCancel);

                            this.InternalFileSize = size;
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.LastError = ex;
                    throw;
                }
            }
        }

        public void SetFileSize(long size, CancellationToken cancel = default) => SetFileSizeAsync(size, cancel).GetResult();

        public long GetFileSize(bool refresh = false, CancellationToken cancel = default) => GetFileSizeAsync(refresh, cancel).GetResult();

        public async Task FlushAsync(CancellationToken cancel = default)
        {
            try
            {
                EventListeners.Fire(this, FileObjectEventType.Flush);

                using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken operationCancel, this.CancelToken, cancel))
                {
                    using (await AsyncLockObj.LockWithAwait())
                    {
                        CheckIsOpened();

                        operationCancel.ThrowIfCancellationRequested();

                        await FlushImplAsync(operationCancel);
                    }
                }
            }
            catch (Exception ex)
            {
                this.LastError = ex;
                throw;
            }
        }

        public void Flush(CancellationToken cancel = default) => FlushAsync(cancel).GetResult();

        public async Task CloseAsync()
        {
            CancelSource.TryCancelNoBlock();

            using (await AsyncLockObj.LockWithAwait())
            {
                if (ClosedFlag.IsFirstCall())
                {
                    this.LastError = new ApplicationException($"File '{this.FileParams.Path}' is closed.");

                    try
                    {
                        await CloseImplAsync();
                    }
                    finally
                    {
                        EventListeners.Fire(this, FileObjectEventType.Closed);
                    }
                }
            }
        }

        public void Dispose() => Dispose(true);
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;

            CancelSource.TryCancelNoBlock();
            CloseAsync().LaissezFaire();
        }

        public void Close() => Dispose();
    }

    class FileSystemEntity
    {
        public string FullPath { get; set; }
        public string Name { get; set; }
        public bool IsDirectory => Attributes.Bit(FileAttributes.Directory);
        public bool IsSymbolicLink => Attributes.Bit(FileAttributes.ReparsePoint);
        public bool IsCurrentDirectory => (Name == ".");
        public long Size { get; set; }
        public string SymbolicLinkTarget { get; set; }
        public FileAttributes Attributes { get; set; }
        public DateTimeOffset Updated { get; set; }
        public DateTimeOffset Created { get; set; }
    }

    class FileSystemMetadata
    {
        public FileAttributes? Attributes { get; set; }
        public DateTimeOffset? Updated { get; set; }
        public DateTimeOffset? Created { get; set; }
    }

    [Flags]
    enum SpecialFileNameKind
    {
        Normal = 0,
        CurrentDirectory = 1,
        ParentDirectory = 2,
    }

    class FileSystemObjectPool : ObjectPoolBase<FileObjectBase>
    {
        public FileSystemBase FileSystem { get; }

        public FileSystemObjectPool(FileSystemBase FileSystem, int delayTimeout) : base(delayTimeout, Env.FilePathStringComparer)
        {
            this.FileSystem = FileSystem;
        }

        protected override async Task<FileObjectBase> OpenImplAsync(string name, CancellationToken cancel)
        {
            name = await FileSystem.NormalizePathAsync(name, cancel);

            return await FileSystem.CreateAsync(name, cancel: cancel);
        }

        protected override Task<string> NormalizeNameAsync(string name, CancellationToken cancel)
            => FileSystem.NormalizePathAsync(name, cancel);
    }

    abstract class FileSystemBase : AsyncCleanupable
    {
        public static PalFileSystem Local { get; } = new PalFileSystem(LeakChecker.SuperGrandLady);

        public DirectoryWalker DirectoryWalker { get; }

        CriticalSection LockObj = new CriticalSection();
        List<FileObjectBase> OpenedHandleList = new List<FileObjectBase>();
        RefInt CriticalCounter = new RefInt();
        CancellationTokenSource CancelSource = new CancellationTokenSource();

        public FileSystemBase(AsyncCleanuperLady lady) : base(lady)
        {
            try
            {
                DirectoryWalker = new DirectoryWalker(this);
            }
            catch
            {
                Lady.DisposeAllSafe();
                throw;
            }
        }

        void CheckNotDisposed()
        {
            if (DisposeFlag.IsSet)
                throw new FileSystemException("The file system is already disposed.");
        }

        public FileSystemObjectPool GetObjectPool(int delayTimeout) => new FileSystemObjectPool(this, delayTimeout);

        Once DisposeFlag;
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;
                CancelSource.Cancel();
            }
            finally { base.Dispose(disposing); }
        }

        public override async Task _CleanupAsyncInternal()
        {
            try
            {
                while (CriticalCounter.Value >= 1)
                {
                    await Task.Delay(10);
                }

                FileObjectBase[] fileHandles;

                lock (LockObj)
                {
                    fileHandles = OpenedHandleList.ToArray();
                    OpenedHandleList.Clear();
                }

                foreach (var fileHandle in fileHandles)
                {
                    await fileHandle.CloseAsync();
                }
            }
            finally { await base._CleanupAsyncInternal(); }
        }

        protected abstract Task<string> NormalizePathImplAsync(string path, CancellationToken cancel = default);
        protected abstract Task<FileObjectBase> CreateFileImplAsync(FileParameters option, CancellationToken cancel = default);
        protected abstract Task<FileSystemEntity[]> EnumDirectoryImplAsync(string directoryPath, CancellationToken cancel = default);

        public async Task<string> NormalizePathAsync(string path, CancellationToken cancel = default)
        {
            path = path.NonNullTrim();

            using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken opCancel, cancel, this.CancelSource.Token))
            {
                using (TaskUtil.EnterCriticalCounter(CriticalCounter))
                {
                    CheckNotDisposed();

                    return await NormalizePathImplAsync(path, cancel);
                }
            }
        }
        public string NormalizePath(string path, CancellationToken cancel = default)
            => NormalizePathAsync(path, cancel).GetResult();

        public async Task<FileObjectBase> CreateFileAsync(FileParameters option, CancellationToken cancel = default)
        {
            using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken opCancel, cancel, this.CancelSource.Token))
            {
                using (TaskUtil.EnterCriticalCounter(CriticalCounter))
                {
                    CheckNotDisposed();

                    FileObjectBase f = await CreateFileImplAsync(option, opCancel);

                    lock (LockObj)
                    {
                        OpenedHandleList.Add(f);
                    }

                    f.EventListeners.RegisterCallback(FileClosedEvent);

                    return f;
                }
            }
        }

        void FileClosedEvent(FileObjectBase obj, FileObjectEventType eventType, object userState)
        {
            lock (LockObj)
            {
                OpenedHandleList.Remove(obj);
            }
        }

        public FileObjectBase CreateFile(FileParameters option, CancellationToken cancel = default)
            => CreateFileAsync(option, cancel).GetResult();

        public Task<FileObjectBase> CreateAsync(string path, bool noShare = false, FileOperationFlags operationFlags = FileOperationFlags.None, CancellationToken cancel = default)
            => CreateFileAsync(new FileParameters(path, FileMode.Create, FileAccess.ReadWrite, noShare ? FileShare.None : FileShare.Read, operationFlags), cancel);

        public FileObjectBase Create(string path, bool noShare = false, FileOperationFlags operationFlags = FileOperationFlags.None, CancellationToken cancel = default)
            => CreateAsync(path, noShare, operationFlags, cancel).GetResult();

        public Task<FileObjectBase> OpenAsync(string path, bool writeMode = false, bool readLock = false, FileOperationFlags operationFlags = FileOperationFlags.None, CancellationToken cancel = default)
            => CreateFileAsync(new FileParameters(path, FileMode.Open, (writeMode ? FileAccess.ReadWrite : FileAccess.Read),
                (readLock ? FileShare.None : FileShare.Read), operationFlags), cancel);

        public FileObjectBase Open(string path, bool writeMode = false, bool readLock = false, FileOperationFlags operationFlags = FileOperationFlags.None, CancellationToken cancel = default)
            => OpenAsync(path, writeMode, readLock, operationFlags, cancel).GetResult();

        async Task<FileSystemEntity[]> EnumDirectoryInternalAsync(string directoryPath, CancellationToken opCancel)
        {
            using (TaskUtil.EnterCriticalCounter(CriticalCounter))
            {
                CheckNotDisposed();

                opCancel.ThrowIfCancellationRequested();

                FileSystemEntity[] list = await EnumDirectoryImplAsync(directoryPath, opCancel);

                if (list.Select(x => x.Name).Distinct().Count() != list.Count())
                {
                    throw new ApplicationException("There are duplicated entities returned by EnumDirectoryImplAsync().");
                }

                if (list.First().IsCurrentDirectory == false || list.First().IsDirectory == false)
                {
                    throw new ApplicationException("The first entry returned by EnumDirectoryImplAsync() is not a current directory.");
                }

                return list.Skip(1).Where(x => GetSpecialFileNameKind(x.Name) == SpecialFileNameKind.Normal).Prepend(list[0]).ToArray();
            }
        }

        async Task<bool> EnumDirectoryRecursiveInternalAsync(List<FileSystemEntity> currentList, string directoryPath, bool recursive, CancellationToken opCancel)
        {
            CheckNotDisposed();

            opCancel.ThrowIfCancellationRequested();

            FileSystemEntity[] entityList = await EnumDirectoryInternalAsync(directoryPath, opCancel);

            foreach (FileSystemEntity entity in entityList)
            {
                currentList.Add(entity);

                if (recursive)
                {
                    if (entity.IsDirectory && entity.IsCurrentDirectory == false)
                    {
                        if (await EnumDirectoryRecursiveInternalAsync(currentList, entity.FullPath, true, opCancel) == false)
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        public async Task<FileSystemEntity[]> EnumDirectoryAsync(string directoryPath, bool recursive = false, CancellationToken cancel = default)
        {
            using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken opCancel, cancel, this.CancelSource.Token))
            {
                CheckNotDisposed();

                opCancel.ThrowIfCancellationRequested();

                List<FileSystemEntity> currentList = new List<FileSystemEntity>();

                if (await EnumDirectoryRecursiveInternalAsync(currentList, directoryPath, recursive, opCancel) == false)
                {
                    throw new OperationCanceledException();
                }

                return currentList.ToArray();
            }
        }

        public FileSystemEntity[] EnumDirectory(string directoryPath, bool recursive = false, CancellationToken cancel = default)
            => EnumDirectoryAsync(directoryPath, recursive, cancel).GetResult();

        public static SpecialFileNameKind GetSpecialFileNameKind(string fileName)
        {
            SpecialFileNameKind ret = SpecialFileNameKind.Normal;

            if (fileName == ".") ret |= SpecialFileNameKind.CurrentDirectory;
            if (fileName == "..") ret |= SpecialFileNameKind.ParentDirectory;

            return ret;
        }
    }

    class DirectoryWalker
    {
        public FileSystemBase FileSystem { get; }

        public DirectoryWalker(FileSystemBase fileSystem)
        {
            this.FileSystem = fileSystem;
        }

        async Task<bool> WalkDirectoryInternalAsync(string directoryPath, Func<FileSystemEntity[], CancellationToken, Task<bool>> callback, Func<string, Exception, bool> exceptionHandler, bool recursive, CancellationToken opCancel)
        {
            opCancel.ThrowIfCancellationRequested();

            FileSystemEntity[] entityList;

            try
            {
                entityList = await FileSystem.EnumDirectoryAsync(directoryPath, false, opCancel);
            }
            catch (Exception ex)
            {
                if (exceptionHandler(directoryPath, ex) == false)
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }

            if (await callback(entityList, opCancel) == false)
            {
                return false;
            }

            if (recursive)
            {
                foreach (FileSystemEntity entity in entityList.Where(x => x.IsCurrentDirectory == false))
                {
                    if (entity.IsDirectory)
                    {
                        opCancel.ThrowIfCancellationRequested();

                        if (await WalkDirectoryInternalAsync(entity.FullPath, callback, exceptionHandler, true, opCancel) == false)
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        public async Task<bool> WalkDirectoryAsync(string rootDirectory, Func<FileSystemEntity[], CancellationToken, Task<bool>> callback, Func<string, Exception, bool> exceptionHandler, bool recursive = true, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();

            return await WalkDirectoryInternalAsync(rootDirectory, callback, exceptionHandler, recursive, cancel);
        }

        public bool WalkDirectory(string rootDirectory, Func<FileSystemEntity[], CancellationToken, bool> callback, Func<string, Exception, bool> exceptionHandler, bool recursive = true, CancellationToken cancel = default)
            => WalkDirectoryAsync(rootDirectory, async (entity, c) => { await Task.CompletedTask; return callback(entity, c); }, exceptionHandler, recursive, cancel).GetResult();
    }
}

