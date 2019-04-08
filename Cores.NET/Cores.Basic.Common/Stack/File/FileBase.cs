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

    enum FileMetadataCopyMode
    {
        None = 0,
        All = 1,
        Attributes = 2,
        ReplicateArchiveBit = 4,
        CreationTime = 8,
        LastWriteTime = 16,
        LastAccessTime = 32,
        AllDates = CreationTime | LastWriteTime | LastAccessTime,
        Default = Attributes | LastWriteTime | LastAccessTime,
    }

    class FileMetadata : ICloneable
    {
        public bool IsDirectory;
        public long Size;
        public FileAttributes? Attributes;
        public DateTimeOffset? CreationTime;
        public DateTimeOffset? LastWriteTime;
        public DateTimeOffset? LastAccessTime;

        public const FileAttributes CopyableAttributes = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive | FileAttributes.Directory | FileAttributes.NotContentIndexed;

        public FileMetadata() { }

        public FileMetadata(bool isDirectory, FileAttributes? attributes = null, DateTimeOffset? creationTime = null, DateTimeOffset? lastWriteTime = null, DateTimeOffset? lastAccessTime = null)
        {
            this.IsDirectory = isDirectory;
            this.Attributes = attributes;
            this.CreationTime = creationTime;
            this.LastWriteTime = lastWriteTime;
            this.LastAccessTime = lastAccessTime;
        }

        public object Clone() => this.MemberwiseClone();
        public FileMetadata Clone(FileMetadataCopyMode mode)
        {
            if (mode.Bit(FileMetadataCopyMode.All))
            {
                var ret = (FileMetadata)this.Clone();
                if (ret.Attributes != null)
                {
                    ret.Attributes &= CopyableAttributes;
                    if (((FileAttributes)ret.Attributes).Bit(FileAttributes.Directory) == false)
                    {
                        if (ret.Attributes == 0)
                            ret.Attributes = FileAttributes.Normal;
                    }
                }
                return ret;
            }

            FileMetadata dest = new FileMetadata();

            this.CopyTo(dest, mode);

            return dest;
        }

        public void CopyTo(FileMetadata dest, FileMetadataCopyMode mode)
        {
            dest.IsDirectory = this.IsDirectory;

            if (this.Attributes is FileAttributes srcAttributes)
            {
                if (mode.Bit(FileMetadataCopyMode.Attributes))
                {
                    FileAttributes destAttributes = srcAttributes & CopyableAttributes;

                    if (mode.Bit(FileMetadataCopyMode.ReplicateArchiveBit) == false)
                    {
                        if (srcAttributes.Bit(FileAttributes.Directory))
                            destAttributes &= ~FileAttributes.Archive;
                        else
                            destAttributes |= FileAttributes.Archive;
                    }

                    if (srcAttributes.Bit(FileAttributes.Directory) == false)
                    {
                        if (destAttributes == 0)
                            destAttributes = FileAttributes.Normal;
                    }

                    dest.Attributes = destAttributes;
                }
            }

            if (mode.Bit(FileMetadataCopyMode.CreationTime))
                if (this.CreationTime != null)
                    dest.CreationTime = this.CreationTime;

            if (mode.Bit(FileMetadataCopyMode.LastWriteTime))
                if (this.LastWriteTime != null)
                    dest.LastWriteTime = this.LastWriteTime;

            if (mode.Bit(FileMetadataCopyMode.LastAccessTime))
                if (this.LastAccessTime != null)
                    dest.LastAccessTime = this.LastAccessTime;
        }
    }

    class FileMetadataCopier
    {
        public FileMetadataCopyMode Mode { get; }
        public FileMetadataCopier(FileMetadataCopyMode mode = FileMetadataCopyMode.Default)
        {
            this.Mode = mode;
        }
        public virtual FileMetadata Copy(FileMetadata src)
        {
            if (src == null) return null;
            return src.Clone(this.Mode);
        }
    }

    class FileParameters
    {
        public string Path { get; private set; }
        public FileMode Mode { get; }
        public FileShare Share { get; }
        public FileAccess Access { get; }
        public FileOperationFlags Flags { get; }

        public FileParameters(string path, FileMode mode = FileMode.Open, FileAccess access = FileAccess.Read, FileShare share = FileShare.Read, FileOperationFlags flags = FileOperationFlags.None)
        {
            this.Path = path;
            this.Mode = mode;
            this.Share = share;
            this.Access = access;
            this.Flags = flags;
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
        LargeFileSystemDoNotAppendBeyondBorder = 32,
        IgnoreReadOnlyOrHiddenBits = 64,
    }

    class FileBaseStream : FileStream
    {
        FileBase File;
        bool DisposeObject = false;

        private FileBaseStream() : base((SafeFileHandle)null, FileAccess.Read) { }

        private void _InternalInit(FileBase obj, bool disposeObject)
        {
            File = obj;
            DisposeObject = disposeObject;
        }

        public static FileBaseStream CreateFromFileObject(FileBase file, bool disposeObject = false)
        {
            FileBaseStream ret = Util.NewWithoutConstructor<FileBaseStream>();

            ret._InternalInit(file, disposeObject);

            return ret;
        }

        Once DisposeFlag;
        protected override void Dispose(bool disposing)
        {
            if (DisposeFlag.IsFirstCall() && disposing)
            {
                if (this.DisposeObject)
                    File.DisposeSafe();
            }
            base.Dispose(disposing);
        }

        public override bool CanRead => this.File.FileParams.Access.Bit(FileAccess.Read);
        public override bool CanWrite => this.File.FileParams.Access.Bit(FileAccess.Write);
        public override bool CanSeek => true;
        public override long Length => File.GetFileSize();
        public override long Position { get => File.Position; set => File.Position = value; }
        public override long Seek(long offset, SeekOrigin origin) => File.Seek(offset, origin);
        public override void SetLength(long value) => File.SetFileSize(value);

        public override bool CanTimeout => false;
        public override int ReadTimeout => throw new NotImplementedException();
        public override int WriteTimeout => throw new NotImplementedException();

        [Obsolete]
        public override IntPtr Handle => IntPtr.Zero;

        public override bool IsAsync => true;

        public override string Name => File.FileParams.Path;

        public override SafeFileHandle SafeFileHandle => null;

        public override void Flush() => File.FlushAsync().GetResult();
        public override async Task FlushAsync(CancellationToken cancellationToken = default)
        {
            using (cancellationToken.Register(() => File.Close()))
            {
                await File.FlushAsync();
            }
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            using (cancellationToken.Register(() => File.Close()))
            {
                return await File.ReadAsync(buffer.AsMemory(offset, count));
            }
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            using (cancellationToken.Register(() => File.Close()))
            {
                if (File.FileParams.Flags.Bit(FileOperationFlags.RandomAccessOnly))
                    await File.AppendAsync(buffer.AsReadOnlyMemory(offset, count));
                else
                    await File.WriteAsync(buffer.AsReadOnlyMemory(offset, count));
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
        public override string ToString() => this.File.ToString();
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
            using (cancellationToken.Register(() => File.Close()))
            {
                return await File.ReadAsync(buffer);
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
            using (cancellationToken.Register(() => File.Close()))
            {
                await File.WriteAsync(buffer);
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

    interface IRandomAccess : IDisposable
    {
        FileStream GetStream();

        Task<int> ReadRandomAsync(long position, Memory<byte> data, CancellationToken cancel = default);
        int ReadRandom(long position, Memory<byte> data, CancellationToken cancel = default);

        Task WriteRandomAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default);
        void WriteRandom(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default);

        Task AppendAsync(ReadOnlyMemory<byte> data, CancellationToken cancel = default);
        void Append(ReadOnlyMemory<byte> data, CancellationToken cancel = default);

        Task<long> GetFileSizeAsync(bool refresh = false, CancellationToken cancel = default);
        long GetFileSize(bool refresh = false, CancellationToken cancel = default);

        Task SetFileSizeAsync(long size, CancellationToken cancel = default);
        void SetFileSize(long size, CancellationToken cancel = default);

        Task FlushAsync(CancellationToken cancel = default);
        void Flush(CancellationToken cancel = default);
    }

    class RandomAccessHandle : IRandomAccess, IDisposable
    {
        readonly RefObjectHandle<FileBase> Ref;
        readonly FileBase File;
        bool DisposeFile = false;

        public RandomAccessHandle(FileBase fileHandle, bool disposeObject = false)
        {
            this.Ref = null;
            this.File = fileHandle;
            this.DisposeFile = disposeObject;
        }

        public RandomAccessHandle(RefObjectHandle<FileBase> objHandle)
        {
            this.Ref = objHandle;
            this.File = this.Ref.Object;
        }

        public void Dispose() => Dispose(true);
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;

            if (Ref != null)
                this.Ref.DisposeSafe();

            if (DisposeFile)
                this.File.DisposeSafe();
        }

        public FileStream GetStream() => this.File.GetStream();

        public Task<int> ReadRandomAsync(long position, Memory<byte> data, CancellationToken cancel = default)
            => this.File.ReadRandomAsync(position, data, cancel);
        public int ReadRandom(long position, Memory<byte> data, CancellationToken cancel = default)
            => ReadRandomAsync(position, data, cancel).GetResult();

        public Task WriteRandomAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
            => this.File.WriteRandomAsync(position, data, cancel);
        public void WriteRandom(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
            => WriteRandomAsync(position, data, cancel).GetResult();

        public Task AppendAsync(ReadOnlyMemory<byte> data, CancellationToken cancel = default)
            => this.File.AppendAsync(data, cancel);
        public void Append(ReadOnlyMemory<byte> data, CancellationToken cancel = default)
            => this.AppendAsync(data, cancel).GetResult();

        public Task<long> GetFileSizeAsync(bool refresh = false, CancellationToken cancel = default)
            => this.File.GetFileSizeAsync(refresh, cancel);
        public long GetFileSize(bool refresh = false, CancellationToken cancel = default) => GetFileSizeAsync(refresh, cancel).GetResult();

        public Task SetFileSizeAsync(long size, CancellationToken cancel = default)
            => this.File.SetFileSizeAsync(size, cancel);
        public void SetFileSize(long size, CancellationToken cancel = default) => SetFileSizeAsync(size, cancel).GetResult();

        public Task FlushAsync(CancellationToken cancel = default)
            => this.File.FlushAsync(cancel);
        public void Flush(CancellationToken cancel = default) => FlushAsync(cancel).GetResult();
    }

    abstract class FileBase : IDisposable, IAsyncClosable, IRandomAccess
    {
        public FileParameters FileParams { get; }
        public abstract bool IsOpened { get; }
        public abstract Exception LastError { get; protected set; }
        public FastEventListenerList<FileBase, FileObjectEventType> EventListeners { get; }
            = new FastEventListenerList<FileBase, FileObjectEventType>();

        protected FileBase(FileParameters fileParams)
        {
            this.FileParams = fileParams;
        }

        public override string ToString() => $"FileObject('{FileParams.Path}')";

        protected virtual async Task InternalInitAsync(CancellationToken cancel = default)
        {
            this.LastError = null;

            await Task.CompletedTask;
        }

        public abstract Task<int> ReadAsync(Memory<byte> data, CancellationToken cancel = default);
        public abstract Task<int> ReadRandomAsync(long position, Memory<byte> data, CancellationToken cancel = default);

        public abstract Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancel = default);
        public abstract Task WriteRandomAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default);

        public abstract Task<long> SeekAsync(long offset, SeekOrigin origin, CancellationToken cancel = default);
        public abstract Task<long> GetCurrentPositionAsync(CancellationToken cancel = default);

        public abstract Task SetFileSizeAsync(long size, CancellationToken cancel = default);
        public abstract Task<long> GetFileSizeAsync(bool refresh = false, CancellationToken cancel = default);

        public void Flush(CancellationToken cancel = default) => FlushAsync(cancel).GetResult();
        public abstract Task CloseAsync();


        public FileStream GetStream(bool disposeObject) => FileBaseStream.CreateFromFileObject(this, disposeObject);
        public FileStream GetStream() => GetStream(false);
        public RandomAccessHandle GetRandomAccessHandle(bool disposeObject = false) => new RandomAccessHandle(this, disposeObject);

        public long Position
        {
            set => Seek(value, SeekOrigin.Begin);
            get => GetCurrentPosition();
        }

        public int Read(Memory<byte> data) => ReadAsync(data).GetResult();
        public int ReadRandom(long position, Memory<byte> data, CancellationToken cancel = default)
            => ReadRandomAsync(position, data, cancel).GetResult();

        public void Write(Memory<byte> data, CancellationToken cancel = default) => WriteAsync(data, cancel).GetResult();
        public void WriteRandom(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
            => WriteRandomAsync(position, data, cancel).GetResult();

        public Task AppendAsync(ReadOnlyMemory<byte> data, CancellationToken cancel = default)
            => this.WriteRandomAsync(-1, data, cancel);
        public void Append(ReadOnlyMemory<byte> data, CancellationToken cancel = default)
            => this.AppendAsync(data, cancel).GetResult();

        public long GetCurrentPosition(CancellationToken cancel = default) => GetCurrentPositionAsync().GetResult();
        public long Seek(long offset, SeekOrigin origin, CancellationToken cancel = default)
            => SeekAsync(offset, origin, cancel).GetResult();

        public Task<long> SeekToBeginAsync(CancellationToken cancel = default) => SeekAsync(0, SeekOrigin.Begin, cancel);
        public Task<long> SeekToEndAsync(CancellationToken cancel = default) => SeekAsync(0, SeekOrigin.End, cancel);
        public long SeekToBegin(CancellationToken cancel = default) => SeekToBeginAsync(cancel).GetResult();
        public long SeekToEnd(CancellationToken cancel = default) => SeekToEndAsync(cancel).GetResult();

        public long GetFileSize(bool refresh = false, CancellationToken cancel = default) => GetFileSizeAsync(refresh, cancel).GetResult();
        public void SetFileSize(long size, CancellationToken cancel = default) => SetFileSizeAsync(size, cancel).GetResult();

        public abstract Task FlushAsync(CancellationToken cancel = default);
        public void Close() => Dispose();

        public void Dispose() => Dispose(true);
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;

            try
            {
                CloseAsync().GetResult();
            }
            catch { }
        }

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
            if (FileParams.Flags.Bit(FileOperationFlags.RandomAccessOnly))
                throw new FileException(this.FileParams.Path, "The file object is in RandomAccessOnly mode.");
        }
    }
}
