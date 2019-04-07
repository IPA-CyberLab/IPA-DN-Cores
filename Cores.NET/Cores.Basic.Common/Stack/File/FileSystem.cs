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
    static partial class AppConfig
    {
        public static partial class FileSystemSettings
        {
            public static readonly Copenhagen<int> PooledHandleLifetime = 5 * 1000;
        }
    }

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
        public string Path { get; private set; }
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
            if (this.Access.Bit(FileAccess.Write))
                this.Access |= FileAccess.Read;
            this.OperationFlags = operationFlags;
        }

        public async Task NormalizePathAsync(FileSystemBase fileSystem, CancellationToken cancel = default)
        {
            string ret = await fileSystem.NormalizePathAsync(this.Path, cancel);
            this.Path = ret;
        }

        public void NormalizePath(FileSystemBase fileSystem, CancellationToken cancel = default)
            => NormalizePathAsync(fileSystem, cancel).GetResult();
    }

    [Flags]
    enum FileOperationFlags : ulong
    {
        None = 0,
        NoPartialRead = 1,
        BackupMode = 2,
        AutoCreateDirectoryOnFileCreation = 4,
        SetCompressionFlagOnDirectory = 8,
        RandomAccessOnly = 16,
        LargeFileDoNotDivideOneBlock = 32,
    }

    class FileObjectStream : FileStream
    {
        FileObjectBase FileObject;
        bool DisposeObject = false;

        private FileObjectStream() : base((SafeFileHandle)null, FileAccess.Read) { }

        private void _InternalInit(FileObjectBase obj, bool disposeObject)
        {
            FileObject = obj;
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
                    FileObject.DisposeSafe();
            }
            base.Dispose(disposing);
        }

        public override bool CanRead => this.FileObject.FileParams.Access.Bit(FileAccess.Read);
        public override bool CanWrite => this.FileObject.FileParams.Access.Bit(FileAccess.Write);
        public override bool CanSeek => true;
        public override long Length => FileObject.GetFileSize();
        public override long Position { get => FileObject.Position; set => FileObject.Position = value; }
        public override long Seek(long offset, SeekOrigin origin) => FileObject.Seek(offset, origin);
        public override void SetLength(long value) => FileObject.SetFileSize(value);

        public override bool CanTimeout => false;
        public override int ReadTimeout => throw new NotImplementedException();
        public override int WriteTimeout => throw new NotImplementedException();

        [Obsolete]
        public override IntPtr Handle => IntPtr.Zero;

        public override bool IsAsync => true;

        public override string Name => FileObject.FileParams.Path;

        public override SafeFileHandle SafeFileHandle => null;

        public override void Flush() => FileObject.FlushAsync().GetResult();
        public override async Task FlushAsync(CancellationToken cancellationToken = default)
        {
            using (cancellationToken.Register(() => FileObject.Close()))
            {
                await FileObject.FlushAsync();
            }
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            using (cancellationToken.Register(() => FileObject.Close()))
            {
                return await FileObject.ReadAsync(buffer.AsMemory(offset, count));
            }
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            using (cancellationToken.Register(() => FileObject.Close()))
            {
                if (FileObject.FileParams.OperationFlags.Bit(FileOperationFlags.RandomAccessOnly))
                    await FileObject.AppendAsync(buffer.AsReadOnlyMemory(offset, count));
                else
                    await FileObject.WriteAsync(buffer.AsReadOnlyMemory(offset, count));
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
        public override string ToString() => this.FileObject.ToString();
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
            using (cancellationToken.Register(() => FileObject.Close()))
            {
                return await FileObject.ReadAsync(buffer);
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
            using (cancellationToken.Register(() => FileObject.Close()))
            {
                await FileObject.WriteAsync(buffer);
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

    class RandomAccessHandle : IDisposable
    {
        readonly RefObjectHandle<FileObjectBase> Handle;
        readonly FileObjectBase FileObject;

        public RandomAccessHandle(RefObjectHandle<FileObjectBase> objHandle)
        {
            this.Handle = objHandle;
            this.FileObject = this.Handle.Object;
        }

        public void Dispose() => Dispose(true);
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;

            this.Handle.DisposeSafe();
        }

        public FileStream GetStream() => this.FileObject.GetStream();

        public Task<int> ReadRandomAsync(long position, Memory<byte> data, CancellationToken cancel = default)
            => this.FileObject.ReadRandomAsync(position, data, cancel);
        public int ReadRandom(long position, Memory<byte> data, CancellationToken cancel = default)
            => ReadRandomAsync(position, data, cancel).GetResult();

        public Task WriteRandomAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
            => this.FileObject.WriteRandomAsync(position, data, cancel);
        public void WriteRandom(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
            => WriteRandomAsync(position, data, cancel).GetResult();

        public Task AppendAsync(ReadOnlyMemory<byte> data, CancellationToken cancel = default)
            => this.FileObject.AppendAsync(data, cancel);
        public void Append(ReadOnlyMemory<byte> data, CancellationToken cancel = default)
            => this.AppendAsync(data, cancel).GetResult();

        public Task<long> GetFileSizeAsync(bool refresh = false, CancellationToken cancel = default)
            => this.FileObject.GetFileSizeAsync(refresh, cancel);
        public long GetFileSize(bool refresh = false, CancellationToken cancel = default) => GetFileSizeAsync(refresh, cancel).GetResult();

        public Task SetFileSizeAsync(long size, CancellationToken cancel = default)
            => this.FileObject.SetFileSizeAsync(size, cancel);
        public void SetFileSize(long size, CancellationToken cancel = default) => SetFileSizeAsync(size, cancel).GetResult();

        public Task FlushAsync(CancellationToken cancel = default)
            => this.FileObject.FlushAsync(cancel);
        public void Flush(CancellationToken cancel = default) => FlushAsync(cancel).GetResult();
    }

    abstract class FileObjectBase : IDisposable, IAsyncClosable
    {
        public FileSystemBase FileSystem { get; }
        public FileParameters FileParams { get; }
        public bool IsOpened => !this.ClosedFlag.IsSet;
        public Exception LastError { get; private set; } = null;

        public int MicroOperationSize { get; set; } = 65536;

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

        protected void CheckSequentialAccessProhibited()
        {
            if (FileParams.OperationFlags.Bit(FileOperationFlags.RandomAccessOnly))
                throw new FileException(this.FileParams.Path, "The file object is in RandomAccessOnly mode.");
        }

        public long Position
        {
            set => Seek(value, SeekOrigin.Begin);
            get
            {
                CheckSequentialAccessProhibited();

                return this.InternalPosition;
            }
        }

        public async Task<int> ReadAsync(Memory<byte> data, CancellationToken cancel = default)
        {
            checked
            {
                try
                {
                    CheckSequentialAccessProhibited();

                    EventListeners.Fire(this, FileObjectEventType.Read);

                    if (data.IsEmpty) return 0;

                    using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken operationCancel, this.CancelToken, cancel))
                    {
                        using (await AsyncLockObj.LockWithAwait(operationCancel))
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
                            if (this.FileParams.OperationFlags.Bit(FileOperationFlags.NoPartialRead))
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

                                if (this.FileParams.OperationFlags.Bit(FileOperationFlags.NoPartialRead))
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
                        using (await AsyncLockObj.LockWithAwait(operationCancel))
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
                            if (this.FileParams.OperationFlags.Bit(FileOperationFlags.NoPartialRead))
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

                                if (this.FileParams.OperationFlags.Bit(FileOperationFlags.NoPartialRead))
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

        public int ReadRandom(long position, Memory<byte> data, CancellationToken cancel = default)
            => ReadRandomAsync(position, data, cancel).GetResult();

        public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancel = default)
        {
            checked
            {
                try
                {
                    CheckSequentialAccessProhibited();

                    EventListeners.Fire(this, FileObjectEventType.Write);

                    if (data.IsEmpty) return;

                    using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken operationCancel, this.CancelToken, cancel))
                    {
                        using (await AsyncLockObj.LockWithAwait(operationCancel))
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
                    EventListeners.Fire(this, FileObjectEventType.WriteRandom);

                    if (data.IsEmpty) return;

                    using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken operationCancel, this.CancelToken, cancel))
                    {
                        using (await AsyncLockObj.LockWithAwait(operationCancel))
                        {
                            CheckIsOpened();

                            CheckAccessBit(FileAccess.Write);

                            if (position < 0)
                            {
                                // Append mode
                                position = this.InternalFileSize;
                            }

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
                    CheckSequentialAccessProhibited();

                    EventListeners.Fire(this, FileObjectEventType.Seek);

                    using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken operationCancel, this.CancelToken, cancel))
                    {
                        using (await AsyncLockObj.LockWithAwait(operationCancel))
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
                        using (await AsyncLockObj.LockWithAwait(operationCancel))
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
                if (size < 0)
                    throw new ArgumentOutOfRangeException("size < 0");

                try
                {
                    EventListeners.Fire(this, FileObjectEventType.SetFileSize);

                    using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken operationCancel, this.CancelToken, cancel))
                    {
                        using (await AsyncLockObj.LockWithAwait(operationCancel))
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
                    using (await AsyncLockObj.LockWithAwait(operationCancel))
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
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
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

    class FileSystemObjectPool : ObjectPoolBase<FileObjectBase, FileOperationFlags>
    {
        public FileSystemBase FileSystem { get; }
        public FileOperationFlags DefaultFileOperationFlags { get; }
        public bool IsWriteMode { get; }

        public FileSystemObjectPool(FileSystemBase FileSystem, bool writeMode, int delayTimeout, FileOperationFlags defaultFileOperationFlags = FileOperationFlags.None)
            : base(delayTimeout, new StrComparer(FileSystem.PathInterpreter.PathStringComparison))
        {
            this.FileSystem = FileSystem;
            this.IsWriteMode = writeMode;

            this.DefaultFileOperationFlags = defaultFileOperationFlags;
            this.DefaultFileOperationFlags |= FileOperationFlags.AutoCreateDirectoryOnFileCreation | FileOperationFlags.RandomAccessOnly;
        }

        protected override async Task<FileObjectBase> OpenImplAsync(string name, FileOperationFlags param, CancellationToken cancel)
        {
            if (this.IsWriteMode == false)
            {
                string path = name.Substring(2);
                path = await FileSystem.NormalizePathAsync(path, cancel);

                return await FileSystem.OpenAsync(path, cancel: cancel, operationFlags: this.DefaultFileOperationFlags | param);
            }
            else
            {
                string path = name.Substring(2);
                path = await FileSystem.NormalizePathAsync(path, cancel);

                return await FileSystem.OpenOrCreateAsync(path, cancel: cancel, operationFlags: this.DefaultFileOperationFlags | param);
            }
        }
    }

    enum FileSystemStyle
    {
        Windows,
        Linux,
        Mac,
    }

    class FileSystemPathInterpreter
    {
        public FileSystemStyle Style { get; }
        public string DirectorySeparator { get; }
        public string[] AltDirectorySeparators { get; }
        public StringComparison PathStringComparison { get; }
        public StrComparer PathStringComparer { get; }

        public FileSystemPathInterpreter() : this(Env.IsWindows ? FileSystemStyle.Windows : (Env.IsMac ? FileSystemStyle.Mac : FileSystemStyle.Linux)) { }

        public FileSystemPathInterpreter(FileSystemStyle style)
        {
            this.Style = style;

            switch (this.Style)
            {
                case FileSystemStyle.Windows:
                    this.DirectorySeparator = @"\";
                    this.PathStringComparison = StringComparison.OrdinalIgnoreCase;
                    break;

                case FileSystemStyle.Mac:
                    this.DirectorySeparator = "/";
                    this.PathStringComparison = StringComparison.OrdinalIgnoreCase;
                    break;

                default:
                    this.DirectorySeparator = "/";
                    this.PathStringComparison = StringComparison.Ordinal;
                    break;
            }

            this.AltDirectorySeparators = new string[] { @"\", "/" };

            this.PathStringComparer = new StrComparer(this.PathStringComparison);
        }

        public string RemoveLastSeparatorChar(string path)
        {
            path = path.NonNull();

            if (path.All(c => AltDirectorySeparators.Where(x => x[0] == c).Any()))
            {
                return path;
            }

            if (path.Length == 3 &&
                ((path[0] >= 'a' && path[0] <= 'z') || (path[0] >= 'A' && path[0] <= 'Z')) &&
                path[1] == ':' &&
                AltDirectorySeparators.Where(x => x[0] == path[2]).Any())
            {
                return path;
            }

            while (path.Length >= 1)
            {
                char c = path[path.Length - 1];
                if (AltDirectorySeparators.Where(x => x[0] == c).Any())
                {
                    path = path.Substring(0, path.Length - 1);
                }
                else
                {
                    break;
                }
            }

            return path;
        }

        public string GetDirectoryName(string path)
        {
            if (path == null) return null;
            SepareteDirectoryAndFileName(path, out string dirPath, out _);
            return dirPath;
        }

        public string GetFileName(string path)
        {
            if (path == null) return null;
            SepareteDirectoryAndFileName(path, out _, out string fileName);
            return fileName;
        }

        public string Combine(string path1, string path2)
        {
            if (path1 == null && path2 == null) return null;

            path1 = path1.NonNull();
            path2 = path2.NonNull();

            if (path1.IsEmpty())
                return path2;

            if (path2.IsEmpty())
                return path1;

            if (path2.Length >= 1)
            {
                if (AltDirectorySeparators.Where(x => x[0] == path2[0]).Any())
                    return path2;
            }

            path1 = RemoveLastSeparatorChar(path1);

            string sepStr = this.DirectorySeparator;
            if (path1.Length >= 1 && AltDirectorySeparators.Where(x => x[0] == path1[path1.Length - 1]).Any())
            {
                sepStr = "";
            }

            return path1 + sepStr + path2;
        }

        public string Combine(params string[] pathList)
        {
            if (pathList == null || pathList.Length == 0) return null;
            if (pathList.Length == 1) return pathList[0];

            string path1 = pathList[0];

            for (int i = 0; i < pathList.Length - 1; i++)
            {
                string path2 = pathList[i + 1];

                path1 = Combine(path1, path2);
            }

            return path1;
        }

        public string GetFileNameWithoutExtension(string path, bool longExtension = false)
        {
            if (path == null) return null;
            if (path.IsEmpty()) return "";
            path = GetFileName(path);
            int[] dots = path.FindStringIndexes(".", true);
            if (dots.Length == 0)
                return path;

            int i = longExtension ? dots.First() : dots.Last();
            return path.Substring(0, i);
        }

        public string GetExtension(string path, bool longExtension = false)
        {
            if (path == null) return null;
            if (path.IsEmpty()) return "";
            path = GetFileName(path);
            int[] dots = path.FindStringIndexes(".", true);
            if (dots.Length == 0)
                return path;

            int i = longExtension ? dots.First() : dots.Last();
            return path.Substring(i);
        }

        public void SepareteDirectoryAndFileName(string path, out string dirPath, out string fileName)
        {
            if (path.IsEmpty())
                throw new ArgumentNullException("path");

            path = path.NonNull();

            int i = 0;

            // Skip head separators (e.g. /usr/local/ or \\server\share\)
            for (int j = 0; j < path.Length; j++)
            {
                char c = path[j];

                if (AltDirectorySeparators.Where(x => x[0] == c).Any())
                {
                    i = j;
                }
                else
                {
                    break;
                }
            }

            if (path.StartsWith(@"\\") || path.StartsWith(@"//"))
            {
                // Windows UNC Path
                for (int j = 2; j < path.Length; j++)
                {
                    char c = path[j];

                    if (AltDirectorySeparators.Where(x => x[0] == c).Any())
                    {
                        break;
                    }
                    else
                    {
                        i = j;
                    }
                }
            }

            int lastMatch = -1;
            while (true)
            {
                i = path.FindStringsMulti(i, this.PathStringComparison, out int foundKeyIndex, this.AltDirectorySeparators);
                if (i == -1)
                {
                    break;
                }
                else
                {
                    lastMatch = i;
                    i++;
                }
            }

            if (lastMatch == -1)
            {
                if (path.Any(c => AltDirectorySeparators.Where(x => x[0] == c).Any()))
                {
                    dirPath = RemoveLastSeparatorChar(path);
                    fileName = "";
                }
                else
                {
                    dirPath = "";
                    fileName = path;
                }
            }
            else
            {
                dirPath = RemoveLastSeparatorChar(path.Substring(0, lastMatch + 1));
                fileName = path.Substring(lastMatch + 1);
            }
        }
    }

    abstract class FileSystemBase : AsyncCleanupable
    {
        public static PalFileSystem Local { get; } = new PalFileSystem(LeakChecker.SuperGrandLady);

        public DirectoryWalker DirectoryWalker { get; }
        public FileSystemPathInterpreter PathInterpreter { get; }

        CriticalSection LockObj = new CriticalSection();
        List<FileObjectBase> OpenedHandleList = new List<FileObjectBase>();
        RefInt CriticalCounter = new RefInt();
        CancellationTokenSource CancelSource = new CancellationTokenSource();

        public FileSystemObjectPool ObjectPoolForRead { get; }
        public FileSystemObjectPool ObjectPoolForWrite { get; }

        public FileSystemBase(AsyncCleanuperLady lady, FileSystemPathInterpreter fileSystemMetrics) : base(lady)
        {
            try
            {
                this.PathInterpreter = fileSystemMetrics;
                DirectoryWalker = new DirectoryWalker(this);

                ObjectPoolForRead = new FileSystemObjectPool(this, false, AppConfig.FileSystemSettings.PooledHandleLifetime.Value);
                ObjectPoolForWrite = new FileSystemObjectPool(this, true, AppConfig.FileSystemSettings.PooledHandleLifetime.Value);
            }
            catch
            {
                Lady.DisposeAllSafe();
                throw;
            }
        }

        public async Task<RandomAccessHandle> GetRandomAccessHandleAsync(string fileName, bool writeMode, FileOperationFlags operationFlags = FileOperationFlags.None, CancellationToken cancel = default)
        {
            FileSystemObjectPool pool = writeMode ? ObjectPoolForWrite : ObjectPoolForRead;

            RefObjectHandle<FileObjectBase> refObject = await pool.OpenOrGetAsync(fileName, operationFlags, cancel);

            return new RandomAccessHandle(refObject);
        }

        void CheckNotDisposed()
        {
            if (DisposeFlag.IsSet)
                throw new FileSystemException("The file system is already disposed.");
        }

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
        protected abstract Task DeleteFileImplAsync(string path, CancellationToken cancel = default);
        protected abstract Task<FileSystemEntity[]> EnumDirectoryImplAsync(string directoryPath, CancellationToken cancel = default);
        protected abstract Task CreateDirectoryImplAsync(string directoryPath, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default);
        protected abstract Task<FileSystemMetadata> GetFileMetadataImplAsync(string path, CancellationToken cancel = default);

        public async Task<string> NormalizePathAsync(string path, CancellationToken cancel = default)
        {
            path = path.NonNull();

            using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken opCancel, cancel, this.CancelSource.Token))
            {
                using (TaskUtil.EnterCriticalCounter(CriticalCounter))
                {
                    CheckNotDisposed();

                    cancel.ThrowIfCancellationRequested();

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

                    await option.NormalizePathAsync(this, opCancel);

                    if (option.Access.Bit(FileAccess.Read) == false)
                    {
                        throw new ArgumentException("The Access member must contain the FileAccess.Read bit.");
                    }

                    if (option.Mode == FileMode.Append || option.Mode == FileMode.Create || option.Mode == FileMode.CreateNew ||
                        option.Mode == FileMode.OpenOrCreate || option.Mode == FileMode.Truncate)
                    {
                        if (option.Access.Bit(FileAccess.Write) == false)
                        {
                            throw new ArgumentException("The Access member must contain the FileAccess.Write bit when opening a file with create mode.");
                        }

                        if (option.OperationFlags.Bit(FileOperationFlags.AutoCreateDirectoryOnFileCreation))
                        {
                            string dirName = this.PathInterpreter.GetDirectoryName(option.Path);
                            if (dirName.IsFilled())
                            {
                                await CreateDirectoryImplAsync(dirName, option.OperationFlags, opCancel);
                            }
                        }
                    }

                    FileObjectBase f = await CreateFileImplAsync(option, opCancel);

                    lock (LockObj)
                    {
                        OpenedHandleList.Add(f);
                    }

                    f.EventListeners.RegisterCallback(FileEventListenerCallback);

                    return f;
                }
            }
        }

        void FileEventListenerCallback(FileObjectBase obj, FileObjectEventType eventType, object userState)
        {
            switch (eventType)
            {
                case FileObjectEventType.Closed:
                    lock (LockObj)
                    {
                        OpenedHandleList.Remove(obj);
                    }
                    break;
            }
        }

        public FileObjectBase CreateFile(FileParameters option, CancellationToken cancel = default)
            => CreateFileAsync(option, cancel).GetResult();

        public Task<FileObjectBase> CreateAsync(string path, bool noShare = false, FileOperationFlags operationFlags = FileOperationFlags.None, CancellationToken cancel = default)
            => CreateFileAsync(new FileParameters(path, FileMode.Create, FileAccess.ReadWrite, noShare ? FileShare.None : FileShare.Read, operationFlags), cancel);

        public FileObjectBase Create(string path, bool noShare = false, FileOperationFlags operationFlags = FileOperationFlags.None, CancellationToken cancel = default)
            => CreateAsync(path, noShare, operationFlags, cancel).GetResult();

        public Task<FileObjectBase> OpenAsync(string path, bool writeMode = false, bool noShare = false, bool readLock = false, FileOperationFlags operationFlags = FileOperationFlags.None, CancellationToken cancel = default)
            => CreateFileAsync(new FileParameters(path, FileMode.Open, (writeMode ? FileAccess.ReadWrite : FileAccess.Read),
                (noShare ? FileShare.None : ((writeMode || readLock) ? FileShare.Read : (FileShare.ReadWrite | FileShare.Delete))), operationFlags), cancel);

        public FileObjectBase Open(string path, bool writeMode = false, bool noShare = false, bool readLock = false, FileOperationFlags operationFlags = FileOperationFlags.None, CancellationToken cancel = default)
            => OpenAsync(path, writeMode, noShare, readLock, operationFlags, cancel).GetResult();

        public Task<FileObjectBase> OpenOrCreateAsync(string path, bool noShare = false, FileOperationFlags operationFlags = FileOperationFlags.None, CancellationToken cancel = default)
            => CreateFileAsync(new FileParameters(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, noShare ? FileShare.None : FileShare.Read, operationFlags), cancel);

        public FileObjectBase OpenOrCreate(string path, bool noShare = false, FileOperationFlags operationFlags = FileOperationFlags.None, CancellationToken cancel = default)
            => OpenOrCreateAsync(path, noShare, operationFlags, cancel).GetResult();

        public async Task WriteToFile(string path, bool noShare = false, FileOperationFlags operationFlags = FileOperationFlags.None, CancellationToken cancel = default)
        {
        }

        public async Task CreateDirectoryAsync(string path, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
        {
            using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken opCancel, cancel, this.CancelSource.Token))
            {
                using (TaskUtil.EnterCriticalCounter(CriticalCounter))
                {
                    CheckNotDisposed();

                    path = await NormalizePathAsync(path, opCancel);

                    await CreateDirectoryImplAsync(path, flags, opCancel);
                }
            }
        }

        public void CreateDirectory(string path, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
            => CreateDirectoryAsync(path, flags, cancel).GetResult();

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

        async Task<bool> EnumDirectoryRecursiveInternalAsync(int depth, List<FileSystemEntity> currentList, string directoryPath, bool recursive, CancellationToken opCancel)
        {
            CheckNotDisposed();

            opCancel.ThrowIfCancellationRequested();

            FileSystemEntity[] entityList = await EnumDirectoryInternalAsync(directoryPath, opCancel);

            foreach (FileSystemEntity entity in entityList)
            {
                if (depth == 0 || entity.IsCurrentDirectory == false)
                {
                    currentList.Add(entity);
                }

                if (recursive)
                {
                    if (entity.IsDirectory && entity.IsCurrentDirectory == false)
                    {
                        if (await EnumDirectoryRecursiveInternalAsync(depth + 1, currentList, entity.FullPath, true, opCancel) == false)
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

                directoryPath = await NormalizePathAsync(directoryPath, opCancel);

                List<FileSystemEntity> currentList = new List<FileSystemEntity>();

                if (await EnumDirectoryRecursiveInternalAsync(0, currentList, directoryPath, recursive, opCancel) == false)
                {
                    throw new OperationCanceledException();
                }

                return currentList.ToArray();
            }
        }

        public FileSystemEntity[] EnumDirectory(string directoryPath, bool recursive = false, CancellationToken cancel = default)
            => EnumDirectoryAsync(directoryPath, recursive, cancel).GetResult();

        public async Task<FileSystemMetadata> GetFileMetadataAsync(string path, CancellationToken cancel = default)
        {
            using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken opCancel, cancel, this.CancelSource.Token))
            {
                using (TaskUtil.EnterCriticalCounter(CriticalCounter))
                {
                    CheckNotDisposed();

                    cancel.ThrowIfCancellationRequested();

                    path = await NormalizePathImplAsync(path, opCancel);

                    return await GetFileMetadataImplAsync(path, cancel);
                }
            }
        }
        public FileSystemMetadata GetFileMetadata(string path, CancellationToken cancel = default)
            => GetFileMetadataAsync(path, cancel).GetResult();

        public async Task DeleteFileAsync(string path, CancellationToken cancel = default)
        {
            using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken opCancel, cancel, this.CancelSource.Token))
            {
                using (TaskUtil.EnterCriticalCounter(CriticalCounter))
                {
                    CheckNotDisposed();

                    cancel.ThrowIfCancellationRequested();

                    path = await NormalizePathImplAsync(path, opCancel);

                    await DeleteFileImplAsync(path, opCancel);
                }
            }
        }

        public void DeleteFile(string path, CancellationToken cancel = default)
            => DeleteFileAsync(path, cancel).GetResult();

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

