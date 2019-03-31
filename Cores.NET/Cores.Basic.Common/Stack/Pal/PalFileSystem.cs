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
using IPA.Cores.Helper.Basic;
using System.Runtime.InteropServices;
using System.ComponentModel;

#pragma warning disable CS0162

namespace IPA.Cores.Basic
{
    class PalFileSystem : FileSystem
    {
        public PalFileSystem(AsyncCleanuperLady lady) : base(lady)
        {
        }

        protected override Task<FileObject> CreateFileImplAsync(FileParameters fileParams, CancellationToken cancel = default)
            => PalFileHandle.CreateFileAsync(this, fileParams, cancel);

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

        public static void EnableBackupPrivilege()
        {
            if (Env.IsWindows)
            {
                Win32Api.EnablePrivilege(Win32Api.Advapi32.SeBackupPrivilege, true);
                Win32Api.EnablePrivilege(Win32Api.Advapi32.SeRestorePrivilege, true);
            }
        }

        public static Holder<IDisposable> EnterDisableMediaInsertionPrompt()
        {
            IDisposable token = new EmptyDisposable();

            if (Env.IsWindows)
            {
                 token = Win32Api.Win32DisableMediaInsertionPrompt.Create();
            }

            return new Holder<IDisposable>(x => x.DisposeSafe(), token);
        }
    }

    class PalFileHandle : FileObject
    {
        protected PalFileHandle(FileSystem fileSystem, FileParameters fileParams) : base(fileSystem, fileParams) { }

        FileStream fs;

        public static async Task<FileObject> CreateFileAsync(FileSystem fileSystem, FileParameters fileParams, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();

            PalFileHandle f = new PalFileHandle(fileSystem, fileParams);

            await f.CreateAsync(cancel);

            return f;
        }

        protected override async Task CreateAsync(CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();

            try
            {
                fs = new FileStream(FileParams.Path, FileParams.Mode, FileParams.Access, FileParams.Share, 4096, FileOptions.Asynchronous);

                await base.CreateAsync(cancel);
            }
            catch
            {
                fs.DisposeSafe();
                throw;
            }
        }

        protected override async Task CloseImplAsync()
        {
            Dbg.Where();
            fs.DisposeSafe();
            fs = null;

            await Task.CompletedTask;
        }

        protected override async Task<long> GetCurrentPositionImplAsync(CancellationToken cancel = default)
        {
            await Task.CompletedTask;
            return fs.Position;
        }

        protected override async Task<long> GetFileSizeImplAsync(CancellationToken cancel = default)
        {
            await Task.CompletedTask;
            return fs.Length;
        }
        protected override async Task SetFileSizeImplAsync(long size, CancellationToken cancel = default)
        {
            fs.SetLength(size);
            await Task.CompletedTask;
        }

        protected override async Task FlushImplAsync(CancellationToken cancel = default)
        {
            await fs.FlushAsync(cancel);
        }

        protected override async Task<int> ReadImplAsync(long position, bool seekRequested, Memory<byte> data, CancellationToken cancel = default)
        {
            if (seekRequested)
                fs.Seek(position, SeekOrigin.Begin);

            return await fs.ReadAsync(data, cancel);
        }

        protected override async Task WriteImplAsync(long position, bool seekRequested, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
        {
            if (seekRequested)
                fs.Seek(position, SeekOrigin.Begin);

            await fs.WriteAsync(data, cancel);
        }
    }

    static class PalWin32FileStream
    {
        // Licensed to the .NET Foundation under one or more agreements.
        // The .NET Foundation licenses this file to you under the MIT license.
        // See the LICENSE file in the project root for more information.

        private const int FILE_ATTRIBUTE_NORMAL = 0x00000080;
        private const int FILE_ATTRIBUTE_ENCRYPTED = 0x00004000;
        private const int FILE_FLAG_OVERLAPPED = 0x40000000;
        internal const int GENERIC_READ = unchecked((int)0x80000000);
        private const int GENERIC_WRITE = 0x40000000;

