﻿// IPA Cores.NET
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
    static class Win32Api
    {
        // DLL names
        internal static partial class Libraries
        {
            internal const string Advapi32 = "advapi32.dll";
            internal const string BCrypt = "BCrypt.dll";
            internal const string Crypt32 = "crypt32.dll";
            internal const string Kernel32 = "kernel32.dll";
            internal const string Ole32 = "ole32.dll";
            internal const string OleAut32 = "oleaut32.dll";
            internal const string User32 = "user32.dll";
            internal const string NtDll = "ntdll.dll";
        }

        // DLL import functions
        internal partial class Kernel32
        {
            [DllImport(Libraries.Kernel32, SetLastError = true)]
            internal static extern bool CloseHandle(IntPtr handle);

            [DllImport(Libraries.Kernel32, SetLastError = true)]
            internal static extern SafeProcessHandle GetCurrentProcess();

            [DllImport(Libraries.Kernel32, SetLastError = true, ExactSpelling = true)]

            internal static extern bool SetThreadErrorMode(uint dwNewMode, out uint lpOldMode);
            /// <summary>
            /// WARNING: This method does not implicitly handle long paths. Use CreateFile.
            /// </summary>
            [DllImport(Libraries.Kernel32, EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode, BestFitMapping = false)]
            private static extern SafeFileHandle CreateFilePrivate(
                string lpFileName,
                int dwDesiredAccess,
                System.IO.FileShare dwShareMode,
                ref SECURITY_ATTRIBUTES securityAttrs,
                System.IO.FileMode dwCreationDisposition,
                int dwFlagsAndAttributes,
                IntPtr hTemplateFile);

            internal static SafeFileHandle CreateFile(
                string lpFileName,
                int dwDesiredAccess,
                System.IO.FileShare dwShareMode,
                ref SECURITY_ATTRIBUTES securityAttrs,
                System.IO.FileMode dwCreationDisposition,
                int dwFlagsAndAttributes,
                IntPtr hTemplateFile)
            {
                lpFileName = Win32PathInternal.EnsureExtendedPrefixOverMaxPath(lpFileName);
                return CreateFilePrivate(lpFileName, dwDesiredAccess, dwShareMode, ref securityAttrs, dwCreationDisposition, dwFlagsAndAttributes, hTemplateFile);
            }
        }

        internal partial class Advapi32
        {
            [DllImport(Libraries.Advapi32, CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern bool OpenProcessToken(SafeProcessHandle ProcessHandle, int DesiredAccess, out SafeTokenHandle TokenHandle);

            [DllImport(Libraries.Advapi32, CharSet = CharSet.Unicode, SetLastError = true, BestFitMapping = false, EntryPoint = "LookupPrivilegeValueW")]
            internal static extern bool LookupPrivilegeValue([MarshalAs(UnmanagedType.LPTStr)] string lpSystemName, [MarshalAs(UnmanagedType.LPTStr)] string lpName, out LUID lpLuid);

            [DllImport(Libraries.Advapi32, SetLastError = true)]
            internal unsafe static extern bool AdjustTokenPrivileges(
                SafeTokenHandle TokenHandle,
                bool DisableAllPrivileges,
                TOKEN_PRIVILEGE* NewState,
                uint BufferLength,
                TOKEN_PRIVILEGE* PreviousState,
                uint* ReturnLength);
        }

        // Win32 types
        internal enum BOOL : int
        {
            FALSE = 0,
            TRUE = 1,
        }

        internal partial class Errors
        {
            internal const int ERROR_SUCCESS = 0x0;
            internal const int ERROR_FILE_NOT_FOUND = 0x2;
            internal const int ERROR_PATH_NOT_FOUND = 0x3;
            internal const int ERROR_ACCESS_DENIED = 0x5;
            internal const int ERROR_INVALID_HANDLE = 0x6;
            internal const int ERROR_NOT_ENOUGH_MEMORY = 0x8;
            internal const int ERROR_INVALID_DRIVE = 0xF;
            internal const int ERROR_NO_MORE_FILES = 0x12;
            internal const int ERROR_NOT_READY = 0x15;
            internal const int ERROR_SHARING_VIOLATION = 0x20;
            internal const int ERROR_HANDLE_EOF = 0x26;
            internal const int ERROR_FILE_EXISTS = 0x50;
            internal const int ERROR_INVALID_PARAMETER = 0x57;
            internal const int ERROR_BROKEN_PIPE = 0x6D;
            internal const int ERROR_INSUFFICIENT_BUFFER = 0x7A;
            internal const int ERROR_INVALID_NAME = 0x7B;
            internal const int ERROR_BAD_PATHNAME = 0xA1;
            internal const int ERROR_ALREADY_EXISTS = 0xB7;
            internal const int ERROR_ENVVAR_NOT_FOUND = 0xCB;
            internal const int ERROR_FILENAME_EXCED_RANGE = 0xCE;
            internal const int ERROR_NO_DATA = 0xE8;
            internal const int ERROR_MORE_DATA = 0xEA;
            internal const int ERROR_NO_MORE_ITEMS = 0x103;
            internal const int ERROR_NOT_OWNER = 0x120;
            internal const int ERROR_TOO_MANY_POSTS = 0x12A;
            internal const int ERROR_ARITHMETIC_OVERFLOW = 0x216;
            internal const int ERROR_MUTANT_LIMIT_EXCEEDED = 0x24B;
            internal const int ERROR_OPERATION_ABORTED = 0x3E3;
            internal const int ERROR_IO_PENDING = 0x3E5;
            internal const int ERROR_NO_UNICODE_TRANSLATION = 0x459;
            internal const int ERROR_NOT_FOUND = 0x490;
            internal const int ERROR_BAD_IMPERSONATION_LEVEL = 0x542;
            internal const int ERROR_NO_SYSTEM_RESOURCES = 0x5AA;
            internal const int ERROR_TIMEOUT = 0x000005B4;
        }

        internal partial class Kernel32
        {
            internal const uint SEM_FAILCRITICALERRORS = 1;

            internal partial class HandleOptions
            {
                internal const int DUPLICATE_SAME_ACCESS = 2;
                internal const int STILL_ACTIVE = 0x00000103;
                internal const int TOKEN_ADJUST_PRIVILEGES = 0x20;
            }

            internal partial class IOReparseOptions
            {
                internal const uint IO_REPARSE_TAG_FILE_PLACEHOLDER = 0x80000015;
                internal const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;
            }

            internal partial class FileOperations
            {
                internal const int OPEN_EXISTING = 3;
                internal const int COPY_FILE_FAIL_IF_EXISTS = 0x00000001;

                internal const int FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
                internal const int FILE_FLAG_FIRST_PIPE_INSTANCE = 0x00080000;
                internal const int FILE_FLAG_OVERLAPPED = 0x40000000;

                internal const int FILE_LIST_DIRECTORY = 0x0001;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct SECURITY_ATTRIBUTES
            {
                internal uint nLength;
                internal IntPtr lpSecurityDescriptor;
                internal BOOL bInheritHandle;
            }

            internal partial class SecurityOptions
            {
                internal const int SECURITY_SQOS_PRESENT = 0x00100000;
                internal const int SECURITY_ANONYMOUS = 0 << 16;
                internal const int SECURITY_IDENTIFICATION = 1 << 16;
                internal const int SECURITY_IMPERSONATION = 2 << 16;
                internal const int SECURITY_DELEGATION = 3 << 16;
            }
        }

        internal partial class Advapi32
        {
            internal const string SeDebugPrivilege = "SeDebugPrivilege";
            internal const string SeBackupPrivilege = "SeBackupPrivilege";
            internal const string SeRestorePrivilege = "SeRestorePrivilege";
            internal const string SeShutdownPrivilege = "SeShutdownPrivilege";
            internal const string SeRemoteShutdownPrivilege = "SeRemoteShutdownPrivilege";

            internal partial class SEPrivileges
            {
                internal const uint SE_PRIVILEGE_DISABLED = 0;
                internal const int SE_PRIVILEGE_ENABLED = 2;
            }

            internal partial class PerfCounterOptions
            {
                internal const int NtPerfCounterSizeLarge = 0x00000100;
            }

            internal partial class ProcessOptions
            {
                internal const int PROCESS_TERMINATE = 0x0001;
                internal const int PROCESS_VM_READ = 0x0010;
                internal const int PROCESS_SET_QUOTA = 0x0100;
                internal const int PROCESS_SET_INFORMATION = 0x0200;
                internal const int PROCESS_QUERY_INFORMATION = 0x0400;
                internal const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
                internal const int PROCESS_ALL_ACCESS = STANDARD_RIGHTS_REQUIRED | SYNCHRONIZE | 0xFFF;


                internal const int STANDARD_RIGHTS_REQUIRED = 0x000F0000;
                internal const int SYNCHRONIZE = 0x00100000;
            }

            internal partial class RPCStatus
            {
                internal const int RPC_S_SERVER_UNAVAILABLE = 1722;
                internal const int RPC_S_CALL_FAILED = 1726;
            }

            internal partial class StartupInfoOptions
            {
                internal const int STARTF_USESTDHANDLES = 0x00000100;
                internal const int CREATE_UNICODE_ENVIRONMENT = 0x00000400;
                internal const int CREATE_NO_WINDOW = 0x08000000;
                internal const uint STATUS_INFO_LENGTH_MISMATCH = 0xC0000004;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct LUID
            {
                internal int LowPart;
                internal int HighPart;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal partial struct LUID_AND_ATTRIBUTES
            {
                public LUID Luid;
                public uint Attributes;
            }

            internal struct TOKEN_PRIVILEGE
            {
                public uint PrivilegeCount;
                public LUID_AND_ATTRIBUTES Privileges /*[ANYSIZE_ARRAY]*/;
            }
        }

        // Support routines
        internal sealed class SafeTokenHandle : SafeHandle
        {
            private const int DefaultInvalidHandleValue = 0;

            internal static readonly SafeTokenHandle InvalidHandle = new SafeTokenHandle(new IntPtr(DefaultInvalidHandleValue));

            internal SafeTokenHandle() : base(new IntPtr(DefaultInvalidHandleValue), true) { }

            internal SafeTokenHandle(IntPtr handle)
                : base(new IntPtr(DefaultInvalidHandleValue), true)
            {
                SetHandle(handle);
            }

            public override bool IsInvalid
            {
                get { return handle == IntPtr.Zero || handle == new IntPtr(-1); }
            }

            protected override bool ReleaseHandle()
            {
                return Win32Api.Kernel32.CloseHandle(handle);
            }
        }

        public static void EnablePrivilege(string privilegeName, bool enabled)
        {
            SetPrivilege(privilegeName, enabled ? (int)Win32Api.Advapi32.SEPrivileges.SE_PRIVILEGE_ENABLED : 0);
        }

        public static unsafe void SetPrivilege(string privilegeName, int attrib)
        {
            // this is only a "pseudo handle" to the current process - no need to close it later
            SafeProcessHandle processHandle = Win32Api.Kernel32.GetCurrentProcess();

            SafeTokenHandle hToken = null;

            try
            {
                // get the process token so we can adjust the privilege on it.  We DO need to
                // close the token when we're done with it.
                if (!Win32Api.Advapi32.OpenProcessToken(processHandle, Win32Api.Kernel32.HandleOptions.TOKEN_ADJUST_PRIVILEGES, out hToken))
                {
                    throw new Win32Exception();
                }

                if (!Win32Api.Advapi32.LookupPrivilegeValue(null, privilegeName, out Win32Api.Advapi32.LUID luid))
                {
                    throw new Win32Exception();
                }

                Win32Api.Advapi32.TOKEN_PRIVILEGE tp;
                tp.PrivilegeCount = 1;
                tp.Privileges.Luid = luid;
                tp.Privileges.Attributes = (uint)attrib;

                Win32Api.Advapi32.AdjustTokenPrivileges(hToken, false, &tp, 0, null, null);

                // AdjustTokenPrivileges can return true even if it failed to
                // set the privilege, so we need to use GetLastError
                if (Marshal.GetLastWin32Error() != Win32Api.Errors.ERROR_SUCCESS)
                {
                    throw new Win32Exception();
                }
            }
            finally
            {
                if (hToken != null)
                {
                    hToken.Dispose();
                }
            }
        }

        internal struct Win32DisableMediaInsertionPrompt : IDisposable
        {
            private bool _disableSuccess;
            private uint _oldMode;

            public static Win32DisableMediaInsertionPrompt Create()
            {
                Win32DisableMediaInsertionPrompt prompt = new Win32DisableMediaInsertionPrompt();
                prompt._disableSuccess = Win32Api.Kernel32.SetThreadErrorMode(Win32Api.Kernel32.SEM_FAILCRITICALERRORS, out prompt._oldMode);
                return prompt;
            }

            public void Dispose()
            {
                uint ignore;
                if (_disableSuccess)
                    Win32Api.Kernel32.SetThreadErrorMode(_oldMode, out ignore);
            }
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
            if (options != FileOptions.None && (options & ~(FileOptions.WriteThrough | FileOptions.Asynchronous | FileOptions.RandomAccess | FileOptions.DeleteOnClose | FileOptions.SequentialScan | FileOptions.Encrypted | (FileOptions)0x20000000 /* NoBuffering */
                | (FileOptions)Win32Api.Kernel32.FileOperations.FILE_FLAG_BACKUP_SEMANTICS // added for backup
                )) != 0)
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

                throw new Win32Exception(errorCode);
                //throw Win32Marshal.GetExceptionForWin32Error(errorCode, _path);
            }

            //fileHandle.IsAsync = _useAsyncIO;
            return fileHandle;
        }
    }
}

