using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class CoresLibConfig
    {
        public static readonly Copenhagen<CoresCaps> Caps = CoresCaps.None;
    }
}

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

[Flags]
public enum CoresCaps : long
{
    None = 0,
    BlazorApp = 1,
}

public class CoresLibOptions : ICloneable
{
    public DebugMode DebugMode { get; private set; }
    public bool PrintStatToConsole { get; private set; }
    public bool RecordLeakFullStack { get; private set; }
    public bool NohupMode { get; private set; }
    public bool NoTelnetMode { get; private set; }
    public bool ShowVersion { get; private set; }
    public CoresMode Mode { get; private set; }
    public string AppName { get; }
    public string SmtpServer { get; private set; } = "";
    public bool SmtpUseSsl { get; private set; } = false;
    public string SmtpUsername { get; private set; } = "";
    public string SmtpPassword { get; private set; } = "";
    public string SmtpFrom { get; private set; } = "";
    public string SmtpTo { get; private set; } = "";
    public string SelfUpdateTimestampUrl { get; private set; } = "";
    public string SelfUpdateExeUrl { get; private set; } = "";
    public string SelfUpdateSslHash { get; private set; } = "";
    public bool SelfUpdateInternalCopyMode { get; private set; } = "";
    public int SmtpMaxLines { get; private set; } = SmtpLogRouteSettings.DefaultMaxLines;
    public LogPriority SmtpLogLevel { get; private set; } = LogPriority.Debug;

    public CoresLibOptions(CoresMode mode, string appName, DebugMode defaultDebugMode = DebugMode.Debug, bool defaultPrintStatToConsole = false, bool defaultRecordLeakFullStack = false)
    {
        this.DebugMode = defaultDebugMode;
        this.PrintStatToConsole = defaultPrintStatToConsole;
        this.RecordLeakFullStack = defaultRecordLeakFullStack;

        this.Mode = mode;
        this.AppName = appName._NonNullTrim();

        if (this.AppName._IsEmpty()) throw new ArgumentNullException("AppName");
    }

    public void InternalSetMode(EnsureInternal yes, CoresMode mode)
    {
        this.Mode = mode;
    }

    public string[] OverrideOptionsByArgs(string[] args)
    {
        List<string> newArgsList = new List<string>();

        var procs = new List<(string OptionName, bool consumeNext, Action<string, string> Callback)>();

        // Options definitions
        procs.Add(("debugmode", true, (name, next) => { this.DebugMode = next._ParseEnum(DebugMode.Debug, true, true); }));
        procs.Add(("printstat", false, (name, next) => { this.PrintStatToConsole = true; }));
        procs.Add(("fullleak", false, (name, next) => { this.RecordLeakFullStack = true; }));
        procs.Add(("nohup", false, (name, next) => { this.NohupMode = true; }));
        procs.Add(("notelnet", false, (name, next) => { this.NoTelnetMode = true; }));
        procs.Add(("version", false, (name, next) => { this.ShowVersion = true; }));
        procs.Add(("smtpserver", true, (name, next) => { this.SmtpServer = next; }));
        procs.Add(("smtpusessl", false, (name, next) => { this.SmtpUseSsl = true; }));
        procs.Add(("smtpusername", true, (name, next) => { this.SmtpUsername = next; }));
        procs.Add(("smtppassword", true, (name, next) => { this.SmtpPassword = next; }));
        procs.Add(("smtpfrom", true, (name, next) => { this.SmtpFrom = next; }));
        procs.Add(("smtpto", true, (name, next) => { this.SmtpTo = next; }));
        procs.Add(("smtpmaxlines", true, (name, next) => { this.SmtpMaxLines = next._ToInt(); }));
        procs.Add(("smtploglevel", true, (name, next) => { this.SmtpLogLevel = LogPriority.Debug.ParseAsDefault(next, true); }));
        procs.Add(("selfupdatetimestampurl", true, (name, next) => { this.SelfUpdateTimestampUrl = next; }));
        procs.Add(("selfupdateexeurl", true, (name, next) => { this.SelfUpdateExeUrl = next; }));
        procs.Add(("selfupdatesslhash", true, (name, next) => { this.SelfUpdateSslHash = next; }));
        procs.Add(("selfupdateinternalcopymode", true, (name, next) => { this.SelfUpdateInternalCopyMode = true; }));

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i]._NonNullTrim();

