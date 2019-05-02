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
using static IPA.Cores.Globals.Basic;
using System.Security.AccessControl;

namespace IPA.Cores.Basic
{
    static partial class CoresConfig
    {
        public static partial class LocalFileSystemSettings
        {
            public static readonly Copenhagen<int> SparseFileMinBlockSize = 4096;
        }
    }

    class LocalFileSystem : FileSystem
    {
        public const long Win32MaxAlternateStreamSize = 65536;
        public const int Win32MaxAlternateStreamNum = 16;

        static Singleton<LocalFileSystem> _LocalSingleton = new Singleton<LocalFileSystem>(() => new LocalFileSystem().AsGlobalService(), leakKind: LeakCounterKind.DoNotTrack);
        public static LocalFileSystem Local { get; } = _LocalSingleton;

        static Singleton<AutoUtf8BomViewFileSystem> _LocalAutoUtf8Singleton = new Singleton<AutoUtf8BomViewFileSystem>(() => new AutoUtf8BomViewFileSystem(new AutoUtf8BomViewFileSystemParam(LocalFileSystem.Local)).AsGlobalService(), leakKind: LeakCounterKind.DoNotTrack);
        public static AutoUtf8BomViewFileSystem LocalAutoUtf8 { get; } = _LocalAutoUtf8Singleton;

        private LocalFileSystem() : base(new FileSystemParams(Env.LocalFileSystemPathInterpreter))
        {
        }

        protected override Task<FileObject> CreateFileImplAsync(FileParameters fileParams, CancellationToken cancel = default)
            => LocalFileObject.CreateFileAsync(this, fileParams, cancel);

        protected override async Task<FileSystemEntity[]> EnumDirectoryImplAsync(string directoryPath, EnumDirectoryFlags flags, CancellationToken cancel = default)
        {
            DirectoryInfo di = new DirectoryInfo(directoryPath);

            List<FileSystemEntity> o = new List<FileSystemEntity>();

            FileSystemEntity currentDirectory = ConvertFileSystemInfoToFileSystemEntity(di);
            currentDirectory.Name = ".";
            o.Add(currentDirectory);

            foreach (FileSystemInfo info in di.GetFileSystemInfos().Where(x => x.Exists))
            {
                FileSystemEntity entity = ConvertFileSystemInfoToFileSystemEntity(info);

                // Actual file size
                if (flags.Bit(EnumDirectoryFlags.NoGetPhysicalSize) == false)
                    if (entity.IsDirectory == false)
                        if (entity.Attributes.Bit(FileAttributes.Compressed) || entity.Attributes.Bit(FileAttributes.SparseFile))
                            entity.PhysicalSize = GetPhysicalFileSizeInternal(info.FullName, entity.Size);

                if (entity.IsSymbolicLink)
                {
                    entity.SymbolicLinkTarget = ReadSymbolicLinkTarget(entity.FullPath);
                }

                o.Add(entity);
            }

            await Task.CompletedTask;

            return o.ToArray();
        }

        static long GetPhysicalFileSizeInternal(string path, long defaultSizeOnError)
        {
            try
            {
                if (Env.IsWindows)
                {
                    return Win32ApiUtil.GetCompressedFileSize(path);
                }
            }
            catch { }
            return defaultSizeOnError;
        }