        public static FileStream Create(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (path.Length == 0)
                throw new ArgumentException(nameof(path));

            // don't include inheritable in our bounds check for share
            FileShare tempshare = share & ~FileShare.Inheritable;
            string badArg = null;
            if (mode < FileMode.CreateNew || mode > FileMode.Append)
                badArg = nameof(mode);
            else if (access < FileAccess.Read || access > FileAccess.ReadWrite)
                badArg = nameof(access);
            else if (tempshare < FileShare.None || tempshare > (FileShare.ReadWrite | FileShare.Delete))
                badArg = nameof(share);

            if (badArg != null)
                throw new ArgumentOutOfRangeException(badArg);

            // NOTE: any change to FileOptions enum needs to be matched here in the error validation
            if (options != FileOptions.None && (options & ~(FileOptions.WriteThrough | FileOptions.Asynchronous | FileOptions.RandomAccess | FileOptions.DeleteOnClose | FileOptions.SequentialScan | FileOptions.Encrypted | (FileOptions)0x20000000 /* NoBuffering */)) != 0)
                throw new ArgumentOutOfRangeException(nameof(options));

            if (bufferSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(bufferSize));

            // Write access validation
            if ((access & FileAccess.Write) == 0)
            {
                if (mode == FileMode.Truncate || mode == FileMode.CreateNew || mode == FileMode.Create || mode == FileMode.Append)
                {
                    // No write access, mode and access disagree but flag access since mode comes first
                    throw new ArgumentException(nameof(access));
                }
            }

            if ((access & FileAccess.Read) != 0 && mode == FileMode.Append)
                throw new ArgumentException(nameof(access));

            string fullPath = Path.GetFullPath(path);

            string _path = fullPath;
            FileAccess _access = access;
            int _bufferLength = bufferSize;

            bool _useAsyncIO = false;

            if ((options & FileOptions.Asynchronous) != 0)
                _useAsyncIO = true;

            SafeFileHandle _fileHandle = CreateFileOpenHandle(mode, share, options, _access, _path);

            return new FileStream(_fileHandle, _access, 4096, _useAsyncIO);
        }

        static unsafe SafeFileHandle CreateFileOpenHandle(FileMode mode, FileShare share, FileOptions options, FileAccess _access, string _path)
        {
            Win32Api.Kernel32.SECURITY_ATTRIBUTES secAttrs = GetSecAttrs(share);

            int fAccess =
                ((_access & FileAccess.Read) == FileAccess.Read ? GENERIC_READ : 0) |
                ((_access & FileAccess.Write) == FileAccess.Write ? GENERIC_WRITE : 0);

            // Our Inheritable bit was stolen from Windows, but should be set in
            // the security attributes class.  Don't leave this bit set.
            share &= ~FileShare.Inheritable;
            
            // Must use a valid Win32 constant here...
            if (mode == FileMode.Append)
                mode = FileMode.OpenOrCreate;

            int flagsAndAttributes = (int)options;

            // For mitigating local elevation of privilege attack through named pipes
            // make sure we always call CreateFile with SECURITY_ANONYMOUS so that the
            // named pipe server can't impersonate a high privileged client security context
            // (note that this is the effective default on CreateFile2)
            flagsAndAttributes |= (Win32Api.Kernel32.SecurityOptions.SECURITY_SQOS_PRESENT | Win32Api.Kernel32.SecurityOptions.SECURITY_ANONYMOUS);

            using (PalFileSystem.EnterDisableMediaInsertionPrompt())
            {
                return ValidateFileHandle(
                    Win32Api.Kernel32.CreateFile(_path, fAccess, share, ref secAttrs, mode, flagsAndAttributes, IntPtr.Zero), _path);
            }
        }

        private static unsafe Win32Api.Kernel32.SECURITY_ATTRIBUTES GetSecAttrs(FileShare share)
        {
            Win32Api.Kernel32.SECURITY_ATTRIBUTES secAttrs = default;
            if ((share & FileShare.Inheritable) != 0)
            {
                secAttrs = new Win32Api.Kernel32.SECURITY_ATTRIBUTES
                {
                    nLength = (uint)sizeof(Win32Api.Kernel32.SECURITY_ATTRIBUTES),
                    bInheritHandle = Win32Api.BOOL.TRUE
                };
            }
            return secAttrs;
        }


        private static SafeFileHandle ValidateFileHandle(SafeFileHandle fileHandle, string _path)
        {
            if (fileHandle.IsInvalid)
            {
                // Return a meaningful exception with the full path.

                // NT5 oddity - when trying to open "C:\" as a Win32FileStream,
                // we usually get ERROR_PATH_NOT_FOUND from the OS.  We should
                // probably be consistent w/ every other directory.
                int errorCode = Marshal.GetLastWin32Error();

                if (errorCode == Win32Api.Errors.ERROR_PATH_NOT_FOUND && _path.Length == Win32PathInternal.GetRootLength(_path))
                    errorCode = Win32Api.Errors.ERROR_ACCESS_DENIED;

                throw new Win32Exception(errorCode, _path);
                //throw Win32Marshal.GetExceptionForWin32Error(errorCode, _path);
            }

            //fileHandle.IsAsync = _useAsyncIO;
            return fileHandle;
        }
    }
}

