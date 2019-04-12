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
    class ViewFileObject : FileObject
    {
        readonly ViewFileSystem ViewFileSystem;
        FileSystemBase UnderlayFileSystem => ViewFileSystem.UnderlayFileSystem;
        protected FileObject UnderlayFile { get; private set; }

        public ViewFileObject(ViewFileSystem fileSystem, FileParameters fileParams) : base(fileSystem, fileParams)
        {
            this.ViewFileSystem = fileSystem;
        }

        internal async Task _InternalCreateFileAsync(CancellationToken cancel = default)
        {
            if (UnderlayFile != null)
                throw new ApplicationException("Already inited.");

            this.UnderlayFile = await CreateUnderlayFileImplAsync(this.FileParams, cancel);
        }

        protected virtual Task<FileObject> CreateUnderlayFileImplAsync(FileParameters option, CancellationToken cancel = default)
            => UnderlayFileSystem.CreateFileAsync(option, cancel);

        protected override Task CloseImplAsync()
            => this.UnderlayFile.CloseAsync();

        protected override Task FlushImplAsync(CancellationToken cancel = default)
            => this.UnderlayFile.FlushAsync(cancel);

        protected override Task<long> GetFileSizeImplAsync(CancellationToken cancel = default)
            => this.UnderlayFile.GetFileSizeAsync(false, cancel);

        protected override Task<int> ReadRandomImplAsync(long position, Memory<byte> data, CancellationToken cancel = default)
            => this.UnderlayFile.ReadRandomAsync(position, data, cancel);

        protected override Task SetFileSizeImplAsync(long size, CancellationToken cancel = default)
            => this.UnderlayFile.SetFileSizeAsync(size, cancel);

        protected override Task WriteRandomImplAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
            => this.UnderlayFile.WriteRandomAsync(position, data, cancel);
    }

    abstract class ViewFileSystemParamsBase { }

    class ViewFileSystem : FileSystemBase
    {
        public FileSystemBase UnderlayFileSystem { get; }
        public ViewFileSystemParamsBase Params { get; }

        public ViewFileSystem(FileSystemBase underlayFileSystem, ViewFileSystemParamsBase param) : base(new AsyncCleanuperLady(), underlayFileSystem.PathInterpreter)
        {
            this.UnderlayFileSystem = underlayFileSystem;
            this.Params = param;
        }

        protected override Task<string> NormalizePathImplAsync(string path, CancellationToken cancel = default)
            => UnderlayFileSystem.NormalizePathAsync(path, cancel);

        protected override async Task<FileObject> CreateFileImplAsync(FileParameters option, CancellationToken cancel = default)
        {
            ViewFileObject fileObj = new ViewFileObject(this, option);

            await fileObj._InternalCreateFileAsync(cancel);

            return fileObj;
        }

        protected override Task DeleteFileImplAsync(string path, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
            => UnderlayFileSystem.DeleteFileAsync(path, flags, cancel);

        protected override Task CreateDirectoryImplAsync(string directoryPath, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
            => UnderlayFileSystem.CreateDirectoryAsync(directoryPath, flags, cancel);

        protected override Task DeleteDirectoryImplAsync(string directoryPath, bool recursive, CancellationToken cancel = default)
            => UnderlayFileSystem.DeleteDirectoryAsync(directoryPath, recursive, cancel);

        protected override Task<FileSystemEntity[]> EnumDirectoryImplAsync(string directoryPath, CancellationToken cancel = default)
            => UnderlayFileSystem.EnumDirectoryAsync(directoryPath, false, cancel);

        protected override Task<FileMetadata> GetFileMetadataImplAsync(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.None, CancellationToken cancel = default)
            => UnderlayFileSystem.GetFileMetadataAsync(path, flags, cancel);

        protected override Task SetFileMetadataImplAsync(string path, FileMetadata metadata, CancellationToken cancel = default)
            => UnderlayFileSystem.SetFileMetadataAsync(path, metadata, cancel);

        protected override Task<FileMetadata> GetDirectoryMetadataImplAsync(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.None, CancellationToken cancel = default)
            => UnderlayFileSystem.GetDirectoryMetadataAsync(path, flags, cancel);

        protected override Task SetDirectoryMetadataImplAsync(string path, FileMetadata metadata, CancellationToken cancel = default)
            => UnderlayFileSystem.SetDirectoryMetadataAsync(path, metadata, cancel);

        protected override Task MoveFileImplAsync(string srcPath, string destPath, CancellationToken cancel = default)
            => UnderlayFileSystem.MoveFileAsync(srcPath, destPath, cancel);

        protected override Task MoveDirectoryImplAsync(string srcPath, string destPath, CancellationToken cancel = default)
            => UnderlayFileSystem.MoveDirectoryAsync(srcPath, destPath, cancel);
    }
}

