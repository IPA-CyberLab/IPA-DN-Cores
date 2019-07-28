// IPA Cores.NET
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
using System.Buffers;
using System.Diagnostics;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Text;

namespace IPA.Cores.Basic
{
    [Flags]
    public enum ReadParseFlags
    {
        None = 0,
        ForceInitOnParseError = 1,
        ForceRewrite = 2,

        Both = ForceInitOnParseError | ForceRewrite,
    }

    public abstract partial class FileSystem
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

        public async Task<int> WriteDataToFileAsync(string path, ReadOnlyMemory<byte> srcMemory, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default)
        {
            if (flags.Bit(FileFlags.WriteOnlyIfChanged))
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

            using (var file = await CreateAsync(path, false, flags & ~FileFlags.WriteOnlyIfChanged, doNotOverwrite, cancel))
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
        public int WriteDataToFile(string path, ReadOnlyMemory<byte> data, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default)
            => WriteDataToFileAsync(path, data, flags, doNotOverwrite, cancel)._GetResult();

        public Task<int> WriteStringToFileAsync(string path, string srcString, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, Encoding encoding = null, bool writeBom = false, CancellationToken cancel = default)
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
        public int WriteStringToFile(string path, string srcString, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, Encoding encoding = null, bool writeBom = false, CancellationToken cancel = default)
            => WriteStringToFileAsync(path, srcString, flags, doNotOverwrite, encoding, writeBom, cancel)._GetResult();

        public async Task AppendDataToFileAsync(string path, Memory<byte> srcMemory, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
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
        public void AppendDataToFile(string path, Memory<byte> srcMemory, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
            => AppendDataToFileAsync(path, srcMemory, flags, cancel)._GetResult();

        public async Task<T> ReadAndParseDataFileAsync<T>(string path, ReadParseFlags readParseFlags, Func<ReadOnlyMemory<byte>, T> parseProc, Func<ReadOnlyMemory<byte>> createProc, Func<T, ReadOnlyMemory<byte>> serializeProc = null, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
        {
            if (parseProc == null || createProc == null) throw new ArgumentNullException();

            bool forceInitMode = false;

            L_RETRY:
            if (forceInitMode || (await this.IsFileExistsAsync(path, cancel)) == false)
            {
                ReadOnlyMemory<byte> initialData = createProc();

                T ret = parseProc(initialData);

                await this.WriteDataToFileAsync(path, initialData, flags, false, cancel);

                return ret;
            }
            else
            {
                ReadOnlyMemory<byte> existingData = await this.ReadDataFromFileAsync(path, maxSize, flags, cancel);

                try
                {
                    T ret = parseProc(existingData);

                    if (readParseFlags.Bit(ReadParseFlags.ForceRewrite) && serializeProc != null)
                    {
                        try
                        {
                            await this.WriteDataToFileAsync(path, serializeProc(ret), flags, false, cancel);
                        }
                        catch { }
                    }

                    return ret;
                }
                catch
                {
                    if (readParseFlags.Bit(ReadParseFlags.ForceInitOnParseError) == false) throw;

                    forceInitMode = true;

                    goto L_RETRY;
                }
            }
        }
        public T ReadAndParseDataFile<T>(string path, ReadParseFlags readParseFlags, Func<ReadOnlyMemory<byte>, T> parseProc, Func<ReadOnlyMemory<byte>> createProc, Func<T, ReadOnlyMemory<byte>> serializeProc = null, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
            => ReadAndParseDataFileAsync(path, readParseFlags, parseProc, createProc, serializeProc, maxSize, flags, cancel)._GetResult();

        public async Task<int> ReadDataFromFileAsync(string path, Memory<byte> destMemory, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
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
        public int ReadDataFromFile(string path, Memory<byte> destMemory, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
            => ReadDataFromFileAsync(path, destMemory, flags, cancel)._GetResult();

        public async Task<Memory<byte>> ReadDataFromFileAsync(string path, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
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
        public Memory<byte> ReadDataFromFile(string path, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
            => ReadDataFromFileAsync(path, maxSize, flags, cancel)._GetResult();

        public async Task<string> ReadStringFromFileAsync(string path, Encoding encoding = null, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, bool oneLine = false, CancellationToken cancel = default)
        {
            Memory<byte> data = await ReadDataFromFileAsync(path, maxSize, flags, cancel);

            string str;

            if (encoding == null)
                str = Str.DecodeStringAutoDetect(data.Span, out _);
            else
                str = Str.DecodeString(data.Span, encoding, out _);

            if (oneLine)
            {
                str = str._OneLine("");
            }

            return str;
        }
        public string ReadStringFromFile(string path, Encoding encoding = null, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, bool oneLine = false, CancellationToken cancel = default)
            => ReadStringFromFileAsync(path, encoding, maxSize, flags, oneLine, cancel)._GetResult();

        class FindSingleFileData
        {
            public string FullPath;
            public double MatchRate;
        }

        public async Task<string> EasyReadStringAsync(string partOfFileName, bool exact = false, string rootDir = "/", Encoding encoding = null, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, bool oneLine = false, CancellationToken cancel = default)
            => await ReadStringFromFileAsync(await EasyFindSingleFileAsync(partOfFileName, exact, rootDir, cancel), encoding, maxSize, flags, oneLine, cancel);
        public string EasyReadString(string partOfFileName, bool exact = false, string rootDir = "/", Encoding encoding = null, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, bool oneLine = false, CancellationToken cancel = default)
            => EasyReadStringAsync(partOfFileName, exact, rootDir, encoding, maxSize, flags, oneLine, cancel)._GetResult();

        public async Task<Memory<byte>> EasyReadDataAsync(string partOfFileName, bool exact = false, string rootDir = "/", int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
            => await ReadDataFromFileAsync(await EasyFindSingleFileAsync(partOfFileName, exact, rootDir, cancel), maxSize, flags, cancel);
        public Memory<byte> EasyReadData(string partOfFileName, bool exact = false, string rootDir = "/", int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
            => EasyReadDataAsync(partOfFileName, exact, rootDir, maxSize, flags, cancel)._GetResult();

#pragma warning disable CS1998
        public async Task<string> EasyFindSingleFileAsync(string partOfFileName, bool exact = false, string rootDir = "/", CancellationToken cancel = default)
        {
            if (partOfFileName._IsEmpty())
            {
                throw new ArgumentNullException(nameof(partOfFileName));
            }

            partOfFileName = PathParser.Mac.NormalizeDirectorySeparator(partOfFileName);

            bool partOfFileNameContainsDirName = partOfFileName.IndexOf(PathParser.Mac.DirectorySeparator) != -1;

            DirectoryWalker walk = new DirectoryWalker(this, EnumDirectoryFlags.NoGetPhysicalSize);
            string exactFile = null;

            int numExactMatch = 0;

            List<FindSingleFileData> candidates = new List<FindSingleFileData>();

            await walk.WalkDirectoryAsync(rootDir,
                async (info, entities, c) =>
                {
                    foreach (var file in entities.Where(x => x.IsDirectory == false))
                    {
                        string fullPathTmp = PathParser.Mac.NormalizeDirectorySeparator(file.FullPath);

                        if (partOfFileName._IsSamei(file.Name))
                        {
                            // Exact match
                            exactFile = file.FullPath;
                            numExactMatch++;
                        }
                        else if (partOfFileNameContainsDirName && fullPathTmp.EndsWith(partOfFileName, StringComparison.OrdinalIgnoreCase))
                        {
                            // Exact match
                            exactFile = file.FullPath;
                            numExactMatch++;
                        }
                        else if (file.Name._Search(partOfFileName) != -1)
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
            {
                if (exact && numExactMatch >= 2)
                {
                    throw new FileException(partOfFileName, "Two or more files matched while exact flag is set.");
                }
                return exactFile;
            }

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
                        await this.DeleteFileImplAsync(file.FullPath, FileFlags.ForceClearReadOnlyOrHiddenBitsOnNeed, cancel);
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

    public class DirectoryPathInfo
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

    public class DirectoryWalker
    {
        public FileSystem FileSystem { get; }
        public EnumDirectoryFlags Flags { get; }

        public DirectoryWalker(FileSystem fileSystem, EnumDirectoryFlags flags = EnumDirectoryFlags.None)
        {
            this.FileSystem = fileSystem;
            this.Flags = (flags | EnumDirectoryFlags.IncludeCurrentDirectory).BitRemove(EnumDirectoryFlags.IncludeParentDirectory);
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
                foreach (FileSystemEntity entity in entityList.Where(x => x.IsCurrentOrParentDirectory == false))
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

            rootDirectory = await FileSystem.NormalizePathAsync(rootDirectory, cancel: cancel);

            return await WalkDirectoryInternalAsync(rootDirectory, "", callback, callbackForDirectoryAgain, exceptionHandler, recursive, cancel, null);
        }

#pragma warning disable CS1998
        public bool WalkDirectory(string rootDirectory, Func<DirectoryPathInfo, FileSystemEntity[], CancellationToken, bool> callback, Func<DirectoryPathInfo, FileSystemEntity[], CancellationToken, bool> callbackForDirectoryAgain = null, Func<DirectoryPathInfo, Exception, CancellationToken, bool> exceptionHandler = null, bool recursive = true, CancellationToken cancel = default)
            => WalkDirectoryAsync(rootDirectory,
                async (dirInfo, entity, c) => { return callback(dirInfo, entity, c); },
                async (dirInfo, entity, c) => { return (callbackForDirectoryAgain != null) ? callbackForDirectoryAgain(dirInfo, entity, c) : true; },
                async (dirInfo, exception, c) => { if (exceptionHandler == null) throw exception; return exceptionHandler(dirInfo, exception, c); },
                recursive, cancel)._GetResult();
#pragma warning restore CS1998
    }

    public enum EasyFileAccessType
    {
        String,
        Binary,
        HexParsedBinary,
    }

    public class EasyFileAccess
    {
        // Properties
        public string String => (string)this[EasyFileAccessType.String];
        public ReadOnlyMemory<byte> Binary => (ReadOnlyMemory<byte>)this[EasyFileAccessType.Binary];
        public ReadOnlyMemory<byte> HexParsedBinary => (ReadOnlyMemory<byte>)this[EasyFileAccessType.HexParsedBinary];

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

                case EasyFileAccessType.HexParsedBinary:
                    return (ReadOnlyMemory<byte>)FileSystem.ReadStringFromFile(this.FilePath)._GetHexBytes();

                default:
                    throw new ArgumentOutOfRangeException("type");
            }
        }

        public static implicit operator string(EasyFileAccess access) => access.String;
        public static implicit operator ReadOnlyMemory<byte>(EasyFileAccess access) => access.Binary;
        public static implicit operator ReadOnlySpan<byte>(EasyFileAccess access) => access.Binary.Span;
        public static implicit operator byte[] (EasyFileAccess access) => access.Binary.ToArray();
    }

    public abstract class FileObjectRandomAccessWrapperBase : FileObject
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

    public abstract class FileSystemPath
    {
        public string PathString { get; }
        public FileSystem FileSystem { get; }
        public FileFlags Flags { get; }
        public PathParser PathParser => this.FileSystem.PathParser;

        public FileSystemPath(string pathString, FileSystem fileSystem = null, FileFlags flags = FileFlags.None)
        {
            if (pathString == null || pathString == "") throw new ArgumentNullException("pathString");

            if (fileSystem == null) fileSystem = Lfs;

            this.FileSystem = fileSystem;
            this.Flags = flags;

            this.PathString = this.FileSystem.NormalizePath(pathString);
        }

        public bool IsAbsolutePath() => PathParser.IsAbsolutePath(this.PathString);

        public DirectoryPath GetParentDirectory() => new DirectoryPath(PathParser.GetDirectoryName(this.PathString), this.FileSystem, this.Flags);

        public override string ToString() => this.PathString;

        public static implicit operator string(FileSystemPath path) => path.ToString();
    }

    public class DirectoryPath : FileSystemPath
    {
        public DirectoryPath(string pathString, FileSystem fileSystem = null, FileFlags flags = FileFlags.None) : base(pathString, fileSystem, flags)
        {
        }

        public Task CreateDirectoryAsync(CancellationToken cancel = default)
            => this.FileSystem.CreateDirectoryAsync(this.PathString, this.Flags, cancel);
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

        public async Task<DirectoryPath[]> GetDirectoriesAsync(EnumDirectoryFlags flags = EnumDirectoryFlags.None, CancellationToken cancel = default)
        {
            var ents = await this.FileSystem.EnumDirectoryAsync(this.PathString, cancel: cancel, flags: flags);
            List<DirectoryPath> ret = new List<DirectoryPath>();
            foreach (var dir in ents.Where(x => x.IsDirectory))
            {
                ret.Add(new DirectoryPath(dir.FullPath, this.FileSystem, this.Flags));
            }
            return ret.ToArray();
        }
        public DirectoryPath[] GetDirectories(EnumDirectoryFlags flags = EnumDirectoryFlags.None, CancellationToken cancel = default)
            => GetDirectoriesAsync(flags, cancel)._GetResult();

        public async Task<FilePath[]> GetFilesAsync(EnumDirectoryFlags flags = EnumDirectoryFlags.None, CancellationToken cancel = default)
        {
            var ents = await this.FileSystem.EnumDirectoryAsync(this.PathString, cancel: cancel, flags: flags);
            List<FilePath> ret = new List<FilePath>();
            foreach (var dir in ents.Where(x => x.IsDirectory == false))
            {
                ret.Add(new FilePath(dir.FullPath, this.FileSystem, this.Flags));
            }
            return ret.ToArray();
        }
        public FilePath[] GetFiles(EnumDirectoryFlags flags = EnumDirectoryFlags.None, CancellationToken cancel = default)
            => GetFilesAsync(flags, cancel)._GetResult();

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

        public string GetParentDirectoryName() => PathParser.GetDirectoryName(this.PathString);
        public string GetThisDirectoryName() => PathParser.GetFileName(this.PathString);
        public void SepareteParentDirectoryAndThisDirectory(out string parentPath, out string thisDirectoryName) => PathParser.SepareteDirectoryAndFileName(this.PathString, out parentPath, out thisDirectoryName);

        public FilePath Combine(string path2) => new FilePath(PathParser.Combine(this.PathString, path2), this.FileSystem, this.Flags);
        public FilePath Combine(string path2, bool path2NeverAbsolutePath = false) => new FilePath(PathParser.Combine(this.PathString, path2, path2NeverAbsolutePath), this.FileSystem, this.Flags);
        public FilePath Combine(params string[] pathList)
        {
            if (pathList == null || pathList.Length == 0) return new FilePath(this.PathString, this.FileSystem, this.Flags);
            return new FilePath(PathParser.Combine(this.PathString._SingleArray().Concat(pathList)._ToArrayList()), this.FileSystem, this.Flags);
        }

        public DirectoryPath GetSubDirectory(string path2) => new DirectoryPath(PathParser.Combine(this.PathString, path2), this.FileSystem, this.Flags);
        public DirectoryPath GetSubDirectory(string path2, bool path2NeverAbsolutePath = false) => new DirectoryPath(PathParser.Combine(this.PathString, path2, path2NeverAbsolutePath), this.FileSystem, this.Flags);
        public DirectoryPath GetSubDirectory(params string[] pathList)
        {
            if (pathList == null || pathList.Length == 0) return new DirectoryPath(this.PathString, this.FileSystem, this.Flags);
            return new DirectoryPath(PathParser.Combine(this.PathString._SingleArray().Concat(pathList)._ToArrayList()), this.FileSystem, this.Flags);
        }

        public static implicit operator DirectoryPath(string directoryName) => new DirectoryPath(directoryName);

        public bool IsRootDirectory => this.PathParser.IsRootDirectory(this.PathString);

        public IReadOnlyList<DirectoryPath> GetBreadCrumbList()
        {
            DirectoryPath current = this;

            List<DirectoryPath> ret = new List<DirectoryPath>();

            while (true)
            {
                ret.Add(current);

                if (current.IsRootDirectory)
                {
                    break;
                }

                current = current.GetParentDirectory();
            }

            ret.Reverse();

            return ret;
        }
    }

    public class FilePath : FileSystemPath
    {
        readonly Singleton<EasyFileAccess> EasyAccessSingleton;

        public EasyFileAccess EasyAccess => this.EasyAccessSingleton;

        public FilePath(ResourceFileSystem resFs, string partOfPath, bool exact = false, string rootDir = "/", FileFlags operationFlags = FileFlags.None, CancellationToken cancel = default)
            : this((resFs ?? Res.Cores).EasyFindSingleFile(partOfPath, exact, rootDir, cancel), (resFs ?? Res.Cores)) { }

        public FilePath(string pathString, FileSystem fileSystem = null, FileFlags flags = FileFlags.None)
             : base(pathString, fileSystem, flags)
        {
            this.EasyAccessSingleton = new Singleton<EasyFileAccess>(() => new EasyFileAccess(this.FileSystem, this.PathString));
        }

        public static implicit operator FilePath(string fileName) => new FilePath(fileName, flags: FileFlags.AutoCreateDirectory);

        public Task<FileObject> CreateFileAsync(FileMode mode = FileMode.Open, FileAccess access = FileAccess.Read, FileShare share = FileShare.Read, FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
            => this.FileSystem.CreateFileAsync(new FileParameters(this.PathString, mode, access, share, this.Flags | additionalFlags), cancel);

        public FileObject CreateFile(FileMode mode = FileMode.Open, FileAccess access = FileAccess.Read, FileShare share = FileShare.Read, FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
            => CreateFileAsync(mode, access, share, additionalFlags, cancel)._GetResult();

        public Task<FileObject> CreateAsync(bool noShare = false, FileFlags additionalFlags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default)
            => CreateFileAsync(doNotOverwrite ? FileMode.CreateNew : FileMode.Create, FileAccess.ReadWrite, noShare ? FileShare.None : FileShare.Read, additionalFlags, cancel);

        public FileObject Create(bool noShare = false, FileFlags additionalFlags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default)
            => CreateAsync(noShare, additionalFlags, doNotOverwrite, cancel)._GetResult();

        public Task<FileObject> OpenAsync(bool writeMode = false, bool noShare = false, bool readLock = false, FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
            => CreateFileAsync(FileMode.Open, (writeMode ? FileAccess.ReadWrite : FileAccess.Read),
                (noShare ? FileShare.None : ((writeMode || readLock) ? FileShare.Read : (FileShare.ReadWrite | FileShare.Delete))), additionalFlags, cancel);

        public FileObject Open(bool writeMode = false, bool noShare = false, bool readLock = false, FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
            => OpenAsync(writeMode, noShare, readLock, additionalFlags, cancel)._GetResult();

        public Task<FileObject> OpenOrCreateAsync(bool noShare = false, FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
            => CreateFileAsync(FileMode.OpenOrCreate, FileAccess.ReadWrite, noShare ? FileShare.None : FileShare.Read, additionalFlags, cancel);

        public FileObject OpenOrCreate(bool noShare = false, FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
            => OpenOrCreateAsync(noShare, additionalFlags, cancel)._GetResult();

        public Task<FileObject> OpenOrCreateAppendAsync(bool noShare = false, FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
            => CreateFileAsync(FileMode.Append, FileAccess.Write, noShare ? FileShare.None : FileShare.Read, additionalFlags, cancel);

        public FileObject OpenOrCreateAppend(bool noShare = false, FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
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

        public Task DeleteFileAsync(FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
            => this.FileSystem.DeleteFileAsync(this.PathString, this.Flags | additionalFlags, cancel);

        public void DeleteFile(FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
            => DeleteFileAsync(this.Flags | additionalFlags, cancel)._GetResult();

        public Task MoveFileAsync(string destPath, CancellationToken cancel = default)
            => this.FileSystem.MoveFileAsync(this.PathString, destPath, cancel);

        public Task<bool> TryAddOrRemoveAttributeFromExistingFile(FileAttributes attributesToAdd = 0, FileAttributes attributesToRemove = 0, CancellationToken cancel = default)
            => this.FileSystem.TryAddOrRemoveAttributeFromExistingFile(this.PathString, attributesToAdd, attributesToRemove, cancel);

        public Task<int> WriteDataToFileAsync(ReadOnlyMemory<byte> srcMemory, FileFlags additionalFlags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default)
            => this.FileSystem.WriteDataToFileAsync(this.PathString, srcMemory, this.Flags | additionalFlags, doNotOverwrite, cancel);

        public int WriteDataToFile(ReadOnlyMemory<byte> data, FileFlags additionalFlags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default)
            => WriteDataToFileAsync(data, additionalFlags, doNotOverwrite, cancel)._GetResult();

        public Task<int> WriteStringToFileAsync(string srcString, FileFlags additionalFlags = FileFlags.None, bool doNotOverwrite = false, Encoding encoding = null, bool writeBom = false, CancellationToken cancel = default)
            => this.FileSystem.WriteStringToFileAsync(this.PathString, srcString, this.Flags | additionalFlags, doNotOverwrite, encoding, writeBom, cancel);

        public int WriteStringToFile(string srcString, FileFlags additionalFlags = FileFlags.None, bool doNotOverwrite = false, Encoding encoding = null, bool writeBom = false, CancellationToken cancel = default)
            => WriteStringToFileAsync(srcString, additionalFlags, doNotOverwrite, encoding, writeBom, cancel)._GetResult();

        public Task AppendDataToFileAsync(Memory<byte> srcMemory, FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
            => this.FileSystem.AppendDataToFileAsync(this.PathString, srcMemory, this.Flags | additionalFlags, cancel);

        public void AppendDataToFile(Memory<byte> srcMemory, FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
            => AppendDataToFileAsync(srcMemory, additionalFlags, cancel)._GetResult();

        public Task<int> ReadDataFromFileAsync(Memory<byte> destMemory, FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
            => this.FileSystem.ReadDataFromFileAsync(this.PathString, destMemory, this.Flags | additionalFlags, cancel);

        public int ReadDataFromFile(Memory<byte> destMemory, FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
            => ReadDataFromFileAsync(destMemory, additionalFlags, cancel)._GetResult();

        public Task<Memory<byte>> ReadDataFromFileAsync(int maxSize = int.MaxValue, FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
            => this.FileSystem.ReadDataFromFileAsync(this.PathString, maxSize, this.Flags | additionalFlags, cancel);

        public Memory<byte> ReadDataFromFile(int maxSize = int.MaxValue, FileFlags additionalFlags = FileFlags.None, CancellationToken cancel = default)
            => ReadDataFromFileAsync(maxSize, additionalFlags, cancel)._GetResult();

        public Task<string> ReadStringFromFileAsync(Encoding encoding = null, int maxSize = int.MaxValue, FileFlags additionalFlags = FileFlags.None, bool oneLine = false, CancellationToken cancel = default)
            => this.FileSystem.ReadStringFromFileAsync(this.PathString, encoding, maxSize, this.Flags | additionalFlags, oneLine, cancel);

        public string ReadStringFromFile(Encoding encoding = null, int maxSize = int.MaxValue, FileFlags additionalFlags = FileFlags.None, bool oneLine = false, CancellationToken cancel = default)
            => ReadStringFromFileAsync(encoding, maxSize, additionalFlags, oneLine, cancel)._GetResult();

        public Task<T> ReadAndParseDataFileAsync<T>(ReadParseFlags readParseFlags, Func<ReadOnlyMemory<byte>, T> parseProc, Func<ReadOnlyMemory<byte>> createProc, Func<T, ReadOnlyMemory<byte>> serializeProc = null, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
            => this.FileSystem.ReadAndParseDataFileAsync(this.PathString, readParseFlags, parseProc, createProc, serializeProc, maxSize, flags, cancel);

        public T ReadAndParseDataFile<T>(ReadParseFlags readParseFlags, Func<ReadOnlyMemory<byte>, T> parseProc, Func<ReadOnlyMemory<byte>> createProc, Func<T, ReadOnlyMemory<byte>> serializeProc = null, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
            => ReadAndParseDataFileAsync(readParseFlags, parseProc, createProc, serializeProc, maxSize, flags, cancel)._GetResult();

        public string GetFileNameWithoutExtension(bool longExtension = false) => this.PathParser.GetFileNameWithoutExtension(this.PathString, longExtension);
        public string GetExtension(string path, bool longExtension = false) => this.PathParser.GetExtension(this.PathString, longExtension);
        public string GetFileName() => PathParser.GetFileName(this.PathString);
        public void SepareteDirectoryAndFileName(out string dirPath, out string fileName) => PathParser.SepareteDirectoryAndFileName(this.PathString, out dirPath, out fileName);
    }
}
