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
using System.Collections.Immutable;

#pragma warning disable CS0162

namespace IPA.Cores.Basic
{
    class VfsException : Exception
    {
        public VfsException(string path, string message) : base($"Entity \"{path}\": {message}") { }
    }

    class VfsNotFoundException : VfsException
    {
        public VfsNotFoundException(string path, string message) : base(path, message) { }
    }

    abstract class VfsEntity
    {
        public VirtualFileSystem FileSystem { get; }
        readonly RefInt LinkRef = new RefInt();
        readonly RefInt HandleRef = new RefInt();
        readonly CriticalSection LockObj = new CriticalSection();

        public abstract string Name { get; }
        public abstract FileAttributes Attributes { get; }
        public abstract DateTimeOffset CreationTime { get; }
        public abstract DateTimeOffset LastWriteTime { get; }
        public abstract DateTimeOffset LastAccessTime { get; }

        volatile bool ReleasedFlag = false;
        public bool IsReleased => this.LinkRef.Value <= 0 || ReleasedFlag;

        public VfsEntity(VirtualFileSystem fileSystem)
        {
            this.FileSystem = fileSystem;
        }

        protected virtual Task ReleaseLinkImplAsync() => Task.CompletedTask;

        public async Task<int> ReleaseLinkAsync(bool force = false)
        {
            if (force)
            {
                ReleasedFlag = true;
                try
                {
                    await ReleaseLinkImplAsync();
                }
                catch { }
                return 0;
            }

            int r;

            lock (LockObj)
            {
                Debug.Assert(this.HandleRef.Value >= 0);
                if (this.HandleRef.Value >= 1)
                {
                    throw new VfsException(this.Name, "The object handle is still opened.");
                }

                r = LinkRef.Decrement();
                Debug.Assert(r >= 0);
            }

            if (r == 0)
            {
                try
                {
                    await ReleaseLinkImplAsync();
                    ReleasedFlag = true;
                }
                catch
                {
                    LinkRef.Increment();
                    throw;
                }
            }
            return r;
        }

        public int AddLinkRef()
        {
            lock (LockObj)
            {
                int r = LinkRef.Increment();
                Debug.Assert(r >= 1);
                return r;
            }
        }

        public int AddHandleRef()
        {
            lock (LockObj)
            {
                if (this.IsReleased)
                    throw new VfsException(this.Name, "The object is already released.");

                int r = HandleRef.Increment();
                Debug.Assert(r >= 1);
                return r;
            }
        }

        public int ReleaseHandleRef()
        {
            lock (LockObj)
            {
                int r = HandleRef.Decrement();
                Debug.Assert(r >= 0);
                return r;
            }
        }
    }

    abstract class VfsFile : VfsEntity
    {
        public VfsFile(VirtualFileSystem fileSystem) : base(fileSystem)
        {
        }

        public abstract long Size { get; }
        public abstract long PhysicalSize { get; }
        public abstract Task<FileObject> OpenAsync(FileParameters option, CancellationToken cancel = default);
        public abstract Task<FileMetadata> GetFileMetadataAsync(FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default);
        public abstract Task SetFileMetadataAsync(FileMetadata metadata, CancellationToken cancel = default);
    }

    abstract class VfsDirectory : VfsEntity
    {
        public VfsDirectory(VirtualFileSystem fileSystem) : base(fileSystem)
        {
        }

        public virtual bool IsRoot { get; protected set; }
        public abstract Task<VfsEntity[]> EnumEntitiesAsync(EnumDirectoryFlags flags, CancellationToken cancel = default);
        public abstract Task<Holder<VfsEntity>> OpenEntityAsync(string name, CancellationToken cancel = default);
        public abstract Task AddDirectoryAsync(VfsDirectory directory, CancellationToken cancel = default);
        public abstract Task RemoveDirectoryAsync(VfsDirectory directory, bool recursive, CancellationToken cancel = default);
        public abstract Task AddFileAsync(VfsFile file, CancellationToken cancel = default);
        public abstract Task RemoveFileAsync(VfsFile file, CancellationToken cancel = default);

