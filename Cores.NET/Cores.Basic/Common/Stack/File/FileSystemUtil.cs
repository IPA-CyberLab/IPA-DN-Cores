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
using System.Buffers;
using System.Diagnostics;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Text;

namespace IPA.Cores.Basic
{
    abstract partial class FileSystem
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

        public async Task<int> WriteDataToFileAsync(string path, ReadOnlyMemory<byte> srcMemory, FileOperationFlags flags = FileOperationFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default)
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
        public int WriteDataToFile(string path, ReadOnlyMemory<byte> data, FileOperationFlags flags = FileOperationFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default)
            => WriteDataToFileAsync(path, data, flags, doNotOverwrite, cancel)._GetResult();

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
            => WriteStringToFileAsync(path, srcString, flags, doNotOverwrite, encoding, writeBom, cancel)._GetResult();

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
            => AppendDataToFileAsync(path, srcMemory, flags, cancel)._GetResult();

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
            => ReadDataFromFileAsync(path, destMemory, flags, cancel)._GetResult();

        public async Task<Memory<byte>> ReadDataFromFileAsync(string path, int maxSize = int.MaxValue, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
        {
            using (var file = await OpenAsync(path, false, false, false, flags, cancel))
            {
                try
                {
                    return await file.GetStream()._ReadToEndAsync(maxSize, cancel);
                }
                finally
                {
                    await file.CloseAsync();
                }
            }
        }
        public Memory<byte> ReadDataFromFile(string path, int maxSize = int.MaxValue, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
            => ReadDataFromFileAsync(path, maxSize, flags, cancel)._GetResult();

        public async Task<string> ReadStringFromFileAsync(string path, Encoding encoding = null, int maxSize = int.MaxValue, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
        {
            Memory<byte> data = await ReadDataFromFileAsync(path, maxSize, flags, cancel);

            if (encoding == null)
                return Str.DecodeStringAutoDetect(data.Span, out _);
            else
                return Str.DecodeString(data.Span, encoding, out _);
        }
        public string ReadStringFromFile(string path, Encoding encoding = null, int maxSize = int.MaxValue, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
            => ReadStringFromFileAsync(path, encoding, maxSize, flags, cancel)._GetResult();

        class FindSingleFileData
        {
            public string FullPath;
            public double MatchRate;
        }

        public async Task<string> EasyReadStringAsync(string partOfFileName, bool exact = false, string rootDir = "/", Encoding encoding = null, int maxSize = int.MaxValue, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
            => await ReadStringFromFileAsync(await EasyFindSingleFileAsync(partOfFileName, exact, rootDir, cancel), encoding, maxSize, flags, cancel);
        public string EasyReadString(string partOfFileName, bool exact = false, string rootDir = "/", Encoding encoding = null, int maxSize = int.MaxValue, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
            => EasyReadStringAsync(partOfFileName, exact, rootDir, encoding, maxSize, flags, cancel)._GetResult();

        public async Task<Memory<byte>> EasyReadDataAsync(string partOfFileName, bool exact = false, string rootDir = "/", int maxSize = int.MaxValue, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
            => await ReadDataFromFileAsync(await EasyFindSingleFileAsync(partOfFileName, exact, rootDir, cancel), maxSize, flags, cancel);
        public Memory<byte> EasyReadData(string partOfFileName, bool exact = false, string rootDir = "/", int maxSize = int.MaxValue, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
            => EasyReadDataAsync(partOfFileName, exact, rootDir, maxSize, flags, cancel)._GetResult();

#pragma warning disable CS1998
        public async Task<string> EasyFindSingleFileAsync(string partOfFileName, bool exact = false, string rootDir = "/", CancellationToken cancel = default)
        {
            DirectoryWalker walk = new DirectoryWalker(this, EnumDirectoryFlags.NoGetPhysicalSize);
            string exactFile = null;

            List<FindSingleFileData> candidates = new List<FindSingleFileData>();

            await walk.WalkDirectoryAsync(rootDir,
                async (info, entities, c) =>
                {
                    foreach (var file in entities.Where(x => x.IsDirectory == false))
                    {
                        if (partOfFileName._IsSamei(file.Name))
                        {
                            // Exact match
                            exactFile = file.FullPath;
                            return false;
                        }

                        if (file.Name._Search(partOfFileName) != -1)
                        {
                            int originalLen = file.Name.Length;
                            if (originalLen >= 1)
                            {
                                int replacedLen = file.Name._ReplaceStr(partOfFileName, "").Length;
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

            if (exactFile._IsFilled())
                return exactFile;

            if (exact && candidates.Count >= 2)
                throw new FileException(partOfFileName, "Two or more files matched while exact flag is set.");

            var match = candidates.OrderByDescending(x => x.MatchRate).FirstOrDefault();

            if (match == null)
                throw new FileException(partOfFileName, "The name did not match to any existing files.");

            return match.FullPath;
        }
        public string EasyFindSingleFile(string fileName, bool exact = false, string rootDir = "/", CancellationToken cancel = default)
            => EasyFindSingleFileAsync(fileName, exact, rootDir, cancel)._GetResult();
#pragma warning restore CS1998

        protected async Task DeleteDirectoryRecursiveInternalAsync(string directoryPath, CancellationToken cancel = default)
        {
            DirectoryWalker walker = new DirectoryWalker(this, EnumDirectoryFlags.NoGetPhysicalSize);
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
        public FileSystem FileSystem { get; }
        public EnumDirectoryFlags Flags { get; }

        public DirectoryWalker(FileSystem fileSystem, EnumDirectoryFlags flags = EnumDirectoryFlags.None)
        {
            this.FileSystem = fileSystem;
            this.Flags = flags;
        }

        async Task<bool> WalkDirectoryInternalAsync(string directoryFullPath, string directoryRelativePath,
            Func<DirectoryPathInfo, FileSystemEntity[], CancellationToken, Task<bool>> callback,
            Func<DirectoryPathInfo, FileSystemEntity[], CancellationToken, Task<bool>> callbackForDirectoryAgain,
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

            if (await callback(currentDirInfo, entityList, opCancel) == false)
            {
                return false;
            }

            if (recursive)
            {
                // Deep directory
                foreach (FileSystemEntity entity in entityList.Where(x => x.IsCurrentDirectory == false))
                {
                    if (entity.IsDirectory)
                    {
                        opCancel.ThrowIfCancellationRequested();

                        if (await WalkDirectoryInternalAsync(entity.FullPath, FileSystem.PathParser.Combine(directoryRelativePath, entity.Name), callback, callbackForDirectoryAgain, exceptionHandler, true, opCancel, entity) == false)
                        {
                            return false;
                        }
                    }
                }
            }

            if (callbackForDirectoryAgain != null)
            {
                if (await callbackForDirectoryAgain(currentDirInfo, entityList, opCancel) == false)
                {
                    return false;
                }
            }

            return true;
        }

        public async Task<bool> WalkDirectoryAsync(string rootDirectory, Func<DirectoryPathInfo, FileSystemEntity[], CancellationToken, Task<bool>> callback, Func<DirectoryPathInfo, FileSystemEntity[], CancellationToken, Task<bool>> callbackForDirectoryAgain = null, Func<DirectoryPathInfo, Exception, CancellationToken, Task<bool>> exceptionHandler = null, bool recursive = true, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();

            rootDirectory = await FileSystem.NormalizePathAsync(rootDirectory, cancel);

            return await WalkDirectoryInternalAsync(rootDirectory, "", callback, callbackForDirectoryAgain, exceptionHandler, recursive, cancel, null);
        }

#pragma warning disable CS1998
        public bool WalkDirectory(string rootDirectory, Func<DirectoryPathInfo, FileSystemEntity[], CancellationToken, bool> callback, Func<DirectoryPathInfo, FileSystemEntity[], CancellationToken, bool> callbackForDirectoryAgain = null, Func<DirectoryPathInfo, Exception, CancellationToken, bool> exceptionHandler = null, bool recursive = true, CancellationToken cancel = default)
            => WalkDirectoryAsync(rootDirectory,
                async (dirInfo, entity, c) => { return callback(dirInfo, entity, c); },
                async (dirInfo, entity, c) => { return callbackForDirectoryAgain(dirInfo, entity, c); },
                async (dirInfo, exception, c) => { if (exceptionHandler == null) throw exception; return exceptionHandler(dirInfo, exception, c); },
                recursive, cancel)._GetResult();
#pragma warning restore CS1998
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
        public ReadOnlyMemory<byte> Binary => (ReadOnlyMemory<byte>)this[EasyFileAccessType.Binary];

        // Implementation
        public FileSystem FileSystem { get; }
        public string FilePath { get; }

        readonly Singleton<EasyFileAccessType, object> CachedData;

        public object this[EasyFileAccessType type] => this.GetData(type);

        public EasyFileAccess(FileSystem fileSystem, string filePath)
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
                    return (ReadOnlyMemory<byte>)FileSystem.ReadDataFromFile(this.FilePath);

                default:
                    throw new ArgumentOutOfRangeException("type");
            }
        }

        public static implicit operator string(EasyFileAccess access) => access.String;
        public static implicit operator ReadOnlyMemory<byte>(EasyFileAccess access) => access.Binary;
        public static implicit operator ReadOnlySpan<byte>(EasyFileAccess access) => access.Binary.Span;
        public static implicit operator byte[](EasyFileAccess access) => access.Binary.ToArray();
    }

    abstract class FileObjectRandomAccessWrapperBase : FileObject
    {
        protected readonly ConcurrentRandomAccess<byte> BaseAccess;

        public FileObjectRandomAccessWrapperBase(ConcurrentRandomAccess<byte> sharedBaseAccess, FileSystem fileSystem, FileParameters fileParams) : base(fileSystem, fileParams)
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

    abstract class FileSystemPath
    {
        public string PathString { get; }
        public FileSystem FileSystem { get; }
        public FileOperationFlags OperationFlags { get; }

        public FileSystemPath(string pathString, FileSystem fileSystem = null, FileOperationFlags operationFlags = FileOperationFlags.None)
        {
            if (fileSystem == null) fileSystem = Lfs;

            this.FileSystem = fileSystem;
            this.OperationFlags = operationFlags;

            this.PathString = this.FileSystem.NormalizePath(pathString);
        }

        public override string ToString() => this.PathString;
    }

    class DirectoryPath : FileSystemPath
    {
        public DirectoryPath(string pathString, FileSystem fileSystem = null, FileOperationFlags operationFlags = FileOperationFlags.None) : base(pathString, fileSystem, operationFlags)
        {
        }

        public Task CreateDirectoryAsync(CancellationToken cancel = default)
            => this.FileSystem.CreateDirectoryAsync(this.PathString, this.OperationFlags, cancel);
        public void CreateDirectory(CancellationToken cancel = default)
            => CreateDirectoryAsync(cancel)._GetResult();

        public Task DeleteDirectoryAsync(bool recursive = false, CancellationToken cancel = default)
            => this.FileSystem.DeleteDirectoryAsync(this.PathString, recursive, cancel);
        public void DeleteDirectory(bool recursive = false, CancellationToken cancel = default)
            => DeleteDirectoryAsync(recursive, cancel)._GetResult();

        public Task<FileSystemEntity[]> EnumDirectoryAsync(bool recursive = false, EnumDirectoryFlags flags = EnumDirectoryFlags.None, CancellationToken cancel = default)
            => this.FileSystem.EnumDirectoryAsync(this.PathString, recursive, flags, cancel);
        public FileSystemEntity[] EnumDirectory(bool recursive = false, EnumDirectoryFlags flags = EnumDirectoryFlags.None, CancellationToken cancel = default)
            => EnumDirectoryAsync(recursive, flags, cancel)._GetResult();

        public Task<FileMetadata> GetDirectoryMetadataAsync(FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
            => this.FileSystem.GetDirectoryMetadataAsync(this.PathString, flags, cancel);
        public FileMetadata GetDirectoryMetadata(FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
            => GetDirectoryMetadataAsync(flags, cancel)._GetResult();

        public Task<bool> IsDirectoryExistsAsync(CancellationToken cancel = default)
            => this.FileSystem.IsDirectoryExistsAsync(this.PathString, cancel);
        public bool IsDirectoryExists(CancellationToken cancel = default)
            => IsDirectoryExistsAsync(cancel)._GetResult();

        public Task SetDirectoryMetadataAsync(FileMetadata metadata, CancellationToken cancel = default)
             => this.FileSystem.SetDirectoryMetadataAsync(this.PathString, metadata, cancel);
        public void SetDirectoryMetadata(FileMetadata metadata, CancellationToken cancel = default)
            => SetDirectoryMetadataAsync(metadata, cancel)._GetResult();

        public Task MoveDirectoryAsync(string destPath, CancellationToken cancel = default)
            => this.FileSystem.MoveDirectoryAsync(this.PathString, destPath, cancel);
        public void MoveDirectory(string destPath, CancellationToken cancel = default)
            => MoveDirectoryAsync(destPath, cancel)._GetResult();

        public static implicit operator DirectoryPath(string directoryName) => new DirectoryPath(directoryName);
    }

    class FilePath : FileSystemPath
    {
        readonly Singleton<EasyFileAccess> EasyAccessSingleton;

        public EasyFileAccess EasyAccess => this.EasyAccessSingleton;

        public FilePath(ResourceFileSystem resFs, string partOfPath, bool exact = false, string rootDir = "/", FileOperationFlags operationFlags = FileOperationFlags.None, CancellationToken cancel = default)
            : this((resFs ?? Res.Cores).EasyFindSingleFile(partOfPath, exact, rootDir, cancel), (resFs ?? Res.Cores)) { }

        public FilePath(string pathString, FileSystem fileSystem = null, FileOperationFlags operationFlags = FileOperationFlags.None)
             : base(pathString, fileSystem, operationFlags)
        {
            this.EasyAccessSingleton = new Singleton<EasyFileAccess>(() => new EasyFileAccess(this.FileSystem, this.PathString));
        }

        public static implicit operator FilePath(string fileName) => new FilePath(fileName);

        public Task<FileObject> CreateFileAsync(FileMode mode = FileMode.Open, FileAccess access = FileAccess.Read, FileShare share = FileShare.Read, FileOperationFlags additionalFlags = FileOperationFlags.None, CancellationToken cancel = default)
            => this.FileSystem.CreateFileAsync(new FileParameters(this.PathString, mode, access, share, this.OperationFlags | additionalFlags), cancel);

        public FileObject CreateFile(FileMode mode = FileMode.Open, FileAccess access = FileAccess.Read, FileShare share = FileShare.Read, FileOperationFlags additionalFlags = FileOperationFlags.None, CancellationToken cancel = default)
            => CreateFileAsync(mode, access, share, additionalFlags, cancel)._GetResult();

        public Task<FileObject> CreateAsync(bool noShare = false, FileOperationFlags additionalFlags = FileOperationFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default)
            => CreateFileAsync(doNotOverwrite ? FileMode.CreateNew : FileMode.Create, FileAccess.ReadWrite, noShare ? FileShare.None : FileShare.Read, additionalFlags, cancel);

        public FileObject Create(bool noShare = false, FileOperationFlags additionalFlags = FileOperationFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default)
            => CreateAsync(noShare, additionalFlags, doNotOverwrite, cancel)._GetResult();

        public Task<FileObject> OpenAsync(bool writeMode = false, bool noShare = false, bool readLock = false, FileOperationFlags additionalFlags = FileOperationFlags.None, CancellationToken cancel = default)
            => CreateFileAsync(FileMode.Open, (writeMode ? FileAccess.ReadWrite : FileAccess.Read),
                (noShare ? FileShare.None : ((writeMode || readLock) ? FileShare.Read : (FileShare.ReadWrite | FileShare.Delete))), additionalFlags, cancel);

        public FileObject Open(bool writeMode = false, bool noShare = false, bool readLock = false, FileOperationFlags additionalFlags = FileOperationFlags.None, CancellationToken cancel = default)
            => OpenAsync(writeMode, noShare, readLock, additionalFlags, cancel)._GetResult();

        public Task<FileObject> OpenOrCreateAsync(bool noShare = false, FileOperationFlags additionalFlags = FileOperationFlags.None, CancellationToken cancel = default)
            => CreateFileAsync(FileMode.OpenOrCreate, FileAccess.ReadWrite, noShare ? FileShare.None : FileShare.Read, additionalFlags, cancel);

        public FileObject OpenOrCreate(bool noShare = false, FileOperationFlags additionalFlags = FileOperationFlags.None, CancellationToken cancel = default)
            => OpenOrCreateAsync(noShare, additionalFlags, cancel)._GetResult();

        public Task<FileObject> OpenOrCreateAppendAsync(bool noShare = false, FileOperationFlags additionalFlags = FileOperationFlags.None, CancellationToken cancel = default)
            => CreateFileAsync(FileMode.Append, FileAccess.Write, noShare ? FileShare.None : FileShare.Read, additionalFlags, cancel);

        public FileObject OpenOrCreateAppend(bool noShare = false, FileOperationFlags additionalFlags = FileOperationFlags.None, CancellationToken cancel = default)
            => OpenOrCreateAppendAsync(noShare, additionalFlags, cancel)._GetResult();

        public Task<FileMetadata> GetFileMetadataAsync(FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
            => this.FileSystem.GetFileMetadataAsync(this.PathString, flags, cancel);

        public FileMetadata GetFileMetadata(FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
            => GetFileMetadataAsync(flags, cancel)._GetResult();

        public Task<bool> IsFileExistsAsync(CancellationToken cancel = default)
            => this.FileSystem.IsFileExistsAsync(this.PathString, cancel);

        public bool IsFileExists(CancellationToken cancel = default)
            => IsFileExistsAsync(cancel)._GetResult();

        public Task SetFileMetadataAsync(FileMetadata metadata, CancellationToken cancel = default)
            => this.FileSystem.SetFileMetadataAsync(this.PathString, metadata, cancel);

        public void SetFileMetadata(FileMetadata metadata, CancellationToken cancel = default)
            => SetFileMetadataAsync(metadata, cancel)._GetResult();

        public Task DeleteFileAsync(FileOperationFlags additionalFlags = FileOperationFlags.None, CancellationToken cancel = default)
            => this.FileSystem.DeleteFileAsync(this.PathString, this.OperationFlags | additionalFlags, cancel);

        public void DeleteFile(FileOperationFlags additionalFlags = FileOperationFlags.None, CancellationToken cancel = default)
            => DeleteFileAsync(this.OperationFlags | additionalFlags, cancel)._GetResult();

        public Task MoveFileAsync(string destPath, CancellationToken cancel = default)
            => this.FileSystem.MoveFileAsync(this.PathString, destPath, cancel);

        public Task<bool> TryAddOrRemoveAttributeFromExistingFile(FileAttributes attributesToAdd = 0, FileAttributes attributesToRemove = 0, CancellationToken cancel = default)
            => this.FileSystem.TryAddOrRemoveAttributeFromExistingFile(this.PathString, attributesToAdd, attributesToRemove, cancel);

        public Task<int> WriteDataToFileAsync(ReadOnlyMemory<byte> srcMemory, FileOperationFlags additionalFlags = FileOperationFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default)
            => this.FileSystem.WriteDataToFileAsync(this.PathString, srcMemory, this.OperationFlags | additionalFlags, doNotOverwrite, cancel);

        public int WriteDataToFile(ReadOnlyMemory<byte> data, FileOperationFlags additionalFlags = FileOperationFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default)
            => WriteDataToFileAsync(data, additionalFlags, doNotOverwrite, cancel)._GetResult();

        public Task<int> WriteStringToFileAsync(string srcString, FileOperationFlags additionalFlags = FileOperationFlags.None, bool doNotOverwrite = false, Encoding encoding = null, bool writeBom = false, CancellationToken cancel = default)
            => this.FileSystem.WriteStringToFileAsync(this.PathString, srcString, this.OperationFlags | additionalFlags, doNotOverwrite, encoding, writeBom, cancel);

        public int WriteStringToFile(string srcString, FileOperationFlags additionalFlags = FileOperationFlags.None, bool doNotOverwrite = false, Encoding encoding = null, bool writeBom = false, CancellationToken cancel = default)
            => WriteStringToFileAsync(srcString, additionalFlags, doNotOverwrite, encoding, writeBom, cancel)._GetResult();

        public Task AppendDataToFileAsync(Memory<byte> srcMemory, FileOperationFlags additionalFlags = FileOperationFlags.None, CancellationToken cancel = default)
            => this.FileSystem.AppendDataToFileAsync(this.PathString, srcMemory, this.OperationFlags | additionalFlags, cancel);

        public void AppendDataToFile(Memory<byte> srcMemory, FileOperationFlags additionalFlags = FileOperationFlags.None, CancellationToken cancel = default)
            => AppendDataToFileAsync(srcMemory, additionalFlags, cancel)._GetResult();

        public Task<int> ReadDataFromFileAsync(Memory<byte> destMemory, FileOperationFlags additionalFlags = FileOperationFlags.None, CancellationToken cancel = default)
            => this.FileSystem.ReadDataFromFileAsync(this.PathString, destMemory, this.OperationFlags | additionalFlags, cancel);

        public int ReadDataFromFile(Memory<byte> destMemory, FileOperationFlags additionalFlags = FileOperationFlags.None, CancellationToken cancel = default)
            => ReadDataFromFileAsync(destMemory, additionalFlags, cancel)._GetResult();

        public Task<string> ReadStringFromFileAsync(Encoding encoding = null, int maxSize = int.MaxValue, FileOperationFlags additionalFlags = FileOperationFlags.None, CancellationToken cancel = default)
            => this.FileSystem.ReadStringFromFileAsync(this.PathString, encoding, maxSize, this.OperationFlags | additionalFlags, cancel);

        public string ReadStringFromFile(Encoding encoding = null, int maxSize = int.MaxValue, FileOperationFlags additionalFlags = FileOperationFlags.None, CancellationToken cancel = default)
            => ReadStringFromFileAsync(encoding, maxSize, additionalFlags, cancel)._GetResult();
    }
}
