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
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Diagnostics;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.GlobalFunctions.Basic;

#pragma warning disable CS0162

namespace IPA.Cores.Basic
{
    class PalFileSystem : FileSystemBase
    {
        public PalFileSystem(AsyncCleanuperLady lady) : base(lady, Env.LocalFileSystemPathInterpreter)
        {
        }

        protected override Task<FileObjectBase> CreateFileImplAsync(FileParameters fileParams, CancellationToken cancel = default)
            => PalFileObject.CreateFileAsync(this, fileParams, cancel);

        protected override async Task<FileSystemEntity[]> EnumDirectoryImplAsync(string directoryPath, CancellationToken cancel = default)
        {
            DirectoryInfo di = new DirectoryInfo(directoryPath);

            List<FileSystemEntity> o = new List<FileSystemEntity>();

            FileSystemEntity currentDirectory = ConvertFileSystemInfoToFileSystemEntity(di);
            currentDirectory.Name = ".";
            o.Add(currentDirectory);

            foreach (FileSystemInfo info in di.GetFileSystemInfos().Where(x => x.Exists))
            {
                FileSystemEntity entity = ConvertFileSystemInfoToFileSystemEntity(info);

                if (entity.IsSymbolicLink)
                {
                    entity.SymbolicLinkTarget = ReadSymbolicLinkTarget(entity.FullPath);
                }

                o.Add(entity);
            }

            await Task.CompletedTask;

            return o.ToArray();
        }

        protected override async Task CreateDirectoryImplAsync(string directoryPath, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
        {
            if (Directory.Exists(directoryPath) == false)
            {
                Directory.CreateDirectory(directoryPath);
            }

            if (flags.Bit(FileOperationFlags.SetCompressionFlagOnDirectory))
                Win32FolderCompression.SetFolderCompression(directoryPath, true);

            await Task.CompletedTask;
        }


        protected override async Task DeleteDirectoryImplAsync(string directoryPath, bool recursive, CancellationToken cancel = default)
        {
            Directory.Delete(directoryPath, recursive);

            await Task.CompletedTask;
        }

        public static FileSystemEntity ConvertFileSystemInfoToFileSystemEntity(FileSystemInfo info)
        {
            FileSystemEntity ret = new FileSystemEntity()
            {
                Name = info.Name,
                FullPath = info.FullName,
                Size = info.Attributes.Bit(FileAttributes.Directory) ? 0 : ((FileInfo)info).Length,
                Attributes = info.Attributes,
                Updated = info.LastWriteTime.AsDateTimeOffset(true),
                Created = info.CreationTime.AsDateTimeOffset(true),
            };
            return ret;
        }

        public static FileMetadata ConvertFileSystemInfoToFileMetadata(FileSystemInfo info)
        {
            FileMetadata ret = new FileMetadata()
            {
                Size = info.Attributes.Bit(FileAttributes.Directory) ? 0 : ((FileInfo)info).Length,
                Attributes = info.Attributes,
                Updated = info.LastWriteTime.AsDateTimeOffset(true),
                Created = info.CreationTime.AsDateTimeOffset(true),
                IsDirectory = info.Attributes.Bit(FileAttributes.Directory),
            };
            return ret;
        }

        public static void SetFileMetadataToFileSystemInfo(FileSystemInfo info, FileMetadata metadata)
        {
            if (metadata.Updated is DateTimeOffset dt1)
                info.LastWriteTimeUtc = dt1.UtcDateTime;

            if (metadata.Created is DateTimeOffset dt2)
                info.CreationTimeUtc = dt2.UtcDateTime;

            if (metadata.Attributes is FileAttributes attr)
                info.Attributes = attr;
        }

        public static string ReadSymbolicLinkTarget(string linkPath)
        {
            if (Env.IsUnix)
            {
                return UnixApi.ReadLink(linkPath);
            }
            else
            {
                return "*Error*";
            }
        }

        protected override Task<string> NormalizePathImplAsync(string path, CancellationToken cancel = default)
        {
            return Task.FromResult(Path.GetFullPath(path));
        }

        protected override async Task<FileMetadata> GetFileMetadataImplAsync(string path, CancellationToken cancel = default)
        {
            FileInfo fileInfo = new FileInfo(path);
            FileMetadata ret = ConvertFileSystemInfoToFileMetadata(fileInfo);

            // Try to open the actual physical file
            FileObjectBase f = null;
            try
            {
                f = await PalFileObject.CreateFileAsync(this, new FileParameters(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, FileOperationFlags.None), cancel);
            }
            catch
            {
                try
                {
                    f = await PalFileObject.CreateFileAsync(this, new FileParameters(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, FileOperationFlags.BackupMode), cancel);
                }
                catch { }
            }

            if (f != null)
            {
                try
                {
                    ret.Size = await f.GetFileSizeAsync();
                }
                finally
                {
                    f.DisposeSafe();
                }
            }

            return ret;
        }

        protected async override Task SetFileMetadataImplAsync(string path, FileMetadata metadata, CancellationToken cancel = default)
        {
            FileInfo fileInfo = new FileInfo(path);

            SetFileMetadataToFileSystemInfo(fileInfo, metadata);

            await Task.CompletedTask;
        }

        protected override async Task<FileMetadata> GetDirectoryMetadataImplAsync(string path, CancellationToken cancel = default)
        {
            DirectoryInfo dirInfo = new DirectoryInfo(path);
            FileMetadata ret = ConvertFileSystemInfoToFileMetadata(dirInfo);

            await Task.CompletedTask;

            return ret;
        }

        protected async override Task SetDirectoryMetadataImplAsync(string path, FileMetadata metadata, CancellationToken cancel = default)
        {
            DirectoryInfo dirInfo = new DirectoryInfo(path);

            SetFileMetadataToFileSystemInfo(dirInfo, metadata);

            await Task.CompletedTask;
        }

        protected override async Task DeleteFileImplAsync(string path, CancellationToken cancel = default)
        {
            File.Delete(path);

            await Task.CompletedTask;
        }

        protected override async Task MoveFileImplAsync(string srcPath, string destPath, CancellationToken cancel = default)
        {
            File.Move(srcPath, destPath);

            await Task.CompletedTask;
        }

        protected override async Task MoveDirectoryImplAsync(string srcPath, string destPath, CancellationToken cancel = default)
        {
            Directory.Move(srcPath, destPath);

            await Task.CompletedTask;
        }

        public void EnableBackupPrivilege()
        {
            if (Env.IsWindows)
            {
                Win32Api.EnablePrivilege(Win32Api.Advapi32.SeBackupPrivilege, true);
                Win32Api.EnablePrivilege(Win32Api.Advapi32.SeRestorePrivilege, true);
            }
        }

        public Holder<IDisposable> EnterDisableMediaInsertionPrompt()
        {
            IDisposable token = new EmptyDisposable();

            if (Env.IsWindows)
            {
                 token = Win32Api.Win32DisableMediaInsertionPrompt.Create();
            }

            return new Holder<IDisposable>(x => x.DisposeSafe(), token);
        }
    }

