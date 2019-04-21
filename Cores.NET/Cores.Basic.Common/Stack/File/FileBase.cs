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
using static IPA.Cores.Globals.Basic;

#pragma warning disable CS0162

namespace IPA.Cores.Basic
{
    class FileException : Exception
    {
        public FileException(string path, string message) : base($"File \"{path}\": {message}") { }
    }

    [Flags]
    enum FileSpecialOperationFlags : long
    {
        None = 0,
        SetCompressionFlag = 1,
        RemoveCompressionFlag = 2,
    }

    [Flags]
    enum FileMetadataGetFlags
    {
        DefaultAll = 0,
        NoAttributes = 1,
        NoTimes = 2,
        NoPreciseFileSize = 4,
        NoSecurity = 8,
        NoAlternateStream = 16,
        NoPhysicalFileSize = 32,
    }

    [Flags]
    enum FileMetadataCopyMode : long
    {
        None = 0,
        All = 1,
        Attributes = 2,
        ReplicateArchiveBit = 4,

        CreationTime = 8,
        LastWriteTime = 16,
        LastAccessTime = 32,
        TimeAll = CreationTime | LastWriteTime | LastAccessTime,

        SecurityOwner = 64,
        SecurityGroup = 128,
        SecurityAcl = 256,
        SecurityAudit = 512,
        SecurityAll = SecurityOwner | SecurityGroup | SecurityAcl | SecurityAudit,

        AlternateStream = 1024,

        Default = Attributes | TimeAll | AlternateStream,
    }

    [Serializable]
    class FileSecurityOwner : IEmptyChecker
    {
        public string Win32OwnerSddl;

        public bool IsThisEmpty() => Win32OwnerSddl.IsEmpty();
    }

    [Serializable]
    class FileSecurityGroup : IEmptyChecker
    {
        public string Win32GroupSddl;

        public bool IsThisEmpty() => Win32GroupSddl.IsEmpty();
    }

    [Serializable]
    class FileSecurityAcl : IEmptyChecker
    {
        public string Win32AclSddl;

        public bool IsThisEmpty() => Win32AclSddl.IsEmpty();
    }

    [Serializable]
    class FileSecurityAudit : IEmptyChecker
    {
        public string Win32AuditSddl;

        public bool IsThisEmpty() => Win32AuditSddl.IsEmpty();
    }

    [Serializable]
    class FileSecurityMetadata : IEmptyChecker
    {
        public FileSecurityOwner Owner;
        public FileSecurityGroup Group;
        public FileSecurityAcl Acl;
        public FileSecurityAudit Audit;

        public bool IsThisEmpty() => Owner.IsEmpty() && Group.IsEmpty() && Acl.IsEmpty() && Audit.IsEmpty();

        public FileSecurityMetadata Clone(FileMetadataCopyMode mode = FileMetadataCopyMode.All)
        {
            FileSecurityMetadata dst = new FileSecurityMetadata();
            Copy(this, dst, mode);
            return dst;
        }

        public static void Copy(FileSecurityMetadata src, FileSecurityMetadata dest, FileMetadataCopyMode mode = FileMetadataCopyMode.All)
        {
            if (mode.Bit(FileMetadataCopyMode.All)) mode |= FileMetadataCopyMode.SecurityAll;

            if (mode.Bit(FileMetadataCopyMode.SecurityOwner))
                dest.Owner = src.Owner.FilledOrDefault().CloneDeep();

            if (mode.Bit(FileMetadataCopyMode.SecurityGroup))
                dest.Group = src.Group.FilledOrDefault().CloneDeep();

            if (mode.Bit(FileMetadataCopyMode.SecurityAcl))
                dest.Acl = src.Acl.FilledOrDefault().CloneDeep();

            if (mode.Bit(FileMetadataCopyMode.SecurityAudit))
                dest.Audit = src.Audit.FilledOrDefault().CloneDeep();
        }
    }

    [Serializable]
    class FileAlternateStreamItemMetadata : IEmptyChecker
    {
        public string Name;
        public byte[] Data;

        public bool IsThisEmpty() => Name.IsEmpty() || Data.IsEmpty();
    }

    [Serializable]
    class FileAlternateStreamMetadata : IEmptyChecker
    {
        public FileAlternateStreamItemMetadata[] Items;

        public bool IsThisEmpty() => (Items == null);
    }

