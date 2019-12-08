﻿// IPA Cores.NET
// 
// Copyright (c) 2018- IPA CyberLab.
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
using System.Diagnostics.CodeAnalysis;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

#pragma warning disable CS0649
#pragma warning disable CA2235 // Mark all non-serializable fields

namespace IPA.Cores.Basic
{
    public class FileException : Exception
    {
        public FileException(string path, string message) : base($"File \"{path}\": {message}") { }
    }

    [Flags]
    public enum FileSpecialOperationFlags : long
    {
        None = 0,
        SetCompressionFlag = 1,
        RemoveCompressionFlag = 2,
    }

    [Flags]
    public enum FileMetadataGetFlags
    {
        DefaultAll = 0,
        NoAttributes = 1,
        NoTimes = 2,
        NoPreciseFileSize = 4,
        NoSecurity = 8,
        NoAlternateStream = 16,
        NoPhysicalFileSize = 32,
        NoAuthor = 64,
    }

    [Flags]
    public enum FileMetadataCopyMode : long
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

        Author = 2048,

        Default = Attributes | TimeAll | AlternateStream | Author,
    }

    [Serializable]
    public class FileSecurityOwner : IEmptyChecker
    {
        public string? Win32OwnerSddl;

        public bool IsThisEmpty() => Win32OwnerSddl._IsEmpty();
    }

    [Serializable]
    public class FileSecurityGroup : IEmptyChecker
    {
        public string? Win32GroupSddl;

        public bool IsThisEmpty() => Win32GroupSddl._IsEmpty();
    }

    [Serializable]
    public class FileSecurityAcl : IEmptyChecker
    {
        public string? Win32AclSddl;

        public bool IsThisEmpty() => Win32AclSddl._IsEmpty();
    }

    [Serializable]
    public class FileSecurityAudit : IEmptyChecker
    {
        public string? Win32AuditSddl;

        public bool IsThisEmpty() => Win32AuditSddl._IsEmpty();
    }

    [Serializable]
    public class FileSecurityMetadata : IEmptyChecker
    {
        public FileSecurityOwner? Owner;
        public FileSecurityGroup? Group;
        public FileSecurityAcl? Acl;
        public FileSecurityAudit? Audit;

        public bool IsThisEmpty() => Owner._IsEmpty() && Group._IsEmpty() && Acl._IsEmpty() && Audit._IsEmpty();

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
                dest.Owner = src.Owner._FilledOrDefault()._CloneDeep();

            if (mode.Bit(FileMetadataCopyMode.SecurityGroup))
                dest.Group = src.Group._FilledOrDefault()._CloneDeep();

            if (mode.Bit(FileMetadataCopyMode.SecurityAcl))
                dest.Acl = src.Acl._FilledOrDefault()._CloneDeep();

            if (mode.Bit(FileMetadataCopyMode.SecurityAudit))
                dest.Audit = src.Audit._FilledOrDefault()._CloneDeep();
        }
    }

    [Serializable]
    public class FileAlternateStreamItemMetadata : IEmptyChecker
    {
        public string? Name;
        public byte[]? Data;

        public bool IsThisEmpty() => Name._IsEmpty() || Data._IsEmpty();
    }

    [Serializable]
    public class FileAlternateStreamMetadata : IEmptyChecker
    {
        public FileAlternateStreamItemMetadata[]? Items;

        public bool IsThisEmpty() => (Items == null);
    }

    [Serializable]
    public class FileAuthorMetadata : IEmptyChecker
    {
        public string? CommitId;

        public string? AuthorEmail;
        public string? AuthorName;
        public DateTimeOffset AuthorTimeStamp;

        public string? CommitterEmail;
        public string? CommitterName;
        public DateTimeOffset CommitterTimeStamp;

        public string? Message;

        public bool IsThisEmpty() => (CommitId._IsEmpty() && AuthorName._IsEmpty() && CommitterName._IsEmpty() && Message._IsEmpty());
    }

    [Serializable]
    public class FileMetadata
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

        public FileSecurityMetadata? Security;

        public FileAlternateStreamMetadata? AlternateStream;

        public FileSpecialOperationFlags SpecialOperationFlags;

        public FileAuthorMetadata? Author;

        public FileMetadata() { }

        public FileMetadata(bool isDirectory = false, FileSpecialOperationFlags specialOperation = FileSpecialOperationFlags.None,
            FileAttributes? attributes = null, DateTimeOffset? creationTime = null, DateTimeOffset? lastWriteTime = null, DateTimeOffset? lastAccessTime = null,
            FileSecurityMetadata? securityData = null, FileAlternateStreamMetadata? alternateStream = null, FileAuthorMetadata? author = null,
            long size = 0, long? physicalSize = null)
        {
            this.IsDirectory = isDirectory;
            this.Attributes = attributes;
            this.CreationTime = creationTime;
            this.LastWriteTime = lastWriteTime;
            this.LastAccessTime = lastAccessTime;
            this.Security = securityData._CloneDeep();
            this.AlternateStream = alternateStream._CloneDeep();
            this.Author = author._CloneDeep();
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

            if (mode.BitAny(FileMetadataCopyMode.SecurityAll))
            {
                if (this.Security._IsFilled())
                {
                    var cloned = this.Security.Clone(mode);
                    if (cloned._IsFilled())
                    {
                        dest.Security = cloned;
                    }
                }
            }

            if (mode.Bit(FileMetadataCopyMode.AlternateStream))
            {
                if (this.AlternateStream._IsFilled())
                {
                    dest.AlternateStream = this.AlternateStream._CloneDeep();
                }
            }

            if (mode.Bit(FileMetadataCopyMode.Author))
            {
                if (this.Author._IsFilled())
                {
                    dest.Author = this.Author._CloneDeep();
                }
            }
        }

        public FileSystemEntity ToFileSystemEntity(PathParser parser, string fullPath)
        {
            FileSystemEntity ret = new FileSystemEntity(
                attributes: this.Attributes ?? default,
                creationTime: this.CreationTime ?? Util.ZeroDateTimeOffsetValue,
                lastAccessTime: this.LastAccessTime ?? Util.ZeroDateTimeOffsetValue,
                lastWriteTime: this.LastWriteTime ?? Util.ZeroDateTimeOffsetValue,
                fullPath: fullPath,
                name: "..",
                physicalSize: this.PhysicalSize,
                size: this.Size
                );

            return ret;
        }
    }

    public class FileMetadataCopier
    {
        public FileMetadataCopyMode Mode { get; }
        public FileMetadataGetFlags OptimizedMetadataGetFlags { get; }

        public FileMetadataCopier(FileMetadataCopyMode mode = FileMetadataCopyMode.Default)
        {
            this.Mode = mode;
            this.OptimizedMetadataGetFlags = CalcOptimizedMetadataGetFlags(this.Mode);
        }

        [return: NotNullIfNotNull("src")]
        public virtual FileMetadata? Copy(FileMetadata? src)
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

                if (mode.Bit(FileMetadataCopyMode.Author) == false) ret |= FileMetadataGetFlags.NoAuthor;
            }

            return ret;
        }
    }

    public sealed class FileParameters
    {
        public string Path { get; private set; }
        public FileMode Mode { get; }
        public FileShare Share { get; }
        public FileAccess Access { get; }
        public FileFlags Flags { get; }

        public FileParameters(string path, FileMode mode = FileMode.Open, FileAccess access = FileAccess.Read, FileShare share = FileShare.Read, FileFlags flags = FileFlags.None)
        {
            this.Path = path;
            this.Mode = mode;
            this.Share = share;
            this.Access = access | FileAccess.Read;
            this.Flags = flags;
        }

        public async Task NormalizePathAsync(FileSystem fileSystem, CancellationToken cancel = default)
        {
            string ret = await fileSystem.NormalizePathAsync(this.Path, cancel: cancel);
            this.Path = ret;
        }

        public void NormalizePath(FileSystem fileSystem, CancellationToken cancel = default)
            => NormalizePathAsync(fileSystem, cancel)._GetResult();

        public FileParameters MapPathVirtualToPhysical(IRewriteVirtualPhysicalPath rewriter)
        {
            this.Path = rewriter.MapPathVirtualToPhysical(this.Path);
            return this;
        }

        public FileParameters MapPathPhysicalToVirtual(IRewriteVirtualPhysicalPath rewriter)
        {
            this.Path = rewriter.MapPathPhysicalToVirtual(this.Path);
            return this;
        }

        public FileParameters Clone()
            => (FileParameters)this.MemberwiseClone();
    }

    [Flags]
    public enum FileFlags : ulong
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
        Async = 512,
        SparseFile = 1024,
        WriteOnlyIfChanged = 2048,
        DeleteFileOnClose = 4096,
        DeleteParentDirOnClose = 8192,
    }

    public class FileBaseStream : FileStream
    {
        FileBase File = null!;
        bool DisposeObject = false;

        private FileBaseStream() : base((SafeFileHandle)null!, FileAccess.Read) { }

        private void _InternalInit(FileBase obj, bool disposeObject)
        {
            File = obj;
            DisposeObject = disposeObject;
        }

        public static FileBaseStream CreateFromFileObject(FileBase file, bool disposeObject = false, long? initialPosition = null)
        {
            FileBaseStream ret = Util.NewWithoutConstructor<FileBaseStream>();

            ret._InternalInit(file, disposeObject);

            if (initialPosition.HasValue)
            {
                ret.Seek(initialPosition.Value, SeekOrigin.Begin);
            }

            return ret;
        }

        Once DisposeFlag;
        protected override void Dispose(bool disposing)
        {
            if (DisposeFlag.IsFirstCall() && disposing)
            {
                if (this.DisposeObject)
                    File._DisposeSafe();
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

        public override SafeFileHandle SafeFileHandle => null!;

        public override void Flush() => File.FlushAsync()._GetResult();
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
                if (File.FileParams.Flags.Bit(FileFlags.RandomAccessOnly))
                    await File.AppendAsync(buffer._AsReadOnlyMemory(offset, count));
                else
                    await File.WriteAsync(buffer._AsReadOnlyMemory(offset, count));
            }
        }

        public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer, offset, count, CancellationToken.None)._GetResult();

        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count, CancellationToken.None)._GetResult();

        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object? state)
            => ReadAsync(buffer, offset, count, default)._AsApm(callback, state);

        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object? state)
            => WriteAsync(buffer, offset, count, default)._AsApm(callback, state);

        public override int EndRead(IAsyncResult asyncResult) => ((Task<int>)asyncResult)._GetResult();
        public override void EndWrite(IAsyncResult asyncResult) => ((Task)asyncResult)._GetResult();

        public override bool Equals(object? obj) => object.Equals(this, obj);
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

        override public int Read(Span<byte> buffer)
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

        override public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
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

        override public void Write(ReadOnlySpan<byte> buffer)
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

        override public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
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

    public enum FileObjectEventType
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

    public class WriteOnlyStreamBasedRandomAccess : SequentialWritableBasedRandomAccess<byte>
    {
        public new StreamBasedSequentialWritable BaseWritable => (StreamBasedSequentialWritable)base.BaseWritable;

        readonly IHolder Leak;

        public WriteOnlyStreamBasedRandomAccess(Stream baseStream, bool autoDispose = false) : base(baseStream._GetSequentialWritable(autoDispose))
        {
            this.Leak = LeakChecker.Enter(LeakCounterKind.WriteOnlyStreamBasedRandomAccess);
        }

        Once DisposeFlag;
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;

                BaseWritable._DisposeSafe();

                this.Leak._DisposeSafe();
            }
            finally { base.Dispose(disposing); }
        }
    }

    public class SequentialWritableBasedRandomAccess<T> : IRandomAccess<T>, IHasError
    {
        public ISequentialWritable<T> BaseWritable { get; }

        public AsyncLock SharedAsyncLock { get; set; } = new AsyncLock();

        public Exception? LastError { get; private set; }

        readonly Action? OnDispose = null;

        bool IsNotFirst = false;
        long StartVirtualPosition = 0;

        long CurrentLength = 0;

        public SequentialWritableBasedRandomAccess(ISequentialWritable<T> baseWritable, Action? onDispose = null)
        {
            this.BaseWritable = baseWritable;
            this.OnDispose = onDispose;
        }

        public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;

            if (this.OnDispose != null)
            {
                this.OnDispose();
            }
        }

        public Task<int> ReadRandomAsync(long position, Memory<T> data, CancellationToken cancel = default)
            => throw new NotImplementedException();

        public async Task WriteRandomAsync(long position, ReadOnlyMemory<T> data, CancellationToken cancel = default)
        {
            if (IsNotFirst == false)
            {
                StartVirtualPosition = position;
                IsNotFirst = true;
            }

            ((IHasError)this).ThrowIfError();

            try
            {
                checked
                {
                    // 内部 position に変換する
                    long internalPos = position - StartVirtualPosition;

                    // 書き込み場所が移動していないかどうか確認
                    if (CurrentLength != internalPos)
                    {
                        throw new ArgumentOutOfRangeException(nameof(position), $"CurrentLength != internalPos. CurrentLength: {CurrentLength}, internalPos: {internalPos}, StartVirtualPosition: {StartVirtualPosition}, position: {position}");
                    }

                    await BaseWritable.AppendAsync(data, cancel);

                    CurrentLength += data.Length;
                }
            }
            catch (Exception ex)
            {
                this.LastError = ex;
                throw;
            }
        }

        public Task AppendAsync(ReadOnlyMemory<T> data, CancellationToken cancel = default)
            => WriteRandomAsync(this.CurrentLength, data, cancel);

        public Task<long> GetFileSizeAsync(bool refresh = false, CancellationToken cancel = default)
        {
            if (IsNotFirst == false)
            {
                throw new CoresException("Not yet written any data.");
            }

            ((IHasError)this).ThrowIfError();

            try
            {
                checked
                {
                    return Task.FromResult(this.CurrentLength + StartVirtualPosition);
                }
            }
            catch (Exception ex)
            {
                this.LastError = ex;
                throw;
            }
        }

        public Task<long> GetPhysicalSizeAsync(CancellationToken cancel = default)
            => GetFileSizeAsync(false, cancel);

        public Task SetFileSizeAsync(long size, CancellationToken cancel = default)
            => throw new NotImplementedException();

        public Task FlushAsync(CancellationToken cancel = default)
        {
            ((IHasError)this).ThrowIfError();

            try
            {
                return BaseWritable.FlushAsync(cancel);
            }
            catch (Exception ex)
            {
                this.LastError = ex;
                throw;
            }
        }
    }

