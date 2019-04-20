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

#pragma warning disable CS0162

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

        public async Task<int> WriteToFileAsync(string path, Memory<byte> srcMemory, FileOperationFlags flags = FileOperationFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default)
        {
            if (flags.Bit(FileOperationFlags.WriteOnlyIfChanged))
            {
                try
                {
                    if (await IsFileExistsAsync(path, cancel))
                    {
                        Memory<byte> existingData = await ReadFromFileAsync(path, srcMemory.Length, flags, cancel);
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
        public int WriteToFile(string path, Memory<byte> data, FileOperationFlags flags = FileOperationFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default)
            => WriteToFileAsync(path, data, flags, doNotOverwrite, cancel).GetResult();

        public Task<int> WriteToFileAsync(string path, string srcString, FileOperationFlags flags = FileOperationFlags.None, bool doNotOverwrite = false, Encoding encoding = null, bool writeBom = false, CancellationToken cancel = default)
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

                return WriteToFileAsync(path, buf.Memory, flags, doNotOverwrite, cancel);
            }
        }
        public int WriteToFile(string path, string srcString, FileOperationFlags flags = FileOperationFlags.None, bool doNotOverwrite = false, Encoding encoding = null, bool writeBom = false, CancellationToken cancel = default)
            => WriteToFileAsync(path, srcString, flags, doNotOverwrite, encoding, writeBom, cancel).GetResult();

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

}
