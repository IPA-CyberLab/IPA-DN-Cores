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
using System.Buffers;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Diagnostics;
using System.Collections.Generic;

#pragma warning disable 0618

// Some parts of this program are from Microsoft CoreCLR - https://github.com/dotnet/coreclr
// 
// The MIT License (MIT)
// 
// Copyright (c) .NET Foundation and Contributors
// 
// All rights reserved.
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

namespace IPA.Cores.Basic
{
    static partial class Win32ApiUtil
    {
        public static int FillAttributeInfo(string path, ref Win32Api.Kernel32.WIN32_FILE_ATTRIBUTE_DATA data, bool returnErrorOnNotFound)
        {
            int errorCode = Win32Api.Errors.ERROR_SUCCESS;

            // Neither GetFileAttributes or FindFirstFile like trailing separators
            path = Win32PathInternal.TrimEndingDirectorySeparator(path);

            using (Win32Api.Win32DisableMediaInsertionPrompt.Create())
            {
                if (!Win32Api.Kernel32.GetFileAttributesEx(path, Win32Api.Kernel32.GET_FILEEX_INFO_LEVELS.GetFileExInfoStandard, ref data))
                {
                    errorCode = Marshal.GetLastWin32Error();
                    if (errorCode != Win32Api.Errors.ERROR_FILE_NOT_FOUND
                        && errorCode != Win32Api.Errors.ERROR_PATH_NOT_FOUND
                        && errorCode != Win32Api.Errors.ERROR_NOT_READY
                        && errorCode != Win32Api.Errors.ERROR_INVALID_NAME
                        && errorCode != Win32Api.Errors.ERROR_BAD_PATHNAME
                        && errorCode != Win32Api.Errors.ERROR_BAD_NETPATH
                        && errorCode != Win32Api.Errors.ERROR_BAD_NET_NAME
                        && errorCode != Win32Api.Errors.ERROR_INVALID_PARAMETER
                        && errorCode != Win32Api.Errors.ERROR_NETWORK_UNREACHABLE
                        && errorCode != Win32Api.Errors.ERROR_NETWORK_ACCESS_DENIED
                        && errorCode != Win32Api.Errors.ERROR_INVALID_HANDLE  // eg from \\.\CON
                        )
                    {
                        // Assert so we can track down other cases (if any) to add to our test suite
                        Debug.Assert(errorCode == Win32Api.Errors.ERROR_ACCESS_DENIED || errorCode == Win32Api.Errors.ERROR_SHARING_VIOLATION,
                            $"Unexpected error code getting attributes {errorCode}");

                        // Files that are marked for deletion will not let you GetFileAttributes,
                        // ERROR_ACCESS_DENIED is given back without filling out the data struct.
                        // FindFirstFile, however, will. Historically we always gave back attributes
                        // for marked-for-deletion files.
                        //
                        // Another case where enumeration works is with special system files such as
                        // pagefile.sys that give back ERROR_SHARING_VIOLATION on GetAttributes.
                        //
                        // Ideally we'd only try again for known cases due to the potential performance
                        // hit. The last attempt to do so baked for nearly a year before we found the
                        // pagefile.sys case. As such we're probably stuck filtering out specific
                        // cases that we know we don't want to retry on.

                        var findData = new Win32Api.Kernel32.WIN32_FIND_DATA();
                        using (Win32Api.SafeFindHandle handle = Win32Api.Kernel32.FindFirstFile(path, ref findData))
                        {
                            if (handle.IsInvalid)
                            {
                                errorCode = Marshal.GetLastWin32Error();
                            }
                            else
                            {
                                errorCode = Win32Api.Errors.ERROR_SUCCESS;
                                data.PopulateFrom(ref findData);
                            }
                        }
                    }
                }
            }

            if (errorCode != Win32Api.Errors.ERROR_SUCCESS && !returnErrorOnNotFound)
            {
                switch (errorCode)
                {
                    case Win32Api.Errors.ERROR_FILE_NOT_FOUND:
                    case Win32Api.Errors.ERROR_PATH_NOT_FOUND:
                    case Win32Api.Errors.ERROR_NOT_READY: // Removable media not ready
                        // Return default value for backward compatibility
                        data.dwFileAttributes = -1;
                        return Win32Api.Errors.ERROR_SUCCESS;
                }
            }

            return errorCode;
        }

        public static FileAttributes GetAttributes(string fullPath, bool backupMode = false)
        {
            int flags = backupMode ? Win32Api.Kernel32.FileOperations.FILE_FLAG_BACKUP_SEMANTICS : 0;

            Win32Api.Kernel32.WIN32_FILE_ATTRIBUTE_DATA data = new Win32Api.Kernel32.WIN32_FILE_ATTRIBUTE_DATA();
            int errorCode = FillAttributeInfo(fullPath, ref data, returnErrorOnNotFound: true);
            if (errorCode != 0)
                throw PalWin32FileStream.GetExceptionForWin32Error(errorCode, fullPath);

            return (FileAttributes)data.dwFileAttributes;
        }

