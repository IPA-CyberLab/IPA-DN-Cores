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
using System.Collections.Immutable;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Runtime.InteropServices;

namespace IPA.Cores.Basic
{
    class ConsoleLogRoute : LogRouteBase
    {
        public ConsoleLogRoute(string kind, LogPriority minimalPriority) : base(kind, minimalPriority) { }

        public override void ReceiveLog(LogRecord record)
        {
            if (record.Flags.Bit(LogFlags.NoOutputToConsole) == false)
            {
                Console.WriteLine(record.ConsolePrintableString);
            }
        }
    }

    class LoggerLogRoute : LogRouteBase
    {
        Logger Log = null;

        public LoggerLogRoute(string kind, LogPriority minimalPriority, string prefix, string dir, LogSwitchType switchType = LogSwitchType.Day, LogInfoOptions infoOptions = null,
            long? autoDeleteTotalMaxSize = null) : base(kind, minimalPriority)
        {
            if (minimalPriority == LogPriority.None)
                return;

            Log = new Logger(dir, kind, prefix, Env.UniqueLogProcessId,
                switchType: switchType,
                infoOptions: infoOptions,
                maxLogSize: CoresConfig.Logger.DefaultMaxLogSize,
                autoDeleteTotalMinSize: autoDeleteTotalMaxSize ?? CoresConfig.Logger.DefaultAutoDeleteTotalMinSize);

            AddDirectDisposeLink(Log);
        }


        public override void ReceiveLog(LogRecord record)
        {
            if (Log != null)
            {
                Log.Add(record);
            }
        }
    }

    abstract class LogRouteBase : AsyncService
    {
        public string Kind { get; }
        public LogPriority MinimalPriority { get; }

        public LogRouteBase(string kind, LogPriority minimalPriority)
        {
            this.MinimalPriority = minimalPriority;
            this.Kind = kind;
        }

        public abstract void ReceiveLog(LogRecord record);
    }

    class LogRouter : AsyncService
    {
        CriticalSection LockObj = new CriticalSection();

        ImmutableList<LogRouteBase> RouteList = ImmutableList<LogRouteBase>.Empty;

        protected override void CancelImpl(Exception ex)
        {
            var routeList = this.RouteList;
            foreach (LogRouteBase route in routeList)
            {
                route._CancelSafe();
            }
        }

        protected override async Task CleanupImplAsync(Exception ex)
        {
            var routeList = this.RouteList;
            foreach (LogRouteBase route in routeList)
            {
                await UninstallLogRouteAsync(route);
            }
        }

        protected override void DisposeImpl(Exception ex) { }

        public LogRouteBase InstallLogRoute(LogRouteBase route)
        {
            lock (LockObj)
            {
                CheckNotCanceled();

                this.RouteList = this.RouteList.Add(route);
            }

            return route;
        }

        public async Task UninstallLogRouteAsync(LogRouteBase route)
        {
            lock (LockObj)
            {
                this.RouteList = this.RouteList.Remove(route);
            }

            route._CancelSafe();
            await route._CleanupSafeAsync();
            route._DisposeSafe();
        }

        public void PostLog(LogRecord record, string kind)
        {
            if (record.Priority == LogPriority.None)
                return;

            var routeList = this.RouteList;
            foreach (LogRouteBase route in routeList)
            {
                if (route.MinimalPriority != LogPriority.None)
                {
                    if (route.Kind == kind || route.Kind._IsEmpty())
                    {
                        if (route.MinimalPriority <= record.Priority)
                        {
                            try
                            {
                                route.ReceiveLog(record);
                            }
                            catch { }
                        }
                    }
                }
            }
        }
    }

    static class LocalLogRouter
    {
        public static LogRouter Router { get; private set; }

        public static StaticModule Module { get; } = new StaticModule(ModuleInit, ModuleFree);

