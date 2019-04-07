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
using System.Diagnostics;

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
            public static readonly Copenhagen<int> DefaultMicroOperationSize = 8 * 1024 * 1024; // 8MB
        }

        public static partial class FileUtilSettings
        {
            public static readonly Copenhagen<int> FileCopyBufferSize = 1 * 1024 * 1024; // 1MB
        }
    }

    class FileSystemException : Exception
    {
        public FileSystemException(string message) : base(message) { }
    }

    abstract class FileObjectBase : FileBase
    {
        public FileSystemBase FileSystem { get; }
        public override bool IsOpened => !this.ClosedFlag.IsSet;
        public override Exception LastError { get; protected set; } = null;

        public int MicroOperationSize { get; set; } = AppConfig.FileSystemSettings.DefaultMicroOperationSize.Value;

        long InternalPosition = 0;
        long InternalFileSize = 0;
        CancellationTokenSource CancelSource = new CancellationTokenSource();
        CancellationToken CancelToken => CancelSource.Token;

        AsyncLock AsyncLockObj = new AsyncLock();

        protected FileObjectBase(FileSystemBase fileSystem, FileParameters fileParams) : base(fileParams)
        {
            this.FileSystem = fileSystem;
        }

        public override string ToString() => $"FileObject('{FileParams.Path}')";

        protected override async Task InternalInitAsync(CancellationToken cancel = default)
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

                await base.InternalInitAsync(operationCancel);
            }
        }

        protected abstract Task<int> ReadImplAsync(long position, Memory<byte> data, CancellationToken cancel = default);
        protected abstract Task WriteImplAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default);
        protected abstract Task<long> GetFileSizeImplAsync(CancellationToken cancel = default);
        protected abstract Task SetFileSizeImplAsync(long size, CancellationToken cancel = default);
        protected abstract Task<long> GetCurrentPositionImplAsync(CancellationToken cancel = default);
        protected abstract Task FlushImplAsync(CancellationToken cancel = default);
        protected abstract Task CloseImplAsync();

        Once ClosedFlag;

        public sealed override async Task<int> ReadAsync(Memory<byte> data, CancellationToken cancel = default)
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
                            if (this.FileParams.Flags.Bit(FileOperationFlags.NoPartialRead))
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

                                if (this.FileParams.Flags.Bit(FileOperationFlags.NoPartialRead))
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

        public sealed override async Task<int> ReadRandomAsync(long position, Memory<byte> data, CancellationToken cancel = default)
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
                            if (this.FileParams.Flags.Bit(FileOperationFlags.NoPartialRead))
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

                                if (this.FileParams.Flags.Bit(FileOperationFlags.NoPartialRead))
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

        public sealed override async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancel = default)
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

        public sealed override async Task WriteRandomAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
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

        public sealed override async Task<long> SeekAsync(long offset, SeekOrigin origin, CancellationToken cancel = default)
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

        public sealed override Task<long> GetCurrentPositionAsync(CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();

            CheckSequentialAccessProhibited();

            return Task.FromResult(this.InternalPosition);
        }

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

        public sealed override async Task<long> GetFileSizeAsync(bool refresh = false, CancellationToken cancel = default)
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

        public sealed override async Task SetFileSizeAsync(long size, CancellationToken cancel = default)
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

        public sealed override async Task FlushAsync(CancellationToken cancel = default)
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

        public sealed override async Task CloseAsync()
        {
            CancelSource.TryCancelNoBlock();

            if (ClosedFlag.IsSet) return;

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

        Once DisposeFlag;
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;
                CancelSource.TryCancelNoBlock();
            }
            finally { base.Dispose(disposing); }
        }
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

    [Flags]
    enum SpecialFileNameKind
    {
        Normal = 0,
        CurrentDirectory = 1,
        ParentDirectory = 2,
    }

    class FileSystemObjectPool : ObjectPoolBase<FileBase, FileOperationFlags>
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

        protected override async Task<FileBase> OpenImplAsync(string name, FileOperationFlags flags, CancellationToken cancel)
        {
            if (this.IsWriteMode == false)
            {
                string path = name.Substring(2);
                path = await FileSystem.NormalizePathAsync(path, cancel);

                return await FileSystem.OpenAsync(path, cancel: cancel, flags: this.DefaultFileOperationFlags | flags);
            }
            else
            {
                string path = name.Substring(2);
                path = await FileSystem.NormalizePathAsync(path, cancel);

                return await FileSystem.OpenOrCreateAsync(path, cancel: cancel, flags: this.DefaultFileOperationFlags | flags);
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
        List<FileBase> OpenedHandleList = new List<FileBase>();
        RefInt CriticalCounter = new RefInt();
        CancellationTokenSource CancelSource = new CancellationTokenSource();

        public FileSystemObjectPool ObjectPoolForRead { get; }
        public FileSystemObjectPool ObjectPoolForWrite { get; }

        public LargeFileSystem LargeFileSystem { get; }

        public FileSystemBase(AsyncCleanuperLady lady, FileSystemPathInterpreter fileSystemMetrics) : base(lady)
        {
            try
            {
                this.PathInterpreter = fileSystemMetrics;
                DirectoryWalker = new DirectoryWalker(this);

                ObjectPoolForRead = new FileSystemObjectPool(this, false, AppConfig.FileSystemSettings.PooledHandleLifetime.Value);
                ObjectPoolForWrite = new FileSystemObjectPool(this, true, AppConfig.FileSystemSettings.PooledHandleLifetime.Value);

                //LargeFileSystem = new LargeFileSystem(lady, this, new LargeFileSystemParams(
            }
            catch
            {
                Lady.DisposeAllSafe();
                throw;
            }
        }

        public async Task<RandomAccessHandle> GetRandomAccessHandleAsync(string fileName, bool writeMode, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
        {
            FileSystemObjectPool pool = writeMode ? ObjectPoolForWrite : ObjectPoolForRead;

            RefObjectHandle<FileBase> refFileBase = await pool.OpenOrGetAsync(fileName, flags, cancel);

            return new RandomAccessHandle(refFileBase);
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

                FileBase[] fileHandles;

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

        protected abstract Task CreateDirectoryImplAsync(string directoryPath, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default);
        protected abstract Task DeleteDirectoryImplAsync(string directoryPath, bool recursive, CancellationToken cancel = default);
        protected abstract Task<FileSystemEntity[]> EnumDirectoryImplAsync(string directoryPath, CancellationToken cancel = default);

        protected abstract Task<FileMetadata> GetFileMetadataImplAsync(string path, CancellationToken cancel = default);
        protected abstract Task SetFileMetadataImplAsync(string path, FileMetadata metadata, CancellationToken cancel = default);

        protected abstract Task<FileMetadata> GetDirectoryMetadataImplAsync(string path, CancellationToken cancel = default);
        protected abstract Task SetDirectoryMetadataImplAsync(string path, FileMetadata metadata, CancellationToken cancel = default);


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

                    if (option.Mode == FileMode.Append || option.Mode == FileMode.Create || option.Mode == FileMode.CreateNew ||
                        option.Mode == FileMode.OpenOrCreate || option.Mode == FileMode.Truncate)
                    {
                        if (option.Access.Bit(FileAccess.Write) == false)
                        {
                            throw new ArgumentException("The Access member must contain the FileAccess.Write bit when opening a file with create mode.");
                        }

                        if (option.Flags.Bit(FileOperationFlags.AutoCreateDirectoryOnFileCreation))
                        {
                            string dirName = this.PathInterpreter.GetDirectoryName(option.Path);
                            if (dirName.IsFilled())
                            {
                                await CreateDirectoryImplAsync(dirName, option.Flags, opCancel);
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

        void FileEventListenerCallback(FileBase obj, FileObjectEventType eventType, object userState)
        {
            switch (eventType)
            {
                case FileObjectEventType.Closed:
                    lock (LockObj)
                    {
                        OpenedHandleList.Remove(obj as FileObjectBase);
                    }
                    break;
            }
        }

        public FileObjectBase CreateFile(FileParameters option, CancellationToken cancel = default)
            => CreateFileAsync(option, cancel).GetResult();

        public Task<FileObjectBase> CreateAsync(string path, bool noShare = false, FileOperationFlags flags = FileOperationFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default)
            => CreateFileAsync(new FileParameters(path, doNotOverwrite ? FileMode.CreateNew : FileMode.Create, FileAccess.ReadWrite, noShare ? FileShare.None : FileShare.Read, flags), cancel);

        public FileObjectBase Create(string path, bool noShare = false, FileOperationFlags flags = FileOperationFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default)
            => CreateAsync(path, noShare, flags, doNotOverwrite, cancel).GetResult();

        public Task<FileObjectBase> OpenAsync(string path, bool writeMode = false, bool noShare = false, bool readLock = false, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
            => CreateFileAsync(new FileParameters(path, FileMode.Open, (writeMode ? FileAccess.ReadWrite : FileAccess.Read),
                (noShare ? FileShare.None : ((writeMode || readLock) ? FileShare.Read : (FileShare.ReadWrite | FileShare.Delete))), flags), cancel);

        public FileObjectBase Open(string path, bool writeMode = false, bool noShare = false, bool readLock = false, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
            => OpenAsync(path, writeMode, noShare, readLock, flags, cancel).GetResult();

        public Task<FileObjectBase> OpenOrCreateAsync(string path, bool noShare = false, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
            => CreateFileAsync(new FileParameters(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, noShare ? FileShare.None : FileShare.Read, flags), cancel);

        public FileObjectBase OpenOrCreate(string path, bool noShare = false, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
            => OpenOrCreateAsync(path, noShare, flags, cancel).GetResult();

        public Task<FileObjectBase> OpenOrCreateAppendAsync(string path, bool noShare = false, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
            => CreateFileAsync(new FileParameters(path, FileMode.Append, FileAccess.Write, noShare ? FileShare.None : FileShare.Read, flags), cancel);

        public FileObjectBase OpenOrCreateAppend(string path, bool noShare = false, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
            => OpenOrCreateAsync(path, noShare, flags, cancel).GetResult();

        public async Task WriteToFileAsync(string path, Memory<byte> srcMemory, FileOperationFlags flags = FileOperationFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default)
        {
            using (var file = await CreateAsync(path, false, flags, doNotOverwrite, cancel))
            {
                try
                {
                    await file.WriteAsync(srcMemory, cancel);
                }
                finally
                {
                    await file.CloseAsync();
                }
            }
        }
        public void WriteToFile(string path, Memory<byte> data, FileOperationFlags flags = FileOperationFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default)
            => WriteToFileAsync(path, data, flags, doNotOverwrite, cancel).GetResult();

        public async Task AppendToFileAsync(string path, Memory<byte> srcMemory, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
        {
            using (var file = await OpenOrCreateAppendAsync(path, false, flags, cancel))
            {
                try
                {
                    await file.WriteAsync(srcMemory, cancel);
                }
                finally
                {
                    await file.CloseAsync();
                }
            }
        }
        public void AppendToFile(string path, Memory<byte> srcMemory, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
            => AppendToFileAsync(path, srcMemory, flags, cancel).GetResult();

        public async Task<int> ReadFromFileAsync(string path, Memory<byte> destMemory, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
        {
            using (var file = await OpenAsync(path, false, false, false, flags, cancel))
            {
                try
                {
                    return await file.ReadAsync(destMemory, cancel);
                }
                finally
                {
                    await file.CloseAsync();
                }
            }
        }
        public int ReadFromFile(string path, Memory<byte> destMemory, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
            => ReadFromFileAsync(path, destMemory, flags, cancel).GetResult();

        public async Task<Memory<byte>> ReadFromFileAsync(string path, int maxSize = int.MaxValue, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
        {
            using (var file = await OpenAsync(path, false, false, false, flags, cancel))
            {
                try
                {
                    return await file.GetStream().ReadToEndAsync(maxSize, cancel);
                }
                finally
                {
                    await file.CloseAsync();
                }
            }
        }
        public Memory<byte> ReadFromFile(string path, int maxSize = int.MaxValue, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
            => ReadFromFileAsync(path, maxSize, flags, cancel).GetResult();

        public async Task CreateDirectoryAsync(string path, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
        {
            using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken opCancel, cancel, this.CancelSource.Token))
            {
                using (TaskUtil.EnterCriticalCounter(CriticalCounter))
                {
                    CheckNotDisposed();

                    path = await NormalizePathAsync(path, opCancel);

                    opCancel.ThrowIfCancellationRequested();

                    await CreateDirectoryImplAsync(path, flags, opCancel);
                }
            }
        }

        public void CreateDirectory(string path, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
            => CreateDirectoryAsync(path, flags, cancel).GetResult();

        public async Task DeleteDirectoryAsync(string path, bool recursive = false, CancellationToken cancel = default)
        {
            using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken opCancel, cancel, this.CancelSource.Token))
            {
                using (TaskUtil.EnterCriticalCounter(CriticalCounter))
                {
                    CheckNotDisposed();

                    path = await NormalizePathAsync(path, opCancel);

                    opCancel.ThrowIfCancellationRequested();

                    await DeleteDirectoryImplAsync(path, recursive, opCancel);
                }
            }
        }

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

        public async Task<FileMetadata> GetFileMetadataAsync(string path, CancellationToken cancel = default)
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
        public FileMetadata GetFileMetadata(string path, CancellationToken cancel = default)
            => GetFileMetadataAsync(path, cancel).GetResult();

        public async Task<FileMetadata> GetDirectoryMetadataAsync(string path, CancellationToken cancel = default)
        {
            using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken opCancel, cancel, this.CancelSource.Token))
            {
                using (TaskUtil.EnterCriticalCounter(CriticalCounter))
                {
                    CheckNotDisposed();

                    cancel.ThrowIfCancellationRequested();

                    path = await NormalizePathImplAsync(path, opCancel);

                    return await GetDirectoryMetadataImplAsync(path, cancel);
                }
            }
        }
        public FileMetadata GetDirectoryMetadata(string path, CancellationToken cancel = default)
            => GetDirectoryMetadataAsync(path, cancel).GetResult();

        public virtual async Task<bool> IsFileExistsAsync(string path, CancellationToken cancel = default)
        {
            try
            {
                var metaData = await GetFileMetadataAsync(path);

                if ((metaData.Attributes ?? FileAttributes.Normal).Bit(FileAttributes.Directory) == false)
                {
                    return true;
                }
            }
            catch { }

            return false;
        }

        public bool IsFileExists(string path, CancellationToken cancel = default)
            => IsFileExistsAsync(path, cancel).GetResult();

        public async Task SetFileMetadataAsync(string path, FileMetadata metadata, CancellationToken cancel = default)
        {
            using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken opCancel, cancel, this.CancelSource.Token))
            {
                using (TaskUtil.EnterCriticalCounter(CriticalCounter))
                {
                    CheckNotDisposed();

                    cancel.ThrowIfCancellationRequested();

                    path = await NormalizePathImplAsync(path, opCancel);

                    await SetFileMetadataImplAsync(path, metadata, opCancel);
                }
            }
        }
        public void SetFileMetadata(string path, FileMetadata metadata, CancellationToken cancel = default)
            => SetFileMetadataAsync(path, metadata, cancel).GetResult();

        public async Task SetDirectoryMetadataAsync(string path, FileMetadata metadata, CancellationToken cancel = default)
        {
            using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken opCancel, cancel, this.CancelSource.Token))
            {
                using (TaskUtil.EnterCriticalCounter(CriticalCounter))
                {
                    CheckNotDisposed();

                    cancel.ThrowIfCancellationRequested();

                    path = await NormalizePathImplAsync(path, opCancel);

                    await SetDirectoryMetadataImplAsync(path, metadata, opCancel);
                }
            }
        }
        public void SetDirectoryMetadata(string path, FileMetadata metadata, CancellationToken cancel = default)
            => SetDirectoryMetadataAsync(path, metadata, cancel).GetResult();

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

        public async Task CopyFileAsync(string srcPath, string destPath, CopyFileParams param = null, CancellationToken cancel = default, FileSystemBase destFileSystem = null)
        {
            if (destFileSystem == null) destFileSystem = this;

            await FileUtil.CopyFileAsync(this, srcPath, destFileSystem, destPath, param, cancel);
        }
        public void CopyFile(string srcPath, string destPath, CopyFileParams param = null, CancellationToken cancel = default, FileSystemBase destFileSystem = null)
            => CopyFileAsync(srcPath, destPath, param, cancel, destFileSystem).GetResult();

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

    class CopyFileParams
    {
        public static FileMetadataCopier DefaultMetadataCopier { get; } = new FileMetadataCopier(FileMetadataCopyMode.Default);

        public bool Overwrite { get; }
        public FileOperationFlags Flags { get; }
        public FileMetadataCopier MetadataCopier { get; }
        public int BufferSize { get; }
        public bool AsyncCopy { get; }

        public ProgressReporterFactoryBase ProgressReporterFactory { get; }

        public static ProgressReporterFactoryBase NullReporterFactory { get; } = new NullReporterFactory();
        public static ProgressReporterFactoryBase ConsoleReporterFactory { get; } = new ProgressFileProcessingReporterFactory(ProgressReporterOutputs.Console);
        public static ProgressReporterFactoryBase DebugReporterFactory { get; } = new ProgressFileProcessingReporterFactory(ProgressReporterOutputs.Debug);

        public CopyFileParams(bool overwrite = false, FileOperationFlags flags = FileOperationFlags.None, FileMetadataCopier metadataCopier = null, int bufferSize = 0, bool asyncCopy = true,
            ProgressReporterFactoryBase reporterFactory = null)
        {
            if (metadataCopier == null) metadataCopier = DefaultMetadataCopier;
            if (bufferSize <= 0) bufferSize = AppConfig.FileUtilSettings.FileCopyBufferSize.Value;
            if (reporterFactory == null) reporterFactory = NullReporterFactory;

            this.Overwrite = overwrite;
            this.Flags = flags;
            this.MetadataCopier = metadataCopier;
            this.BufferSize = bufferSize;
            this.AsyncCopy = asyncCopy;
            this.ProgressReporterFactory = reporterFactory;
        }
    }

    static class FileUtil
    {
        public static async Task CopyFileAsync(FileSystemBase srcFileSystem, string srcPath, FileSystemBase destFileSystem, string destPath,
            CopyFileParams param = null, object state = null, CancellationToken cancel = default)
        {
            if (param == null)
                param = new CopyFileParams();

            srcPath = await srcFileSystem.NormalizePathAsync(srcPath, cancel);
            destPath = await destFileSystem.NormalizePathAsync(destPath, cancel);

            if (srcFileSystem == destFileSystem)
                if (srcFileSystem.PathInterpreter.PathStringComparer.Equals(srcPath, destPath))
                    throw new FileException(destPath, "Both source and destination is the same file.");

            using (ProgressReporterBase reporter = param.ProgressReporterFactory.CreateNewReporter($"Copying '{srcFileSystem.PathInterpreter.GetFileName(srcPath)}'", state))
            {
                using (var srcFile = await srcFileSystem.OpenAsync(srcPath, flags: param.Flags, cancel: cancel))
                {
                    try
                    {
                        FileMetadata srcFileMetadata = await srcFileSystem.GetFileMetadataAsync(srcPath, cancel);

                        bool destFileExists = await destFileSystem.IsFileExistsAsync(destPath, cancel);

                        using (var destFile = await destFileSystem.CreateAsync(destPath, flags: param.Flags, doNotOverwrite: !param.Overwrite, cancel: cancel))
                        {
                            try
                            {
                                reporter.ReportProgress(new ProgressData(0, srcFileMetadata.Size));

                                long copiedSize = await CopyBetweenHandleAsync(srcFile, destFile, param, reporter, srcFileMetadata.Size, cancel);

                                reporter.ReportProgress(new ProgressData(copiedSize, copiedSize, true));

                                await destFile.CloseAsync();

                                try
                                {
                                    await destFileSystem.SetFileMetadataAsync(destPath, param.MetadataCopier.Copy(srcFileMetadata), cancel);
                                }
                                catch (Exception ex)
                                {
                                    Con.WriteDebug(ex);
                                }
                            }
                            catch
                            {
                                if (destFileExists == false)
                                {
                                    try
                                    {
                                        await destFileSystem.DeleteFileAsync(destPath);
                                    }
                                    catch { }
                                }

                                throw;
                            }
                            finally
                            {
                                await destFile.CloseAsync();
                            }
                        }
                    }
                    finally
                    {
                        await srcFile.CloseAsync();
                    }
                }
            }
        }

        static async Task<long> CopyBetweenHandleAsync(FileBase src, FileBase dest, CopyFileParams param, ProgressReporterBase reporter, long estimatedSize, CancellationToken cancel)
        {
            checked
            {
                long currentPosition = 0;

                if (param.AsyncCopy == false)
                {
                    // Normal copy
                    using (MemoryHelper.FastAllocMemoryWithUsing(param.BufferSize, out Memory<byte> buffer))
                    {
                        while (true)
                        {
                            int readSize = await src.ReadAsync(buffer, cancel);

                            Debug.Assert(readSize <= buffer.Length);

                            if (readSize <= 0) break;

                            await dest.WriteAsync(buffer.Slice(0, readSize), cancel);

                            currentPosition += readSize;
                            reporter.ReportProgress(new ProgressData(currentPosition, estimatedSize));
                        }
                    }
                }
                else
                {
                    // Async copy
                    using (MemoryHelper.FastAllocMemoryWithUsing(param.BufferSize, out Memory<byte> buffer1))
                    {
                        using (MemoryHelper.FastAllocMemoryWithUsing(param.BufferSize, out Memory<byte> buffer2))
                        {
                            Task lastWriteTask = null;
                            int number = 0;
                            int writeSize = 0;

                            Memory<byte>[] buffers = new Memory<byte>[2] { buffer1, buffer2 };

                            while (true)
                            {
                                Memory<byte> buffer = buffers[(number++) % 2];

                                int readSize = await src.ReadAsync(buffer, cancel);

                                Debug.Assert(readSize <= buffer.Length);

                                if (lastWriteTask != null)
                                {
                                    await lastWriteTask;
                                    currentPosition += writeSize;
                                    reporter.ReportProgress(new ProgressData(currentPosition, estimatedSize));
                                }

                                if (readSize <= 0) break;

                                writeSize = readSize;
                                lastWriteTask = dest.WriteAsync(buffer.Slice(0, writeSize), cancel);
                            }

                            reporter.ReportProgress(new ProgressData(currentPosition, estimatedSize));
                        }
                    }
                }

                return currentPosition;
            }
        }
    }
}

