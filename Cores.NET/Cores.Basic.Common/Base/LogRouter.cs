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

        public override void OnInstalled() { }

        public override Task OnUninstallingAsync() => Task.CompletedTask;

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
        Logger Log;

        public LoggerLogRoute(string kind, LogPriority minimalPriority, string prefix, string dir, LogSwitchType switchType = LogSwitchType.Day, LogInfoOptions infoOptions = null,
            long? autoDeleteTotalMaxSize = null) : base(kind, minimalPriority)
        {
            Log = new Logger(new AsyncCleanuperLady(), dir, kind, prefix,
                switchType: switchType,
                infoOptions: infoOptions,
                maxLogSize: CoresConfig.Logger.DefaultMaxLogSize,
                autoDeleteTotalMinSize: autoDeleteTotalMaxSize ?? CoresConfig.Logger.DefaultAutoDeleteTotalMinSize);
        }

        Once DisposeFlag;
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;
                Log.DisposeSafe();
            }
            finally { base.Dispose(disposing); }
        }

        public override void OnInstalled() { }

        public async override Task OnUninstallingAsync()
        {
            Log.DisposeSafe();

            await Log.Lady;
        }

        public override void ReceiveLog(LogRecord record)
        {
            Log.Add(record);
        }
    }

    abstract class LogRouteBase : IDisposable
    {
        public string Kind { get; }
        public LogPriority MinimalPriority { get; }

        public LogRouteBase(string kind, LogPriority minimalPriority)
        {
            this.MinimalPriority = minimalPriority;
            this.Kind = kind;
        }

        public abstract void OnInstalled();

        public abstract Task OnUninstallingAsync();

        public abstract void ReceiveLog(LogRecord record);

        public void Dispose() => Dispose(true);
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;
        }
    }

    class LogRouter : AsyncCleanupable
    {
        CriticalSection LockObj = new CriticalSection();

        ImmutableList<LogRouteBase> RouteList = ImmutableList<LogRouteBase>.Empty;

        public LogRouter(AsyncCleanuperLady lady) : base(lady)
        {
        }

        Once DisposeFlag;
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;
                var routeList = this.RouteList;
                foreach (LogRouteBase route in routeList)
                {
                    route.DisposeSafe();
                }
            }
            finally { base.Dispose(disposing); }
        }

        public override async Task _CleanupAsyncInternal()
        {
            try
            {
                var routeList = this.RouteList;
                foreach (LogRouteBase route in routeList)
                {
                    await UninstallLogRouteAsync(route);
                }
            }
            finally { await base._CleanupAsyncInternal(); }
        }

        public LogRouteBase InstallLogRoute(LogRouteBase route)
        {
            if (DisposeFlag.IsSet) throw new ObjectDisposedException("LogRouteMachine");

            route.OnInstalled();

            lock (LockObj)
            {
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

            await route.OnUninstallingAsync();
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
                    if (route.Kind == kind || route.Kind.IsEmpty())
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
        public static readonly LogRouter Router = new LogRouter(LeakChecker.SuperGrandLady);

        static LocalLogRouter()
        {
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

            // Access log (file)
            Router.InstallLogRoute(new LoggerLogRoute(LogKind.Access,
                CoresConfig.DebugSettings.LogMinimalAccessLevel,
                "access",
                CoresConfig.LocalLogRouterSettings.LogAccessDir.Value(),
                CoresConfig.LocalLogRouterSettings.SwitchTypeForAccess,
                CoresConfig.LocalLogRouterSettings.InfoOptionsForAccess));

            var snapshot = new EnvInfoSnapshot("--- Process boottime log ---");

            Router.PostLog(new LogRecord(snapshot, LogPriority.Info, LogFlags.NoOutputToConsole, "boottime"), LogKind.Default);
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

        public static void PostAccessLog(object obj, string tag = null, bool copyToDebug = false, LogPriority priority = LogPriority.Info)
        {
            Post(obj, priority, kind: LogKind.Access, tag: tag);
            if (copyToDebug)
            {
                Post(new PostedAccessLog() { Data = obj, Tag = tag }, priority: LogPriority.Debug, kind: LogKind.Default, tag: tag);
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
            public static readonly Copenhagen<LogInfoOptions> InfoOptionsForData = new LogInfoOptions() { WithTypeName = true, WriteAsJsonFormat = true, WithTag = true };

            // Access log
            public static readonly Copenhagen<Func<string>> LogAccessDir = new Func<string>(() => Path.Combine(LogRootDir, "Access"));
            public static readonly Copenhagen<LogSwitchType> SwitchTypeForAccess = LogSwitchType.Day;
            public static readonly Copenhagen<LogInfoOptions> InfoOptionsForAccess = new LogInfoOptions() { WithTypeName = true, WriteAsJsonFormat = true, WithTag = true };
        }
    }
}