        static void ModuleInit()
        {
            Router = new LogRouter();

            // Console log
            Router.InstallLogRoute(new ConsoleLogRoute(LogKind.Default,
                CoresConfig.DebugSettings.ConsoleMinimalLevel));

            // Debug log (file)
            Router.InstallLogRoute(new LoggerLogRoute(LogKind.Default,
                CoresConfig.DebugSettings.LogMinimalDebugLevel,
                "debug",
                CoresConfig.LocalLogRouterSettings.LogDebugDir.Value(),
                CoresConfig.LocalLogRouterSettings.SwitchTypeForDebug,
                CoresConfig.LocalLogRouterSettings.InfoOptionsForDebug));

            // Info log (file)
            Router.InstallLogRoute(new LoggerLogRoute(LogKind.Default,
                CoresConfig.DebugSettings.LogMinimalInfoLevel,
                "info",
                CoresConfig.LocalLogRouterSettings.LogInfoDir.Value(),
                CoresConfig.LocalLogRouterSettings.SwitchTypeForInfo,
                CoresConfig.LocalLogRouterSettings.InfoOptionsForInfo));

            // Error log (file)
            Router.InstallLogRoute(new LoggerLogRoute(LogKind.Default,
                CoresConfig.DebugSettings.LogMinimalErrorLevel,
                "error",
                CoresConfig.LocalLogRouterSettings.LogErrorDir.Value(),
                CoresConfig.LocalLogRouterSettings.SwitchTypeForError,
                CoresConfig.LocalLogRouterSettings.InfoOptionsForError));

            // Data log (file)
            Router.InstallLogRoute(new LoggerLogRoute(LogKind.Data,
                CoresConfig.DebugSettings.LogMinimalDataLevel,
                "data",
                CoresConfig.LocalLogRouterSettings.LogDataDir.Value(),
                CoresConfig.LocalLogRouterSettings.SwitchTypeForData,
                CoresConfig.LocalLogRouterSettings.InfoOptionsForData));

            // Statistics log (file)
            Router.InstallLogRoute(new LoggerLogRoute(LogKind.Stat,
                CoresConfig.DebugSettings.LogMinimalStatLevel,
                "stat",
                CoresConfig.LocalLogRouterSettings.LogStatDir.Value(),
                CoresConfig.LocalLogRouterSettings.SwitchTypeForStat,
                CoresConfig.LocalLogRouterSettings.InfoOptionsForStat));

            // Access log (file)
            Router.InstallLogRoute(new LoggerLogRoute(LogKind.Access,
                CoresConfig.DebugSettings.LogMinimalAccessLevel,
                "access",
                CoresConfig.LocalLogRouterSettings.LogAccessDir.Value(),
                CoresConfig.LocalLogRouterSettings.SwitchTypeForAccess,
                CoresConfig.LocalLogRouterSettings.InfoOptionsForAccess));

            // Socket log (file)
            Router.InstallLogRoute(new LoggerLogRoute(LogKind.Socket,
                CoresConfig.DebugSettings.LogMinimalSocketLevel,
                "socket",
                CoresConfig.LocalLogRouterSettings.LogSocketDir.Value(),
                CoresConfig.LocalLogRouterSettings.SwitchTypeForSocket,
                CoresConfig.LocalLogRouterSettings.InfoOptionsForSocket));

            EnvInfoSnapshot snapshot = new EnvInfoSnapshot("--- Process boottime log ---");

            Router.PostLog(new LogRecord(snapshot, LogPriority.Info, LogFlags.NoOutputToConsole, "boottime"), LogKind.Default);
        }

        static void ModuleFree()
        {
            Router._DisposeSafe(new CoresLibraryShutdowningException());
            Router = null;
        }

        public static void Post(LogRecord record, string kind = LogKind.Default) => Router.PostLog(record, kind);

        public static void Post(object obj, LogPriority priority = LogPriority.Debug, string kind = LogKind.Default, LogFlags flags = LogFlags.None, string tag = null)
            => Router.PostLog(new LogRecord(obj, priority, flags, tag), kind);

        public static void PrintConsole(object obj, bool noConsole = false, LogPriority priority = LogPriority.Info, string tag = null)
            => Post(obj, priority, flags: noConsole ? LogFlags.NoOutputToConsole : LogFlags.None, tag: tag);

        public static void PostData(object obj, string tag = null, bool copyToDebug = false, LogPriority priority = LogPriority.Info)
        {
            Post(obj, priority, kind: LogKind.Data, tag: tag);
            if (copyToDebug)
            {
                Post(new PostedData() { Data = obj, Tag = tag }, priority: LogPriority.Debug, kind: LogKind.Default, tag: tag);
            }
        }

        public static void PostStat(object obj, string tag = null, bool copyToDebug = false, LogPriority priority = LogPriority.Info)
        {
            Post(obj, priority, kind: LogKind.Stat, tag: tag);
            if (copyToDebug)
            {
                Post(new PostedStat() { Data = obj, Tag = tag }, priority: LogPriority.Debug, kind: LogKind.Default, tag: tag);
            }
        }

