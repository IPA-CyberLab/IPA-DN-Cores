using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Diagnostics;

namespace IPA.Cores.Basic
{
    public class CoresLibraryShutdowningException : ApplicationException { }

    public class CoresLibraryResult
    {
        public LeakCheckerResult LeakCheckerResult { get; }

        public CoresLibraryResult(LeakCheckerResult leakCheckerResult)
        {
            this.LeakCheckerResult = leakCheckerResult;
        }
    }

    [Flags]
    public enum CoresMode
    {
        Application = 0,
        Daemon,
        Library,
    }

    public class CoresLibOptions : ICloneable
    {
        public DebugMode DebugMode { get; private set; }
        public bool PrintStatToConsole { get; private set; }
        public bool RecordLeakFullStack { get; private set; }
        public CoresMode Mode {get;}
        public string AppName { get; }

        public CoresLibOptions(CoresMode mode, string appName, DebugMode defaultDebugMode = DebugMode.Debug, bool defaultPrintStatToConsole = false, bool defaultRecordLeakFullStack = false)
        {
            this.DebugMode = defaultDebugMode;
            this.PrintStatToConsole = defaultPrintStatToConsole;
            this.RecordLeakFullStack = defaultRecordLeakFullStack;

            this.Mode = mode;
            this.AppName = appName._NonNullTrim();

            if (this.AppName._IsEmpty()) throw new ArgumentNullException("AppName");
        }

        public string[] OverrideOptionsByArgs(string[] args)
        {
            List<string> newArgsList = new List<string>();

            var procs = new List<(string OptionName, bool consumeNext, Action<string, string> Callback)>();

            // Options definitions
            procs.Add(("debugmode", true, (name, next) => { this.DebugMode = next._ParseEnum(DebugMode.Debug, true, true); }));
            procs.Add(("printstat", false, (name, next) => { this.PrintStatToConsole = true; }));
            procs.Add(("fullleak", false, (name, next) => { this.RecordLeakFullStack = true; }));

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i]._NonNullTrim();

                bool consumed = false;

                if (arg._TryTrimStartWith(out string arg2, StringComparison.OrdinalIgnoreCase, "-", "--", "/"))
                {
                    var proc = procs.Where(x => x.OptionName._IsSamei(arg2)).FirstOrDefault();
                    if (proc != default)
                    {
                        string nextArg = "";
                        if (proc.consumeNext)
                        {
                            nextArg = args[i + 1];
                            i++;
                        }

                        consumed = true;

                        proc.Callback(arg, nextArg);
                    }
                }

                if (consumed == false)
                {
                    newArgsList.Add(args[i]);
                }
            }