        public virtual async Task ParseAsync(VfsPathParserContext ctx, CancellationToken cancel = default)
        {
            string nextName = ctx.RemainingPathElements.Dequeue();

            if (nextName == null)
            {
                // This is the final entity
                ctx.Exception = null;
                return;
            }

            try
            {
                using (var nextEntityHolder = await this.OpenEntityAsync(nextName, cancel))
                {
                    VfsEntity nextEntity = nextEntityHolder.Value;

                    if (nextEntity is VfsDirectory nextDirectory)
                    {
                        ctx.EntityStack.Add(nextDirectory);
                        ctx.NormalizedPathStack.Add(nextDirectory.Name);

                        await nextDirectory.ParseAsync(ctx, cancel);
                    }
                    else if (nextEntity is VfsFile nextFile)
                    {
                        if (ctx.RemainingPathElements.Count >= 1)
                        {
                            throw new VfsNotFoundException(ctx.SpecifiedPath,
                                $"Invalid directory name. The file \"{nextName}\" is found on the directory \"{this.FileSystem.PathParser.BuildPathStringFromElements(ctx.NormalizedPathStack)}\".");
                        }

                        ctx.EntityStack.Add(nextFile);
                        ctx.NormalizedPathStack.Add(nextFile.Name);
                        ctx.Exception = null;

                        return;
                    }
                    else
                    {
                        throw new VfsException(ctx.SpecifiedPath,
                            $"The object \"{nextName}\" is unknown type \"{nextEntity.GetType().ToString()}\".");
                    }
                }
            }
            catch (VfsNotFoundException)
            {
                ctx.Exception = new VfsNotFoundException(ctx.SpecifiedPath,
                    $"The object \"{nextName}\" is not found on the directory \"{this.FileSystem.PathParser.BuildPathStringFromElements(ctx.NormalizedPathStack)}\".");

                return;
            }
        }
    }

    class VfsRamDirectory : VfsDirectory
    {
        readonly Dictionary<string, VfsEntity> EntityTable;
        readonly AsyncLock LockObj = new AsyncLock();

        public VfsRamDirectory(VirtualFileSystem fileSystem, string name, bool isRoot = false) : base(fileSystem)
        {
            if (isRoot)
                name = "";
            else
                fileSystem.PathParser.ValidateFileOrDirectoryName(name);

            this.IsRoot = isRoot;

            this.Name = name;
            this.Attributes = FileAttributes.Directory;
            this.CreationTime = this.LastAccessTime = this.LastWriteTime = DateTimeOffset.Now;

            this.EntityTable = new Dictionary<string, VfsEntity>(fileSystem.PathParser.PathStringComparer);
        }

        public override string Name { get; }
        public override FileAttributes Attributes { get; }
        public override DateTimeOffset CreationTime { get; }
        public override DateTimeOffset LastWriteTime { get; }
        public override DateTimeOffset LastAccessTime { get; }

        public override async Task AddDirectoryAsync(VfsDirectory directory, CancellationToken cancel = default)
        {
            using (await LockObj.LockWithAwait(cancel))
            {
                int r = directory.AddLinkRef();
                try
                {
                    if (r >= 2) throw new VfsException(directory.Name, "The directory object is already referenced by other directory.");
                    if (directory.FileSystem != this.FileSystem) throw new VfsException(directory.Name, "directory.FileSystem != this.FileSystem");
                    FileSystem.PathParser.ValidateFileOrDirectoryName(directory.Name);
                    if (EntityTable.ContainsKey(directory.Name))
                        throw new VfsException(directory.Name, "The same name already exists.");
                    EntityTable.Add(directory.Name, directory);
                }
                catch
                {
                    await directory.ReleaseLinkAsync();
                    throw;
                }
            }
        }