#pragma warning disable CS1998
    public class SeekableStreamBasedRandomAccess : IRandomAccess<byte>
    {
        public Stream BaseStream { get; }

        long InternalFileSize;
        long InternalPosition;

        public int MicroOperationSize { get; set; } = CoresConfig.FileSystemSettings.DefaultMicroOperationSize.Value;

        readonly bool AutoDisposeBase;

        public SeekableStreamBasedRandomAccess(Stream baseStream, bool autoDisposeBase = false)
        {
            this.BaseStream = baseStream;
            this.AutoDisposeBase = autoDisposeBase;

            this.InternalFileSize = GetFileSize(true);
            this.InternalPosition = 0;
        }

        public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;
            if (this.AutoDisposeBase)
            {
                BaseStream._DisposeSafe();
            }
        }

        public void CheckIsOpened()
        {
            if (DisposeFlag.IsSet)
                throw new ObjectDisposedException("StreamRandomAccessWrapper");
        }

        public AsyncLock SharedAsyncLock => new AsyncLock();

        public Task AppendAsync(ReadOnlyMemory<byte> data, CancellationToken cancel = default)
            => WriteRandomAsync(-1, data, cancel);

        public async Task FlushAsync(CancellationToken cancel = default)
        {
            CheckIsOpened();
            cancel.ThrowIfCancellationRequested();
            await BaseStream.FlushAsync(cancel);
        }

        public async Task<long> GetFileSizeAsync(bool refresh = false, CancellationToken cancel = default)
        {
            CheckIsOpened();
            cancel.ThrowIfCancellationRequested();

            if (refresh)
                InternalFileSize = BaseStream.Length;

            return InternalFileSize;
        }

        public Task<long> GetPhysicalSizeAsync(CancellationToken cancel = default)
            => GetFileSizeAsync(false, cancel);

        public async Task<int> ReadRandomAsync(long position, Memory<byte> data, CancellationToken cancel = default)
        {
            if (position < 0) throw new ArgumentOutOfRangeException("position < 0");
            CheckIsOpened();
            cancel.ThrowIfCancellationRequested();

            int r = await TaskUtil.DoMicroReadOperations(async (target, pos, c) =>
            {
                if (this.InternalPosition != pos)
                {
                    BaseStream.Seek(pos, SeekOrigin.Begin);
                    this.InternalPosition = pos;
                }

                int r2 = await BaseStream.ReadAsync(target, c);

                if (r2 >= 1)
                {
                    this.InternalPosition += r2;
                }

                return r2;
            },
            data, MicroOperationSize, position, cancel);

            return r;
        }

        public async Task SetFileSizeAsync(long size, CancellationToken cancel = default)
        {
            if (size < 0) throw new ArgumentOutOfRangeException("size < 0");
            CheckIsOpened();
            cancel.ThrowIfCancellationRequested();

            BaseStream.SetLength(size);

            this.InternalFileSize = size;
            this.InternalPosition = BaseStream.Position;
        }

        public async Task WriteRandomAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
        {
            if (data.IsEmpty) return;
            CheckIsOpened();
            cancel.ThrowIfCancellationRequested();

            if (position < 0)
            {
                // Append mode
                position = this.InternalFileSize;
            }

            if (this.InternalFileSize < position)
            {
                await GetFileSizeAsync(true, cancel);

                if (this.InternalFileSize < position)
                {
                    await SetFileSizeAsync(position, cancel);
                }
            }

            await TaskUtil.DoMicroWriteOperations(async (target, pos, c) =>
            {
                if (this.InternalPosition != pos)
                {
                    BaseStream.Seek(pos, SeekOrigin.Begin);
                    this.InternalPosition = pos;
                }

                await BaseStream.WriteAsync(target, c);

                this.InternalPosition += target.Length;

                if (this.InternalFileSize < (pos + target.Length))
                    this.InternalFileSize = (pos + target.Length);
            },
            data, MicroOperationSize, position, cancel);
        }

        public void Append(ReadOnlyMemory<byte> data, CancellationToken cancel = default)
            => AppendAsync(data, cancel)._GetResult();
        public void Flush(CancellationToken cancel = default)
            => FlushAsync(cancel)._GetResult();
        public long GetFileSize(bool refresh = false, CancellationToken cancel = default)
            => GetFileSizeAsync(refresh, cancel)._GetResult();
        public long GetPhysicalSize(CancellationToken cancel = default)
            => GetPhysicalSizeAsync(cancel)._GetResult();
        public int ReadRandom(long position, Memory<byte> data, CancellationToken cancel = default)
            => ReadRandomAsync(position, data, cancel)._GetResult();
        public void SetFileSize(long size, CancellationToken cancel = default)
            => SetFileSizeAsync(size, cancel)._GetResult();
        public void WriteRandom(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
            => WriteRandomAsync(position, data, cancel)._GetResult();
    }
