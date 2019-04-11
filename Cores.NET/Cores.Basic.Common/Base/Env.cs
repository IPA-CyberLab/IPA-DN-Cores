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
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

using IPA.Cores.Basic;
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
    }


    static class Env
    {
        static GlobalInitializer gInit = new GlobalInitializer();

        static object lockObj = new object();

        // 初期化の必要のあるプロパティ値
        static public Version FrameworkVersion { get; }
        public static bool IsNET4OrGreater => (FrameworkVersion.Major >= 4);
        static public string HomeDir { get; }
        static public string UnixMutantDir { get; }
        static public string ExeFileName { get; }
        static public string ExeFileDir { get; }
        static public string AppRootDir { get; }
        static public string WindowsDir { get; }
        static public string SystemDir { get; }
        static public string TempDir { get; }
        static public string WinTempDir { get; }
        static public string WindowsDrive { get; }
        static public string ProgramFilesDir { get; }
        static public string PersonalStartMenuDir { get; }
        static public string PersonalProgramsDir { get; }
        static public string PersonalStartupDir { get; }
        static public string PersonalAppDataDir { get; }
        static public string PersonalDesktopDir { get; }
        static public string MyDocumentsDir { get; }
        static public string LocalAppDataDir { get; }
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
        public static string MyTempDir { get; }
        public static string PathSeparator { get; }
        public static string StartupCurrentDir { get; }
        public static bool IsDotNetCore { get; }
        public static Assembly ExeAssembly { get; }
        public static string ExeAssemblySimpleName { get; }
        public static string ExeAssemblyFullName { get; }
        public static bool IgnoreCaseInFileSystem => (IsWindows || IsMac);
        public static StrComparer FilePathStringComparer { get; }
        public static FileSystemPathInterpreter LocalFileSystemPathInterpreter { get; }

        static IO lockFile;

        public static bool Is64BitProcess => (IntPtr.Size == 8);
        public static bool Is64BitWindows => (Is64BitProcess || Kernel.InternalCheckIsWow64());
        public static bool IsWow64 => Kernel.InternalCheckIsWow64();

        public static Architecture CpuInfo { get; } = RuntimeInformation.ProcessArchitecture;
        public static string FrameworkInfoString = RuntimeInformation.FrameworkDescription.Trim();
        public static string OsInfoString = RuntimeInformation.OSDescription.Trim();


        // 初期化
        static Env()
        {
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
                UnixMutantDir = Path.Combine(HomeDir, ".dnmutant");
            }
            else
            {
                HomeDir = AppRootDir;
                if (IsUnix)
                {
                    UnixMutantDir = Path.Combine("/tmp", ".dnmutant");
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
                SystemDir = IO.RemoveLastEnMark(Environment.GetFolderPath(Environment.SpecialFolder.System));
                WindowsDir = IO.RemoveLastEnMark(Path.GetDirectoryName(SystemDir));
                TempDir = IO.RemoveLastEnMark(Path.GetTempPath());
                WinTempDir = IO.RemoveLastEnMark(Path.Combine(WindowsDir, "Temp"));
                IO.MakeDir(WinTempDir);
                if (WindowsDir.Length >= 2 && WindowsDir[1] == ':')
                {
                    WindowsDir = WindowsDir.Substring(0, 2).ToUpper();
                }
                else
                {
                    WindowsDrive = "C:";
                }
            }
            else
            {
                // UNIX
                SystemDir = "/bin";
                WindowsDir = "/bin";
                WindowsDrive = "/";
                if (Str.IsEmptyStr(HomeDir) == false)
                {
                    TempDir = Path.Combine(HomeDir, ".dntmp");
                }
                else
                {
                    TempDir = "/tmp";
                }
                WinTempDir = TempDir;
            }
            FilePathStringComparer = new StrComparer(!Env.IgnoreCaseInFileSystem);
            LocalFileSystemPathInterpreter = FileSystemPathInterpreter.GetInstance(FileSystemStyle.LocalSystem);
            ProgramFilesDir = IO.RemoveLastEnMark(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            PersonalStartMenuDir = IO.RemoveLastEnMark(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));
            PersonalProgramsDir = IO.RemoveLastEnMark(Environment.GetFolderPath(Environment.SpecialFolder.Programs));
            PersonalStartupDir = IO.RemoveLastEnMark(Environment.GetFolderPath(Environment.SpecialFolder.Startup));
            PersonalAppDataDir = IO.RemoveLastEnMark(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            PersonalDesktopDir = IO.RemoveLastEnMark(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            MyDocumentsDir = IO.RemoveLastEnMark(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            LocalAppDataDir = IO.RemoveLastEnMark(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            if (IsUnix)
            {
                // ダミーディレクトリ
                SystemDir = "/bin";
                WindowsDir = "/bin";
                WindowsDrive = "/";
                ProgramFilesDir = "/bin";
                PersonalStartMenuDir = Path.Combine(HomeDir, "dummy/starmenu");
                PersonalProgramsDir = Path.Combine(HomeDir, "dummy/starmenu/programs");
                PersonalStartupDir = Path.Combine(HomeDir, "dummy/starmenu/startup");
                LocalAppDataDir = PersonalAppDataDir = Path.Combine(HomeDir, ".dnappdata");
                PersonalDesktopDir = Path.Combine(HomeDir, "dummy/desktop");
                MyDocumentsDir = HomeDir;
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

            // 自分用の temp ディレクトリの初期化
            try
            {
                deleteUnusedTempDir();
            }
            catch
            {
            }

            int num = 0;

            while (true)
            {
                byte[] rand = Secure.Rand(2);
                string tmp2 = Str.ByteToStr(rand);

                string tmp = Path.Combine(Env.TempDir, "NET_" + tmp2);

                if (IO.IsDirExists(tmp) == false && IO.MakeDir(tmp))
                {
                    Env.MyTempDir = tmp;

                    break;
                }

                if ((num++) >= 100)
                {
                    throw new SystemException();
                }
            }

            if (IsWindows)
            {
                UnixMutantDir = Env.MyTempDir;
            }

            // ロックファイルの作成
            string lockFileName = Path.Combine(Env.MyTempDir, "LockFile.dat");
            lockFile = IO.FileCreate(lockFileName, Env.IsUnix);

        }


        static void deleteUnusedTempDir()
        {
            DirEntry[] files;

            files = IO.EnumDir(Env.TempDir);

            foreach (DirEntry e in files)
            {
                if (e.IsFolder)
                {
                    if (e.FileName.StartsWith("NET_", StringComparison.OrdinalIgnoreCase) && e.FileName.Length == 8)
                    {
                        string dirFullName = Path.Combine(Env.TempDir, e.fileName);
                        string lockFileName = Path.Combine(dirFullName, "LockFile.dat");
                        bool deleteNow = false;

                        try
                        {
                            IO io = IO.FileOpen(lockFileName);
                            io.Close();

                            try
                            {
                                io = IO.FileOpen(lockFileName, true);
                                deleteNow = true;
                                io.Close();
                            }
                            catch
                            {
                            }
                        }
                        catch
                        {
                            DirEntry[] files2;

                            deleteNow = true;

                            try
                            {
                                files2 = IO.EnumDir(dirFullName);

                                foreach (DirEntry e2 in files2)
                                {
                                    if (e2.IsFolder == false)
                                    {
                                        string fullPath = Path.Combine(dirFullName, e2.fileName);

                                        try
                                        {
                                            IO io2 = IO.FileOpen(fullPath, true);
                                            io2.Close();
                                        }
                                        catch
                                        {
                                            deleteNow = false;
                                        }
                                    }
                                }
                            }
                            catch
                            {
                                deleteNow = false;
                            }
                        }

                        if (deleteNow)
                        {
                            IO.DeleteDir(dirFullName, true);
                        }
                    }
                }
            }
        }

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
    }
}