        protected override async Task CreateDirectoryImplAsync(string directoryPath, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
        {
            if (Directory.Exists(directoryPath) == false)
            {
                Directory.CreateDirectory(directoryPath);

                if (Env.IsWindows)
                {
                    if (flags.Bit(FileOperationFlags.OnCreateSetCompressionFlag))
                        await Win32ApiUtil.SetCompressionFlagAsync(directoryPath, true, true, cancel);
                    else if (flags.Bit(FileOperationFlags.OnCreateRemoveCompressionFlag))
                        await Win32ApiUtil.SetCompressionFlagAsync(directoryPath, true, false, cancel);
                }
            }
        }

        protected override async Task DeleteDirectoryImplAsync(string directoryPath, bool recursive, CancellationToken cancel = default)
        {
            Directory.Delete(directoryPath, recursive);

            await Task.CompletedTask;
        }

        protected override Task<bool> IsFileExistsImplAsync(string path, CancellationToken cancel = default)
            => Task.FromResult(File.Exists(path));

        protected override Task<bool> IsDirectoryExistsImplAsync(string path, CancellationToken cancel = default)
            => Task.FromResult(Directory.Exists(path));

        static FileSystemEntity ConvertFileSystemInfoToFileSystemEntity(FileSystemInfo info)
        {
            FileSystemEntity ret = new FileSystemEntity()
            {
                Name = info.Name,
                FullPath = info.FullName,
                Size = info.Attributes.Bit(FileAttributes.Directory) ? 0 : ((FileInfo)info).Length,
                Attributes = info.Attributes,
                CreationTime = info.CreationTime.AsDateTimeOffset(true),
                LastWriteTime = info.LastWriteTime.AsDateTimeOffset(true),
                LastAccessTime = info.LastAccessTime.AsDateTimeOffset(true),
            };

            ret.PhysicalSize = ret.Size;

            return ret;
        }

        static FileMetadata ConvertFileSystemInfoToFileMetadata(FileSystemInfo info, FileMetadataGetFlags flags)
        {
            FileMetadata ret = new FileMetadata()
            {
                Size = info.Attributes.Bit(FileAttributes.Directory) ? 0 : ((FileInfo)info).Length,
                Attributes = flags.Bit(FileMetadataGetFlags.NoAttributes) == false ? info.Attributes : (FileAttributes ?)null,
                CreationTime = flags.Bit(FileMetadataGetFlags.NoTimes) == false ? info.CreationTime.AsDateTimeOffset(true) : (DateTimeOffset?)null,
                LastWriteTime = flags.Bit(FileMetadataGetFlags.NoTimes) == false ? info.LastWriteTime.AsDateTimeOffset(true) : (DateTimeOffset?)null,
                LastAccessTime = flags.Bit(FileMetadataGetFlags.NoTimes) == false ? info.LastAccessTime.AsDateTimeOffset(true) : (DateTimeOffset?)null,
                IsDirectory = info.Attributes.Bit(FileAttributes.Directory),
            };

            ret.PhysicalSize = ret.Size;

            return ret;
        }

        void SetDirectoryCreationTimeUtc(string path, DateTime dt) => Directory.SetCreationTimeUtc(path, dt);
        void SetDirectoryLastWriteTimeUtc(string path, DateTime dt) => Directory.SetLastWriteTimeUtc(path, dt);
        void SetDirectoryLastAccessTimeUtc(string path, DateTime dt) => Directory.SetLastAccessTimeUtc(path, dt);

        void SetDirectoryAttributes(string path, FileAttributes fileAttributes)
        {
            DirectoryInfo di = new DirectoryInfo(path);
            di.Attributes = fileAttributes;
        }

        void SetFileCreationTimeUtc(string path, DateTime dt)
        {
            try
            {
                File.SetCreationTimeUtc(path, dt);
            }
            catch (UnauthorizedAccessException)
            {
                if (Env.IsWindows)
                {
                    Win32ApiUtil.SetCreationTime(path, dt.AsDateTimeOffset(false), false, true);
                    return;
                }

                throw;
            }
        }

        void SetFileLastWriteTimeUtc(string path, DateTime dt)
        {
            try
            {
                File.SetLastWriteTimeUtc(path, dt);
            }
            catch (UnauthorizedAccessException)
            {
                if (Env.IsWindows)
                {
                    Win32ApiUtil.SetLastWriteTime(path, dt.AsDateTimeOffset(false), false, true);
                    return;
                }

                throw;
            }
        }

        void SetFileLastAccessTimeUtc(string path, DateTime dt)
        {
            try
            {
                File.SetLastAccessTimeUtc(path, dt);
            }
            catch (UnauthorizedAccessException)
            {
                if (Env.IsWindows)
                {
                    Win32ApiUtil.SetLastAccessTime(path, dt.AsDateTimeOffset(false), false, true);
                    return;
                }

                throw;
            }
        }

        void SetFileAttributes(string path, FileAttributes fileAttributes)
        {
            File.SetAttributes(path, fileAttributes);
            //PalWin32FileSystem.SetAttributes(path, fileAttributes);
        }

        void SetFileMetadataToFileSystemInfo(FileSystemInfo info, FileMetadata metadata)
        {
            if (metadata.CreationTime is DateTimeOffset creationTime)
                info.CreationTimeUtc = creationTime.UtcDateTime;

            if (metadata.LastWriteTime is DateTimeOffset lastWriteTime)
                info.LastWriteTimeUtc = lastWriteTime.UtcDateTime;

            if (metadata.LastAccessTime is DateTimeOffset lastAccessTime)
                info.LastAccessTimeUtc = lastAccessTime.UtcDateTime;

            if (metadata.Attributes is FileAttributes attr)
                info.Attributes = attr;
        }

        string ReadSymbolicLinkTarget(string linkPath)
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

        string Win32GetFileOrDirectorySecuritySddlInternal(string path, bool isDirectory, AccessControlSections section)
        {
            if (Env.IsWindows == false) return null;
            try
            {
                FileSystemSecurity sec = isDirectory ? (FileSystemSecurity)(new DirectorySecurity(path, section)) : (FileSystemSecurity)(new FileSecurity(path, section));
                string ret = sec.GetSecurityDescriptorSddlForm(section);

                return ret;
            }
            catch
            {
                return null;
            }
        }

        void Win32SetFileOrDirectorySecuritySddlInternal(string path, bool isDirectory, string sddl, AccessControlSections section)
        {
            if (Env.IsWindows == false) return;
            if (sddl.IsEmpty()) return;

            bool setProtected = false;
            if (sddl.StartsWith("!"))
            {
                sddl = sddl.Substring(1);
                setProtected = true;
            }

            if (sddl.IsEmpty()) return;

            FileSystemSecurity sec = isDirectory ? (FileSystemSecurity)(new DirectorySecurity()) : (FileSystemSecurity)(new FileSecurity());

            sec.SetSecurityDescriptorSddlForm(sddl, section);

            if (setProtected)
            {
                if (section == AccessControlSections.Audit)
                    sec.SetAuditRuleProtection(true, true);

                if (section == AccessControlSections.Access)
                    sec.SetAccessRuleProtection(true, true);
            }

            if (isDirectory == false)
            {
                FileInfo fi = new FileInfo(path);
                fi.SetAccessControl((FileSecurity)sec);
            }
            else
            {
                DirectoryInfo di = new DirectoryInfo(path);
                di.SetAccessControl((DirectorySecurity)sec);
            }
        }

        async Task SetFileAlternateStreamMetadataAsync(string path, FileAlternateStreamMetadata data, CancellationToken cancel = default)
        {
            if (data.IsEmpty())
                return;

            if (Env.IsWindows)
            {
                var currentStreams = Win32ApiUtil.EnumAlternateStreams(path, Win32MaxAlternateStreamSize, Win32MaxAlternateStreamNum);

                if (currentStreams == null)
                    throw new FileException(path, "Win32EnumAlternateStreamsInternal() failed.");

                // Copy streams
                foreach (var d in data.Items)
                {
                    if (d.IsFilled())
                    {
                        if (d.Name.IsEmpty() == false)
                        {
                            if (d.Name.IndexOfAny(PathParser.PossibleDirectorySeparators) == -1)
                            {
                                string fullpath = path + d.Name;

                                try
                                {
                                    await this.WriteDataToFileAsync(fullpath, d.Data, FileOperationFlags.None, cancel: cancel);
                                }
                                catch
                                {
                                    await this.WriteDataToFileAsync(fullpath, d.Data, FileOperationFlags.BackupMode, cancel: cancel);
                                }
                            }
                        }
                    }
                }

                // Remove any streams on the destination which is not existing on the source
                foreach (var existingStream in currentStreams)
                {
                    if (existingStream.Item1.IsEmpty() == false)
                    {
                        if (existingStream.Item2 <= Win32MaxAlternateStreamSize)
                        {
                            if (data.Items.Select(x => x.Name).Where(x => x.IsSamei(existingStream.Item1)).Any() == false)
                            {
                                string fullpath = path + existingStream.Item1;

                                try
                                {
                                    Con.WriteDebug($"Deleting {fullpath}");
                                    await this.DeleteFileImplAsync(fullpath);
                                }
                                catch (Exception ex)
                                {
                                    ex.Debug();
                                }
                            }
                        }
                    }
                }
            }
        }

        async Task<FileAlternateStreamMetadata> GetFileAlternateStreamMetadataAsync(string path, CancellationToken cancel = default)
        {
            FileAlternateStreamMetadata ret = new FileAlternateStreamMetadata();

            List<FileAlternateStreamItemMetadata> itemList = new List<FileAlternateStreamItemMetadata>();

            if (Env.IsWindows)
            {
                var list = Win32ApiUtil.EnumAlternateStreams(path, Win32MaxAlternateStreamSize, Win32MaxAlternateStreamNum);

                if (list == null)
                {
                    return null;
                }

                foreach (var item in list)
                {
                    if (item.Item1.IsFilled() && item.Item2 >= 1)
                    {
                        int readSize = (int)Math.Min(item.Item2, Win32MaxAlternateStreamSize);
                        string fileName = path + item.Item1;

                        Memory<byte> memory;

                        try
                        {
                            memory = await this.ReadDataFromFileAsync(fileName, (int)Win32MaxAlternateStreamSize, FileOperationFlags.None, cancel);
                        }
                        catch
                        {
                            memory = await this.ReadDataFromFileAsync(fileName, (int)Win32MaxAlternateStreamSize, FileOperationFlags.BackupMode, cancel);
                        }

                        FileAlternateStreamItemMetadata newItem = new FileAlternateStreamItemMetadata()
                        {
                            Name = item.Item1,
                            Data = memory.ToArray(),
                        };

                        itemList.Add(newItem);
                    }
                }
            }

            ret.Items = itemList.ToArray();

            return ret;
        }

        FileSecurityMetadata GetFileOrDirectorySecurityMetadata(string path, bool isDirectory)
        {
            FileSecurityMetadata ret = new FileSecurityMetadata();
            string sddl;

            if (Env.IsWindows)
            {
                sddl = Win32GetFileOrDirectorySecuritySddlInternal(path, isDirectory, AccessControlSections.Owner);
                if (sddl.IsFilled())
                    ret.Owner = new FileSecurityOwner() { Win32OwnerSddl = sddl };

                sddl = Win32GetFileOrDirectorySecuritySddlInternal(path, isDirectory, AccessControlSections.Group);
                if (sddl.IsFilled())
                    ret.Group = new FileSecurityGroup() { Win32GroupSddl = sddl };

                sddl = Win32GetFileOrDirectorySecuritySddlInternal(path, isDirectory, AccessControlSections.Access);
                if (sddl.IsFilled())
                    ret.Acl = new FileSecurityAcl() { Win32AclSddl = sddl };

                sddl = Win32GetFileOrDirectorySecuritySddlInternal(path, isDirectory, AccessControlSections.Audit);
                if (sddl.IsFilled())
                    ret.Audit = new FileSecurityAudit() { Win32AuditSddl = sddl };
            }

            return ret.FilledOrDefault();
        }

        async Task ApplyFileOrDirectorySpecialOperationFlagsMedatataAsync(string path, bool isDirectory, FileSpecialOperationFlags operationFlags, CancellationToken cancel = default)
        {
            await Util.DoMultipleActionsAsync(MultipleActionsFlag.AllOk, cancel,
                async () =>
                {
                    if (operationFlags.Bit(FileSpecialOperationFlags.SetCompressionFlag))
                        if (Env.IsWindows)
                            await Win32ApiUtil.SetCompressionFlagAsync(path, isDirectory, true, cancel);
                },
                async () =>
                {
                    if (operationFlags.Bit(FileSpecialOperationFlags.RemoveCompressionFlag))
                        if (Env.IsWindows)
                            await Win32ApiUtil.SetCompressionFlagAsync(path, isDirectory, false, cancel);
                }
                );
        }

        void SetFileOrDirectorySecurityMetadata(string path, bool isDirectory, FileSecurityMetadata data)
        {
            if (Env.IsWindows)
            {
                if (data.IsFilled())
                {
                    Util.DoMultipleActions(MultipleActionsFlag.AllOk, default,
                        () => Win32SetFileOrDirectorySecuritySddlInternal(path, isDirectory, data?.Owner?.Win32OwnerSddl, AccessControlSections.Owner),
                        () => Win32SetFileOrDirectorySecuritySddlInternal(path, isDirectory, data?.Group?.Win32GroupSddl, AccessControlSections.Group),
                        () => Win32SetFileOrDirectorySecuritySddlInternal(path, isDirectory, data?.Acl?.Win32AclSddl, AccessControlSections.Access),
                        () => Win32SetFileOrDirectorySecuritySddlInternal(path, isDirectory, data?.Audit?.Win32AuditSddl, AccessControlSections.Audit)
                        );
                }
            }
        }

        protected override Task<string> NormalizePathImplAsync(string path, CancellationToken cancel = default)
        {
            return Task.FromResult(Path.GetFullPath(path));
        }

        protected override async Task<FileMetadata> GetFileMetadataImplAsync(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
        {
            FileInfo fileInfo = new FileInfo(path);

            if (fileInfo.Exists == false)
                throw new FileNotFoundException($"The file '{path}' not found.");

            FileMetadata ret = ConvertFileSystemInfoToFileMetadata(fileInfo, flags);

            if (flags.Bit(FileMetadataGetFlags.NoSecurity) == false)
            {
                ret.Security = GetFileOrDirectorySecurityMetadata(path, false);
            }

            var obtainedAttributes = ret.Attributes ?? 0;

            if (flags.Bit(FileMetadataGetFlags.NoPhysicalFileSize) == false)
                if (obtainedAttributes.Bit(FileAttributes.Compressed) || obtainedAttributes.Bit(FileAttributes.SparseFile))
                    ret.PhysicalSize = GetPhysicalFileSizeInternal(path, ret.Size);

            // Try to open to retrieve the actual physical file
            if (flags.Bit(FileMetadataGetFlags.NoPreciseFileSize) == false)
            {
                FileObject f = null;
                try
                {
                    f = await LocalFileObject.CreateFileAsync(this, new FileParameters(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOperationFlags.None), cancel);
                }
                catch
                {
                    try
                    {
                        f = await LocalFileObject.CreateFileAsync(this, new FileParameters(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOperationFlags.BackupMode), cancel);
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
            }

            if (flags.Bit(FileMetadataGetFlags.NoAlternateStream) == false)
            {
                ret.AlternateStream = await GetFileAlternateStreamMetadataAsync(path, cancel);
            }

            return ret;
        }

        protected async override Task SetFileMetadataImplAsync(string path, FileMetadata metadata, CancellationToken cancel = default)
        {
            await Util.DoMultipleActionsAsync(MultipleActionsFlag.AllOk, cancel,
                () =>
                {
                    if (metadata.AlternateStream != null)
                        SetFileAlternateStreamMetadataAsync(path, metadata.AlternateStream, cancel).GetResult();
                    return Task.CompletedTask;
                },
                () =>
                {
                    if (metadata.CreationTime != null)
                        SetFileCreationTimeUtc(path, ((DateTimeOffset)metadata.CreationTime).UtcDateTime);
                    return Task.CompletedTask;
                },
                () =>
                {
                    if (metadata.LastWriteTime != null)
                        SetFileLastWriteTimeUtc(path, ((DateTimeOffset)metadata.LastWriteTime).UtcDateTime);
                    return Task.CompletedTask;
                },
                () =>
                {
                    if (metadata.LastAccessTime != null)
                        SetFileLastAccessTimeUtc(path, ((DateTimeOffset)metadata.LastAccessTime).UtcDateTime);
                    return Task.CompletedTask;
                },
                () =>
                {
                    if (metadata.Attributes != null)
                        SetFileAttributes(path, (FileAttributes)metadata.Attributes);
                    return Task.CompletedTask;
                },
                async () =>
                {
                    await ApplyFileOrDirectorySpecialOperationFlagsMedatataAsync(path, false, metadata.SpecialOperationFlags, cancel);
                },
                () =>
                {
                    if (metadata.Security != null)
                        SetFileOrDirectorySecurityMetadata(path, false, metadata.Security);
                    return Task.CompletedTask;
                }
                );
        }

        protected override async Task<FileMetadata> GetDirectoryMetadataImplAsync(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
        {
            DirectoryInfo dirInfo = new DirectoryInfo(path);

            if (dirInfo.Exists == false)
                throw new FileNotFoundException($"The directory '{path}' not found.");

            FileMetadata ret = ConvertFileSystemInfoToFileMetadata(dirInfo, flags);

            if (flags.Bit(FileMetadataGetFlags.NoSecurity) == false)
            {
                ret.Security = GetFileOrDirectorySecurityMetadata(path, true);
            }

            await Task.CompletedTask;

            return ret;
        }


        protected async override Task SetDirectoryMetadataImplAsync(string path, FileMetadata metadata, CancellationToken cancel = default)
        {
            await Util.DoMultipleActionsAsync(MultipleActionsFlag.AllOk, cancel,
                () =>
                {
                    if (metadata.Attributes != null)
                        SetDirectoryAttributes(path, (FileAttributes)metadata.Attributes);
                    return Task.CompletedTask;
                },
                async () =>
                {
                    await ApplyFileOrDirectorySpecialOperationFlagsMedatataAsync(path, true, metadata.SpecialOperationFlags, cancel);
                },
                () =>
                {
                    if (metadata.CreationTime != null)
                        SetDirectoryCreationTimeUtc(path, ((DateTimeOffset)metadata.CreationTime).UtcDateTime);
                    return Task.CompletedTask;
                },
                () =>
                {
                    if (metadata.LastWriteTime != null)
                        SetDirectoryLastWriteTimeUtc(path, ((DateTimeOffset)metadata.LastWriteTime).UtcDateTime);
                    return Task.CompletedTask;
                },
                () =>
                {
                    if (metadata.LastAccessTime != null)
                        SetDirectoryLastAccessTimeUtc(path, ((DateTimeOffset)metadata.LastAccessTime).UtcDateTime);
                    return Task.CompletedTask;
                },
                () =>
                {
                    if (metadata.Security != null)
                        SetFileOrDirectorySecurityMetadata(path, true, metadata.Security);
                    return Task.CompletedTask;
                }
                );
        }

        protected override async Task DeleteFileImplAsync(string path, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
        {
            if (flags.Bit(FileOperationFlags.BackupMode) || flags.Bit(FileOperationFlags.ForceClearReadOnlyOrHiddenBitsOnNeed))
            {
                await this.TryAddOrRemoveAttributeFromExistingFile(path, 0, FileAttributes.ReadOnly, cancel);
            }

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
                Util.DoMultipleActions(MultipleActionsFlag.AllOk, default,
                    () => Win32Api.EnablePrivilege(Win32Api.Advapi32.SeBackupPrivilege, true),
                    () => Win32Api.EnablePrivilege(Win32Api.Advapi32.SeRestorePrivilege, true),
                    () => Win32Api.EnablePrivilege(Win32Api.Advapi32.SeTakeOwnershipPrivilege, true)
                    );
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

    class LocalFileObject : FileObject
    {
        string _PhysicalFinalPath = null;
        public override string FinalPhysicalPath => _PhysicalFinalPath.FilledOrException();

        protected LocalFileObject(FileSystem fileSystem, FileParameters fileParams) : base(fileSystem, fileParams) { }

        protected FileStream BaseStream;
        long CurrentPosition;

        public static async Task<FileObject> CreateFileAsync(LocalFileSystem fileSystem, FileParameters fileParams, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();

            LocalFileObject f = new LocalFileObject(fileSystem, fileParams);

            await f.InternalInitAsync(cancel);

            return f;
        }

        static FileStream Win32CreateFileStreamInternal(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options)
        {
            FileStream ret;

            if ((options & (FileOptions)Win32Api.Kernel32.FileOperations.FILE_FLAG_BACKUP_SEMANTICS) != 0)
            {
                // Use our private FileStream implementation
                ret = PalWin32FileStream.Create(path, mode, access, share, bufferSize, options);
            }
            else
            {
                // Use normal FileStream
                ret = new FileStream(path, mode, access, share, bufferSize, options);
            }

            return ret;
        }

        protected async Task InternalInitAsync(CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();

            try
            {
                Con.WriteTrace($"InternalInitAsync '{FileParams.Path}'");

                FileOptions options = FileOptions.None;
                if (this.FileParams.Flags.Bit(FileOperationFlags.NoAsync) == false)
                    options |= FileOptions.Asynchronous;

                if (Env.IsWindows)
                {
                    if (FileParams.Flags.Bit(FileOperationFlags.BackupMode))
                        options |= (FileOptions)Win32Api.Kernel32.FileOperations.FILE_FLAG_BACKUP_SEMANTICS;

                    if (FileParams.Access.Bit(FileAccess.Write))
                    {
                        if (FileParams.Flags.Bit(FileOperationFlags.BackupMode) || FileParams.Flags.Bit(FileOperationFlags.ForceClearReadOnlyOrHiddenBitsOnNeed))
                        {
                            FileAttributes attributesToRemove = 0;

                            if (FileParams.Mode == FileMode.Create)
                                attributesToRemove |= FileAttributes.Hidden | FileAttributes.ReadOnly;

                            if (FileParams.Mode == FileMode.Append || FileParams.Mode == FileMode.Open || FileParams.Mode == FileMode.OpenOrCreate || FileParams.Mode == FileMode.Truncate)
                                attributesToRemove |= FileAttributes.ReadOnly;

                            if (attributesToRemove != 0)
                                await FileSystem.TryAddOrRemoveAttributeFromExistingFile(FileParams.Path, 0, attributesToRemove, cancel);
                        }
                    }

                    FileMode fileMode = (FileParams.Mode != FileMode.Append) ? FileParams.Mode : FileMode.OpenOrCreate;

                    BaseStream = Win32CreateFileStreamInternal(FileParams.Path, fileMode, FileParams.Access, FileParams.Share, 4096, options);
                }
                else
                {
                    FileMode fileMode = (FileParams.Mode != FileMode.Append) ? FileParams.Mode : FileMode.OpenOrCreate;

                    BaseStream = new FileStream(FileParams.Path, fileMode, FileParams.Access, FileParams.Share, 4096, FileOptions.Asynchronous);
                }

                if (FileParams.Mode == FileMode.Append)
                    BaseStream.Seek(0, SeekOrigin.End);

                if (Env.IsWindows)
                {
                    if (FileParams.Mode == FileMode.Create || FileParams.Mode == FileMode.CreateNew || FileParams.Mode == FileMode.OpenOrCreate || FileParams.Mode == FileMode.Append)
                    {
                        if (BaseStream.Length == 0)
                        {
                            // Special operations on file creation
                            await Util.DoMultipleActionsAsync(MultipleActionsFlag.IgnoreError, cancel,
                                async () =>
                                {
                                    if (FileParams.Flags.Bit(FileOperationFlags.OnCreateSetCompressionFlag))
                                        await Win32ApiUtil.SetCompressionFlagAsync(BaseStream.SafeFileHandle, true, FileParams.Path, cancel);
                                },
                                async () =>
                                {
                                    if (FileParams.Flags.Bit(FileOperationFlags.OnCreateRemoveCompressionFlag))
                                        await Win32ApiUtil.SetCompressionFlagAsync(BaseStream.SafeFileHandle, false, FileParams.Path, cancel);
                                }
                                );
                        }
                    }

                    if (FileParams.Access.Bit(FileAccess.Write))
                    {
                        await Util.DoMultipleActionsAsync(MultipleActionsFlag.IgnoreError, cancel,
                            async () =>
                            {
                                if (FileParams.Flags.Bit(FileOperationFlags.SparseFile))
                                    await SetAsSparseFileAsync();
                            });
                    }
                }

                string physicalFinalPathTmp = "";
                if (Env.IsWindows)
                {
                    try
                    {
                        physicalFinalPathTmp = Win32ApiUtil.GetFinalPath(BaseStream.SafeFileHandle);
                    }
                    catch { }
                }

                if (physicalFinalPathTmp.IsEmpty())
                    physicalFinalPathTmp = FileParams.Path;

                _PhysicalFinalPath = physicalFinalPathTmp;

                this.CurrentPosition = BaseStream.Position;

                InitAndCheckFileSizeAndPosition(this.CurrentPosition, await GetFileSizeImplAsync(cancel), cancel);
            }
            catch
            {
                BaseStream.DisposeSafe();
                BaseStream = null;
                throw;
            }
        }

        bool isSparseFile = false;
        async Task SetAsSparseFileAsync(CancellationToken cancel = default)
        {
            if (Env.IsWindows == false) return;
            if (isSparseFile) return;

            await Win32ApiUtil.SetSparseFileAsync(BaseStream.SafeFileHandle, FileParams.Path, cancel);

            isSparseFile = true;
        }

        protected override async Task CloseImplAsync()
        {
            BaseStream.DisposeSafe();
            BaseStream = null;

            Con.WriteTrace($"CloseImplAsync '{FileParams.Path}'");

            await Task.CompletedTask;
        }

        protected override async Task<long> GetFileSizeImplAsync(CancellationToken cancel = default)
        {
            await Task.CompletedTask;
            return BaseStream.Length;
        }
        protected override async Task SetFileSizeImplAsync(long size, CancellationToken cancel = default)
        {
            BaseStream.SetLength(size);
            this.CurrentPosition = BaseStream.Position;
            await Task.CompletedTask;
        }

        protected override async Task FlushImplAsync(CancellationToken cancel = default)
        {
            await BaseStream.FlushAsync(cancel);
        }

        protected override async Task<int> ReadRandomImplAsync(long position, Memory<byte> data, CancellationToken cancel = default)
        {
            checked
            {
                if (this.CurrentPosition != position)
                {
                    BaseStream.Seek(position, SeekOrigin.Begin);
                    this.CurrentPosition = position;
                }

                int ret = await BaseStream.ReadAsync(data, cancel);

                if (ret >= 1)
                {
                    this.CurrentPosition += ret;
                }

                return ret;
            }
        }

        protected override async Task WriteRandomImplAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
        {
            if (this.FileParams.Flags.Bit(FileOperationFlags.WriteOnlyIfChanged))
            {
                try
                {
                    using (MemoryHelper.FastAllocMemoryWithUsing(data.Length, out Memory<byte> readBuffer))
                    {
                        int readSize = await ReadRandomImplAsync(position, readBuffer, cancel);
                        if (readSize == data.Length)
                            if (data.Span.SequenceEqual(readBuffer.Span))
                            {
                                Dbg.Where("skip");
                                return;
                            }
                    }
                }
                catch { }
            }

            if (isSparseFile)
            {
                await WriteRandomAutoSparseAsync(position, data, cancel);
            }
            else
            {
                await WriteRandomImplInternalAsync(position, data, cancel);
            }
        }

        async Task WriteRandomImplInternalAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
        {
            checked
            {
                if (this.CurrentPosition != position)
                {
                    BaseStream.Seek(position, SeekOrigin.Begin);
                    this.CurrentPosition = position;
                }

                await BaseStream.WriteAsync(data, cancel);

                this.CurrentPosition += data.Length;
            }
        }

        static readonly ReadOnlyMemory<byte> FillZeroBlockSize = ZeroedSharedMemory<byte>.Memory;
        async Task FileZeroClearDataAsync(long position, long size, CancellationToken cancel = default)
        {
            checked
            {
                if (position < 0) throw new ArgumentOutOfRangeException("position");
                if (size < 0) throw new ArgumentOutOfRangeException("size");
                if (size == 0) return;

                if (Env.IsWindows)
                {
                    // Use FSCTL_SET_ZERO_DATA first
                    try
                    {
                        await Win32ApiUtil.FileZeroClearAsync(this.BaseStream.SafeFileHandle, this.FileParams.Path, position, size);
                        return;
                    }
                    catch { }
                }

                // Use normal zero-clear method
                while (size >= 1)
                {
                    long currentSize = Math.Min(size, FillZeroBlockSize.Length);
                    
                    await WriteRandomImplInternalAsync(position, FillZeroBlockSize.Slice(0, (int)currentSize), cancel);

                    //debug
                    //Memory<byte> xxx = new byte[(int)currentSize];
                    //xxx.Span.Fill((byte)'-');
                    //await WriteRandomImplInternalAsync(position, xxx, cancel);

                    position += currentSize;
                    size -= currentSize;
                }
            }
        }

        async Task WriteRandomAutoSparseAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
        {
            if (position < 0) throw new ArgumentOutOfRangeException("position");

            checked
            {
                long currentFileSize = BaseStream.Length;
                long expandingSize = (position + data.Length) - currentFileSize;

                long existingDataRegionSize;
                long expandingDataRegionSize;

                if (expandingSize >= 1)
                {
                    existingDataRegionSize = Math.Max(data.Length - expandingSize, 0);
                    expandingDataRegionSize = data.Length - existingDataRegionSize;
                }
                else
                {
                    existingDataRegionSize = data.Length;
                    expandingDataRegionSize = 0;
                }

                long existingDataRegionPosition = position;
                long expandingDataRegionPosition = position + existingDataRegionSize;

                Debug.Assert(existingDataRegionPosition >= position);
                Debug.Assert(expandingDataRegionPosition >= existingDataRegionPosition);
                Debug.Assert(existingDataRegionSize >= 0);
                Debug.Assert(expandingDataRegionSize >= 0);
                Debug.Assert((existingDataRegionSize + expandingDataRegionSize) == data.Length);

                if (existingDataRegionSize >= 1)
                {
                    var subData = data.Slice(0, (int)existingDataRegionSize);
                    var chunkList = Util.GetSparseChunks(subData, CoresConfig.LocalFileSystemSettings.SparseFileMinBlockSize.Value);

                    foreach (var chunk in chunkList)
                    {
                        if (chunk.IsSparse)
                        {
                            await FileZeroClearDataAsync(existingDataRegionPosition + chunk.Offset, chunk.Size, cancel);
                        }
                        else
                        {
                            await WriteRandomImplInternalAsync(existingDataRegionPosition + chunk.Offset, chunk.Memory, cancel);
                        }
                    }
                }

                if (expandingDataRegionSize >= 1)
                {
                    var subData = data.Slice((int)existingDataRegionSize, (int)expandingDataRegionSize);
                    var chunkList = Util.GetSparseChunks(subData, CoresConfig.LocalFileSystemSettings.SparseFileMinBlockSize.Value);

                    foreach (var chunk in chunkList)
                    {
                        if (chunk.IsSparse)
                        {
                            long newFileSize = expandingDataRegionPosition + chunk.Offset + chunk.Size;
                            Debug.Assert(this.BaseStream.Length <= newFileSize);

                            BaseStream.SetLength(newFileSize);
                        }
                        else
                        {
                            await WriteRandomImplInternalAsync(expandingDataRegionPosition + chunk.Offset, chunk.Memory, cancel);
                        }
                    }
                }
            }
        }
    }
}

