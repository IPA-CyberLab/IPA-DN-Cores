using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    class GitFileObject : FileObject
    {
        protected GitFileObject(GitFileSystem fileSystem, FileParameters fileParams) : base(fileSystem, fileParams) { }

        public static async Task<GitFileObject> CreateFileAsync(GitFileSystem fileSystem, FileParameters fileParams, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();

            GitFileObject f = new GitFileObject(fileSystem, fileParams);

            await f.InternalInitAsync(cancel);

            return f;
        }

        protected async Task InternalInitAsync(CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();

            await Task.CompletedTask;
        }

        protected override Task CloseImplAsync()
        {
            throw new NotImplementedException();
        }

        protected override Task FlushImplAsync(CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }

        protected override Task<long> GetFileSizeImplAsync(CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }

        protected override Task<int> ReadRandomImplAsync(long position, Memory<byte> data, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }

        protected override Task SetFileSizeImplAsync(long size, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }

        protected override Task WriteRandomImplAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }
    }

    class GitFileSystemParams : FileSystemParams
    {
        public Repository Repository { get; }
        public string CommitId { get; }

        public GitFileSystemParams(Repository repository, string commitId)
            : base(FileSystemPathParser.GetInstance(FileSystemStyle.Linux), FileSystemMode.ReadOnly)
        {
            this.Repository = repository;
            this.CommitId = commitId;
        }
    }

    class GitFileSystem : FileSystem
    {
        protected new GitFileSystemParams Params => (GitFileSystemParams)base.Params;
        protected Repository Repository => Params.Repository;

        public GitFileSystem(GitFileSystemParams param) : base(param)
        {
            
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

        protected override Task<string> NormalizePathImplAsync(string path, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
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

        protected override Task MoveDirectoryImplAsync(string srcPath, string destPath, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }

        protected override Task MoveFileImplAsync(string srcPath, string destPath, CancellationToken cancel = default)
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