        public static void SetAttributes(string fullPath, FileAttributes attributes)
        {
            if (!Win32Api.Kernel32.SetFileAttributes(fullPath, (int)attributes))
            {
                int errorCode = Marshal.GetLastWin32Error();
                if (errorCode == Win32Api.Errors.ERROR_INVALID_PARAMETER)
                    throw new ArgumentException(nameof(attributes));
                throw PalWin32FileStream.GetExceptionForWin32Error(errorCode, fullPath);
            }
        }

        public static void SetLastWriteTime(string fullPath, DateTimeOffset time, bool asDirectory, bool backupMode = false)
        {
            using (SafeFileHandle handle = OpenHandle(fullPath, asDirectory, true, backupMode))
            {
                if (!Win32Api.Kernel32.SetFileTime(handle, lastWriteTime: time.ToFileTime()))
                {
                    throw PalWin32FileStream.GetExceptionForLastWin32Error(fullPath);
                }
            }
        }

        public static void SetCreationTime(string fullPath, DateTimeOffset time, bool asDirectory, bool backupMode = false)
        {
            using (SafeFileHandle handle = OpenHandle(fullPath, asDirectory, true, backupMode))
            {
                if (!Win32Api.Kernel32.SetFileTime(handle, creationTime: time.ToFileTime()))
                {
                    throw PalWin32FileStream.GetExceptionForLastWin32Error(fullPath);
                }
            }
        }

        public static void SetLastAccessTime(string fullPath, DateTimeOffset time, bool asDirectory, bool backupMode = false)
        {
            using (SafeFileHandle handle = OpenHandle(fullPath, asDirectory, true, backupMode))
            {
                if (!Win32Api.Kernel32.SetFileTime(handle, lastAccessTime: time.ToFileTime()))
                {
                    throw PalWin32FileStream.GetExceptionForLastWin32Error(fullPath);
                }
            }
        }

        public static SafeFileHandle OpenHandle(string fullPath, bool asDirectory, bool writeMode = false, bool backupMode = false, int additionalFlags = 0)
        {
            string root = fullPath.Substring(0, Win32PathInternal.GetRootLength(fullPath.AsSpan()));
            if (root == fullPath && root[1] == Path.VolumeSeparatorChar)
            {
                // intentionally not fullpath, most upstack public APIs expose this as path.
                throw new ArgumentException("path");
            }

            SafeFileHandle handle = Win32Api.Kernel32.CreateFile(
                fullPath,
                writeMode ? Win32Api.Kernel32.GenericOperations.GENERIC_WRITE | Win32Api.Kernel32.GenericOperations.GENERIC_READ : Win32Api.Kernel32.GenericOperations.GENERIC_READ,
                FileShare.ReadWrite | FileShare.Delete,
                FileMode.Open,
                ((asDirectory || backupMode) ? Win32Api.Kernel32.FileOperations.FILE_FLAG_BACKUP_SEMANTICS : 0) | additionalFlags);

            if (handle.IsInvalid)
            {
                int errorCode = Marshal.GetLastWin32Error();

                // NT5 oddity - when trying to open "C:\" as a File,
                // we usually get ERROR_PATH_NOT_FOUND from the OS.  We should
                // probably be consistent w/ every other directory.
                if (!asDirectory && errorCode == Win32Api.Errors.ERROR_PATH_NOT_FOUND && fullPath.Equals(Directory.GetDirectoryRoot(fullPath)))
                    errorCode = Win32Api.Errors.ERROR_ACCESS_DENIED;

                throw PalWin32FileStream.GetExceptionForWin32Error(errorCode, fullPath);
            }

            return handle;
        }

        public static void ThrowLastWin32Error(string argument = null)
            => ThrowWin32Error(null, argument);

        public static Exception ThrowWin32Error(int? errorCode = null, string argument = null)
        {
            var exception = GetWin32ErrorException(errorCode, argument);
            throw exception;
#pragma warning disable CS0162
            return exception;
#pragma warning restore CS0162
        }

        public static Exception GetWin32ErrorException(int? errorCode = null, string argument = null)
            => PalWin32FileStream.GetExceptionForWin32Error(errorCode ?? Marshal.GetLastWin32Error(), argument);