        public override async Task AddFileAsync(VfsFile file, CancellationToken cancel = default)
        {
            using (await LockObj.LockWithAwait(cancel))
            {
                int r = file.AddLinkRef();
                try
                {
                    if (file.FileSystem != this.FileSystem) throw new VfsException(file.Name, "file.FileSystem != this.FileSystem");
                    FileSystem.PathParser.ValidateFileOrDirectoryName(file.Name);
                    if (EntityTable.ContainsKey(file.Name))
                        throw new VfsException(file.Name, "The same name already exists.");
                    EntityTable.Add(file.Name, file);
                }
                catch
                {
                    await file.ReleaseLinkAsync();
                    throw;
                }
            }
        }

        public override async Task<VfsEntity[]> EnumEntitiesAsync(EnumDirectoryFlags flags, CancellationToken cancel = default)
        {
            await Task.CompletedTask;

            using (await LockObj.LockWithAwait(cancel))
            {
                var ret = this.EntityTable.Values.OrderBy(x => x.Name, this.FileSystem.PathParser.PathStringComparer).ToArray();

                return ret;
            }
        }

        public override async Task<Holder<VfsEntity>> OpenEntityAsync(string name, CancellationToken cancel = default)
        {
            using (await LockObj.LockWithAwait(cancel))
            {
                if (this.EntityTable.TryGetValue(name, out VfsEntity entity) == false)
                    throw new VfsNotFoundException(name, $"The object \"{name}\" not found on the directory.");

                entity.AddHandleRef();

                return new Holder<VfsEntity>(
                    (e) =>
                    {
                        e.ReleaseHandleRef();
                    },
                    leakCheckKind: LeakCounterKind.VfsOpenEntity);
            }
        }

        public override async Task RemoveDirectoryAsync(VfsDirectory directory, bool recursive, CancellationToken cancel = default)
        {
            using (await LockObj.LockWithAwait(cancel))
            {
                if (EntityTable.ContainsValue(directory) == false)
                    throw new VfsException(directory.Name, $"The object is not contained on the parent directory \"{this.Name}\".");

                await directory.ReleaseLinkAsync();

                EntityTable.Remove(directory.Name);
            }
        }

        public override async Task RemoveFileAsync(VfsFile file, CancellationToken cancel = default)
        {
            using (await LockObj.LockWithAwait(cancel))
            {
                if (EntityTable.ContainsValue(file) == false)
                    throw new VfsException(file.Name, $"The object is not contained on the parent directory \"{this.Name}\".");

                await file.ReleaseLinkAsync();

                EntityTable.Remove(file.Name);
            }
        }

        protected override Task ReleaseLinkImplAsync()
        {
            lock (LockObj)
            {
                if (EntityTable.Count >= 2)
                    throw new VfsException(this.Name, $"This directory has one or more entities.");

                return Task.CompletedTask;
            }
        }
    }

    abstract class VirtualFileSystemParamsBase { }

    class VfsPathParserContext : IDisposable
    {
        readonly FileSystemPathParser Parser;
        public readonly string SpecifiedPath;
        public readonly Queue<string> RemainingPathElements = new Queue<string>();

        public VfsPathParserContext(FileSystemPathParser parser, string[] pathElements)
        {
            this.Parser = parser;
            foreach (string element in pathElements)
            {
                this.RemainingPathElements.Enqueue(element);
            }
            this.SpecifiedPath = this.Parser.BuildPathStringFromElements(pathElements);

            Exception = new VfsException(SpecifiedPath, "Unknown path parser error.");
        }

        public readonly List<VfsEntity> EntityStack = new List<VfsEntity>();
        public readonly List<string> NormalizedPathStack = new List<string>();
        public string NormalizedPath => Parser.BuildPathStringFromElements(NormalizedPathStack);
        public Exception Exception = null;
        public VfsEntity LastEntity => EntityStack.LastOrDefault();

        public void AddToEntityStack(VfsEntity entity)
        {
            entity.AddHandleRef();
            EntityStack.Add(entity);
        }

