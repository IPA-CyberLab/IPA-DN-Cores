// IPA Cores.NET
// 
// Copyright (c) 2019- IPA CyberLab.
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

// Author: Daiyuu Nobori
// Description

#if CORES_CODES_WEBAUTO

using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Runtime.CompilerServices;
using System.Net;
using System.Net.Sockets;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;


using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using OpenQA.Selenium.Interactions;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Codes;
using IPA.Cores.Helper.Codes;
using static IPA.Cores.Globals.Codes;

namespace IPA.Cores.Codes;

public static partial class WebAutoConsts
{
    public static readonly Copenhagen<int> DefaultCommandTimeoutMsecs = 15 * 1000;
    public static readonly Copenhagen<int> DefaultPageLoadCommandTimeoutMsecs = 15 * 1000;
    public static readonly Copenhagen<int> DefaultWaitTimeoutMsecs = 5 * 1000;
    public static readonly Copenhagen<int> DefaultStartupTimeoutMsecs = 3 * 1000;
    public static readonly Copenhagen<int> DefaultPortCheckTimeoutMsecs = 1 * 200;
    public static readonly Copenhagen<int> DefaultStartupRrytyCount = 3;
}


public class WebAutoSettings
{
    public string ChromeExePath = "";
    public string ChromeProfilePath_Normal = "";
    public string ChromeProfilePath_Incognito = "";
    public int ChromeDebuggerPort_Normal = Consts.Ports.WebAutoChromeDebuggerPort_Normal;
    public int ChromeDebuggerPort_Incognito = Consts.Ports.WebAutoChromeDebuggerPort_Incognito;
    public string ChromeProxyServer = "";

    public bool IncognitoMode;

    public bool KillProcessOnExitIfExec;

    public int StartupTimeoutMsecs = WebAutoConsts.DefaultStartupTimeoutMsecs;
    public int PortCheckTimeoutMsecs = WebAutoConsts.DefaultPortCheckTimeoutMsecs;
    public int StartupRetryCount = WebAutoConsts.DefaultStartupRrytyCount;
    public int CommandTimeoutMsecs = WebAutoConsts.DefaultCommandTimeoutMsecs;
    public int PageLoadCommandTimeoutMsecs = WebAutoConsts.DefaultPageLoadCommandTimeoutMsecs;
    public int FindElementWaitTimeoutMsecs = WebAutoConsts.DefaultWaitTimeoutMsecs;
}

public class WebAutoWindow : AsyncService
{
    AsyncLock Lock => this.Auto.WindowLock;

    public WebAuto Auto { get; }
    public ChromeDriver Driver => Auto.Driver;

    public string WindowHandle { get; }

    public WebAutoWindow(WebAuto auto, bool tabMode) : base()
    {
        try
        {
            this.Auto = auto;

            using (Lock.LockLegacy())
            {
                this.Driver.SwitchTo().NewWindow(tabMode ? WindowType.Tab : WindowType.Window);

                this.WindowHandle = this.Driver.CurrentWindowHandle;
            }
        }
        catch (Exception ex)
        {
            this._DisposeSafe(ex);
            throw;
        }
    }

    public async Task GoToUrlAsync(string url)
    {
        await this.Driver.Navigate().GoToUrlAsync(url);
    }

    public Actions StartActionAt(IWebElement element)
    {
        var action = new Actions(this.Driver);

        action.MoveToElement(element);

        return action;
    }

    public async Task<IWebElement> WaitAndFindElementAsync(Func<IWebDriver, IWebElement?> condition, int? timeout = null, CancellationToken cancel = default)
    {
        return await WaitAndFindElementAsync(driver =>
        {
            var item = condition(driver);
            if (item == null) return null;
            return item._SingleList();
        }, timeout, cancel);
    }

