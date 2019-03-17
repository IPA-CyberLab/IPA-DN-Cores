// CoreUtil
// 
// Copyright (C) 1997-2010 Daiyuu Nobori. All Rights Reserved.
// Copyright (C) 2004-2010 SoftEther Corporation. All Rights Reserved.

using System;
using System.Threading;
using System.Data;
using System.Data.Sql;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using System.Text;
using System.Configuration;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.IO;
using System.Drawing;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Net.Mail;
using System.Net.Mime;
using System.Runtime.InteropServices;

using IPA.DN.CoreUtil.Helper.Basic;

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
            Console.WriteLine("SuspendForDebug() called.");
            SleepThread(ThreadObj.Infinite);
        }

        // 環境変数文字列の取得
        public static string GetEnvStr(string name)
        {
            string ret = Environment.GetEnvironmentVariable(name);
            
            if (ret == null)
            {
                ret = "";
            }

            return ret;
        }

        // 現在のプロセスを強制終了する
        static public void SelfKill(string msg = null)
        {
            if (msg.IsFilled()) msg.Print();
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
    }

    // 子プロセスの起動・制御用クラス
    public class ChildProcess
    {
        string stdout = "", stderr = "";
        int exitcode = -1;
        int timeout;
        Event timeout_thread_event = null;
        Process proc;
        bool finished = false;
        bool killed = false;

        void timeout_thread(object param)
        {
            this.timeout_thread_event.Wait(this.timeout);

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

        public ChildProcess(string exe, string args = "", string input = "", bool throw_exception_on_exit_error = false, int timeout = ThreadObj.Infinite)
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

            ThreadObj t = null;

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
                        stderr = $"Process run timeout ({timeout.ToStr3()} msecs).";
                    }
                }

                exitcode = p.ExitCode;

                if (throw_exception_on_exit_error)
                {
                    if (exitcode != 0)
                    {
                        throw new ApplicationException($"ChildProcess: '{exe}': exitcode = {exitcode}, errorstr = {stderr.OneLine()}");
                    }
                }
            }
        }
    }
}
