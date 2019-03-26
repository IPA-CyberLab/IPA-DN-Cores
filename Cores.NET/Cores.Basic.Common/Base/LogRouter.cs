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

using IPA.Cores.Helper.Basic;

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
                maxLogSize: AppConfig.Logger.DefaultMaxLogSize,
                autoDeleteTotalMinSize: autoDeleteTotalMaxSize ?? AppConfig.Logger.DefaultAutoDeleteTotalMinSize);
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

    class LogRouteMachine : AsyncCleanupable
    {
        CriticalSection LockObj = new CriticalSection();

        ImmutableList<LogRouteBase> RouteList = ImmutableList<LogRouteBase>.Empty;

        public LogRouteMachine(AsyncCleanuperLady lady) : base(lady)
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

        public void InstallLogRoute(LogRouteBase route)
        {
            if (DisposeFlag.IsSet) throw new ObjectDisposedException("LogRouteMachine");

            route.OnInstalled();

            lock (LockObj)
            {
                this.RouteList = this.RouteList.Add(route);
            }
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

    static class GlobalLogRouter
    {
        public static readonly LogRouteMachine Machine = new LogRouteMachine(LeakChecker.SuperGrandLady);

        static GlobalLogRouter()
        {
            // Console log
            Machine.InstallLogRoute(new ConsoleLogRoute(LogKind.Default,
                AppConfig.DebugSettings.ConsoleMinimalLevel));

            // Debug log (file)
            Machine.InstallLogRoute(new LoggerLogRoute(LogKind.Default,
                AppConfig.DebugSettings.LogMinimalDebugLevel, 
                "debug",
                AppConfig.GlobalLogRouteMachineSettings.LogDebugDir.Value(),
                AppConfig.GlobalLogRouteMachineSettings.SwitchTypeForDebug,
                AppConfig.GlobalLogRouteMachineSettings.InfoOptionsForDebug));

            // Info log (file)
            Machine.InstallLogRoute(new LoggerLogRoute(LogKind.Default,
                AppConfig.DebugSettings.LogMinimalInfoLevel, 
                "info",
                AppConfig.GlobalLogRouteMachineSettings.LogInfoDir.Value(),
                AppConfig.GlobalLogRouteMachineSettings.SwitchTypeForInfo,
                AppConfig.GlobalLogRouteMachineSettings.InfoOptionsForInfo));

            // Data log (file)
            Machine.InstallLogRoute(new LoggerLogRoute(LogKind.Data,
                AppConfig.DebugSettings.LogMinimalDataLevel,
                "data",
                AppConfig.GlobalLogRouteMachineSettings.LogDataDir.Value(),
                AppConfig.GlobalLogRouteMachineSettings.SwitchTypeForData,
                AppConfig.GlobalLogRouteMachineSettings.InfoOptionsForData));
        }

        public static void Post(LogRecord record, string kind = LogKind.Default) => Machine.PostLog(record, kind);

        public static void Post(object obj, LogPriority priority = LogPriority.Debug, string kind = LogKind.Default, LogFlags flags = LogFlags.None, string tag = null)
            => Machine.PostLog(new LogRecord(obj, priority, flags, tag), kind);

        public static void PrintConsole(object obj, bool noConsole = false, LogPriority priority = LogPriority.Information, string tag = null)
            => Post(obj, priority, flags: noConsole ? LogFlags.NoOutputToConsole : LogFlags.None, tag: tag);

        public static void PostData(object obj, string tag = null, bool copyToDebug = false)
        {
            Post(obj, priority: LogPriority.Information, kind: LogKind.Data, tag: tag);
            if (copyToDebug)
            {
                Post(new PostedData() { Data = obj, Tag = tag }, priority: LogPriority.Debug, kind: LogKind.Default, tag: tag);
            }
        }

        class PostedData
        {
            public string Tag;
            public object Data;
        }
    }

    static partial class AppConfig
    {
        public static partial class GlobalLogRouteMachineSettings
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

            // Data
            public static readonly Copenhagen<Func<string>> LogDataDir = new Func<string>(() => Path.Combine(LogRootDir, "Data"));
            public static readonly Copenhagen<LogSwitchType> SwitchTypeForData = LogSwitchType.Day;
            public static readonly Copenhagen<LogInfoOptions> InfoOptionsForData = new LogInfoOptions() { WithTypeName = true, WriteAsJsonFormat = true, WithTag = true };
        }
    }
}