    public async Task<IWebElement> WaitAndFindElementAsync(Func<IWebDriver, IEnumerable<IWebElement>?> condition, int? timeout = null, CancellationToken cancel = default)
    {
        IWebElement? ret = null;

        timeout ??= this.Auto.Settings.FindElementWaitTimeoutMsecs;

        bool ok = await TaskUtil.AwaitWithPollAsync(timeout.Value, 33, () =>
        {
            if (Driver.Url._IsEmpty())
            {
                return false;
            }
            try
            {
                var candidates = condition(Driver);

                if (candidates != null)
                {
                    ret = candidates.Where(x => x.Displayed).SingleOrDefault();

                    if (ret != null)
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                ex._Error();
            }
            return false;
        },
        cancel,
        true);

        if (ok == false)
        {
            throw new CoresLibException("WaitForFoundAsync: Timed out");
        }

        ret._NullCheck();

        return ret;
    }

    public async Task WaitUntilAsync(Func<IWebDriver, bool> condition, int? timeout = null, CancellationToken cancel = default)
    {
        timeout ??= this.Auto.Settings.FindElementWaitTimeoutMsecs;

        bool ok = await TaskUtil.AwaitWithPollAsync(timeout.Value, 33, () =>
        {
            if (Driver.Url._IsEmpty())
            {
                return false;
            }
            try
            {
                if (condition(Driver))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                ex._Error();
            }
            return false;
        },
        cancel,
        true);

        if (ok == false)
        {
            throw new CoresLibException("WaitUntilAsync: Timed out");
        }
    }

    public async Task DoCriticalAsync(Func<CancellationToken, Task> task, CancellationToken cancel = default)
    {
        await using (this.CreatePerTaskCancellationToken(out var opCancel, cancel))
        {
            using (await Lock.LockWithAwait(opCancel))
            {
                if (this.Driver.CurrentWindowHandle != this.WindowHandle)
                {
                    this.Driver.SwitchTo().Window(this.WindowHandle);
                }

                await task(opCancel);
            }
        }
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            using (await Lock.LockWithAwait())
            {
                if (this.WindowHandle._IsFilled())
                {
                    try
                    {
                        Driver.SwitchTo().Window(this.WindowHandle);
                        Driver.Close();
                    }
                    catch (Exception ex2)
                    {
                        ex2._Error();
                    }
                }
            }
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

public class WebAuto : AsyncService
{
    public WebAutoSettings Settings { get; }

    ExecInstance? ChromeProcess;

    public ChromeDriverService Service { get; }
    public ChromeDriver Driver { get; }
    public bool IsNewProcessStarted { get; }

    readonly static AsyncLock StartupLock = new AsyncLock();

    public readonly AsyncLock WindowLock = new AsyncLock();

    public WebAuto(WebAutoSettings settings, CancellationToken cancel = default) : base(cancel)
    {
        try
        {
            this.Settings = settings;

            int port = settings.IncognitoMode ? settings.ChromeDebuggerPort_Incognito : settings.ChromeDebuggerPort_Normal;

            List<string> chromeArgs = new();
            chromeArgs.Add("--disable-session-crashed-bubble");
            chromeArgs.Add($"--remote-debugging-port={port}");
            chromeArgs.Add($"--user-data-dir={(settings.IncognitoMode ? settings.ChromeProfilePath_Incognito : settings.ChromeProfilePath_Normal)._EnsureQuotation()}");
            if (this.Settings.ChromeProxyServer._IsFilled())
            {
                chromeArgs.Add($"--proxy-server={settings.ChromeProxyServer}");
            }
            if (settings.IncognitoMode)
            {
                chromeArgs.Add("--incognito");
            }
            chromeArgs.Add("--no-first-run");

            ExecFlags processFlags = ExecFlags.KillProcessGroup | ExecFlags.DoNotConnectPipe;
            if (this.Settings.KillProcessOnExitIfExec == false)
            {
                processFlags |= ExecFlags.DoNotKill;
            }

            string exeName = PP.GetFileNameWithoutExtension(settings.ChromeExePath);

            using (StartupLock.LockLegacy(this.GrandCancel))
            {
                bool alreadyRunning = false;

                bool processExists = Process.GetProcesses().Where(x => x.ProcessName._IsSamei(exeName) && (x.MainModule?.FileName?._IsSamei(settings.ChromeExePath) ?? false)).Any();

                if (processExists)
                {
                    if (IsPortOpen("127.0.0.1", port, TimeSpan.FromMilliseconds(settings.PortCheckTimeoutMsecs)))
                    {
                        alreadyRunning = true;
                    }
                }

                if (alreadyRunning == false)
                {
                    this.ChromeProcess = new ExecInstance(new ExecOptions(Settings.ChromeExePath, chromeArgs._Combine(" "),
                        PP.GetDirectoryName(Settings.ChromeExePath),
                        flags: processFlags));

                    this.IsNewProcessStarted = true;

                    RetryHelper.RunAsync(async () =>
                    {
                        if (IsPortOpen("127.0.0.1", port, TimeSpan.FromMilliseconds(settings.StartupTimeoutMsecs)))
                        {
                            return true;
                        }

                        await Task.CompletedTask;

                        throw new CoresException("Chrome debugger port is not open");
                    }, 100, Settings.StartupRetryCount, cancel, true)._GetResult();
                }

                ChromeOptions options = new ChromeOptions
                {
                    DebuggerAddress = $"127.0.0.1:{port}",
                };

                this.Service = ChromeDriverService.CreateDefaultService();
                this.Driver = new ChromeDriver(this.Service, options, TimeSpan.FromMilliseconds(settings.CommandTimeoutMsecs));

                this.Driver.Manage().Timeouts().PageLoad = this.Settings.PageLoadCommandTimeoutMsecs._ToTimeSpanMSecs();
            }
        }
        catch (Exception ex)
        {
            this._DisposeSafe(ex);
            throw;
        }
    }

    public WebAutoWindow CreateWindowOrTab(bool tabMode)
    {
        return new WebAutoWindow(this, tabMode);
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            Driver.Quit();
            await this.Driver._DisposeSafeAsync2();
            await this.Service._DisposeSafeAsync2();

            await this.ChromeProcess._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }

    static bool IsPortOpen(string host, int port, TimeSpan timeout)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);

            if (!connectTask.Wait(timeout))
            {
                return false;
            }
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }
}

#endif