    class PalFileObject : FileObjectBase
    {
        protected PalFileObject(FileSystemBase fileSystem, FileParameters fileParams) : base(fileSystem, fileParams) { }

        FileStream fileStream;

        long CurrentPosition;

        public static async Task<FileObjectBase> CreateFileAsync(PalFileSystem fileSystem, FileParameters fileParams, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();

            PalFileObject f = new PalFileObject(fileSystem, fileParams);

            await f.InternalInitAsync(cancel);

            return f;
        }

        protected override async Task InternalInitAsync(CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();

            try
            {
                Con.WriteDebug($"InternalInitAsync '{FileParams.Path}'");

                FileOptions options = FileOptions.Asynchronous;

                if (Env.IsWindows)
                    if (FileParams.Flags.Bit(FileOperationFlags.BackupMode))
                        options |= (FileOptions)Win32Api.Kernel32.FileOperations.FILE_FLAG_BACKUP_SEMANTICS;
    

                if ((options & (FileOptions)Win32Api.Kernel32.FileOperations.FILE_FLAG_BACKUP_SEMANTICS) != 0)
                {
                    // Use our private FileStream implementation
                    fileStream = PalWin32FileStream.Create(FileParams.Path, FileParams.Mode, FileParams.Access, FileParams.Share, 4096, options);
                }
                else
                {
                    // Use normal FileStream
                    fileStream = new FileStream(FileParams.Path, FileParams.Mode, FileParams.Access, FileParams.Share, 4096, FileOptions.Asynchronous);
                }

                this.CurrentPosition = fileStream.Position;

                await base.InternalInitAsync(cancel);
            }
            catch
            {
                fileStream.DisposeSafe();
                fileStream = null;
                throw;
            }
        }

        protected override async Task CloseImplAsync()
        {
            fileStream.DisposeSafe();
            fileStream = null;

            Con.WriteDebug($"CloseImplAsync '{FileParams.Path}'");

            await Task.CompletedTask;
        }

        protected override async Task<long> GetCurrentPositionImplAsync(CancellationToken cancel = default)
        {
            await Task.CompletedTask;
            long ret = fileStream.Position;
            Debug.Assert(this.CurrentPosition == ret);
            return ret;
        }

        protected override async Task<long> GetFileSizeImplAsync(CancellationToken cancel = default)
        {
            await Task.CompletedTask;
            return fileStream.Length;
        }
        protected override async Task SetFileSizeImplAsync(long size, CancellationToken cancel = default)
        {
            fileStream.SetLength(size);
            await Task.CompletedTask;
        }

        protected override async Task FlushImplAsync(CancellationToken cancel = default)
        {
            await fileStream.FlushAsync(cancel);
        }

        protected override async Task<int> ReadImplAsync(long position, Memory<byte> data, CancellationToken cancel = default)
        {
            checked
            {
                if (this.CurrentPosition != position)
                {
                    fileStream.Seek(position, SeekOrigin.Begin);
                    this.CurrentPosition = position;
                }

                int ret = await fileStream.ReadAsync(data, cancel);

                if (ret >= 1)
                {
                    this.CurrentPosition += ret;
                }

                return ret;
            }
        }

        protected override async Task WriteImplAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
        {
            checked
            {
                if (this.CurrentPosition != position)
                {
                    fileStream.Seek(position, SeekOrigin.Begin);
                    this.CurrentPosition = position;
                }

                await fileStream.WriteAsync(data, cancel);

                this.CurrentPosition += data.Length;
            }
        }
    }

}

