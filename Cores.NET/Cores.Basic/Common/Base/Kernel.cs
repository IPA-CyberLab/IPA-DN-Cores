// IPA Cores.NET
// 
// Copyright (c) 2018- IPA CyberLab.
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
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using IPA.Cores.Basic;
using IPA.Cores.Basic.Legacy;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    public static class Kernel
    {
        [DllImport("kernel32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWow64Process(
            [In] IntPtr hProcess,
            [Out] out bool wow64Process
        );

        public static PlatformID GetOsPlatform()
        {
            return Environment.OSVersion.Platform;
        }

        public static bool InternalCheckIsWow64()
        {
            if (GetOsPlatform() == PlatformID.Win32NT)
            {
                if ((Environment.OSVersion.Version.Major == 5 && Environment.OSVersion.Version.Minor >= 1) ||
                    Environment.OSVersion.Version.Major >= 6)
                {
                    using (Process p = Process.GetCurrentProcess())
                    {
                        bool retVal;
                        if (!IsWow64Process(p.Handle, out retVal))
                        {
                            return false;
                        }
                        return retVal;
                    }
                }
                else
                {
                    return false;
                }
            }

            return false;
        }

        // スリープ
        public static void SleepThread(int millisec)
        {
            ThreadObj.Sleep(millisec);
        }

        // デバッグのため停止
        public static void SuspendForDebug()
        {
            Dbg.WriteLine("SuspendForDebug() called.");
            SleepThread(ThreadObj.Infinite);
        }

        // 環境変数文字列の取得
        public static string GetEnvStr(string name)
            => Environment.GetEnvironmentVariable(name)._NonNull();

        // 現在のプロセスを強制終了する
        static public void SelfKill(string msg = "")
        {
            if (msg._IsFilled()) msg._Print();
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }

        // プログラムを起動する
        public static Process Run(string exeName, string args)
        {
            Process p = new Process();
            p.StartInfo.FileName = IO.InnerFilePath(exeName);
            p.StartInfo.Arguments = args;

            p.Start();

            return p;
        }

        // OS の再起動
        static Once RebootOnceFlag;
        public static void RebootOperatingSystemForcefullyDangerous()
        {
            if (Env.IsLinux)
            {
                if (RebootOnceFlag.IsFirstCall())
                {
                    // sync, sync, sync
                    for (int i = 0; i < 3; i++)
                    {
                        UnixTryRunSystemProgram(EnsureInternal.Yes, "sync", "", 5 * 1000);
                    }

                    Sleep(300);

                    // reboot
                    UnixTryRunSystemProgram(EnsureInternal.Yes, "reboot", "--reboot --force", 10 * 1000);

                    Sleep(300);

                    // reboot with BIOS (最後の手段)
                    Lfs.WriteStringToFile(@"/proc/sys/kernel/sysrq", "1");

                    Sleep(300);

                    Lfs.WriteStringToFile(@"/proc/sysrq-trigger", "b");
                }
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        public static bool UnixTryRunSystemProgram(EnsureInternal yes, string commandName, string args, int timeout = Timeout.Infinite)
        {
            string[] dirs = { "/sbin/", "/bin/", "/usr/bin/", "/usr/local/bin/", "/usr/local/sbin/", "/usr/local/bin/" };

            foreach (string dir in dirs)
            {
                if (UnixTryRunProgramInternal(yes, Path.Combine(dir, commandName), args, timeout))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool UnixTryRunProgramInternal(EnsureInternal yes, string exe, string args, int timeout = Timeout.Infinite)
        {
            if (exe._IsEmpty()) throw new ArgumentNullException(nameof(exe));
            args = args._NonNull();

            try
            {
                ProcessStartInfo info = new ProcessStartInfo()
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    RedirectStandardInput = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Env.AppRootDir,
                };

                using (Process p = Process.Start(info))
                {
                    return p.WaitForExit(timeout);
                }
            }
            catch (Exception ex)
            {
                ex._Debug();
            }

            return false;
        }
    }

    namespace Legacy
    {
        // 子プロセスの起動・制御用クラス
        public class ChildProcess
        {
            string stdout = "", stderr = "";
            int exitcode = -1;
            int timeout;
            Event? timeout_thread_event = null;
            Process proc;
            bool finished = false;
            bool killed = false;

            void timeout_thread(object param)
            {
                this.timeout_thread_event!.Wait(this.timeout);

                if (finished == false)
                {
                    try
                    {
                        proc.Kill();
                        killed = true;
                    }
                    catch
                    {
                    }
                }
            }

            public string StdOut => stdout;
            public string StdErr => stderr;
            public int ExitCode => exitcode;
            public bool TimeoutKilled => killed;
            public bool IsOk => exitcode == 0;
            public bool IsError => !IsOk;

            public ChildProcess(string exe, string args = "", string input = "", bool throwExceptionOnExitError = false, int timeout = ThreadObj.Infinite)
            {
                this.timeout = timeout;

                Str.NormalizeString(ref args);

                ProcessStartInfo info = new ProcessStartInfo()
                {
                    FileName = IO.InnerFilePath(exe),
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = !Str.IsEmptyStr(input),
                };

                ThreadObj? t = null;

                using (Process p = Process.Start(info))
                {
                    this.proc = p;

                    if (timeout != ThreadObj.Infinite)
                    {
                        timeout_thread_event = new Event();

                        t = new ThreadObj(timeout_thread);
                    }

                    if (Str.IsEmptyStr(input) == false)
                    {
                        p.StandardInput.Write(input);
                        p.StandardInput.Flush();
                        p.StandardInput.Close();
                    }

                    stdout = p.StandardOutput.ReadToEnd();
                    stderr = p.StandardError.ReadToEnd();

                    p.WaitForExit();
                    finished = true;

                    if (timeout_thread_event != null)
                    {
                        timeout_thread_event.Set();
                    }

                    if (t != null) t.WaitForEnd();

                    if (killed)
                    {
                        if (Str.IsEmptyStr(stderr))
                        {
                            stderr = $"Process run timeout ({timeout._ToString3()} msecs).";
                        }
                    }

                    exitcode = p.ExitCode;

                    if (throwExceptionOnExitError)
                    {
                        if (exitcode != 0)
                        {
                            throw new ApplicationException($"ChildProcess: '{exe}': exitcode = {exitcode}, errorstr = {stderr._OneLine()}");
                        }
                    }
                }
            }
        }
    }
}