        public void Dispose() => Dispose(true);
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;

            foreach (VfsEntity entity in EntityStack)
                entity.ReleaseHandleRef();
        }
    }

    class VirtualFileSystem : FileSystemBase
    {
        readonly VfsDirectory Root;

        public VirtualFileSystem(AsyncCleanuperLady lady) : base(lady, FileSystemPathParser.GetInstance(FileSystemStyle.Linux))
        {
            this.Root = new VfsRamDirectory(this, "/", true);
        }

        async Task<VfsPathParserContext> ParsePathInternalAsync(string path, CancellationToken cancel = default)
        {
            if (path.StartsWith("/") == false)
                throw new VfsException(path, "The speficied path is not an absolute path.");

            string[] pathStack = this.PathParser.SplitPathToElements(path);

            VfsPathParserContext ctx = new VfsPathParserContext(this.PathParser, pathStack);
            try
            {
                await Root.ParseAsync(ctx, cancel);

                return ctx;
            }
            catch
            {
                ctx.DisposeSafe();
                throw;
            }
        }

        protected override async Task CreateDirectoryImplAsync(string directoryPath, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
        {
            using (VfsPathParserContext ctx = await ParsePathInternalAsync(directoryPath, cancel))
            {
                if (ctx.Exception == null)
                {
                    if (ctx.LastEntity is VfsDirectory)
                    {
                        // Already exists
                        return;
                    }
                    else
                    {
                        // There is existing another type object
                        throw new VfsException(directoryPath, $"There are existing object at the speficied path.");
                    }
                }

                if (ctx.Exception is VfsNotFoundException && ctx.RemainingPathElements.Count >= 1 && ctx.LastEntity is VfsDirectory)
                {
                    var lastDir = ctx.LastEntity as VfsDirectory;

                    while (true)
                    {
                        string nextDirName = ctx.RemainingPathElements.Dequeue();

                        if (nextDirName == null)
                            return;

                        var newDirectory = new VfsRamDirectory(this, nextDirName);
                        try
                        {
                            await lastDir.AddDirectoryAsync(newDirectory);
                        }
                        catch
                        {
                            await newDirectory.ReleaseLinkAsync(true);
                            throw;
                        }

                        lastDir = newDirectory;
                    }
                }
                else
                {
                    throw ctx.Exception;
                }
            }
        }

        protected override Task<FileObject> CreateFileImplAsync(FileParameters option, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }

        protected override Task DeleteDirectoryImplAsync(string directoryPath, bool recursive, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }

        protected override Task DeleteFileImplAsync(string path, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }

        protected override async Task<FileSystemEntity[]> EnumDirectoryImplAsync(string directoryPath, EnumDirectoryFlags flags, CancellationToken cancel = default)
        {
            using (VfsPathParserContext ctx = await ParsePathInternalAsync(directoryPath, cancel))
            {
                if (ctx.Exception != null)
                    throw ctx.Exception;

                if (ctx.LastEntity is VfsDirectory thisDirObject)
                {
                    var entities = await thisDirObject.EnumEntitiesAsync(flags, cancel);
                    List<FileSystemEntity> ret = new List<FileSystemEntity>();

                    FileSystemEntity thisDir = new FileSystemEntity()
                    {
                        FullPath = PathParser.Combine(ctx.NormalizedPath),
                        Name = ".",
                        Attributes = thisDirObject.Attributes,
                        CreationTime = thisDirObject.CreationTime,
                        LastWriteTime = thisDirObject.LastWriteTime,
                        LastAccessTime = thisDirObject.LastAccessTime,
                    };
                    ret.Add(thisDir);

                    foreach (var entity in entities)
                    {
                        if (entity is VfsDirectory dirObject)
                        {
                            FileSystemEntity dir = new FileSystemEntity()
                            {
                                FullPath = PathParser.Combine(ctx.NormalizedPath, entity.Name),
                                Name = entity.Name,
                                Attributes = (entity.Attributes | FileAttributes.Directory) & ~FileAttributes.Normal,
                                CreationTime = entity.CreationTime,
                                LastWriteTime = entity.LastWriteTime,
                                LastAccessTime = entity.LastAccessTime,
                            };
                            ret.Add(dir);
                        }
                        else if (entity is VfsFile fileObject)
                        {
                            FileSystemEntity file = new FileSystemEntity()
                            {
                                FullPath = PathParser.Combine(ctx.NormalizedPath, entity.Name),
                                Name = entity.Name,
                                Size = fileObject.Size,
                                PhysicalSize = fileObject.PhysicalSize,
                                Attributes = entity.Attributes & ~FileAttributes.Directory,
                                CreationTime = entity.CreationTime,
                                LastWriteTime = entity.LastWriteTime,
                                LastAccessTime = entity.LastAccessTime,
                            };
                            ret.Add(file);
                        }
                    }

                    return ret.ToArray();
                }
                else
                {
                    throw new VfsNotFoundException(directoryPath, "Directory not found.");
                }
            }
        }

        protected override async Task<FileMetadata> GetDirectoryMetadataImplAsync(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
        {
            using (VfsPathParserContext ctx = await ParsePathInternalAsync(path, cancel))
            {
                if (ctx.Exception != null)
                    throw ctx.Exception;

                if (ctx.LastEntity is VfsDirectory dir)
                {
                    return new FileMetadata(
                        isDirectory: true,
                        attributes: (dir.Attributes | FileAttributes.Directory) & ~FileAttributes.Normal,
                        creationTime: dir.CreationTime,
                        lastWriteTime: dir.LastWriteTime,
                        lastAccessTime: dir.LastAccessTime
                        );
                }
                else
                {
                    throw new VfsNotFoundException(path, "Directory not found.");
                }
            }
        }

        protected override async Task<FileMetadata> GetFileMetadataImplAsync(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
        {
            using (VfsPathParserContext ctx = await ParsePathInternalAsync(path, cancel))
            {
                if (ctx.Exception != null)
                    throw ctx.Exception;

                if (ctx.LastEntity is VfsFile file)
                {
                    return new FileMetadata(
                        isDirectory: false,
                        attributes: (file.Attributes) & ~FileAttributes.Directory,
                        creationTime: file.CreationTime,
                        lastWriteTime: file.LastWriteTime,
                        lastAccessTime: file.LastAccessTime,
                        size: file.Size,
                        physicalSize: file.PhysicalSize
                        );
                }
                else
                {
                    throw new VfsNotFoundException(path, "File not found.");
                }
            }
        }

        protected override async Task<bool> IsDirectoryExistsImplAsync(string path, CancellationToken cancel = default)
        {
            using (VfsPathParserContext ctx = await ParsePathInternalAsync(path, cancel))
            {
                if (ctx.Exception == null)
                    if (ctx.LastEntity is VfsDirectory)
                        return true;
            }

            return false;
        }

        protected override async Task<bool> IsFileExistsImplAsync(string path, CancellationToken cancel = default)
        {
            using (VfsPathParserContext ctx = await ParsePathInternalAsync(path, cancel))
            {
                if (ctx.Exception == null)
                    if (ctx.LastEntity is VfsFile)
                        return true;
            }

            return false;
        }

        protected override Task MoveDirectoryImplAsync(string srcPath, string destPath, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }

        protected override Task MoveFileImplAsync(string srcPath, string destPath, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }

        protected override async Task<string> NormalizePathImplAsync(string path, CancellationToken cancel = default)
        {
            using (VfsPathParserContext ctx = await ParsePathInternalAsync(path, cancel))
            {
                return this.PathParser.BuildPathStringFromElements(ctx.NormalizedPathStack.Concat(ctx.RemainingPathElements));
            }
        }

        protected override Task SetDirectoryMetadataImplAsync(string path, FileMetadata metadata, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }

        protected override Task SetFileMetadataImplAsync(string path, FileMetadata metadata, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }
    }
}