            bool consumed = false;

            if (arg._TryTrimStartWith(out string arg2, StringComparison.OrdinalIgnoreCase, "--", "-", "/"))
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

    public static bool Inited { get; private set; } = false;
    static readonly CriticalSection InitLockObj = new CriticalSection();

    public static IReadOnlyList<string> Args { get; private set; } = null!;
    public static CoresLibOptions Options { get; private set; } = null!;
    public static CoresCaps Caps => CoresConfig.CoresLibConfig.Caps;

    public static string AppName { get; private set; } = null!;
    public static string AppNameFnSafe { get; private set; } = null!;

    public static string Report_CommandName { get; set; } = "";
    public static string Report_SimpleResult { get; set; } = "";
    public static bool Report_HasError { get; set; } = false;

    static CoresMode mode;
    public static CoresMode Mode
    {
        get
        {
            CheckInited();
            return mode;
        }
        private set => mode = value;
    }

    public static string LogFileSuffix { get; private set; } = "";

    public static readonly string CoresLibSourceCodeFileName = Dbg.GetCallerSourceCodeFilePath();

    public static string[] Init(CoresLibOptions options, params string[] args)
    {
        UnixConsoleSpecialUtil.DisableDotNetConsoleModeChange();
        PalUnixOpenSslSpecialUtil.TryInit();

        lock (InitLockObj)
        {
            if (Inited)
            {
                throw new ApplicationException("CoresLib is already inited.");
            }

            options = (CoresLibOptions)options.Clone();

            CoresLib.Report_CommandName = "";
            CoresLib.Report_SimpleResult = "";
            CoresLib.Report_HasError = false;

            CoresLib.AppName = options.AppName;
            CoresLib.AppNameFnSafe = PathParser.Windows.MakeSafeFileName(CoresLib.AppName);
            CoresLib.Mode = options.Mode;
            CoresLib.LogFileSuffix = "";

#if CORES_BASIC_DAEMON
            // Daemon モードの場合は LogFileSuffix を決定するためにスタートアップ引数を先読みして動作モードを決定する
            bool isDaemonExecMode = false;

            foreach (string arg in args)
            {
                DaemonCmdType type = arg._ParseEnum(DaemonCmdType.Unknown);
                if (type.EqualsAny(DaemonCmdType.ExecMain, DaemonCmdType.Test, DaemonCmdType.TestDebug, DaemonCmdType.TestWeb, DaemonCmdType.WinExecSvc))
                {
                    if (CoresLib.mode == CoresMode.Daemon || args.Where(x => x._InStr("daemon", true)).Any())
                    {
                        // 明示的に CoresLib.Mode == Daemon と指定された場合か、
                        // またはコマンドライン引数によって Daemon である旨が指定された場合
                        isDaemonExecMode = true;
                        break;
                    }
                }
            }

            if (isDaemonExecMode)
            {
                // Daemon のメイン処理を実行するモードのようであるから LogFileSuffix にそのことがわかる文字列を設定する
                CoresLib.LogFileSuffix = Consts.Strings.DaemonExecModeLogFileSuffix;

                CoresLib.Mode = CoresMode.Daemon;

                options.InternalSetMode(EnsureInternal.Yes, CoresMode.Daemon);
            }
#endif

            string[] newArgs = options.OverrideOptionsByArgs(args);

            if (options.ShowVersion)
            {
                // Show version
                Console.WriteLine($"{options.AppName} {options.Mode}");
                Console.WriteLine();

                var vals = Env.GetCoresEnvValuesList();

                foreach (var kv in vals)
                {
                    Console.WriteLine($"  {kv.Key}: {kv.Value}");
                }

                Console.WriteLine();

                Environment.Exit(0);
            }

            if (SetDebugModeOnce.IsFirstCall())
            {
                Dbg.SetDebugMode(options.DebugMode, options.PrintStatToConsole, options.RecordLeakFullStack);
            }

            Inited = true;

            try
            {
                InitModules(options);
            }
            catch
            {
                Inited = false;
                throw;
            }

            CoresLib.Args = newArgs.ToList();

            Env._SetCommandLineInternal(Str.BuildCmdLine(CoresLib.Args));

            CoresLib.Options = options;

            return newArgs;
        }
    }