    class FileMetadata
    {
        public const FileAttributes CopyableAttributes = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive
            | FileAttributes.Directory | FileAttributes.NotContentIndexed | FileAttributes.Offline | FileAttributes.NoScrubData | FileAttributes.IntegrityStream;

        public bool IsDirectory;
        public long Size;
        public long PhysicalSize;

        public FileAttributes? Attributes;
        public DateTimeOffset? CreationTime;
        public DateTimeOffset? LastWriteTime;
        public DateTimeOffset? LastAccessTime;

        public FileSecurityMetadata Security;

        public FileAlternateStreamMetadata AlternateStream;

        public FileSpecialOperationFlags SpecialOperationFlags;

        public FileMetadata() { }

        public FileMetadata(bool isDirectory = false, FileSpecialOperationFlags specialOperation = FileSpecialOperationFlags.None,
            FileAttributes? attributes = null, DateTimeOffset? creationTime = null, DateTimeOffset? lastWriteTime = null, DateTimeOffset? lastAccessTime = null,
            FileSecurityMetadata securityData = null, FileAlternateStreamMetadata alternateStream = null,
            long size = 0, long? physicalSize = null)
        {
            this.IsDirectory = isDirectory;
            this.Attributes = attributes;
            this.CreationTime = creationTime;
            this.LastWriteTime = lastWriteTime;
            this.LastAccessTime = lastAccessTime;
            this.Security = securityData.CloneDeep();
            this.AlternateStream = alternateStream.CloneDeep();
            this.SpecialOperationFlags = specialOperation;
            this.Size = size;
            this.PhysicalSize = physicalSize ?? size;
        }

        public FileMetadata Clone(FileMetadataCopyMode mode)
        {
            if (mode.Bit(FileMetadataCopyMode.All))
            {
                var ret = (FileMetadata)this.MemberwiseClone();
                if (ret.Attributes != null)
                {
                    ret.Attributes &= CopyableAttributes;
                    if (((FileAttributes)ret.Attributes).Bit(FileAttributes.Directory) == false)
                    {
                        if (ret.Attributes == 0)
                            ret.Attributes = FileAttributes.Normal;
                    }
                }
                ret.SpecialOperationFlags = FileSpecialOperationFlags.None;
                return ret;
            }

            FileMetadata dest = new FileMetadata();

            this.CopyToInternal(dest, mode);

            return dest;
        }

        void CopyToInternal(FileMetadata dest, FileMetadataCopyMode mode)
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

            if (this.Security.IsFilled())
            {
                var cloned = this.Security.Clone(mode);
                if (cloned.IsFilled())
                {
                    dest.Security = cloned;
                }
            }