            return newArgsList.ToArray();
        }

        public object Clone() => this.MemberwiseClone();
    }

    public static class CoresLib
    {
        static Once SetDebugModeOnce;

        static bool Inited = false;
        static readonly CriticalSection InitLockObj = new CriticalSection();

        public static IReadOnlyList<string> Args { get; private set; } = null!;
        public static CoresLibOptions Options { get; private set; } = null!;

        public static string AppName { get; private set; } = null!;
        public static string AppNameFnSafe { get; private set; } = null!;
        public static CoresMode Mode { get; private set; }

        public static string LogFileSuffix { get; private set; } = "";

        public static readonly string CoresLibSourceCodeFileName = Dbg.GetCallerSourceCodeFilePath();

        public static string[] Init(CoresLibOptions options, params string[] args)
        {
            lock (InitLockObj)
            {
                if (Inited)
                {
                    throw new ApplicationException("CoresLib is already inited.");
                }

                options = (CoresLibOptions)options.Clone();

                CoresLib.AppName = options.AppName;
                CoresLib.AppNameFnSafe = PathParser.Windows.MakeSafeFileName(CoresLib.AppName);
                CoresLib.Mode = options.Mode;
                CoresLib.LogFileSuffix = "";

#if CORES_BASIC_DAEMON
                if (CoresLib.Mode == CoresMode.Daemon)
                {
                    // Daemon モードの場合は LogFileSuffix を決定するためにスタートアップ引数を先読みして動作モードを決定する
                    bool isDaemonExecMode = false;

                    foreach (string arg in args)
                    {
                        DaemonCmdType type = arg._ParseEnum(DaemonCmdType.Unknown);
                        if (type.EqualsAny(DaemonCmdType.ExecMain, DaemonCmdType.Test, DaemonCmdType.TestDebug, DaemonCmdType.WinExecSvc))
                        {
                            isDaemonExecMode = true;
                            break;
                        }
                    }

                    if (isDaemonExecMode)
                    {
                        // Daemon のメイン処理を実行するモードのようであるから LogFileSuffix にそのことがわかる文字列を設定する
                        CoresLib.LogFileSuffix = Consts.Strings.DaemonExecModeLogFileSuffix;
                    }
                }
#endif

                string[] newArgs = options.OverrideOptionsByArgs(args);

                if (SetDebugModeOnce.IsFirstCall())
                {
                    Dbg.SetDebugMode(options.DebugMode, options.PrintStatToConsole, options.RecordLeakFullStack);
                }

                InitModules(options);

                Inited = true;

                CoresLib.Args = newArgs.ToList();

                CoresLib.Options = options;

                return newArgs;
            }
        }

        public static CoresLibraryResult Free()
        {
            lock (InitLockObj)
            {
                if (Inited == false) throw new ApplicationException("CoresLib is not inited yet.");

                var ret = FreeModules();

                Inited = false;

                CoresLib.Args = null!;
                CoresLib.Options = null!;

                return ret;
            }
        }

        static void InitModules(CoresLibOptions options)
        {
            // Initialize
            LeakChecker.Module.Init();

            CoresLocalDirs.Module.Init();

            LocalLogRouter.Module.Init();

            CoresRuntimeStatReporter.Module.Init();

            NetPalDnsClient.Module.Init();

            LocalTcpIpSystem.Module.Init();

            LocalFileSystem.Module.Init();

            LargeFileSystem.Module.Init();

            ResourceFileSystem.Module.Init();

            Hive.Module.Init();

            GlobalMicroBenchmark.Module.Init();

#if CORES_BASIC_GIT
            GitGlobalFs.Module.Init();
#endif // CORES_BASIC_GIT

#if CORES_BASIC_JSON
#if CORES_BASIC_SECURITY
            GlobalCertVault.Module.Init();
#endif  // CORES_BASIC_JSON
#endif  // CORES_BASIC_SECURITY;


            TelnetLocalLogWatcher.Module.Init();

            // After all initialization completed
            LocalLogRouter.PutGitIgnoreFileOnLogDirectory();
            CoresLocalDirs.CreateLocalDirGitIgnore();
        }

        static CoresLibraryResult FreeModules()
        {
            // Finalize
#if CORES_BASIC_JSON
#if CORES_BASIC_SECURITY
            GlobalCertVault.Module.Free();
#endif  // CORES_BASIC_JSON
#endif  // CORES_BASIC_SECURITY;

            TelnetLocalLogWatcher.Module.Free();

#if CORES_BASIC_GIT
            GitGlobalFs.Module.Free();
#endif // CORES_BASIC_GIT

            GlobalMicroBenchmark.Module.Free();

            Hive.Module.Free();

            ResourceFileSystem.Module.Free();

            LargeFileSystem.Module.Free();

            LocalFileSystem.Module.Free();

            int openSockets = LocalTcpIpSystem.Local.GetOpenedSockCount();
            if (openSockets > 0)
            {
                Con.WriteDebug($"Still opening sockets: {openSockets}");
                LeakChecker.Enter(LeakCounterKind.StillOpeningSockets);
            }

            LocalTcpIpSystem.Module.Free();

            NetPalDnsClient.Module.Free();

            CoresRuntimeStatReporter.Module.Free();

            LocalLogRouter.Module.Free();

            CoresLocalDirs.Module.Free();

            LeakCheckerResult leakCheckerResult = LeakChecker.Module.Free();

            // Print the leak results
            if (Dbg.IsConsoleDebugMode)
            {
                Console.WriteLine();
                leakCheckerResult.Print();
            }

            return new CoresLibraryResult(leakCheckerResult);
        }
    }
}