#pragma warning restore CS1998

    public class ConcurrentRandomAccess<T> : IRandomAccess<T>
    {
        public readonly AsyncLock TargetLock;
        public readonly IRandomAccess<T> Target;

        Exception? LastError = null;

        public ConcurrentRandomAccess(IRandomAccess<T> target)
        {
            this.Target = target;
            this.TargetLock = this.Target.SharedAsyncLock;
        }

        public AsyncLock SharedAsyncLock { get; } = new AsyncLock();

        public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;
        }

        void CheckState()
        {
            if (this.LastError != null) throw this.LastError;
            if (DisposeFlag.IsSet) throw new ObjectDisposedException("ConcurrentRandomAccess");
        }

        void SetError(Exception error)
        {
            this.LastError = error;
        }

        public async Task AppendAsync(ReadOnlyMemory<T> data, CancellationToken cancel = default)
        {
            using (await TargetLock.LockWithAwait(cancel))
            {
                CheckState();

                try
                {
                    await Target.AppendAsync(data, cancel);
                }
                catch (Exception ex)
                {
                    SetError(ex);
                    throw;
                }
            }
        }

        public async Task FlushAsync(CancellationToken cancel = default)
        {
            using (await TargetLock.LockWithAwait(cancel))
            {
                CheckState();

                try
                {
                    await Target.FlushAsync(cancel);
                }
                catch (Exception ex)
                {
                    SetError(ex);
                    throw;
                }
            }
        }

        public async Task<long> GetFileSizeAsync(bool refresh = false, CancellationToken cancel = default)
        {
            using (await TargetLock.LockWithAwait(cancel))
            {
                CheckState();

                try
                {
                    return await Target.GetFileSizeAsync(refresh, cancel);
                }
                catch (Exception ex)
                {
                    SetError(ex);
                    throw;
                }
            }
        }

        public async Task<long> GetPhysicalSizeAsync(CancellationToken cancel = default)
        {
            using (await TargetLock.LockWithAwait(cancel))
            {
                CheckState();

                try
                {
                    return await Target.GetPhysicalSizeAsync(cancel);
                }
                catch (Exception ex)
                {
                    SetError(ex);
                    throw;
                }
            }
        }

        public async Task<int> ReadRandomAsync(long position, Memory<T> data, CancellationToken cancel = default)
        {
            using (await TargetLock.LockWithAwait(cancel))
            {
                CheckState();

                try
                {
                    return await Target.ReadRandomAsync(position, data, cancel);
                }
                catch (Exception ex)
                {
                    SetError(ex);
                    throw;
                }
            }
        }

        public async Task SetFileSizeAsync(long size, CancellationToken cancel = default)
        {
            using (await TargetLock.LockWithAwait(cancel))
            {
                CheckState();

                try
                {
                    await Target.SetFileSizeAsync(size, cancel);
                }
                catch (Exception ex)
                {
                    SetError(ex);
                    throw;
                }
            }
        }

        public async Task WriteRandomAsync(long position, ReadOnlyMemory<T> data, CancellationToken cancel = default)
        {
            using (await TargetLock.LockWithAwait(cancel))
            {
                CheckState();

                try
                {
                    await Target.WriteRandomAsync(position, data, cancel);
                }
                catch (Exception ex)
                {
                    SetError(ex);
                    throw;
                }
            }
        }

        public void Append(ReadOnlyMemory<T> data, CancellationToken cancel = default) => AppendAsync(data, cancel)._GetResult();
        public void Flush(CancellationToken cancel = default) => FlushAsync(cancel)._GetResult();
        public long GetFileSize(bool refresh = false, CancellationToken cancel = default) => GetFileSizeAsync(refresh, cancel)._GetResult();
        public long GetPhysicalSize(CancellationToken cancel = default) => GetPhysicalSizeAsync(cancel)._GetResult();
        public int ReadRandom(long position, Memory<T> data, CancellationToken cancel = default) => ReadRandomAsync(position, data, cancel)._GetResult();
        public void SetFileSize(long size, CancellationToken cancel = default) => SetFileSizeAsync(size, cancel)._GetResult();
        public void WriteRandom(long position, ReadOnlyMemory<T> data, CancellationToken cancel = default) => WriteRandomAsync(position, data, cancel)._GetResult();
    }

    // Stream に対して書き込むことができる ISequentialWritable<byte> オブジェクト。Stream に対して決して Seek 操作をしない。
    public class StreamBasedSequentialWritable : SequentialWritableImpl<byte>, IDisposable, IAsyncDisposable
    {
        readonly IHolder Leak;

        public Stream BaseStream { get; }
        public bool AutoDispose { get; }

        public StreamBasedSequentialWritable(Stream baseStream, bool autoDispose = false)
        {
            try
            {
                this.BaseStream = baseStream;
                this.AutoDispose = autoDispose;

                this.Leak = LeakChecker.Enter(LeakCounterKind.StreamBasedSequentialWritable);

                this.StartAsync()._GetResult();
            }
            catch
            {
                this._DisposeSafe();
                throw;
            }
        }

        public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
        Once DisposeFlag;
        public async ValueTask DisposeAsync()
        {
            if (DisposeFlag.IsFirstCall() == false) return;
            await DisposeInternalAsync();
        }
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;
            DisposeInternalAsync()._GetResult();
        }
        async Task DisposeInternalAsync()
        {
            this.Leak._DisposeSafe();
            
            if (this.AutoDispose)
            {
                await BaseStream._DisposeSafeAsync();
            }
        }

        protected override async Task AppendImplAsync(ReadOnlyMemory<byte> data, long hintCurrentLength, long hintNewLength, CancellationToken cancel = default)
        {
            await BaseStream.WriteAsync(data, cancel);
        }

        protected override Task CompleteImplAsync(bool ok, long hintCurrentLength, CancellationToken cancel = default)
        {
            return Task.CompletedTask;
        }

        protected override async Task FlushImplAsync(long hintCurrentLength, CancellationToken cancel = default)
        {
            await BaseStream.FlushAsync(cancel);
        }

        protected override Task StartImplAsync(CancellationToken cancel = default)
        {
            return Task.CompletedTask;
        }
    }

    // ISequentialWritable<byte> に対して書き込むことができる Stream オブジェクト。シーケンシャルでない操作 (現在の番地以外へのシークなど) を要求すると例外が発生する
    public class SequentialWritableBasedStream : Stream
    {
        public ISequentialWritable<byte> Target { get; }

        long PositionCache;

        readonly Func<Task>? OnDisposing;

        public SequentialWritableBasedStream(ISequentialWritable<byte> target, Func<Task>? onDisposing = null)
        {
            Target = target;
            this.PositionCache = target.CurrentPosition;
            this.OnDisposing = onDisposing;
        }

        Once DisposeFlag;
        public override async ValueTask DisposeAsync()
        {
            try
            {
                if (DisposeFlag.IsFirstCall() == false) return;
                await DisposeInternalAsync();
            }
            finally
            {
                await base.DisposeAsync();
            }
        }
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;
                DisposeInternalAsync()._GetResult();
            }
            finally { base.Dispose(disposing); }
        }
        Task DisposeInternalAsync()
        {
            if (OnDisposing != null)
                OnDisposing();

            return Task.CompletedTask;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => Target.CurrentPosition;

        public override long Position
        {
            get => Target.CurrentPosition;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            Target.Flush();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.End:
                    if (offset == 0)
                    {
                        return PositionCache;
                    }
                    else
                    {
                        throw new ArgumentOutOfRangeException(nameof(offset), "Seek is not suppoered.");
                    }

                case SeekOrigin.Begin:
                    if (offset == PositionCache)
                    {
                        return PositionCache;
                    }
                    else
                    {
                        throw new ArgumentOutOfRangeException(nameof(offset), "Seek is not suppoered.");
                    }

                case SeekOrigin.Current:
                    if (offset == 0)
                    {
                        return PositionCache;
                    }
                    else
                    {
                        throw new ArgumentOutOfRangeException(nameof(offset), "Seek is not suppoered.");
                    }

                default:
                    throw new ArgumentOutOfRangeException(nameof(origin));
            }
        }

        public override void SetLength(long value)
        {
            if (PositionCache == value)
                return;
            else
                throw new ArgumentOutOfRangeException(nameof(value), "Changing the length is not supported.");
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            this.Flush();
            return Task.CompletedTask;
        }

        override public int Read(Span<byte> buffer)
            => throw new NotSupportedException();

        override public void Write(ReadOnlySpan<byte> buffer)
        {
            Target.Append(buffer.ToArray());
        }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        override public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Target.AppendAsync(buffer.AsMemory(offset, count), cancellationToken);
        }

        override public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Target.AppendAsync(buffer, cancellationToken);
        }
    }

    public interface IHasError
    {
        Exception? LastError { get; }
        bool HasError => LastError != null;

        public void ThrowIfError(Exception? overrideError = null)
        {
            Exception? lastError = this.LastError;
            if (lastError != null)
            {
                Exception throwError = overrideError ?? lastError;
                throw throwError;
            }
        }
    }

    public interface ISequentialWritable<T> : IHasError
    {
        public long CurrentPosition { get; }
        public bool NeedFlush { get; }

        Task<long> AppendAsync(ReadOnlyMemory<T> data, CancellationToken cancel = default);
        Task<long> FlushAsync(CancellationToken cancel = default);

        long Append(ReadOnlyMemory<T> data, CancellationToken cancel = default)
            => AppendAsync(data, cancel)._GetResult();
        long Flush(CancellationToken cancel = default)
            => FlushAsync(cancel)._GetResult();
    }

    public static class ISequentialWritableExtension
    {
        public static StreamBasedSequentialWritable _GetSequentialWritable(this Stream baseStream, bool autoDispose = false)
            => new StreamBasedSequentialWritable(baseStream, autoDispose);

        public static WriteOnlyStreamBasedRandomAccess _GetWriteOnlyStreamBasedRandomAccess(this Stream baseStream, bool autoDispose = false)
            => new WriteOnlyStreamBasedRandomAccess(baseStream, autoDispose);

        public static SequentialWritableBasedStream GetStream(this ISequentialWritable<byte> writable, Func<Task>? onDisposing = null)
            => new SequentialWritableBasedStream(writable, onDisposing);

        public static async Task<long> CopyFromBufferAsync(this ISequentialWritable<byte> dest, IBuffer<byte> src, CancellationToken cancel = default)
        {
            long totalSize = 0;

            int bufferSize = CoresConfig.BufferSizes.FileCopyBufferSize;

            long len = src.LongLength;
            long pos = src.LongPosition;
            if (len <= pos)
                bufferSize = 1;
            else
            {
                long tmp = len - pos;
                if (tmp > 0)
                    bufferSize = (int)Math.Min((long)bufferSize, tmp);
            }

            for (; ; )
            {
                ReadOnlyMemory<byte> buffer = src.ReadAsMemory(bufferSize, true);

                if (buffer.Length == 0)
                {
                    break;
                }

                totalSize += buffer.Length;

                await dest.AppendAsync(buffer, cancel);
            }

            return totalSize;
        }
        public static Task<long> CopyToSequentialWritableAsync(this IBuffer<byte> src, ISequentialWritable<byte> dest, CancellationToken cancel = default)
            => CopyFromBufferAsync(dest, src, cancel);

        public static async Task<long> CopyFromStreamAsync(this ISequentialWritable<byte> dest, Stream src, CancellationToken cancel = default)
        {
            long totalSize = 0;

            int bufferSize = CoresConfig.BufferSizes.FileCopyBufferSize;

            if (src.CanSeek)
            {
                long len = src.Length;
                long pos = src.Position;
                if (len <= pos)
                    bufferSize = 1;
                else
                {
                    long tmp = len - pos;
                    if (tmp > 0)
                        bufferSize = (int)Math.Min((long)bufferSize, tmp);
                }
            }

            byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);

            try
            {
                for (; ; )
                {
                    int num = await src.ReadAsync(new Memory<byte>(buffer), cancel);
                    if (num == 0)
                    {
                        break;
                    }

                    totalSize += num;

                    await dest.AppendAsync(new ReadOnlyMemory<byte>(buffer, 0, num), cancel);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, false);
            }

            return totalSize;
        }
        public static Task<long> CopyToSequentialWritableAsync(this Stream src, ISequentialWritable<byte> dest, CancellationToken cancel = default)
            => CopyFromStreamAsync(dest, src, cancel);
    }

    // ISequentialWritable<T> を容易に実装するためのクラス
    public abstract class SequentialWritableImpl<T> : ISequentialWritable<T>
    {
        protected abstract Task StartImplAsync(CancellationToken cancel = default);
        protected abstract Task AppendImplAsync(ReadOnlyMemory<T> data, long hintCurrentLength, long hintNewLength, CancellationToken cancel = default);
        protected abstract Task FlushImplAsync(long hintCurrentLength, CancellationToken cancel = default);
        protected abstract Task CompleteImplAsync(bool ok, long hintCurrentLength, CancellationToken cancel = default);

        public long CurrentPosition { get; private set; }

        public bool NeedFlush { get; private set; }

        public Exception? LastError { get; private set; }

        bool IsStarted = false;
        bool IsCompleted = false;

        public async Task StartAsync(CancellationToken cancel = default)
        {
            if (this.LastError != null) throw this.LastError;

            cancel.ThrowIfCancellationRequested();

            if (IsStarted) throw new CoresException("Already started.");

            try
            {
                await StartImplAsync(cancel);

                IsStarted = true;
            }
            catch (Exception ex)
            {
                this.LastError = ex;
                throw;
            }
        }

        public async Task<long> AppendAsync(ReadOnlyMemory<T> data, CancellationToken cancel = default)
        {
            if (this.LastError != null) throw this.LastError;

            cancel.ThrowIfCancellationRequested();

            if (data.Length == 0) return this.CurrentPosition;

            if (IsStarted == false) throw new CoresException("Not started.");

            try
            {
                long newPosition = CurrentPosition + data.Length;

                await AppendImplAsync(data, CurrentPosition, newPosition, cancel);

                CurrentPosition = newPosition;

                NeedFlush = true;

                return this.CurrentPosition;
            }
            catch (Exception ex)
            {
                this.LastError = ex;
                throw;
            }
        }

        public async Task<long> FlushAsync(CancellationToken cancel = default)
        {
            if (this.LastError != null) throw this.LastError;

            cancel.ThrowIfCancellationRequested();

            if (IsStarted == false) throw new CoresException("Not started.");

            if (NeedFlush)
            {
                try
                {
                    await FlushImplAsync(this.CurrentPosition, cancel);

                    NeedFlush = false;
                }
                catch (Exception ex)
                {
                    this.LastError = ex;
                    throw;
                }
            }

            return this.CurrentPosition;
        }

        public async Task<long> CompleteAsync(bool ok, CancellationToken cancel = default)
        {
            if (this.LastError != null) throw this.LastError;

            cancel.ThrowIfCancellationRequested();

            if (IsCompleted)
            {
                throw new CoresException("Already completed.");
            }

            if (IsStarted == false) throw new CoresException("Not started.");

            if (ok)
            {
                await FlushAsync(cancel);
            }

            await CompleteImplAsync(ok, this.CurrentPosition, cancel);

            IsCompleted = true;

            return this.CurrentPosition;
        }
    }

    // IRandomAccess インターフェイスのターゲットに対して追記のみの書き込みを提供するクラス
    public class SequentialWritable<T> : SequentialWritableImpl<T>
    {
        public IRandomAccess<T> Target { get; }

        public Func<bool, Task>? OnCompleted;

        public SequentialWritable(IRandomAccess<T> target, Func<bool, Task>? onCompleted = null)
        {
            this.Target = target;
            this.OnCompleted = onCompleted;

            this.StartAsync()._GetResult();
        }

        protected override Task AppendImplAsync(ReadOnlyMemory<T> data, long hintCurrentLength, long hintNewLength, CancellationToken cancel = default)
            => Target.WriteRandomAsync(hintCurrentLength, data, cancel);

        protected override Task FlushImplAsync(long hintCurrentLength, CancellationToken cancel = default)
            => Target.FlushAsync(cancel);

        protected override async Task CompleteImplAsync(bool ok, long hintCurrentLength, CancellationToken cancel = default)
        {
            if (this.OnCompleted != null)
            {
                await this.OnCompleted(ok);
            }
        }

        protected override Task StartImplAsync(CancellationToken cancel = default)
            => Task.CompletedTask;
    }

    public interface IRandomAccess<T> : IDisposable
    {
        Task<int> ReadRandomAsync(long position, Memory<T> data, CancellationToken cancel = default);
        int ReadRandom(long position, Memory<T> data, CancellationToken cancel = default)
            => ReadRandomAsync(position, data, cancel)._GetResult();

        Task WriteRandomAsync(long position, ReadOnlyMemory<T> data, CancellationToken cancel = default);
        void WriteRandom(long position, ReadOnlyMemory<T> data, CancellationToken cancel = default)
            => WriteRandomAsync(position, data, cancel)._GetResult();

        Task AppendAsync(ReadOnlyMemory<T> data, CancellationToken cancel = default);
        void Append(ReadOnlyMemory<T> data, CancellationToken cancel = default)
            => AppendAsync(data, cancel)._GetResult();

        Task<long> GetFileSizeAsync(bool refresh = false, CancellationToken cancel = default);
        long GetFileSize(bool refresh = false, CancellationToken cancel = default)
            => GetFileSizeAsync(refresh, cancel)._GetResult();

        Task<long> GetPhysicalSizeAsync(CancellationToken cancel = default);
        long GetPhysicalSize(CancellationToken cancel = default)
            => GetPhysicalSizeAsync(cancel)._GetResult();

        Task SetFileSizeAsync(long size, CancellationToken cancel = default);
        void SetFileSize(long size, CancellationToken cancel = default)
            => SetFileSizeAsync(size, cancel)._GetResult();

        Task FlushAsync(CancellationToken cancel = default);
        void Flush(CancellationToken cancel = default)
            => FlushAsync(cancel)._GetResult();

        AsyncLock SharedAsyncLock { get; }
    }

    public class RandomAccessHandle : IRandomAccess<byte>, IDisposable
    {
        readonly RefCounterObjectHandle<FileBase>? Ref;
        readonly FileBase File;
        bool DisposeFile = false;

        public AsyncLock SharedAsyncLock { get; } = new AsyncLock();

        public RandomAccessHandle(FileBase fileHandle, bool disposeObject = false)
        {
            this.Ref = null;
            this.File = fileHandle;
            this.DisposeFile = disposeObject;
        }

        public RandomAccessHandle(RefCounterObjectHandle<FileBase> objHandle)
        {
            this.Ref = objHandle;
            this.File = this.Ref.Object;
        }

        public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;

            if (Ref != null)
                this.Ref._DisposeSafe();

            if (DisposeFile)
                this.File._DisposeSafe();
        }

        public FileStream GetStream() => this.File.GetStream();

        public Task<int> ReadRandomAsync(long position, Memory<byte> data, CancellationToken cancel = default)
            => this.File.ReadRandomAsync(position, data, cancel);
        public int ReadRandom(long position, Memory<byte> data, CancellationToken cancel = default)
            => ReadRandomAsync(position, data, cancel)._GetResult();

        public Task WriteRandomAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
            => this.File.WriteRandomAsync(position, data, cancel);
        public void WriteRandom(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
            => WriteRandomAsync(position, data, cancel)._GetResult();

        public Task AppendAsync(ReadOnlyMemory<byte> data, CancellationToken cancel = default)
            => this.File.AppendAsync(data, cancel);
        public void Append(ReadOnlyMemory<byte> data, CancellationToken cancel = default)
            => this.AppendAsync(data, cancel)._GetResult();

        public Task<long> GetFileSizeAsync(bool refresh = false, CancellationToken cancel = default)
            => this.File.GetFileSizeAsync(refresh, cancel);
        public long GetFileSize(bool refresh = false, CancellationToken cancel = default) => GetFileSizeAsync(refresh, cancel)._GetResult();

        public Task SetFileSizeAsync(long size, CancellationToken cancel = default)
            => this.File.SetFileSizeAsync(size, cancel);
        public void SetFileSize(long size, CancellationToken cancel = default) => SetFileSizeAsync(size, cancel)._GetResult();

        public Task FlushAsync(CancellationToken cancel = default)
            => this.File.FlushAsync(cancel);
        public void Flush(CancellationToken cancel = default) => FlushAsync(cancel)._GetResult();

        public Task<long> GetPhysicalSizeAsync(CancellationToken cancel = default)
            => this.File.GetPhysicalSizeAsync(cancel);
        public long GetPhysicalSize(CancellationToken cancel = default) => GetPhysicalSizeAsync(cancel)._GetResult();
    }

    public abstract class FileBase : IDisposable, IAsyncClosable, IRandomAccess<byte>
    {
        public FileParameters FileParams { get; }
        public virtual string FinalPhysicalPath => throw new NotImplementedException();
        public abstract bool IsOpened { get; }
        public abstract Exception? LastError { get; protected set; }
        public FastEventListenerList<FileBase, FileObjectEventType> EventListeners { get; }
            = new FastEventListenerList<FileBase, FileObjectEventType>();

        public AsyncLock SharedAsyncLock { get; } = new AsyncLock();

        IHolder Leak;

        protected FileBase(FileParameters fileParams)
        {
            this.FileParams = fileParams;
            this.Leak = LeakChecker.Enter(LeakCounterKind.FileBaseObject);
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
        public virtual long GetPhysicalSize(CancellationToken cancel = default) => GetPhysicalSizeAsync(cancel)._GetResult();

        public void Flush(CancellationToken cancel = default) => FlushAsync(cancel)._GetResult();
        public abstract Task CloseAsync();


        public FileStream GetStream(bool disposeObject, long? initialPosition = null) => FileBaseStream.CreateFromFileObject(this, disposeObject, initialPosition);
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

        public int Read(Memory<byte> data) => ReadAsync(data)._GetResult();
        public int ReadRandom(long position, Memory<byte> data, CancellationToken cancel = default)
            => ReadRandomAsync(position, data, cancel)._GetResult();

        public void Write(ReadOnlyMemory<byte> data, CancellationToken cancel = default) => WriteAsync(data, cancel)._GetResult();
        public void WriteRandom(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
            => WriteRandomAsync(position, data, cancel)._GetResult();

        public Task AppendAsync(ReadOnlyMemory<byte> data, CancellationToken cancel = default)
            => this.WriteRandomAsync(-1, data, cancel);
        public void Append(ReadOnlyMemory<byte> data, CancellationToken cancel = default)
            => this.AppendAsync(data, cancel)._GetResult();

        public long GetCurrentPosition(CancellationToken cancel = default) => GetCurrentPositionAsync()._GetResult();
        public long Seek(long offset, SeekOrigin origin, CancellationToken cancel = default)
            => SeekAsync(offset, origin, cancel)._GetResult();

        public Task<long> SeekToBeginAsync(CancellationToken cancel = default) => SeekAsync(0, SeekOrigin.Begin, cancel);
        public Task<long> SeekToEndAsync(CancellationToken cancel = default) => SeekAsync(0, SeekOrigin.End, cancel);
        public long SeekToBegin(CancellationToken cancel = default) => SeekToBeginAsync(cancel)._GetResult();
        public long SeekToEnd(CancellationToken cancel = default) => SeekToEndAsync(cancel)._GetResult();

        public long GetFileSize(bool refresh = false, CancellationToken cancel = default) => GetFileSizeAsync(refresh, cancel)._GetResult();
        public void SetFileSize(long size, CancellationToken cancel = default) => SetFileSizeAsync(size, cancel)._GetResult();

        public abstract Task FlushAsync(CancellationToken cancel = default);
        public void Close() => Dispose();

        public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;

            try
            {
                CloseAsync()._GetResult();
            }
            catch { }

            this.Leak._DisposeSafe();
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
            if (FileParams.Flags.Bit(FileFlags.RandomAccessOnly))
                throw new FileException(this.FileParams.Path, "The file object is in RandomAccessOnly mode.");
        }
    }
}
