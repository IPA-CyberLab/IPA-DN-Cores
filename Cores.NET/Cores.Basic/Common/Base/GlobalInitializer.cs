using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    class CoresLibraryShutdowningException : ApplicationException { }

    class CoresLibraryResult
    {
        public LeakCheckerResult LeakCheckerResult { get; }

        public CoresLibraryResult(LeakCheckerResult leakCheckerResult)
        {
            this.LeakCheckerResult = leakCheckerResult;
        }
    }

    static class CoresLibrary
    {
        public static StaticModule<CoresLibraryResult> Main { get; } = new StaticModule<CoresLibraryResult>(GlobalInit, GlobalFree);

        static void GlobalInit()
        {
            // Initialize
            LeakChecker.Module.Init();

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

            TelnetLocalLogWatcher.Module.Init();

            // After all initialization completed
            LocalLogRouter.PutGitIgnoreFileOnLogDirectory();
        }

        static CoresLibraryResult GlobalFree()
        {
            // Finalize
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

    static class GlobalInitializer
    {
        public static void Ensure()
        {
            try
            {
                NormalInitializeOnce();
            }
            catch { }
        }

        static Once once;
        static void NormalInitializeOnce()
        {
            if (once.IsFirstCall() == false) return;

            // Start the global reporter
            var reporter = CoresRuntimeStatReporter.Reporter;
        }
    }
}