        public static void PostAccessLog(object obj, string tag = null, bool copyToDebug = false, LogPriority priority = LogPriority.Info)
        {
            Post(obj, priority, kind: LogKind.Access, tag: tag);
            if (copyToDebug)
            {
                Post(new PostedAccessLog() { Data = obj, Tag = tag }, priority: LogPriority.Debug, kind: LogKind.Default, tag: tag);
            }
        }

        public static void PostSocketLog(object obj, string tag = null, bool copyToDebug = false, LogPriority priority = LogPriority.Info)
        {
            Post(obj, priority, kind: LogKind.Socket, tag: tag);
            if (copyToDebug)
            {
                Post(new PostedSocketLog() { Data = obj, Tag = tag }, priority: LogPriority.Debug, kind: LogKind.Default, tag: tag);
            }
        }

        class PostedData
        {
            public string Tag;
            public object Data;
        }

        class PostedAccessLog
        {
            public string Tag;
            public object Data;
        }

        class PostedSocketLog
        {
            public string Tag;
            public object Data;
        }

        class PostedStat
        {
            public string Tag;
            public object Data;
        }
    }

    static partial class CoresConfig
    {
        public static partial class LocalLogRouterSettings
        {
            public static readonly Copenhagen<string> LogRootDir = Path.Combine(Env.AppRootDir, "Log");

            // Debug
            public static readonly Copenhagen<Func<string>> LogDebugDir = new Func<string>(() => Path.Combine(LogRootDir, "Debug"));
            public static readonly Copenhagen<LogSwitchType> SwitchTypeForDebug = LogSwitchType.Hour;
            public static readonly Copenhagen<LogInfoOptions> InfoOptionsForDebug = new LogInfoOptions() { WithPriority = true };

            // Info
            public static readonly Copenhagen<Func<string>> LogInfoDir = new Func<string>(() => Path.Combine(LogRootDir, "Info"));
            public static readonly Copenhagen<LogSwitchType> SwitchTypeForInfo = LogSwitchType.Day;
            public static readonly Copenhagen<LogInfoOptions> InfoOptionsForInfo = new LogInfoOptions() { };

            // Error
            public static readonly Copenhagen<Func<string>> LogErrorDir = new Func<string>(() => Path.Combine(LogRootDir, "Error"));
            public static readonly Copenhagen<LogSwitchType> SwitchTypeForError = LogSwitchType.Day;
            public static readonly Copenhagen<LogInfoOptions> InfoOptionsForError = new LogInfoOptions() { };

            // Data
            public static readonly Copenhagen<Func<string>> LogDataDir = new Func<string>(() => Path.Combine(LogRootDir, "Data"));
            public static readonly Copenhagen<LogSwitchType> SwitchTypeForData = LogSwitchType.Day;
            public static readonly Copenhagen<LogInfoOptions> InfoOptionsForData = new LogInfoOptions() { WithTypeName = true, WriteAsJsonFormat = true, WithTag = true, WithGuid = true };

            // Stat
            public static readonly Copenhagen<Func<string>> LogStatDir = new Func<string>(() => Path.Combine(LogRootDir, "Stat"));
            public static readonly Copenhagen<LogSwitchType> SwitchTypeForStat = LogSwitchType.Day;
            public static readonly Copenhagen<LogInfoOptions> InfoOptionsForStat = new LogInfoOptions() { WithTypeName = true, WriteAsJsonFormat = true, WithTag = true, WithGuid = true };

            // Access log
            public static readonly Copenhagen<Func<string>> LogAccessDir = new Func<string>(() => Path.Combine(LogRootDir, "Access"));
            public static readonly Copenhagen<LogSwitchType> SwitchTypeForAccess = LogSwitchType.Day;
            public static readonly Copenhagen<LogInfoOptions> InfoOptionsForAccess = new LogInfoOptions() { WithTypeName = true, WriteAsJsonFormat = true, WithTag = true, WithGuid = true };

            // Socket log
            public static readonly Copenhagen<Func<string>> LogSocketDir = new Func<string>(() => Path.Combine(LogRootDir, "Socket"));
            public static readonly Copenhagen<LogSwitchType> SwitchTypeForSocket = LogSwitchType.Day;
            public static readonly Copenhagen<LogInfoOptions> InfoOptionsForSocket = new LogInfoOptions() { WithTypeName = true, WriteAsJsonFormat = true, WithTag = true, WithGuid = true };
        }
    }
}

