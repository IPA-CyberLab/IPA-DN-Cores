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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

using IPA.Cores.Basic;
using IPA.Cores.Basic.Legacy;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    [Serializable]
    class EnvInfoSnapshot
    {
        public EnvInfoSnapshot(string headerText)
        {
            HeaderText = headerText;
        }

        public string HeaderText;
        public DateTimeOffset TimeStamp = DateTime.Now;
        public string MachineName = Env.MachineName;
        public string FrameworkVersion = Env.FrameworkVersion.ToString();
        public string ExeFileName = Env.ExeFileName;
        public string AppRootDir = Env.AppRootDir;
        public string UserName = Env.UserName;
        public string UserNameEx = Env.UserNameEx;
        public string CommandLine = Env.CommandLine;
        public bool IsAdmin = Env.IsAdmin;
        public long ProcessId = Env.ProcessId;
        public bool IsDotNetCore = Env.IsDotNetCore;
        public string ExeAssemblySimpleName = Env.ExeAssemblySimpleName;
        public string ExeAssemblyFullName = Env.ExeAssemblyFullName;
        public bool IsWindows = Env.IsWindows;
        public bool IsUnix = Env.IsUnix;
        public bool IsMac = Env.IsMac;
        public bool IsLittenEndian = Env.IsLittleEndian;
        public bool IsBigEndian = Env.IsBigEndian;
        public bool Is64BitProcess => Env.Is64BitProcess;
        public bool Is64BitWindows => Env.Is64BitWindows;
        public bool IsWow64 => Env.IsWow64;
        public Architecture CpuInfo = Env.CpuInfo;
        public string FrameworkInfoString = Env.FrameworkInfoString;
        public string OsInfoString = Env.OsInfoString;
        public bool IsCoresLibraryDebugBuild = Env.IsCoresLibraryDebugBuild;
        public bool IsHostedByDotNetProcess = Env.IsHostedByDotNetProcess;
        public string DotNetHostProcessExeName = Env.DotNetHostProcessExeName;
        public bool IsDebuggerAttached = Env.IsDebuggerAttached;
        public int NumCpus => Env.NumCpus;
    }


    static class Env
    {
        static object lockObj = new object();

        // 初期化の必要のあるプロパティ値
        static public Version FrameworkVersion { get; }
        public static bool IsNET4OrGreater => (FrameworkVersion.Major >= 4);
        static public string HomeDir { get; }
        static public string UnixMutantDir { get; }
        static public string ExeFileName { get; }
        static public string ExeFileDir { get; }
        static public string AppRootDir { get; }
        static public string AppLocalDir => CoresLocalDirs.AppLocalDir;
        static public string Win32_WindowsDir { get; }
        static public string Win32_SystemDir { get; }
        static public string TempDir { get; }
        static public string Win32_WinTempDir { get; }
        static public string Win32_WindowsDrive { get; }
        static public string Win32_ProgramFilesDir { get; }
        static public string Win32_PersonalStartMenuDir { get; }
        static public string Win32_PersonalProgramsDir { get; }
        static public string Win32_PersonalStartupDir { get; }
        static public string Win32_PersonalAppDataDir { get; }
        static public string Win32_PersonalDesktopDir { get; }
        static public string Win32_MyDocumentsDir { get; }
        static public string Win32_LocalAppDataDir { get; }
        static public string UserName { get; }
        static public string UserNameEx { get; }
        static public string MachineName { get; }
        public static string CommandLine { get; }
        public static StrToken CommandLineList { get; }
        public static OperatingSystem OsInfo { get; }
        public static bool IsWindows { get; }
        public static bool IsUnix => !IsWindows;
        public static bool IsMac { get; }
        public static bool IsLinux { get; }
        public static bool IsLittleEndian { get; }
        public static bool IsBigEndian => !IsLittleEndian;
        public static bool IsAdmin { get; }
        public static long ProcessId { get; }
        public static string MyGlobalTempDir => CoresLocalDirs.MyGlobalTempDir;
        public static string PathSeparator { get; }
        public static char PathSeparatorChar { get; }
        public static string StartupCurrentDir { get; }
        public static bool IsDotNetCore { get; }
        public static Assembly ExeAssembly { get; }
        public static string ExeAssemblySimpleName { get; }
        public static string ExeAssemblyFullName { get; }
        public static bool IgnoreCaseInFileSystem => (IsWindows || IsMac);
        public static StrComparer FilePathStringComparer { get; }
        public static PathParser LocalPathParser => PathParser.Local;
        public static bool IsCoresLibraryDebugBuild { get; }
        public static bool IsHostedByDotNetProcess { get; }
        public static string DotNetHostProcessExeName { get; }
        public static int NumCpus { get; }

        public static bool IsDebuggerAttached => System.Diagnostics.Debugger.IsAttached;

        public static bool Is64BitProcess => (IntPtr.Size == 8);
        public static bool Is64BitWindows => (Is64BitProcess || Kernel.InternalCheckIsWow64());
        public static bool IsWow64 => Kernel.InternalCheckIsWow64();

        public static Architecture CpuInfo { get; } = RuntimeInformation.ProcessArchitecture;
        public static string FrameworkInfoString = RuntimeInformation.FrameworkDescription.Trim();
        public static string OsInfoString = RuntimeInformation.OSDescription.Trim();

        // 初期化
        static Env()
        {
            NumCpus = Math.Max(Environment.ProcessorCount, 1);

            int debugChecker = 0;
            Debug.Assert((++debugChecker) >= 1);
            Env.IsCoresLibraryDebugBuild = (debugChecker >= 1);

            ExeAssembly = Assembly.GetExecutingAssembly();
            var asmName = ExeAssembly.GetName();
            ExeAssemblySimpleName = asmName.Name;
            ExeAssemblyFullName = asmName.FullName;

            FrameworkVersion = Environment.Version;
            if (FrameworkInfoString.StartsWith(".NET Core", StringComparison.OrdinalIgnoreCase))
            {
                IsDotNetCore = true;
            }
            OsInfo = Environment.OSVersion;
            IsWindows = (OsInfo.Platform == PlatformID.Win32NT);
            if (IsUnix)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    IsLinux = true;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    IsMac = true;
                }
            }

            PathSeparator = "" + Path.DirectorySeparatorChar;
            if (Str.IsEmptyStr(PathSeparator))
            {
                PathSeparator = "/";
                if (Environment.OSVersion.Platform == PlatformID.Win32NT) PathSeparator = "\\";
            }
            PathSeparatorChar = PathSeparator[0];
            ExeFileName = IO.RemoveLastEnMark(getMyExeFileName());
            if (Str.IsEmptyStr(ExeFileName) == false)
            {
                AppRootDir = ExeFileDir = IO.RemoveLastEnMark(System.AppContext.BaseDirectory);
                // プログラムのあるディレクトリから 1 つずつ遡ってアプリケーションの root ディレクトリを取得する
                string tmp = ExeFileDir;
                while (true)
                {
                    try
                    {
                        tmp = Path.GetDirectoryName(tmp);
                        if (File.Exists(Path.Combine(tmp, "approot")) || File.Exists(Path.Combine(tmp, "appsettings.json")) || File.Exists(Path.Combine(tmp, "appsettings.Development.json")))
                        {
                            AppRootDir = tmp;
                            break;
                        }
                    }
                    catch
                    {
                        break;
                    }
                }
            }
            else
            {
                ExeFileName = "/tmp/dummyexe";
                ExeFileDir = "/tmp";
                AppRootDir = IO.RemoveLastEnMark(Environment.CurrentDirectory);
            }

            HomeDir = IO.RemoveLastEnMark(Kernel.GetEnvStr("HOME"));
            if (Str.IsEmptyStr(HomeDir))
            {
                HomeDir = IO.RemoveLastEnMark(Kernel.GetEnvStr("HOMEDRIVE") + Kernel.GetEnvStr("HOMEPATH"));
            }
            if (Str.IsEmptyStr(HomeDir) == false)
            {
                UnixMutantDir = Path.Combine(HomeDir, ".dotnet_temp/.Cores.NET.Mutex");
            }
            else
            {
                HomeDir = AppRootDir;
                if (IsUnix)
                {
                    UnixMutantDir = Path.Combine("/tmp", ".dotnet_temp/.Cores.NET.Mutex");
                }
            }
            if (IsWindows) UnixMutantDir = "";
            if (Str.IsEmptyStr(UnixMutantDir) == false)
            {
                IO.MakeDirIfNotExists(UnixMutantDir);
            }
            if (IsWindows)
            {
                // Windows
                Win32_SystemDir = IO.RemoveLastEnMark(Environment.GetFolderPath(Environment.SpecialFolder.System));
                Win32_WindowsDir = IO.RemoveLastEnMark(Path.GetDirectoryName(Win32_SystemDir));
                TempDir = IO.RemoveLastEnMark(Path.GetTempPath());
                Win32_WinTempDir = IO.RemoveLastEnMark(Path.Combine(Win32_WindowsDir, "Temp"));
                IO.MakeDir(Win32_WinTempDir);
                if (Win32_WindowsDir.Length >= 2 && Win32_WindowsDir[1] == ':')
                {
                    Win32_WindowsDir = Win32_WindowsDir.Substring(0, 2).ToUpper();
                }
                else
                {
                    Win32_WindowsDrive = "C:";
                }
            }
            else
            {
                // UNIX
                Win32_SystemDir = "/bin";
                Win32_WindowsDir = "/bin";
                Win32_WindowsDrive = "/";
                if (Str.IsEmptyStr(HomeDir) == false)
                {
                    TempDir = Path.Combine(HomeDir, ".dotnet_temp/.Cores.NET.PerProcess.Temp");
                }
                else
                {
                    TempDir = "/tmp";
                }
                Win32_WinTempDir = TempDir;
            }
            FilePathStringComparer = new StrComparer(!Env.IgnoreCaseInFileSystem);
            Win32_ProgramFilesDir = IO.RemoveLastEnMark(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            Win32_PersonalStartMenuDir = IO.RemoveLastEnMark(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));
            Win32_PersonalProgramsDir = IO.RemoveLastEnMark(Environment.GetFolderPath(Environment.SpecialFolder.Programs));
            Win32_PersonalStartupDir = IO.RemoveLastEnMark(Environment.GetFolderPath(Environment.SpecialFolder.Startup));
            Win32_PersonalAppDataDir = IO.RemoveLastEnMark(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            Win32_PersonalDesktopDir = IO.RemoveLastEnMark(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            Win32_MyDocumentsDir = IO.RemoveLastEnMark(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            Win32_LocalAppDataDir = IO.RemoveLastEnMark(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            if (IsUnix)
            {
                // ダミーディレクトリ
                Win32_SystemDir = "/bin";
                Win32_WindowsDir = "/bin";
                Win32_WindowsDrive = "/";
                Win32_ProgramFilesDir = "/bin";
                Win32_PersonalStartMenuDir = Path.Combine(HomeDir, "dummy/starmenu");
                Win32_PersonalProgramsDir = Path.Combine(HomeDir, "dummy/starmenu/programs");
                Win32_PersonalStartupDir = Path.Combine(HomeDir, "dummy/starmenu/startup");
                Win32_LocalAppDataDir = Win32_PersonalAppDataDir = Path.Combine(HomeDir, ".dnappdata");
                Win32_PersonalDesktopDir = Path.Combine(HomeDir, "dummy/desktop");
                Win32_MyDocumentsDir = HomeDir;
            }
            StartupCurrentDir = CurrentDir;
            UserName = Environment.UserName;
            try
            {
                UserNameEx = Environment.UserDomainName + "\\" + UserName;
            }
            catch
            {
                UserNameEx = UserName;
            }
            MachineName = Environment.MachineName;
            CommandLine = initCommandLine(Environment.CommandLine);
            IsLittleEndian = BitConverter.IsLittleEndian;
            ProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;
            IsAdmin = checkIsAdmin();

            Env.IsHostedByDotNetProcess = ExeFileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

            if (Env.IsHostedByDotNetProcess)
            {
                Env.DotNetHostProcessExeName = Process.GetCurrentProcess().MainModule.FileName;
            }

            if (IsUnix)
            {
                MutantUnixImpl.DeleteUnusedMutantFiles();
            }
        }

        public static string MyLocalTempDir => CoresLocalDirs.MyLocalTempDir;

        static bool checkIsAdmin()
        {
            // TODO
            return true;
        }

        static string initCommandLine(string src)
        {
            try
            {
                int i;
                // 実行可能ファイル本体の部分を除去する
                if (src.Length >= 1 && src[0] == '\"')
                {
                    i = src.IndexOf('\"', 1);
                }
                else
                {
                    i = src.IndexOf(' ');
                }

                if (i == -1)
                {
                    return "";
                }
                else
                {
                    return src.Substring(i + 1).TrimStart(' ');
                }
            }
            catch
            {
                return "";
            }
        }

        static string getMyExeFileName()
        {
            try
            {
                Assembly mainAssembly = Assembly.GetEntryAssembly();
                Module[] modules = mainAssembly.GetModules();
                return modules[0].FullyQualifiedName;
            }
            catch
            {
                return "";
            }
        }

        // 初期化の必要のないプロパティ値
        static public string CurrentDir => IO.RemoveLastEnMark(Environment.CurrentDirectory);
        static public string NewLine => Environment.NewLine;

        public static void PutGitIgnoreFileOnAppLocalDirectory()
        {
            Util.PutGitIgnoreFileOnDirectory(Lfs.PathParser.Combine(Env.AppLocalDir));
        }
    }


    static class CoresLocalDirs
    {
        static readonly CriticalSection MyLocalTempDirInitLock = new CriticalSection();
        public static readonly StaticModule Module = new StaticModule(InitModule, FreeModule);

        static string _MyLocalTempDir;

        public static string AppLocalDir { get; private set; }
        public static string AppRootLocalTempDirRoot_Internal { get; private set; }
        public static string MyGlobalTempDir { get; private set; }

        static void InitModule()
        {
            string dirPrefix = "App";

            if (CoresLib.Mode == CoresMode.Library) dirPrefix = "Lib";

            AppLocalDir = Path.Combine(Env.AppRootDir, "Local", $"{dirPrefix}_{CoresLib.AppNameFnSafe}");

            AppRootLocalTempDirRoot_Internal = Path.Combine(AppLocalDir, "Temp");

            _MyLocalTempDir = null;

            // Global app temp dir
            if (MyGlobalTempDir == null)
            {
                SystemUniqueDirectoryProvider myGlobalTempDirProvider = new SystemUniqueDirectoryProvider(Path.Combine(Env.TempDir, "Cores.NET.Temp"), $"{dirPrefix}_{CoresLib.AppNameFnSafe}");
                MyGlobalTempDir = myGlobalTempDirProvider.CurrentDirPath;
            }
        }

        static void FreeModule()
        {
        }

        public static string MyLocalTempDir
        {
            get
            {
                if (_MyLocalTempDir != null) return _MyLocalTempDir;

                lock (MyLocalTempDirInitLock)
                {
                    if (_MyLocalTempDir == null)
                    {
                        // Local app temp dir
                        SystemUniqueDirectoryProvider myLocalTempDirProvider = new SystemUniqueDirectoryProvider(AppRootLocalTempDirRoot_Internal, CoresLib.AppNameFnSafe);
                        _MyLocalTempDir = myLocalTempDirProvider.CurrentDirPath;

                        Env.PutGitIgnoreFileOnAppLocalDirectory();
                    }

                    return _MyLocalTempDir;
                }
            }
        }
    }
}
