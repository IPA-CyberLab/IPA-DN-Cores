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
using System.Security.AccessControl;

#pragma warning disable CS0162

namespace IPA.Cores.Basic
{
    class LocalFileSystem : FileSystemBase
    {
        public const long Win32MaxAlternateStreamSize = 65536;
        public const int Win32MaxAlternateStreamNum = 16;

        public static LocalFileSystem Local { get; } = LocalFileSystem.CreateFirstLocalInstance();

        static LocalFileSystem _SingletonInstance;

        static LocalFileSystem CreateFirstLocalInstance()
        {
            if (_SingletonInstance == null)
                _SingletonInstance = new LocalFileSystem(LeakChecker.SuperGrandLady);

            return _SingletonInstance;
        }

        private LocalFileSystem(AsyncCleanuperLady lady) : base(lady, Env.LocalFileSystemPathInterpreter)
        {
        }

        protected override Task<FileObjectBase> CreateFileImplAsync(FileParameters fileParams, CancellationToken cancel = default)
            => LocalFileObject.CreateFileAsync(this, fileParams, cancel);

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

                if (flags.Bit(FileOperationFlags.SetCompressionFlagOnCreate))
                    Win32ApiUtil.SetCompressionFlag(directoryPath, true, true);
                else if (flags.Bit(FileOperationFlags.RemoveCompressionFlagOnCreate))
                    Win32ApiUtil.SetCompressionFlag(directoryPath, true, false);
            }

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
                CreationTime = info.CreationTime.AsDateTimeOffset(true),
                LastWriteTime = info.LastWriteTime.AsDateTimeOffset(true),
                LastAccessTime = info.LastAccessTime.AsDateTimeOffset(true),
            };
            return ret;
        }

        public static FileMetadata ConvertFileSystemInfoToFileMetadata(FileSystemInfo info, FileMetadataGetFlags flags)
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
            return ret;
        }

        public void SetDirectoryCreationTimeUtc(string path, DateTime dt) => Directory.SetCreationTimeUtc(path, dt);
        public void SetDirectoryLastWriteTimeUtc(string path, DateTime dt) => Directory.SetLastWriteTimeUtc(path, dt);
        public void SetDirectoryLastAccessTimeUtc(string path, DateTime dt) => Directory.SetLastAccessTimeUtc(path, dt);

        public void SetDirectoryAttributes(string path, FileAttributes fileAttributes)
        {
            DirectoryInfo di = new DirectoryInfo(path);
            di.Attributes = fileAttributes;
        }

        public void SetFileCreationTimeUtc(string path, DateTime dt)
        {
            try
            {
                File.SetLastWriteTimeUtc(path, dt);
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

        public void SetFileLastWriteTimeUtc(string path, DateTime dt)
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

        public void SetFileLastAccessTimeUtc(string path, DateTime dt)
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

        public void SetFileAttributes(string path, FileAttributes fileAttributes)
        {
            File.SetAttributes(path, fileAttributes);
            //PalWin32FileSystem.SetAttributes(path, fileAttributes);
        }

        public void SetFileMetadataToFileSystemInfo(FileSystemInfo info, FileMetadata metadata)
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

        public string ReadSymbolicLinkTarget(string linkPath)
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

        string Win32GetFileOrDirectorySecuritySsdlInternal(string path, bool isDirectory, AccessControlSections section)
        {
            if (Env.IsWindows == false) return null;
            try
            {
                FileSystemSecurity sec = isDirectory ? (FileSystemSecurity)(new DirectorySecurity(path, section)) : (FileSystemSecurity)(new FileSecurity(path, section));
                return sec.GetSecurityDescriptorSddlForm(section);
            }
            catch
            {
                return null;
            }
        }

        void Win32SetFileOrDirectorySecuritySsdlInternal(string path, bool isDirectory, string ssdl, AccessControlSections section)
        {
            if (Env.IsWindows == false) return;
            if (ssdl.IsEmpty()) return;

            FileSystemSecurity sec = isDirectory ? (FileSystemSecurity)(new DirectorySecurity()) : (FileSystemSecurity)(new FileSecurity());
            sec.SetSecurityDescriptorSddlForm(ssdl, section);

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

        public async Task SetFileAlternateStreamMetadataAsync(string path, FileAlternateStreamMetadata data, CancellationToken cancel = default)
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
                            if (d.Name.IndexOfAny(PathInterpreter.PossibleDirectorySeparators) == -1)
                            {
                                string fullpath = path + d.Name;

                                try
                                {
                                    await this.WriteToFileAsync(fullpath, d.Data, FileOperationFlags.None, cancel: cancel);
                                }
                                catch
                                {
                                    await this.WriteToFileAsync(fullpath, d.Data, FileOperationFlags.BackupMode, cancel: cancel);
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

        public async Task<FileAlternateStreamMetadata> GetFileAlternateStreamMetadataAsync(string path, CancellationToken cancel = default)
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
                            memory = await this.ReadFromFileAsync(fileName, (int)Win32MaxAlternateStreamSize, FileOperationFlags.None, cancel);
                        }
                        catch
                        {
                            memory = await this.ReadFromFileAsync(fileName, (int)Win32MaxAlternateStreamSize, FileOperationFlags.BackupMode, cancel);
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

        public FileSecurityMetadata GetFileOrDirectorySecurityMetadata(string path, bool isDirectory)
        {
            FileSecurityMetadata ret = new FileSecurityMetadata();
            string ssdl;

            if (Env.IsWindows)
            {
                ssdl = Win32GetFileOrDirectorySecuritySsdlInternal(path, isDirectory, AccessControlSections.Owner);
                if (ssdl.IsFilled())
                    ret.Owner = new FileSecurityOwner() { Win32OwnerSsdl = ssdl };

                ssdl = Win32GetFileOrDirectorySecuritySsdlInternal(path, isDirectory, AccessControlSections.Group);
                if (ssdl.IsFilled())
                    ret.Group = new FileSecurityGroup() { Win32GroupSsdl = ssdl };

                ssdl = Win32GetFileOrDirectorySecuritySsdlInternal(path, isDirectory, AccessControlSections.Access);
                if (ssdl.IsFilled())
                    ret.Acl = new FileSecurityAcl() { Win32AclSsdl = ssdl };

                ssdl = Win32GetFileOrDirectorySecuritySsdlInternal(path, isDirectory, AccessControlSections.Audit);
                if (ssdl.IsFilled())
                    ret.Audit = new FileSecurityAudit() { Win32AuditSsdl = ssdl };
            }

            return ret.FilledOrDefault();
        }

        public void ApplyFileOrDirectorySpecialOperationFlagsMedatata(string path, bool isDirectory, FileSpecialOperationFlags operationFlags)
        {
            Util.DoMultipleActions(MultipleActionsFlag.AllOk,
                () =>
                {
                    if (operationFlags.Bit(FileSpecialOperationFlags.SetCompressionFlag))
                        if (Env.IsWindows)
                            Win32ApiUtil.SetCompressionFlag(path, isDirectory, true);
                },
                () =>
                {
                    if (operationFlags.Bit(FileSpecialOperationFlags.RemoveCompressionFlag))
                        if (Env.IsWindows)
                            Win32ApiUtil.SetCompressionFlag(path, isDirectory, false);
                }
                );
        }

        public void SetFileOrDirectorySecurityMetadata(string path, bool isDirectory, FileSecurityMetadata data)
        {
            if (Env.IsWindows)
            {
                if (data.IsFilled())
                {
                    Util.DoMultipleActions(MultipleActionsFlag.AllOk,
                        () => Win32SetFileOrDirectorySecuritySsdlInternal(path, isDirectory, data?.Owner?.Win32OwnerSsdl, AccessControlSections.Owner),
                        () => Win32SetFileOrDirectorySecuritySsdlInternal(path, isDirectory, data?.Group?.Win32GroupSsdl, AccessControlSections.Group),
                        () => Win32SetFileOrDirectorySecuritySsdlInternal(path, isDirectory, data?.Acl?.Win32AclSsdl, AccessControlSections.Access),
                        () => Win32SetFileOrDirectorySecuritySsdlInternal(path, isDirectory, data?.Audit?.Win32AuditSsdl, AccessControlSections.Audit)
                        );
                }
            }
        }

        protected override Task<string> NormalizePathImplAsync(string path, CancellationToken cancel = default)
        {
            return Task.FromResult(Path.GetFullPath(path));
        }

        protected override async Task<FileMetadata> GetFileMetadataImplAsync(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.None, CancellationToken cancel = default)
        {
            FileInfo fileInfo = new FileInfo(path);

            if (fileInfo.Exists == false)
                throw new FileNotFoundException($"The file '{path}' not found.");

            FileMetadata ret = ConvertFileSystemInfoToFileMetadata(fileInfo, flags);

            if (flags.Bit(FileMetadataGetFlags.NoSecurity) == false)
            {
                ret.Security = GetFileOrDirectorySecurityMetadata(path, false);
            }

            // Try to open to retrieve the actual physical file
            if (flags.Bit(FileMetadataGetFlags.NoPreciseFileSize) == false)
            {
                FileObjectBase f = null;
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
            Util.DoMultipleActions(MultipleActionsFlag.AllOk,
                () =>
                {
                    if (metadata.AlternateStream != null)
                        SetFileAlternateStreamMetadataAsync(path, metadata.AlternateStream, cancel).GetResult();
                },
                () =>
                {
                    if (metadata.Attributes != null)
                        SetFileAttributes(path, (FileAttributes)metadata.Attributes);
                },
                () =>
                {
                    ApplyFileOrDirectorySpecialOperationFlagsMedatata(path, false, metadata.SpecialOperationFlags);
                },
                () =>
                {
                    if (metadata.CreationTime != null)
                        SetFileCreationTimeUtc(path, ((DateTimeOffset)metadata.CreationTime).UtcDateTime);
                },
                () =>
                {
                    if (metadata.LastWriteTime != null)
                        SetFileLastWriteTimeUtc(path, ((DateTimeOffset)metadata.LastWriteTime).UtcDateTime);
                },
                () =>
                {
                    if (metadata.LastAccessTime != null)
                        SetFileLastAccessTimeUtc(path, ((DateTimeOffset)metadata.LastAccessTime).UtcDateTime);
                },
                () =>
                {
                    if (metadata.Security != null)
                        SetFileOrDirectorySecurityMetadata(path, false, metadata.Security);
                }
                );

            await Task.CompletedTask;
        }

        protected override async Task<FileMetadata> GetDirectoryMetadataImplAsync(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.None, CancellationToken cancel = default)
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
            Util.DoMultipleActions(MultipleActionsFlag.AllOk,
                () =>
                {
                    if (metadata.Attributes != null)
                        SetDirectoryAttributes(path, (FileAttributes)metadata.Attributes);
                },
                () =>
                {
                    ApplyFileOrDirectorySpecialOperationFlagsMedatata(path, true, metadata.SpecialOperationFlags);
                },
                () =>
                {
                    if (metadata.CreationTime != null)
                        SetDirectoryCreationTimeUtc(path, ((DateTimeOffset)metadata.CreationTime).UtcDateTime);
                },
                () =>
                {
                    if (metadata.LastWriteTime != null)
                        SetDirectoryLastWriteTimeUtc(path, ((DateTimeOffset)metadata.LastWriteTime).UtcDateTime);
                },
                () =>
                {
                    if (metadata.LastAccessTime != null)
                        SetDirectoryLastAccessTimeUtc(path, ((DateTimeOffset)metadata.LastAccessTime).UtcDateTime);
                },
                () =>
                {
                    if (metadata.Security != null)
                        SetFileOrDirectorySecurityMetadata(path, true, metadata.Security);
                }
                );

            await Task.CompletedTask;
        }

        protected override async Task DeleteFileImplAsync(string path, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
        {
            if (flags.Bit(FileOperationFlags.BackupMode) || flags.Bit(FileOperationFlags.IgnoreReadOnlyOrHiddenBits))
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
                Util.DoMultipleActions(MultipleActionsFlag.AllOk,
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

    class LocalFileObject : FileObjectBase
    {
        string _PhysicalFinalPath = null;
        public override string FinalPhysicalPath => _PhysicalFinalPath.FilledOrException();

        protected LocalFileObject(FileSystemBase fileSystem, FileParameters fileParams) : base(fileSystem, fileParams) { }

        FileStream fileStream;

        long CurrentPosition;

        public static async Task<FileObjectBase> CreateFileAsync(LocalFileSystem fileSystem, FileParameters fileParams, CancellationToken cancel = default)
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
                ret = new FileStream(path, mode, access, share, bufferSize, FileOptions.Asynchronous);
            }

            return ret;
        }

        protected override async Task InternalInitAsync(CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();

            try
            {
                Con.WriteDebug($"InternalInitAsync '{FileParams.Path}'");

                FileOptions options = FileOptions.Asynchronous;

                if (Env.IsWindows)
                {
                    if (FileParams.Flags.Bit(FileOperationFlags.BackupMode))
                        options |= (FileOptions)Win32Api.Kernel32.FileOperations.FILE_FLAG_BACKUP_SEMANTICS;

                    if (FileParams.Access.Bit(FileAccess.Write))
                    {
                        if (FileParams.Flags.Bit(FileOperationFlags.BackupMode) || FileParams.Flags.Bit(FileOperationFlags.IgnoreReadOnlyOrHiddenBits))
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

                    fileStream = Win32CreateFileStreamInternal(FileParams.Path, fileMode, FileParams.Access, FileParams.Share, 4096, options);
                }
                else
                {
                    FileMode fileMode = (FileParams.Mode != FileMode.Append) ? FileParams.Mode : FileMode.OpenOrCreate;

                    fileStream = new FileStream(FileParams.Path, fileMode, FileParams.Access, FileParams.Share, 4096, FileOptions.Asynchronous);
                }

                if (FileParams.Mode == FileMode.Append)
                    fileStream.Seek(0, SeekOrigin.End);

                if (Env.IsWindows)
                {
                    if (FileParams.Mode == FileMode.Create || FileParams.Mode == FileMode.CreateNew || FileParams.Mode == FileMode.OpenOrCreate || FileParams.Mode == FileMode.Append)
                    {
                        if (fileStream.Length == 0)
                        {
                            // Special operations on file creation
                            Util.DoMultipleActions(MultipleActionsFlag.AllOk,
                                () =>
                                {
                                    if (FileParams.Flags.Bit(FileOperationFlags.SetCompressionFlagOnCreate))
                                        Win32ApiUtil.SetCompressionFlag(fileStream.SafeFileHandle, true, FileParams.Path);
                                },
                                () =>
                                {
                                    if (FileParams.Flags.Bit(FileOperationFlags.RemoveCompressionFlagOnCreate))
                                        Win32ApiUtil.SetCompressionFlag(fileStream.SafeFileHandle, false, FileParams.Path);
                                }
                                );
                        }
                    }
                }

                string physicalFinalPathTmp = "";
                if (Env.IsWindows)
                {
                    try
                    {
                        physicalFinalPathTmp = Win32ApiUtil.GetFinalPath(fileStream.SafeFileHandle);
                    }
                    catch { }
                }

                if (physicalFinalPathTmp.IsEmpty())
                    physicalFinalPathTmp = FileParams.Path;

                _PhysicalFinalPath = physicalFinalPathTmp;

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