            if (mode.Bit(FileMetadataCopyMode.AlternateStream))
            {
                if (this.AlternateStream.IsFilled())
                {
                    dest.AlternateStream = this.AlternateStream.CloneDeep();
                }
            }
        }
    }

    class FileMetadataCopier
    {
        public FileMetadataCopyMode Mode { get; }
        public FileMetadataGetFlags OptimizedMetadataGetFlags { get; }

        public FileMetadataCopier(FileMetadataCopyMode mode = FileMetadataCopyMode.Default)
        {
            this.Mode = mode;
            this.OptimizedMetadataGetFlags = CalcOptimizedMetadataGetFlags(this.Mode);
        }
        public virtual FileMetadata Copy(FileMetadata src)
        {
            if (src == null) return null;
            return src.Clone(this.Mode);
        }

        public static FileMetadataGetFlags CalcOptimizedMetadataGetFlags(FileMetadataCopyMode mode)
        {
            FileMetadataGetFlags ret = FileMetadataGetFlags.DefaultAll;

            if (mode.Bit(FileMetadataCopyMode.All) == false)
            {
                if (mode.Bit(FileMetadataCopyMode.Attributes) == false) ret |= FileMetadataGetFlags.NoAttributes;

                if (mode.BitAny(FileMetadataCopyMode.TimeAll) == false) ret |= FileMetadataGetFlags.NoTimes;

                if (mode.BitAny(FileMetadataCopyMode.SecurityAll) == false) ret |= FileMetadataGetFlags.NoSecurity;

                if (mode.Bit(FileMetadataCopyMode.AlternateStream) == false) ret |= FileMetadataGetFlags.NoAlternateStream;
            }

            return ret;
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
            this.Access = access | FileAccess.Read;
            this.Flags = flags;
        }

        public async Task NormalizePathAsync(FileSystemBase fileSystem, CancellationToken cancel = default)
        {
            string ret = await fileSystem.NormalizePathAsync(this.Path, cancel);
            this.Path = ret;
        }

        public void NormalizePath(FileSystemBase fileSystem, CancellationToken cancel = default)
            => NormalizePathAsync(fileSystem, cancel).GetResult();

        public FileParameters Clone()
            => (FileParameters)this.MemberwiseClone();
    }

    [Flags]
    enum FileOperationFlags : ulong
    {
        None = 0,
        NoPartialRead = 1,
        BackupMode = 2,
        AutoCreateDirectory = 4,
        RandomAccessOnly = 8,
        LargeFs_AppendWithoutCrossBorder = 16,
        LargeFs_AppendNewLineForCrossBorder = 32,
        ForceClearReadOnlyOrHiddenBitsOnNeed = 64,
        OnCreateSetCompressionFlag = 128,
        OnCreateRemoveCompressionFlag = 256,
        NoAsync = 512,
        SparseFile = 1024,
        WriteOnlyIfChanged = 2048,
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

    class ConcurrentRandomAccess<T> : IRandomAccess<T>
    {
        public readonly AsyncLock TargetLock;
        public readonly IRandomAccess<T> Target;

        public ConcurrentRandomAccess(IRandomAccess<T> target)
        {
            this.Target = target;
            this.TargetLock = this.Target.SharedAsyncLock;
        }

        public AsyncLock SharedAsyncLock { get; } = new AsyncLock();

        public void Dispose() => Dispose(true);
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;
        }

        public async Task AppendAsync(ReadOnlyMemory<T> data, CancellationToken cancel = default)
        {
            using (await TargetLock.LockWithAwait(cancel))
            {
                if (DisposeFlag.IsSet)
                    throw new ObjectDisposedException("ConcurrentRandomAccess");
            }
        }

        public async Task FlushAsync(CancellationToken cancel = default)
        {
            using (await TargetLock.LockWithAwait(cancel))
            {
                if (DisposeFlag.IsSet)
                    throw new ObjectDisposedException("ConcurrentRandomAccess");

                await Target.FlushAsync(cancel);
            }
        }

        public async Task<long> GetFileSizeAsync(bool refresh = false, CancellationToken cancel = default)
        {
            using (await TargetLock.LockWithAwait(cancel))
            {
                if (DisposeFlag.IsSet)
                    throw new ObjectDisposedException("ConcurrentRandomAccess");

                return await Target.GetFileSizeAsync(refresh, cancel);
            }
        }

        public async Task<long> GetPhysicalSizeAsync(CancellationToken cancel = default)
        {
            using (await TargetLock.LockWithAwait(cancel))
            {
                if (DisposeFlag.IsSet)
                    throw new ObjectDisposedException("ConcurrentRandomAccess");

                return await Target.GetPhysicalSizeAsync(cancel);
            }
        }

        public async Task<int> ReadRandomAsync(long position, Memory<T> data, CancellationToken cancel = default)
        {
            using (await TargetLock.LockWithAwait(cancel))
            {
                if (DisposeFlag.IsSet)
                    throw new ObjectDisposedException("ConcurrentRandomAccess");

                return await Target.ReadRandomAsync(position, data, cancel);
            }
        }

        public async Task SetFileSizeAsync(long size, CancellationToken cancel = default)
        {
            using (await TargetLock.LockWithAwait(cancel))
            {
                if (DisposeFlag.IsSet)
                    throw new ObjectDisposedException("ConcurrentRandomAccess");

                await Target.SetFileSizeAsync(size, cancel);
            }
        }

        public async Task WriteRandomAsync(long position, ReadOnlyMemory<T> data, CancellationToken cancel = default)
        {
            using (await TargetLock.LockWithAwait(cancel))
            {
                if (DisposeFlag.IsSet)
                    throw new ObjectDisposedException("ConcurrentRandomAccess");

                await Target.WriteRandomAsync(position, data, cancel);
            }
        }

        public void Append(ReadOnlyMemory<T> data, CancellationToken cancel = default) => AppendAsync(data, cancel).GetResult();
        public void Flush(CancellationToken cancel = default) => FlushAsync(cancel).GetResult();
        public long GetFileSize(bool refresh = false, CancellationToken cancel = default) => GetFileSizeAsync(refresh, cancel).GetResult();
        public long GetPhysicalSize(CancellationToken cancel = default) => GetPhysicalSizeAsync(cancel).GetResult();
        public int ReadRandom(long position, Memory<T> data, CancellationToken cancel = default) => ReadRandomAsync(position, data, cancel).GetResult();
        public void SetFileSize(long size, CancellationToken cancel = default) => SetFileSizeAsync(size, cancel).GetResult();
        public void WriteRandom(long position, ReadOnlyMemory<T> data, CancellationToken cancel = default) => WriteRandomAsync(position, data, cancel).GetResult();
    }

    interface IRandomAccess<T> : IDisposable
    {
        Task<int> ReadRandomAsync(long position, Memory<T> data, CancellationToken cancel = default);
        int ReadRandom(long position, Memory<T> data, CancellationToken cancel = default);

        Task WriteRandomAsync(long position, ReadOnlyMemory<T> data, CancellationToken cancel = default);
        void WriteRandom(long position, ReadOnlyMemory<T> data, CancellationToken cancel = default);

        Task AppendAsync(ReadOnlyMemory<T> data, CancellationToken cancel = default);
        void Append(ReadOnlyMemory<T> data, CancellationToken cancel = default);

        Task<long> GetFileSizeAsync(bool refresh = false, CancellationToken cancel = default);
        long GetFileSize(bool refresh = false, CancellationToken cancel = default);

        Task<long> GetPhysicalSizeAsync(CancellationToken cancel = default);
        long GetPhysicalSize(CancellationToken cancel = default);

        Task SetFileSizeAsync(long size, CancellationToken cancel = default);
        void SetFileSize(long size, CancellationToken cancel = default);

        Task FlushAsync(CancellationToken cancel = default);
        void Flush(CancellationToken cancel = default);

        AsyncLock SharedAsyncLock { get; }
    }

    class RandomAccessHandle : IRandomAccess<byte>, IDisposable
    {
        readonly RefObjectHandle<FileBase> Ref;
        readonly FileBase File;
        bool DisposeFile = false;

        public AsyncLock SharedAsyncLock { get; } = new AsyncLock();

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

        public Task<long> GetPhysicalSizeAsync(CancellationToken cancel = default)
            => this.File.GetPhysicalSizeAsync(cancel);
        public long GetPhysicalSize(CancellationToken cancel = default) => GetPhysicalSizeAsync(cancel).GetResult();
    }

    abstract class FileBase : IDisposable, IAsyncClosable, IRandomAccess<byte>
    {
        public FileParameters FileParams { get; }
        public virtual string FinalPhysicalPath => throw new NotImplementedException();
        public abstract bool IsOpened { get; }
        public abstract Exception LastError { get; protected set; }
        public FastEventListenerList<FileBase, FileObjectEventType> EventListeners { get; }
            = new FastEventListenerList<FileBase, FileObjectEventType>();

        public AsyncLock SharedAsyncLock { get; } = new AsyncLock();

        protected FileBase(FileParameters fileParams)
        {
            this.FileParams = fileParams;
        }

        public override string ToString() => $"FileObject('{FileParams.Path}')";

        public abstract Task<int> ReadAsync(Memory<byte> data, CancellationToken cancel = default);
        public abstract Task<int> ReadRandomAsync(long position, Memory<byte> data, CancellationToken cancel = default);

        public abstract Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancel = default);
        public abstract Task WriteRandomAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default);

        public abstract Task<long> SeekAsync(long offset, SeekOrigin origin, CancellationToken cancel = default);
        public abstract Task<long> GetCurrentPositionAsync(CancellationToken cancel = default);

        public abstract Task SetFileSizeAsync(long size, CancellationToken cancel = default);
        public abstract Task<long> GetFileSizeAsync(bool refresh = false, CancellationToken cancel = default);

        public virtual Task<long> GetPhysicalSizeAsync(CancellationToken cancel = default) => GetFileSizeAsync(false, cancel);
        public virtual long GetPhysicalSize(CancellationToken cancel = default) => GetPhysicalSizeAsync(cancel).GetResult();

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

        public long Size
        {
            set => SetFileSize(value);
            get => GetFileSize();
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
