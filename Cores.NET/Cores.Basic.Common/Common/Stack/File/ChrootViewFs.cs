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

namespace IPA.Cores.Basic
{
    class ChrootViewFileSystemParam : ViewFileSystemParams
    {
        public ChrootViewFileSystemParam(FileSystem underlayFileSystem)
            : base(underlayFileSystem, underlayFileSystem.PathParser.Style == FileSystemStyle.Windows ? FileSystemPathParser.GetInstance(FileSystemStyle.Mac) : underlayFileSystem.PathParser)
            // Use the Mac OS X path parser if the underlay file system is Windows
        {
        }
    }


    class ChrootViewFileSystem : ViewFileSystem
    {
        public ChrootViewFileSystem(ViewFileSystemParams param) : base(param)
        {
        }

        protected override Task CreateDirectoryImplAsync(string directoryPath, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
        {
            return base.CreateDirectoryImplAsync(directoryPath, flags, cancel);
        }

        protected override Task<FileObject> CreateFileImplAsync(FileParameters option, CancellationToken cancel = default)
        {
            return base.CreateFileImplAsync(option, cancel);
        }

        protected override Task DeleteDirectoryImplAsync(string directoryPath, bool recursive, CancellationToken cancel = default)
        {
            return base.DeleteDirectoryImplAsync(directoryPath, recursive, cancel);
        }

        protected override Task DeleteFileImplAsync(string path, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
        {
            return base.DeleteFileImplAsync(path, flags, cancel);
        }

        protected override Task<FileSystemEntity[]> EnumDirectoryImplAsync(string directoryPath, EnumDirectoryFlags flags, CancellationToken cancel = default)
        {
            return base.EnumDirectoryImplAsync(directoryPath, flags, cancel);
        }

        protected override string FindEasyAccessFilePathFromNameImpl(string name)
        {
            return base.FindEasyAccessFilePathFromNameImpl(name);
        }

        protected override Task<FileMetadata> GetDirectoryMetadataImplAsync(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
        {
            return base.GetDirectoryMetadataImplAsync(path, flags, cancel);
        }

        protected override Task<FileMetadata> GetFileMetadataImplAsync(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
        {
            return base.GetFileMetadataImplAsync(path, flags, cancel);
        }

        protected override Task<bool> IsDirectoryExistsImplAsync(string path, CancellationToken cancel = default)
        {
            return base.IsDirectoryExistsImplAsync(path, cancel);
        }

        protected override Task<bool> IsFileExistsImplAsync(string path, CancellationToken cancel = default)
        {
            return base.IsFileExistsImplAsync(path, cancel);
        }

        protected override Task MoveDirectoryImplAsync(string srcPath, string destPath, CancellationToken cancel = default)
        {
            return base.MoveDirectoryImplAsync(srcPath, destPath, cancel);
        }

        protected override Task MoveFileImplAsync(string srcPath, string destPath, CancellationToken cancel = default)
        {
            return base.MoveFileImplAsync(srcPath, destPath, cancel);
        }

        protected override Task<string> NormalizePathImplAsync(string path, CancellationToken cancel = default)
        {
            return base.NormalizePathImplAsync(path, cancel);
        }

        protected override Task SetDirectoryMetadataImplAsync(string path, FileMetadata metadata, CancellationToken cancel = default)
        {
            return base.SetDirectoryMetadataImplAsync(path, metadata, cancel);
        }

        protected override Task SetFileMetadataImplAsync(string path, FileMetadata metadata, CancellationToken cancel = default)
        {
            return base.SetFileMetadataImplAsync(path, metadata, cancel);
        }
    }
}
