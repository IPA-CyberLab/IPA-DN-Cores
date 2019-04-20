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

#pragma warning disable CS0162

namespace IPA.Cores.Basic
{
    class VfsException : Exception
    {
        public VfsException(string path, string message) : base($"Entity \"{path}\": {message}") { }
    }

    abstract class VfsEntity
    {
        public VirtualFileSystem FileSystem { get; }
        public readonly RefInt Ref = new RefInt(0);

        public abstract string Name { get; }
        public abstract FileAttributes Attributes { get; }
        public abstract DateTimeOffset CreationTime { get; }
        public abstract DateTimeOffset LastWriteTime { get; }
        public abstract DateTimeOffset LastAccessTime { get; }

        public VfsEntity(VirtualFileSystem fileSystem)
        {
            this.FileSystem = fileSystem;
        }

        protected virtual Task ReleaseImplAsync() => Task.CompletedTask;

        public async Task<int> ReleaseAsync(bool force = false)
        {
            if (force)
            {
                await ReleaseImplAsync();
                return 0;
            }

            int r = Ref.Decrement();
            Debug.Assert(r >= 0);
            if (r == 0)
            {
                try
                {
                    await ReleaseImplAsync();
                }
                catch
                {
                    Ref.Increment();
                    throw;
                }
            }
            return r;
        }

        public int AddRef()
        {
            int r = Ref.Increment();
            Debug.Assert(r >= 1);
            return r;
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
        public abstract Task AddDirectoryAsync(VfsDirectory directory, CancellationToken cancel = default);
        public abstract Task RemoveDirectoryAsync(VfsDirectory directory, bool recursive, CancellationToken cancel = default);
        public abstract Task AddFileAsync(VfsFile file, CancellationToken cancel = default);
        public abstract Task RemoveFileAsync(VfsFile file, CancellationToken cancel = default);
    }

    class VfsRamDirectory : VfsDirectory
    {
        readonly Dictionary<string, VfsEntity> EntityTable;
        readonly AsyncLock LockObj = new AsyncLock();

        public VfsRamDirectory(VirtualFileSystem fileSystem, string name, bool isRoot = false) : base(fileSystem)
        {
            if (isRoot)
                name = "/";
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
                int r = directory.AddRef();
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
                    await directory.ReleaseAsync();
                    throw;
                }
            }
        }

        public override async Task AddFileAsync(VfsFile file, CancellationToken cancel = default)
        {
            using (await LockObj.LockWithAwait(cancel))
            {
                int r = file.AddRef();
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
                    await file.ReleaseAsync();
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

        public override async Task RemoveDirectoryAsync(VfsDirectory directory, bool recursive, CancellationToken cancel = default)
        {
            using (await LockObj.LockWithAwait(cancel))
            {
                if (EntityTable.ContainsValue(directory) == false)
                    throw new VfsException(directory.Name, $"The object is not contained on the parent directory \"{this.Name}\".");

                await directory.ReleaseAsync();

                EntityTable.Remove(directory.Name);
            }
        }

        public override async Task RemoveFileAsync(VfsFile file, CancellationToken cancel = default)
        {
            using (await LockObj.LockWithAwait(cancel))
            {
                if (EntityTable.ContainsValue(file) == false)
                    throw new VfsException(file.Name, $"The object is not contained on the parent directory \"{this.Name}\".");

                await file.ReleaseAsync();

                EntityTable.Remove(file.Name);
            }
        }

        protected override Task ReleaseImplAsync()
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

    class VirtualFileSystem : FileSystemBase
    {
        readonly VfsDirectory Root;

        public VirtualFileSystem(AsyncCleanuperLady lady) : base(lady, FileSystemPathParser.GetInstance(FileSystemStyle.Linux))
        {
            this.Root = new VfsRamDirectory(this, "/", true);
        }

        protected override Task CreateDirectoryImplAsync(string directoryPath, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
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

        protected override Task<FileSystemEntity[]> EnumDirectoryImplAsync(string directoryPath, EnumDirectoryFlags flags, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }

        protected override Task<FileMetadata> GetDirectoryMetadataImplAsync(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }

        protected override Task<FileMetadata> GetFileMetadataImplAsync(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }

        protected override Task<bool> IsDirectoryExistsImplAsync(string path, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }

        protected override Task<bool> IsFileExistsImplAsync(string path, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }

        protected override Task MoveDirectoryImplAsync(string srcPath, string destPath, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }

        protected override Task MoveFileImplAsync(string srcPath, string destPath, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }

        protected override Task<string> NormalizePathImplAsync(string path, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
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
