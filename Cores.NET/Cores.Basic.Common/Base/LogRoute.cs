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
    class LoggerLogRoute : LogRouteBase
    {
        Logger Log;

        public LoggerLogRoute(LogPriority minimalPriority, string kind, string prefix, string dir, LogSwitchType switchType = LogSwitchType.Day, LogInfoOptions infoOptions = null,
            long? autoDeleteTotalMaxSize = null) : base(minimalPriority, kind)
        {
            Log = new Logger(new AsyncCleanuperLady(), dir, kind, prefix,
                switchType: switchType,
                infoOptions: infoOptions,
                maxLogSize: AppConfig.DefaultLoggerSettings.MaxLogSize,
                autoDeleteTotalMinSize: autoDeleteTotalMaxSize ?? AppConfig.DefaultLoggerSettings.AutoDeleteTotalMinSize);
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
        public LogPriority MinimalPriority { get; }
        public string Kind { get; }

        public LogRouteBase(LogPriority minimalPriority, string kind)
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

        public void PostLog(string kind, LogRecord record)
        {
            var routeList = this.RouteList;
            foreach (LogRouteBase route in routeList)
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

    static partial class AppConfig
    {
        public static partial class GlobalLogRouteMachine
        {
            public static readonly Copenhagen<LogSwitchType> SwitchTypeForInfo = LogSwitchType.Day;
            public static readonly Copenhagen<LogSwitchType> SwitchTypeForDebug = LogSwitchType.Hour;
            public static readonly Copenhagen<string> LogRootDir = Path.Combine(Env.AppRootDir, "Log");
            public static readonly Copenhagen<Func<string>> LogDebugDir = new Func<string>(() => Path.Combine(LogRootDir, "Debug"));
            public static readonly Copenhagen<Func<string>> LogInfoDir = new Func<string>(() => Path.Combine(LogRootDir, "Info"));

            public static readonly LogRouteMachine Machine = new LogRouteMachine(LeakChecker.SuperGrandLady);

            static GlobalLogRouteMachine()
            {
                // Debug log
                Machine.InstallLogRoute(new LoggerLogRoute(LogPriority._Minimal, LogKind.Default, "debug", LogDebugDir.Value(), SwitchTypeForDebug,
                    new LogInfoOptions() { WithPriority = true }));

                // Info log
                Machine.InstallLogRoute(new LoggerLogRoute(LogPriority.Information, LogKind.Default, "info", LogInfoDir.Value(), SwitchTypeForInfo,
                    new LogInfoOptions() { WithPriority = true }));
            }
        }
    }
}