    public static bool CheckInited()
    {
        if (Inited == false)
            throw new CoresException("Cores Library is not inited.");

        return true;
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
        ThreadPoolConfigUtil.Module.Init();

        LeakChecker.Module.Init();

        CoresLocalDirs.Module.Init();

        LocalLogRouter.OptionsForSmtpLogRouterInit.TrySetValue(options);
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

        ThreadPoolConfigUtil.Module.Free();

        // Print the leak results
        if (Dbg.IsConsoleDebugMode)
        {
            Console.WriteLine();
            leakCheckerResult.Print();
        }

        return new CoresLibraryResult(leakCheckerResult);
    }

    // EXE ファイル自体の更新を試行する
    public static void TryUpdateSelfIfNewerVersionIsReleased()
    {
        if (Env.IsWindows == false)
        {
            // Windows 以外では対応していない。
            // 将来対応させるときは nohup 等が必要であることに注意する。
            return;
        }

        var opt = CoresLib.Options;

        if (opt.SelfUpdateInternalCopyMode)
        {
            try
            {
                // --selfupdateinternalcopymode オプションが付けられて起動した。
                // 同一ディレクトリに copydef.txt ファイルが存在するはずであるから、これを読み取る。
                string copydefPath = Path.Combine(Path.GetDirectoryName(Env.AppRealProcessExeFileName!)!, "copydef.txt");

                string body = Lfs.ReadStringFromFile(copydefPath);

                string[] lines = body._GetLines();

                string targetExe = lines[0];
                string args = lines[1];

                Con.WriteLine($"targetExe: '{targetExe}'");
                Con.WriteLine($"args: '{args}'");

                if (targetExe._IsEmpty())
                {
                    throw new CoresLibException($"Target EXE is empty.");
                }

                // 元の EXE ファイルに上書きをする。
                // 元の EXE ファイルが実行中の場合があるので、60 秒間くらいリトライする。
                RetryHelper.RunAsync(async () =>
                {
                    // *** Update Started *** という文字列を表示する。
                    Con.WriteLine("*** Update Started ***");

                    // コピーを試す
                    await Lfs.CopyFileAsync(Env.AppRealProcessExeFileName, targetExe);
                },
                1000,
                60,
                randomInterval: true)._GetResult();

                // コピーが完了したら元プロセスを起動する
                Con.WriteLine("Copy completed. Starting the process...");

                Kernel.Run(targetExe, args);

                Con.WriteLine("Start OK. Terminating this process...");

                Kernel.SelfKill("SelfUpdateInternalCopyMode Start OK. Terminating this process.");
            }
            catch (Exception ex)
            {
                ex._Print();

                Kernel.SelfKill("SelfUpdateInternalCopyMode error.");
            }
        }

        //if (opt.SelfUpdateExeUrl._IsFilled() && opt.SelfUpdateTimestampUrl._IsFilled())
        {
            string exePath = @"C:\Users\yagi\Desktop\test1\test.exe";

            string randStr = Secure.Rand(8)._GetHexString().ToLowerInvariant();

            string tmpDir = Path.Combine(Env.MyGlobalTempDir, $"_update_tmp_{randStr}");

            TryUpdateSelfMainAsync(exePath, tmpDir, Util.ZeroDateTimeOffsetValue,
                "https://private.lts.dn.ipantt.net/d/210308_001_dev_test_81740/Dev.Test.Win.x86_64.exe",
                "https://private.lts.dn.ipantt.net/d/210308_001_dev_test_81740/TimeStamp.txt",
                "dd6668c8f3db6b53c593b83e9511ecfb5a9fdefd")._GetResult();
        }
    }