        public static string GetFinalPath(SafeFileHandle handle,
            Win32Api.Kernel32.FinalPathFlags flags = Win32Api.Kernel32.FinalPathFlags.FILE_NAME_NORMALIZED | Win32Api.Kernel32.FinalPathFlags.VOLUME_NAME_DOS)
        {
            StringBuilder str = new StringBuilder(260 + 10);
            int r;

            r = Win32Api.Kernel32.GetFinalPathNameByHandle(handle, str, 260, flags);

            if (r >= 260)
            {
                str = new StringBuilder(65536 + 10);
                r = Win32Api.Kernel32.GetFinalPathNameByHandle(handle, str, 65536, flags);
            }

            if (r == 0)
                Win32ApiUtil.ThrowLastWin32Error();

            return str.ToString();
        }

        public static long GetCompressedFileSize(string filename)
            => (long)Win32Api.Kernel32.GetCompressedFileSize(filename);

        public static void SetCompressionFlag(string path, bool isDirectory, bool compressionEnabled)
            => Util.DoMultipleActions(MultipleActionsFlag.AnyOk,
                () => SetCompressionFlag(path, isDirectory, compressionEnabled, false),
                () => SetCompressionFlag(path, isDirectory, compressionEnabled, true)
                );

        public static void SetCompressionFlag(string path, bool isDirectory, bool compressionEnabled, bool isBackupMode)
        {
            using (var handle = OpenHandle(path, isDirectory, true, isBackupMode))
            {
                SetCompressionFlag(handle, compressionEnabled, path);
            }
        }

        public static void SetCompressionFlag(SafeFileHandle handle, bool compressionEnabled, string pathForReference = null)
        {
            if (Env.IsWindows == false) return;

            ushort lpInBuffer = (ushort)(compressionEnabled ? Win32Api.Kernel32.COMPRESSION_FORMAT_DEFAULT : Win32Api.Kernel32.COMPRESSION_FORMAT_NONE);
            uint lpBytesReturned = 0;

            if (Win32Api.Kernel32.DeviceIoControl(handle, Win32Api.Kernel32.FSCTL_SET_COMPRESSION, ref lpInBuffer, sizeof(short), IntPtr.Zero, 0, out lpBytesReturned, IntPtr.Zero) == false)
            {
                Win32ApiUtil.ThrowLastWin32Error(pathForReference);
            }
        }

        public static List<Tuple<string, long>> EnumAlternateStreams(string path, long maxSize, int maxNum)
        {
            if (Env.IsWindows == false) return null;

            return Util.DoMultipleFuncs(MultipleActionsFlag.AnyOk,
                () => EnumAlternateStreamsInternal_UseFindFirstApi(path, maxSize, maxNum),
                () => EnumAlternateStreamsInternal_UseNtDllApi(path, maxSize, maxNum, true)
                );
        }

        public static List<Tuple<string, long>> EnumAlternateStreamsInternal_UseNtDllApi(string path, long maxSize, int maxNum, bool backupMode = false)
        {
            if (Env.IsWindows == false) return null;

            using (var fileHandle = Win32ApiUtil.OpenHandle(path, false, false, backupMode))
            {
                var list = Win32Api.NtDll.EnumAlternateStreamInformation(fileHandle);

                List<Tuple<string, long>> ret = new List<Tuple<string, long>>();

                foreach (var info in list)
                {
                    if (info.StreamName.IsSamei("::$DATA") == false && info.StreamName.StartsWith(":") && info.StreamName.EndsWith(":$DATA", StringComparison.OrdinalIgnoreCase)
                           && info.StreamSize <= maxSize)
                    {
                        ret.Add(new Tuple<string, long>(info.StreamName, info.StreamSize));
                        if (ret.Count >= maxNum)
                        {
                            break;
                        }
                    }
                }

                return ret;
            }
        }

        public static List<Tuple<string, long>> EnumAlternateStreamsInternal_UseFindFirstApi(string path, long maxSize, int maxNum)
        {
            if (Env.IsWindows == false) return null;

            Win32Api.Kernel32.WIN32_FIND_STREAM_DATA data;

            var findHandle = Win32Api.Kernel32.FindFirstStreamW(path, out data);

            if (findHandle.IsInvalid)
            {
                throw new ApplicationException($"FindFirstStreamW Error: {Marshal.GetLastWin32Error()}");
            }

            List<Tuple<string, long>> ret = new List<Tuple<string, long>>();

            using (findHandle)
            {
                while (true)
                {
                    if (data.cStreamName.IsSamei("::$DATA") == false && data.cStreamName.StartsWith(":") && data.cStreamName.EndsWith(":$DATA", StringComparison.OrdinalIgnoreCase)
                        && data.StreamSize.QuadPart <= maxSize)
                    {
                        ret.Add(new Tuple<string, long>(data.cStreamName, data.StreamSize.QuadPart));
                        if (ret.Count >= maxNum)
                        {
                            break;
                        }
                    }

                    if (Win32Api.Kernel32.FindNextStreamW(findHandle, out data) == false)
                    {
                        break;
                    }
                }
            }

            return ret;
        }
    }
}


