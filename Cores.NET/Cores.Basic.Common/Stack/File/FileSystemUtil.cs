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
using System.Buffers;
using System.Diagnostics;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Text;

namespace IPA.Cores.Basic
{
    abstract partial class FileSystemBase
    {
        public async Task<bool> TryAddOrRemoveAttributeFromExistingFile(string path, FileAttributes attributesToAdd = 0, FileAttributes attributesToRemove = 0, CancellationToken cancel = default)
        {
            try
            {
                if (File.Exists(path) == false)
                    return false;

                var existingFileMetadata = await this.GetFileMetadataAsync(path, FileMetadataGetFlags.NoAlternateStream | FileMetadataGetFlags.NoSecurity | FileMetadataGetFlags.NoTimes, cancel);
                var currentAttributes = existingFileMetadata.Attributes ?? 0;
                if (currentAttributes.Bit(FileAttributes.Hidden) || currentAttributes.Bit(FileAttributes.ReadOnly))
                {
                    var newAttributes = (currentAttributes & ~(attributesToRemove)) | attributesToAdd;
                    if (currentAttributes != newAttributes)
                    {
                        try
                        {
                            await this.SetFileMetadataAsync(path, new FileMetadata(false, attributes: newAttributes), cancel);

                            return true;
                        }
                        catch { }
                    }
                    else
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        public async Task<int> WriteDataToFileAsync(string path, Memory<byte> srcMemory, FileOperationFlags flags = FileOperationFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default)
        {
            if (flags.Bit(FileOperationFlags.WriteOnlyIfChanged))
            {
                try
                {
                    if (await IsFileExistsAsync(path, cancel))
                    {
                        Memory<byte> existingData = await ReadDataFromFileAsync(path, srcMemory.Length, flags, cancel);
                        if (existingData.Length == srcMemory.Length && existingData.Span.SequenceEqual(srcMemory.Span))
                        {
                            return srcMemory.Length;
                        }
                    }
                }
                catch { }
            }

            using (var file = await CreateAsync(path, false, flags & ~FileOperationFlags.WriteOnlyIfChanged, doNotOverwrite, cancel))
            {
                try
                {
                    await file.WriteAsync(srcMemory, cancel);
                    return srcMemory.Length;
                }
                finally
                {
                    await file.CloseAsync();
                }
            }
        }
        public int WriteDataToFile(string path, Memory<byte> data, FileOperationFlags flags = FileOperationFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default)
            => WriteDataToFileAsync(path, data, flags, doNotOverwrite, cancel).GetResult();

        public Task<int> WriteStringToFileAsync(string path, string srcString, FileOperationFlags flags = FileOperationFlags.None, bool doNotOverwrite = false, Encoding encoding = null, bool writeBom = false, CancellationToken cancel = default)
        {
            checked
            {
                if (encoding == null) encoding = Str.Utf8Encoding;
                MemoryBuffer<byte> buf = new MemoryBuffer<byte>();

                ReadOnlySpan<byte> bomSpan = default;

                if (writeBom)
                    bomSpan = Str.GetBOMSpan(encoding);

                buf.Write(bomSpan);

                int sizeReserved = srcString.Length * 4 + 128;
                int encodedSize = encoding.GetBytes(srcString, buf.Walk(sizeReserved));
                buf.SetLength(bomSpan.Length + encodedSize);

                return WriteDataToFileAsync(path, buf.Memory, flags, doNotOverwrite, cancel);
            }
        }
        public int WriteStringToFile(string path, string srcString, FileOperationFlags flags = FileOperationFlags.None, bool doNotOverwrite = false, Encoding encoding = null, bool writeBom = false, CancellationToken cancel = default)
            => WriteStringToFileAsync(path, srcString, flags, doNotOverwrite, encoding, writeBom, cancel).GetResult();

        public async Task AppendDataToFileAsync(string path, Memory<byte> srcMemory, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
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
        public void AppendDataToFile(string path, Memory<byte> srcMemory, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
            => AppendDataToFileAsync(path, srcMemory, flags, cancel).GetResult();

        public async Task<int> ReadDataFromFileAsync(string path, Memory<byte> destMemory, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
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
        public int ReadDataFromFile(string path, Memory<byte> destMemory, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
            => ReadDataFromFileAsync(path, destMemory, flags, cancel).GetResult();

        public async Task<Memory<byte>> ReadDataFromFileAsync(string path, int maxSize = int.MaxValue, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
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
        public Memory<byte> ReadDataFromFile(string path, int maxSize = int.MaxValue, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
            => ReadDataFromFileAsync(path, maxSize, flags, cancel).GetResult();

        public async Task<string> ReadStringFromFileAsync(string path, Encoding encoding = null, int maxSize = int.MaxValue, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
        {
            Memory<byte> data = await ReadDataFromFileAsync(path, maxSize, flags, cancel);

            if (encoding == null)
                return Str.DecodeStringAutoDetect(data.Span, out _);
            else
                return Str.DecodeString(data.Span, encoding, out _);
        }
        public string ReadStringFromFile(string path, Encoding encoding = null, int maxSize = int.MaxValue, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
            => ReadStringFromFileAsync(path, encoding, maxSize, flags, cancel).GetResult();

        class FindSingleFileData
        {
            public string FullPath;
            public double MatchRate;
        }

        public async Task<string> EasyReadStringAsync(string partOfFileName, bool exact = false, string rootDir = "/", Encoding encoding = null, int maxSize = int.MaxValue, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
            => await ReadStringFromFileAsync(await EasyFindSingleFileAsync(partOfFileName, exact, rootDir, cancel), encoding, maxSize, flags, cancel);
        public string EasyReadString(string partOfFileName, bool exact = false, string rootDir = "/", Encoding encoding = null, int maxSize = int.MaxValue, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
            => EasyReadStringAsync(partOfFileName, exact, rootDir, encoding, maxSize, flags, cancel).GetResult();

        public async Task<string> EasyFindSingleFileAsync(string partOfFileName, bool exact = false, string rootDir = "/", CancellationToken cancel = default)
        {
            DirectoryWalker walk = new DirectoryWalker(this, false, EnumDirectoryFlags.NoGetPhysicalSize);
            string exactFile = null;

            List<FindSingleFileData> candidates = new List<FindSingleFileData>();

            await walk.WalkDirectoryAsync(rootDir,
                async (info, entities, c) =>
                {
                    await Task.CompletedTask;

                    foreach (var file in entities.Where(x => x.IsDirectory == false))
                    {
                        if (partOfFileName.IsSamei(file.Name))
                        {
                            // Exact match
                            exactFile = file.FullPath;
                            return false;
                        }

                        if (file.Name.Search(partOfFileName) != -1)
                        {
                            int originalLen = file.Name.Length;
                            if (originalLen >= 1)
                            {
                                int replacedLen = file.Name.ReplaceStr(partOfFileName, "").Length;
                                int matchLen = originalLen - replacedLen;
                                FindSingleFileData d = new FindSingleFileData()
                                {
                                    FullPath = file.FullPath,
                                    MatchRate = (double)matchLen / (double)originalLen,
                                };
                                candidates.Add(d);
                            }
                        }
                    }
                    return true;
                },
                cancel: cancel);

            if (exactFile.IsFilled())
                return exactFile;

            if (exact && candidates.Count >= 2)
                throw new FileException(partOfFileName, "Two or more files matched while exact flag is set.");

            var match = candidates.OrderByDescending(x => x.MatchRate).FirstOrDefault();

            if (match == null)
                throw new FileException(partOfFileName, "The name did not match to any existing files.");

            return match.FullPath;
        }
        public string EasyFindSingleFile(string fileName, bool exact = false, string rootDir = "/", CancellationToken cancel = default)
            => EasyFindSingleFileAsync(fileName, exact, rootDir, cancel).GetResult();

        protected async Task DeleteDirectoryRecursiveInternalAsync(string directoryPath, CancellationToken cancel = default)
        {
            DirectoryWalker walker = new DirectoryWalker(this, true, EnumDirectoryFlags.NoGetPhysicalSize);
            await walker.WalkDirectoryAsync(directoryPath,
                async (info, entities, c) =>
                {
                    foreach (var file in entities.Where(x => x.IsDirectory == false))
                    {
                        await this.DeleteFileImplAsync(file.FullPath, FileOperationFlags.ForceClearReadOnlyOrHiddenBitsOnNeed, cancel);
                    }

                    await this.DeleteDirectoryImplAsync(info.FullPath, false, cancel);

                    return true;
                },
                cancel: cancel);
        }

        Singleton<string, EasyFileAccess> EasyFileAccessSingleton;
        Singleton<string, string> EasyAccessFileNameCache;
        void InitEasyFileAccessSingleton()
        {
            EasyFileAccessSingleton = new Singleton<string, EasyFileAccess>(filePath => new EasyFileAccess(this, filePath));
            EasyAccessFileNameCache = new Singleton<string, string>(name => FindEasyAccessFilePathFromNameImpl(name));
        }

        protected virtual string FindEasyAccessFilePathFromNameImpl(string name)
        {
            EasyAccessPathFindMode mode = this.Params.EasyAccessPathFindMode.Value;
            switch (mode)
            {
                case EasyAccessPathFindMode.MostMatch:
                    return this.EasyFindSingleFile(name, false);

                case EasyAccessPathFindMode.MostMatchExact:
                    return this.EasyFindSingleFile(name, true);

                case EasyAccessPathFindMode.ExactFullPath:
                    return name;

                default:
                    throw new NotSupportedException();
            }
        }

        public virtual EasyFileAccess GetEasyAccess(string name)
        {
            string fullPath = EasyAccessFileNameCache[name];

            return this.EasyFileAccessSingleton[fullPath];
        }

        public virtual EasyFileAccess this[string name] => GetEasyAccess(name);
    }

    class DirectoryPathInfo
    {
        public bool IsRoot { get; }
        public string FullPath { get; }
        public string RelativePath { get; }
        public FileSystemEntity Entity { get; }

        public DirectoryPathInfo(bool isRoot, string fullPath, string relativePath, FileSystemEntity entity)
        {
            this.IsRoot = isRoot;
            this.FullPath = fullPath;
            this.RelativePath = relativePath;
            this.Entity = entity;
        }
    }

    class DirectoryWalker
    {
        public FileSystemBase FileSystem { get; }
        public bool DeeperFirstInRecursive { get; }
        public EnumDirectoryFlags Flags { get; }

        public DirectoryWalker(FileSystemBase fileSystem, bool deeperFirstInRecursive = false, EnumDirectoryFlags flags = EnumDirectoryFlags.None)
        {
            this.FileSystem = fileSystem;
            this.DeeperFirstInRecursive = deeperFirstInRecursive;
            this.Flags = flags;
        }

        async Task<bool> WalkDirectoryInternalAsync(string directoryFullPath, string directoryRelativePath,
            Func<DirectoryPathInfo, FileSystemEntity[], CancellationToken, Task<bool>> callback,
            Func<DirectoryPathInfo, Exception, CancellationToken, Task<bool>> exceptionHandler,
            bool recursive, CancellationToken opCancel, FileSystemEntity dirEntity)
        {
            opCancel.ThrowIfCancellationRequested();

            FileSystemEntity[] entityList;

            bool isRootDir = false;

            if (dirEntity == null)
            {
                isRootDir = true;

                dirEntity = new FileSystemEntity()
                {
                    FullPath = directoryFullPath,
                    Name = this.FileSystem.PathParser.GetFileName(directoryFullPath),
                };
            }

            DirectoryPathInfo currentDirInfo = new DirectoryPathInfo(isRootDir, directoryFullPath, directoryRelativePath, dirEntity);

            try
            {
                entityList = await FileSystem.EnumDirectoryAsync(directoryFullPath, false, this.Flags, opCancel);
            }
            catch (Exception ex)
            {
                if (exceptionHandler == null)
                {
                    throw;
                }

                if (await exceptionHandler(currentDirInfo, ex, opCancel) == false)
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }

            if (isRootDir)
            {
                var rootDirEntry = entityList.Where(x => x.IsCurrentDirectory).Single();
                currentDirInfo = new DirectoryPathInfo(true, directoryFullPath, directoryRelativePath, rootDirEntry);
            }

            if (this.DeeperFirstInRecursive == false)
            {
                // Deeper last
                if (await callback(currentDirInfo, entityList, opCancel) == false)
                {
                    return false;
                }
            }

            if (recursive)
            {
                // Deep directory
                foreach (FileSystemEntity entity in entityList.Where(x => x.IsCurrentDirectory == false))
                {
                    if (entity.IsDirectory)
                    {
                        opCancel.ThrowIfCancellationRequested();

                        if (await WalkDirectoryInternalAsync(entity.FullPath, FileSystem.PathParser.Combine(directoryRelativePath, entity.Name), callback, exceptionHandler, true, opCancel, entity) == false)
                        {
                            return false;
                        }
                    }
                }
            }

            if (this.DeeperFirstInRecursive)
            {
                // Deeper first
                if (await callback(currentDirInfo, entityList, opCancel) == false)
                {
                    return false;
                }
            }

            return true;
        }

        public async Task<bool> WalkDirectoryAsync(string rootDirectory, Func<DirectoryPathInfo, FileSystemEntity[], CancellationToken, Task<bool>> callback, Func<DirectoryPathInfo, Exception, CancellationToken, Task<bool>> exceptionHandler = null, bool recursive = true, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();

            rootDirectory = await FileSystem.NormalizePathAsync(rootDirectory, cancel);

            return await WalkDirectoryInternalAsync(rootDirectory, "", callback, exceptionHandler, recursive, cancel, null);
        }

        public bool WalkDirectory(string rootDirectory, Func<DirectoryPathInfo, FileSystemEntity[], CancellationToken, bool> callback, Func<DirectoryPathInfo, Exception, CancellationToken, bool> exceptionHandler = null, bool recursive = true, CancellationToken cancel = default)
            => WalkDirectoryAsync(rootDirectory,
                async (dirInfo, entity, c) => { await Task.CompletedTask; return callback(dirInfo, entity, c); },
                async (dirInfo, exception, c) => { await Task.CompletedTask; return exceptionHandler(dirInfo, exception, c); },
                recursive, cancel).GetResult();
    }

    enum EasyFileAccessType
    {
        String,
        Binary,
    }

    class EasyFileAccess
    {
        // Properties
        public string String => (string)this[EasyFileAccessType.String];
        public Memory<byte> Binary => (Memory<byte>)this[EasyFileAccessType.Binary];

        // Implementation
        public FileSystemBase FileSystem { get; }
        public string FilePath { get; }

        readonly Singleton<EasyFileAccessType, object> CachedData;

        public object this[EasyFileAccessType type] => this.GetData(type);

        public EasyFileAccess(FileSystemBase fileSystem, string filePath)
        {
            this.FileSystem = fileSystem;
            this.FilePath = filePath;
            this.CachedData = new Singleton<EasyFileAccessType, object>(type => this.InternalReadData(type));
        }

        public object GetData(EasyFileAccessType type) => this.CachedData[type];

        object InternalReadData(EasyFileAccessType type)
        {
            switch (type)
            {
                case EasyFileAccessType.String:
                    return FileSystem.ReadStringFromFile(this.FilePath);

                case EasyFileAccessType.Binary:
                    return FileSystem.ReadDataFromFile(this.FilePath);

                default:
                    throw new ArgumentOutOfRangeException("type");
            }
        }

        public static implicit operator string(EasyFileAccess access) => access.String;
        public static implicit operator Memory<byte>(EasyFileAccess access) => access.Binary;
        public static implicit operator Span<byte>(EasyFileAccess access) => access.Binary.Span;
        public static implicit operator ReadOnlyMemory<byte>(EasyFileAccess access) => access.Binary;
        public static implicit operator ReadOnlySpan<byte>(EasyFileAccess access) => access.Binary.Span;
        public static implicit operator byte[](EasyFileAccess access) => access.Binary.ToArray();
    }

    abstract class FileObjectRandomAccessWrapperBase : FileObject
    {
        protected readonly ConcurrentRandomAccess<byte> BaseAccess;

        public FileObjectRandomAccessWrapperBase(ConcurrentRandomAccess<byte> sharedBaseAccess, FileSystemBase fileSystem, FileParameters fileParams) : base(fileSystem, fileParams)
        {
            this.BaseAccess = sharedBaseAccess;

            long initialPosition = 0;

            if (fileParams.Mode == FileMode.Create || fileParams.Mode == FileMode.CreateNew || fileParams.Mode == FileMode.Truncate)
            {
                this.BaseAccess.SetFileSize(0);
            }

            long initialFileSize = this.BaseAccess.GetFileSize(true);

            if (fileParams.Mode == FileMode.Append)
            {
                initialPosition = initialFileSize;
            }

            InitAndCheckFileSizeAndPosition(initialPosition, initialFileSize);
        }

        protected abstract void OnCloseImpl();

        protected override Task CloseImplAsync()
        {
            try
            {
                OnCloseImpl();
            }
            catch { }

            return Task.CompletedTask;
        }

        protected override Task FlushImplAsync(CancellationToken cancel = default)
            => this.BaseAccess.FlushAsync(cancel);

        protected override Task<long> GetFileSizeImplAsync(CancellationToken cancel = default)
            => this.BaseAccess.GetFileSizeAsync(cancel: cancel);

        protected override Task<int> ReadRandomImplAsync(long position, Memory<byte> data, CancellationToken cancel = default)
            => this.BaseAccess.ReadRandomAsync(position, data, cancel);

        protected override Task SetFileSizeImplAsync(long size, CancellationToken cancel = default)
            => this.BaseAccess.SetFileSizeAsync(size, cancel);

        protected override Task WriteRandomImplAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
            => this.BaseAccess.WriteRandomAsync(position, data, cancel);
    }
}
