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
using static IPA.Cores.GlobalFunctions.Basic;
using System.Diagnostics;

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

            [DllImport(Libraries.Kernel32, EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode, BestFitMapping = false, ExactSpelling = true)]
            private unsafe static extern IntPtr CreateFilePrivate(
                string lpFileName,
                int dwDesiredAccess,
                FileShare dwShareMode,
                SECURITY_ATTRIBUTES* securityAttrs,
                FileMode dwCreationDisposition,
                int dwFlagsAndAttributes,
                IntPtr hTemplateFile);

            internal unsafe static SafeFileHandle CreateFile(
                string lpFileName,
                int dwDesiredAccess,
                FileShare dwShareMode,
                ref SECURITY_ATTRIBUTES securityAttrs,
                FileMode dwCreationDisposition,
                int dwFlagsAndAttributes,
                IntPtr hTemplateFile)
            {
                lpFileName = Win32PathInternal.EnsureExtendedPrefixIfNeeded(lpFileName);
                fixed (SECURITY_ATTRIBUTES* sa = &securityAttrs)
                {
                    IntPtr handle = CreateFilePrivate(lpFileName, dwDesiredAccess, dwShareMode, sa, dwCreationDisposition, dwFlagsAndAttributes, hTemplateFile);
                    try
                    {
                        return new SafeFileHandle(handle, ownsHandle: true);
                    }
                    catch
                    {
                        CloseHandle(handle);
                        throw;
                    }
                }
            }

            internal unsafe static SafeFileHandle CreateFile(
                string lpFileName,
                int dwDesiredAccess,
                FileShare dwShareMode,
                FileMode dwCreationDisposition,
                int dwFlagsAndAttributes)
            {
                IntPtr handle = CreateFile_IntPtr(lpFileName, dwDesiredAccess, dwShareMode, dwCreationDisposition, dwFlagsAndAttributes);
                try
                {
                    return new SafeFileHandle(handle, ownsHandle: true);
                }
                catch
                {
                    CloseHandle(handle);
                    throw;
                }
            }

            internal unsafe static IntPtr CreateFile_IntPtr(
                string lpFileName,
                int dwDesiredAccess,
                FileShare dwShareMode,
                FileMode dwCreationDisposition,
                int dwFlagsAndAttributes)
            {
                lpFileName = Win32PathInternal.EnsureExtendedPrefixIfNeeded(lpFileName);
                return CreateFilePrivate(lpFileName, dwDesiredAccess, dwShareMode, null, dwCreationDisposition, dwFlagsAndAttributes, IntPtr.Zero);
            }


            [DllImport(Libraries.Kernel32, CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern bool DeviceIoControl
            (
                SafeFileHandle fileHandle,
                uint ioControlCode,
                ref ushort inBuffer,
                uint cbInBuffer,
                IntPtr outBuffer,
                uint cbOutBuffer,
                out uint cbBytesReturned,
                IntPtr overlapped
            );

            [DllImport(Libraries.Kernel32, SetLastError = true, ExactSpelling = true)]
            internal static extern bool SetFileInformationByHandle(SafeFileHandle hFile, FILE_INFO_BY_HANDLE_CLASS FileInformationClass, ref FILE_BASIC_INFO lpFileInformation, uint dwBufferSize);

            // Default values indicate "no change".  Use defaults so that we don't force callsites to be aware of the default values
            internal static unsafe bool SetFileTime(
                SafeFileHandle hFile,
                long creationTime = -1,
                long lastAccessTime = -1,
                long lastWriteTime = -1,
                long changeTime = -1,
                uint fileAttributes = 0)
            {
                FILE_BASIC_INFO basicInfo = new FILE_BASIC_INFO()
                {
                    CreationTime = creationTime,
                    LastAccessTime = lastAccessTime,
                    LastWriteTime = lastWriteTime,
                    ChangeTime = changeTime,
                    FileAttributes = fileAttributes
                };

                return SetFileInformationByHandle(hFile, FILE_INFO_BY_HANDLE_CLASS.FileBasicInfo, ref basicInfo, (uint)sizeof(FILE_BASIC_INFO));
            }

            [DllImport(Libraries.Kernel32, EntryPoint = "SetFileAttributesW", SetLastError = true, CharSet = CharSet.Unicode, BestFitMapping = false)]
            private static extern bool SetFileAttributesPrivate(string name, int attr);

            internal static bool SetFileAttributes(string name, int attr)
            {
                name = Win32PathInternal.EnsureExtendedPrefixIfNeeded(name);
                return SetFileAttributesPrivate(name, attr);
            }

            [DllImport(Libraries.Kernel32, EntryPoint = "GetFileAttributesExW", SetLastError = true, CharSet = CharSet.Unicode)]
            private static extern bool GetFileAttributesExPrivate(string name, GET_FILEEX_INFO_LEVELS fileInfoLevel, ref WIN32_FILE_ATTRIBUTE_DATA lpFileInformation);

            internal static bool GetFileAttributesEx(string name, GET_FILEEX_INFO_LEVELS fileInfoLevel, ref WIN32_FILE_ATTRIBUTE_DATA lpFileInformation)
            {
                name = Win32PathInternal.EnsureExtendedPrefixOverMaxPath(name);
                return GetFileAttributesExPrivate(name, fileInfoLevel, ref lpFileInformation);
            }

            [DllImport(Libraries.Kernel32, SetLastError = true)]
            internal static extern bool FindClose(IntPtr hFindFile);

            [DllImport(Libraries.Kernel32, EntryPoint = "FindFirstFileExW", SetLastError = true, CharSet = CharSet.Unicode)]
            private static extern SafeFindHandle FindFirstFileExPrivate(string lpFileName, FINDEX_INFO_LEVELS fInfoLevelId, ref WIN32_FIND_DATA lpFindFileData, FINDEX_SEARCH_OPS fSearchOp, IntPtr lpSearchFilter, int dwAdditionalFlags);

            internal static SafeFindHandle FindFirstFile(string fileName, ref WIN32_FIND_DATA data)
            {
                fileName = Win32PathInternal.EnsureExtendedPrefixIfNeeded(fileName);

                // use FindExInfoBasic since we don't care about short name and it has better perf
                return FindFirstFileExPrivate(fileName, FINDEX_INFO_LEVELS.FindExInfoBasic, ref data, FINDEX_SEARCH_OPS.FindExSearchNameMatch, IntPtr.Zero, 0);
            }

            [DllImport(Libraries.Kernel32, EntryPoint = "FindFirstStreamW", SetLastError = true, CharSet = CharSet.Unicode, BestFitMapping = false)]
            private static extern SafeFindHandle FindFirstStreamWPrivate(string lpFileName, STREAM_INFO_LEVELS InfoLevel, out WIN32_FIND_STREAM_DATA lpFindStreamData, int dwFlags);

            internal static SafeFindHandle FindFirstStreamW(string lpFileName, out WIN32_FIND_STREAM_DATA lpFindStreamData)
            {
                lpFileName = Win32PathInternal.EnsureExtendedPrefixIfNeeded(lpFileName);

                return FindFirstStreamWPrivate(lpFileName, STREAM_INFO_LEVELS.FindStreamInfoStandard, out lpFindStreamData, 0);
            }

            [DllImport(Libraries.Kernel32, SetLastError = true, CharSet = CharSet.Auto, BestFitMapping = false)]
            public static extern bool FindNextStreamW(SafeFindHandle hFindStream, out WIN32_FIND_STREAM_DATA lpFindStreamData);

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
            internal const int ERROR_INVALID_FUNCTION = 0x1;
            internal const int ERROR_FILE_NOT_FOUND = 0x2;
            internal const int ERROR_PATH_NOT_FOUND = 0x3;
            internal const int ERROR_ACCESS_DENIED = 0x5;
            internal const int ERROR_INVALID_HANDLE = 0x6;
            internal const int ERROR_NOT_ENOUGH_MEMORY = 0x8;
            internal const int ERROR_INVALID_DATA = 0xD;
            internal const int ERROR_INVALID_DRIVE = 0xF;
            internal const int ERROR_NO_MORE_FILES = 0x12;
            internal const int ERROR_NOT_READY = 0x15;
            internal const int ERROR_BAD_COMMAND = 0x16;
            internal const int ERROR_BAD_LENGTH = 0x18;
            internal const int ERROR_SHARING_VIOLATION = 0x20;
            internal const int ERROR_LOCK_VIOLATION = 0x21;
            internal const int ERROR_HANDLE_EOF = 0x26;
            internal const int ERROR_BAD_NETPATH = 0x35;
            internal const int ERROR_NETWORK_ACCESS_DENIED = 0x41;
            internal const int ERROR_BAD_NET_NAME = 0x43;
            internal const int ERROR_FILE_EXISTS = 0x50;
            internal const int ERROR_INVALID_PARAMETER = 0x57;
            internal const int ERROR_BROKEN_PIPE = 0x6D;
            internal const int ERROR_SEM_TIMEOUT = 0x79;
            internal const int ERROR_CALL_NOT_IMPLEMENTED = 0x78;
            internal const int ERROR_INSUFFICIENT_BUFFER = 0x7A;
            internal const int ERROR_INVALID_NAME = 0x7B;
            internal const int ERROR_NEGATIVE_SEEK = 0x83;
            internal const int ERROR_DIR_NOT_EMPTY = 0x91;
            internal const int ERROR_BAD_PATHNAME = 0xA1;
            internal const int ERROR_LOCK_FAILED = 0xA7;
            internal const int ERROR_BUSY = 0xAA;
            internal const int ERROR_ALREADY_EXISTS = 0xB7;
            internal const int ERROR_BAD_EXE_FORMAT = 0xC1;
            internal const int ERROR_ENVVAR_NOT_FOUND = 0xCB;
            internal const int ERROR_FILENAME_EXCED_RANGE = 0xCE;
            internal const int ERROR_EXE_MACHINE_TYPE_MISMATCH = 0xD8;
            internal const int ERROR_PIPE_BUSY = 0xE7;
            internal const int ERROR_NO_DATA = 0xE8;
            internal const int ERROR_PIPE_NOT_CONNECTED = 0xE9;
            internal const int ERROR_MORE_DATA = 0xEA;
            internal const int ERROR_NO_MORE_ITEMS = 0x103;
            internal const int ERROR_DIRECTORY = 0x10B;
            internal const int ERROR_PARTIAL_COPY = 0x12B;
            internal const int ERROR_ARITHMETIC_OVERFLOW = 0x216;
            internal const int ERROR_PIPE_CONNECTED = 0x217;
            internal const int ERROR_PIPE_LISTENING = 0x218;
            internal const int ERROR_OPERATION_ABORTED = 0x3E3;
            internal const int ERROR_IO_INCOMPLETE = 0x3E4;
            internal const int ERROR_IO_PENDING = 0x3E5;
            internal const int ERROR_NO_TOKEN = 0x3f0;
            internal const int ERROR_SERVICE_DOES_NOT_EXIST = 0x424;
            internal const int ERROR_DLL_INIT_FAILED = 0x45A;
            internal const int ERROR_COUNTER_TIMEOUT = 0x461;
            internal const int ERROR_NO_ASSOCIATION = 0x483;
            internal const int ERROR_DDE_FAIL = 0x484;
            internal const int ERROR_DLL_NOT_FOUND = 0x485;
            internal const int ERROR_NOT_FOUND = 0x490;
            internal const int ERROR_NETWORK_UNREACHABLE = 0x4CF;
            internal const int ERROR_NON_ACCOUNT_SID = 0x4E9;
            internal const int ERROR_NOT_ALL_ASSIGNED = 0x514;
            internal const int ERROR_UNKNOWN_REVISION = 0x519;
            internal const int ERROR_INVALID_OWNER = 0x51B;
            internal const int ERROR_INVALID_PRIMARY_GROUP = 0x51C;
            internal const int ERROR_NO_SUCH_PRIVILEGE = 0x521;
            internal const int ERROR_PRIVILEGE_NOT_HELD = 0x522;
            internal const int ERROR_INVALID_ACL = 0x538;
            internal const int ERROR_INVALID_SECURITY_DESCR = 0x53A;
            internal const int ERROR_INVALID_SID = 0x539;
            internal const int ERROR_BAD_IMPERSONATION_LEVEL = 0x542;
            internal const int ERROR_CANT_OPEN_ANONYMOUS = 0x543;
            internal const int ERROR_NO_SECURITY_ON_OBJECT = 0x546;
            internal const int ERROR_CLASS_ALREADY_EXISTS = 0x582;
            internal const int ERROR_EVENTLOG_FILE_CHANGED = 0x5DF;
            internal const int ERROR_TRUSTED_RELATIONSHIP_FAILURE = 0x6FD;
            internal const int ERROR_RESOURCE_LANG_NOT_FOUND = 0x717;
            internal const int EFail = unchecked((int)0x80004005);
            internal const int E_FILENOTFOUND = unchecked((int)0x80070002);
        }

        internal partial class Kernel32
        {
            internal const uint SEM_FAILCRITICALERRORS = 1;

            internal const int FSCTL_SET_COMPRESSION = 0x9C040;
            internal const short COMPRESSION_FORMAT_NONE = 0;
            internal const short COMPRESSION_FORMAT_DEFAULT = 1;
            
            internal partial class GenericOperations
            {
                internal const int GENERIC_READ = unchecked((int)0x80000000);
                internal const int GENERIC_WRITE = 0x40000000;
            }

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

            internal struct FILE_BASIC_INFO
            {
                internal long CreationTime;
                internal long LastAccessTime;
                internal long LastWriteTime;
                internal long ChangeTime;
                internal uint FileAttributes;
            }

            internal enum FILE_INFO_BY_HANDLE_CLASS : uint
            {
                FileBasicInfo = 0x0u,
                FileStandardInfo = 0x1u,
                FileNameInfo = 0x2u,
                FileRenameInfo = 0x3u,
                FileDispositionInfo = 0x4u,
                FileAllocationInfo = 0x5u,
                FileEndOfFileInfo = 0x6u,
                FileStreamInfo = 0x7u,
                FileCompressionInfo = 0x8u,
                FileAttributeTagInfo = 0x9u,
                FileIdBothDirectoryInfo = 0xAu,
                FileIdBothDirectoryRestartInfo = 0xBu,
                FileIoPriorityHintInfo = 0xCu,
                FileRemoteProtocolInfo = 0xDu,
                FileFullDirectoryInfo = 0xEu,
                FileFullDirectoryRestartInfo = 0xFu,
                FileStorageInfo = 0x10u,
                FileAlignmentInfo = 0x11u,
                FileIdInfo = 0x12u,
                FileIdExtdDirectoryInfo = 0x13u,
                FileIdExtdDirectoryRestartInfo = 0x14u,
                MaximumFileInfoByHandleClass = 0x15u,
            }
            internal enum GET_FILEEX_INFO_LEVELS : uint
            {
                GetFileExInfoStandard = 0x0u,
                GetFileExMaxInfoLevel = 0x1u,
            }

            internal struct WIN32_FILE_ATTRIBUTE_DATA
            {
                internal int dwFileAttributes;
                internal uint ftCreationTimeLow;
                internal uint ftCreationTimeHigh;
                internal uint ftLastAccessTimeLow;
                internal uint ftLastAccessTimeHigh;
                internal uint ftLastWriteTimeLow;
                internal uint ftLastWriteTimeHigh;
                internal uint fileSizeHigh;
                internal uint fileSizeLow;

                internal void PopulateFrom(ref WIN32_FIND_DATA findData)
                {
                    // Copy the information to data
                    dwFileAttributes = (int)findData.dwFileAttributes;
                    ftCreationTimeLow = findData.ftCreationTime.dwLowDateTime;
                    ftCreationTimeHigh = findData.ftCreationTime.dwHighDateTime;
                    ftLastAccessTimeLow = findData.ftLastAccessTime.dwLowDateTime;
                    ftLastAccessTimeHigh = findData.ftLastAccessTime.dwHighDateTime;
                    ftLastWriteTimeLow = findData.ftLastWriteTime.dwLowDateTime;
                    ftLastWriteTimeHigh = findData.ftLastWriteTime.dwHighDateTime;
                    fileSizeHigh = findData.nFileSizeHigh;
                    fileSizeLow = findData.nFileSizeLow;
                }
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            [BestFitMapping(false)]
            internal unsafe struct WIN32_FIND_DATA
            {
                internal uint dwFileAttributes;
                internal FILE_TIME ftCreationTime;
                internal FILE_TIME ftLastAccessTime;
                internal FILE_TIME ftLastWriteTime;
                internal uint nFileSizeHigh;
                internal uint nFileSizeLow;
                internal uint dwReserved0;
                internal uint dwReserved1;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
                internal string cFileName;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
                internal string cAlternateFileName;
            }

            public enum STREAM_INFO_LEVELS
            {
                FindStreamInfoStandard,
                FindStreamInfoMaxInfoLevel
            }

            [StructLayout(LayoutKind.Explicit)]
            internal unsafe struct LARGE_INTEGER
            {
                [FieldOffset(0)]
                internal int LowPart;

                [FieldOffset(4)]
                internal int HighPart;

                [FieldOffset(0)]
                internal long QuadPart;
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            public struct WIN32_FIND_STREAM_DATA
            {
                public LARGE_INTEGER StreamSize;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260 + 36)]
                public string cStreamName;
            }

            internal struct FILE_TIME
            {
                internal uint dwLowDateTime;
                internal uint dwHighDateTime;

                internal FILE_TIME(long fileTime)
                {
                    dwLowDateTime = (uint)fileTime;
                    dwHighDateTime = (uint)(fileTime >> 32);
                }

                internal long ToTicks()
                {
                    return ((long)dwHighDateTime << 32) + dwLowDateTime;
                }
            }

            internal enum FINDEX_INFO_LEVELS : uint
            {
                FindExInfoStandard = 0x0u,
                FindExInfoBasic = 0x1u,
                FindExInfoMaxInfoLevel = 0x2u,
            }

            internal enum FINDEX_SEARCH_OPS : uint
            {
                FindExSearchNameMatch = 0x0u,
                FindExSearchLimitToDirectories = 0x1u,
                FindExSearchLimitToDevices = 0x2u,
                FindExSearchMaxSearchOp = 0x3u,
            }
        }

        internal partial class Advapi32
        {
            internal const string SeDebugPrivilege = "SeDebugPrivilege";
            internal const string SeBackupPrivilege = "SeBackupPrivilege";
            internal const string SeRestorePrivilege = "SeRestorePrivilege";
            internal const string SeShutdownPrivilege = "SeShutdownPrivilege";
            internal const string SeRemoteShutdownPrivilege = "SeRemoteShutdownPrivilege";
            internal const string SeTakeOwnershipPrivilege = "SeTakeOwnershipPrivilege";

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

        internal sealed class SafeFindHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            internal SafeFindHandle() : base(true) { }

            override protected bool ReleaseHandle()
            {
                return Win32Api.Kernel32.FindClose(handle);
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

            FileStream ret = new FileStream(_fileHandle, _access, 4096, _useAsyncIO);

            if (mode == FileMode.Append)
            {
                ret.Seek(0, SeekOrigin.End);
            }

            return ret;
        }

        public static unsafe SafeFileHandle CreateFileOpenHandle(FileMode mode, FileShare share, FileOptions options, FileAccess _access, string _path)
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

            using (FileSystemBase.Local.EnterDisableMediaInsertionPrompt())
            {
                return ValidateFileHandle(
                    Win32Api.Kernel32.CreateFile(_path, fAccess, share, mode, flagsAndAttributes), _path);
            }
        }

        public static unsafe Win32Api.Kernel32.SECURITY_ATTRIBUTES GetSecAttrs(FileShare share)
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


        public static SafeFileHandle ValidateFileHandle(SafeFileHandle fileHandle, string _path)
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

                //throw new Win32Exception(errorCode);
                throw GetExceptionForWin32Error(errorCode, _path);
            }

            //fileHandle.IsAsync = _useAsyncIO;
            return fileHandle;
        }

        internal static Exception GetExceptionForLastWin32Error(string path = "")
            => GetExceptionForWin32Error(Marshal.GetLastWin32Error(), path);

        public static Exception GetExceptionForWin32Error(int errorCode, string path = "")
        {
            path = path.NonNull();

            string msg = $"Error code = {errorCode} (0x{MakeHRFromErrorCode(errorCode):X}), Path = '{path}'.";

            switch (errorCode)
            {
                case Win32Api.Errors.ERROR_FILE_NOT_FOUND:
                    return new FileNotFoundException(msg);
                case Win32Api.Errors.ERROR_PATH_NOT_FOUND:
                    return new DirectoryNotFoundException(msg);
                case Win32Api.Errors.ERROR_ACCESS_DENIED:
                    return new UnauthorizedAccessException(msg);
                case Win32Api.Errors.ERROR_ALREADY_EXISTS:
                    if (string.IsNullOrEmpty(msg))
                        goto default;
                    return new IOException(msg, MakeHRFromErrorCode(errorCode));
                case Win32Api.Errors.ERROR_FILENAME_EXCED_RANGE:
                    return new PathTooLongException(msg);
                case Win32Api.Errors.ERROR_SHARING_VIOLATION:
                    return new IOException(msg, MakeHRFromErrorCode(errorCode));
                case Win32Api.Errors.ERROR_FILE_EXISTS:
                    if (string.IsNullOrEmpty(msg))
                        goto default;
                    return new IOException(msg, MakeHRFromErrorCode(errorCode));
                case Win32Api.Errors.ERROR_OPERATION_ABORTED:
                    return new OperationCanceledException();
                case Win32Api.Errors.ERROR_INVALID_PARAMETER:
                default:
                    return new Win32Exception(errorCode);
            }
        }

        private static int MakeHRFromErrorCode(int errorCode)
        {
            // Don't convert it if it is already an HRESULT
            if ((0xFFFF0000 & errorCode) != 0)
                return errorCode;

            return unchecked(((int)0x80070000) | errorCode);
        }


    }

    static class PalWin32FileSystem
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
            int flags = backupMode ? Win32Api.Kernel32.FileOperations.FILE_FLAG_BACKUP_SEMANTICS : 0;

            using (SafeFileHandle handle = OpenHandle(fullPath, asDirectory, flags))
            {
                if (!Win32Api.Kernel32.SetFileTime(handle, lastWriteTime: time.ToFileTime()))
                {
                    throw PalWin32FileStream.GetExceptionForLastWin32Error(fullPath);
                }
            }
        }

        public static void SetCreationTime(string fullPath, DateTimeOffset time, bool asDirectory, bool backupMode = false)
        {
            int flags = backupMode ? Win32Api.Kernel32.FileOperations.FILE_FLAG_BACKUP_SEMANTICS : 0;

            using (SafeFileHandle handle = OpenHandle(fullPath, asDirectory, flags))
            {
                if (!Win32Api.Kernel32.SetFileTime(handle, creationTime: time.ToFileTime()))
                {
                    throw PalWin32FileStream.GetExceptionForLastWin32Error(fullPath);
                }
            }
        }

        public static void SetLastAccessTime(string fullPath, DateTimeOffset time, bool asDirectory, bool backupMode = false)
        {
            int flags = backupMode ? Win32Api.Kernel32.FileOperations.FILE_FLAG_BACKUP_SEMANTICS : 0;

            using (SafeFileHandle handle = OpenHandle(fullPath, asDirectory, flags))
            {
                if (!Win32Api.Kernel32.SetFileTime(handle, lastAccessTime: time.ToFileTime()))
                {
                    throw PalWin32FileStream.GetExceptionForLastWin32Error(fullPath);
                }
            }
        }

        public static SafeFileHandle OpenHandle(string fullPath, bool asDirectory, int additionalFlags = 0)
        {
            string root = fullPath.Substring(0, Win32PathInternal.GetRootLength(fullPath.AsSpan()));
            if (root == fullPath && root[1] == Path.VolumeSeparatorChar)
            {
                // intentionally not fullpath, most upstack public APIs expose this as path.
                throw new ArgumentException("path");
            }

            SafeFileHandle handle = Win32Api.Kernel32.CreateFile(
                fullPath,
                Win32Api.Kernel32.GenericOperations.GENERIC_WRITE,
                FileShare.ReadWrite | FileShare.Delete,
                FileMode.Open,
                (asDirectory ? Win32Api.Kernel32.FileOperations.FILE_FLAG_BACKUP_SEMANTICS : 0) | additionalFlags);

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
    }
}