    static async Task TryUpdateSelfMainAsync(string currentExePath, string tmpDir, DateTimeOffset currentExeTimeStamp, string exeUrl, string timeStampUrl, string sslCertHash)
    {
        var webSettings = new WebApiSettings
        {
            MaxRecvSize = 2_000_000_000,
            SslAcceptCertSHAHashList = sslCertHash._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ",", ";", "/", " ", "\t", "　").ToList(),
        };

        var webOptions = new WebApiOptions(webSettings, doNotUseTcpStack: true);

        string timeStampStr = "";
        DateTimeOffset timeStampValue;

        // Web 上のタイムスタンプを取得
        try
        {
            var timeStampResult = await SimpleHttpDownloader.DownloadAsync(timeStampUrl, options: webOptions);

            timeStampStr = timeStampResult.Data._GetString_UTF8()._GetFirstFilledLineFromLines();

            timeStampValue = Str.DtstrToDateTimeOffset(timeStampStr);
        }
        catch (Exception ex)
        {
            // エラーが発生した
            ex._Print();

            // 何もせずに継続する
            return;
        }


        if (timeStampValue <= currentExeTimeStamp || timeStampStr._IsEmpty() || timeStampValue._IsZeroDateTime())
        {
            // 更新なし
            return;
        }

        try
        {
            CoresLib.Report_CommandName = "UpdateSelf";

            Con.WriteLine($"Current EXE Path: {currentExePath}");
            Con.WriteLine($"Temp Dir: {tmpDir}");
            Con.WriteLine($"New EXE URL: {exeUrl}");
            Con.WriteLine($"Local EXE TimeStamp: {currentExeTimeStamp._ToDtStr()}");
            Con.WriteLine($"New EXE TimeStamp: {timeStampValue._ToDtStr()}");

            await Lfs.CreateDirectoryAsync(tmpDir);

            // コピー指示ファイルの保存
            string copyDefFilePath = Path.Combine(tmpDir, "copydef.txt");
            StringWriter w = new StringWriter();

            // 1 行目は上書き対象の EXE ファイルパス
            w.WriteLine(currentExePath);

            // 2 行目はコマンドライン
            w.WriteLine(Env.CommandLine);

            // 3 行目は空行
            w.WriteLine();

            await Lfs.WriteStringToFileAsync(copyDefFilePath, w.ToString());

            // EXE 本体をダウンロード
            FileDownloadOption downOpt = new FileDownloadOption(webApiOptions: webOptions);

            string exeNamePart = Path.GetFileNameWithoutExtension(Env.AppRealProcessExeFileName);


            string tmpExeFileName = Path.Combine(tmpDir, exeNamePart + $"_update.exe");

            tmpExeFileName._Print();

            {
                Con.WriteLine($"Downloading New EXE to {tmpExeFileName}...");
                await using var destStream = new FileStream(tmpExeFileName, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);

                var size = await FileDownloader.DownloadFileParallelAsync(exeUrl, destStream, downOpt);

                Con.WriteLine($"Download completed. Filesize = {size._ToString3()} bytes.");
            }

            // TODO: ダウンロードされた EXE ファイルのデジタル署名のチェック
            if (Env.IsWindows)
            {
                Con.WriteLine($"Checking the new EXE file digital signagure...");
            }

            Con.WriteLine($"Run new EXE file for update copy...");

            // EXE 本体を実行
            await EasyExec.ExecAsync(tmpExeFileName, "--selfupdateinternalcopymode", tmpDir,
                easyOneLineRecvCallbackAsync: async (line) =>
                {
                    if (line._InStri("*** Update Started ***"))
                    {
                        // 子プロセスが無事に起動しアップデートが開始された
                        // このプロセスは自ら終了する
                        Con.WriteLine($"OK. New EXE file is now running. It returns '*** Update Started ***' signature. It seems healthy. I decoded to kill myself. Hopefully new EXE will replace me. Bye bye.");

                        await LocalLogRouter.FlushAsync();

                        Kernel.SelfKill("UpdateSelf: self kill");
                    }
                    await Task.CompletedTask;
                    return true;
                });
        }
        catch (Exception ex)
        {
            // エラーが発生した
            ex._Print();

            // 何もせずに継続する
            return;
        }
    }
}

