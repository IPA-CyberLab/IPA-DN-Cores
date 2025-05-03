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
using System.Drawing;

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

using OpenQA.Selenium.DevTools;
using OpenQA.Selenium.DevTools.V134;
using OpenQA.Selenium.DevTools.V134.Browser;
//using OpenQA.Selenium.DevTools.V134.Network;
//using DevToolsSessionDomains = OpenQA.Selenium.DevTools.V134.DevToolsSessionDomains;

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

public static class WebAutoSeleniumHelper
{
    public static IWebElement GetParent(this IWebElement tag) => tag.FindElement(By.XPath(".."));
    public static string GetOuterHtml(this IWebElement tag) => tag.GetAttribute("outerHTML")._NonNull();
    public static string GetInnerHtml(this IWebElement tag) => tag.GetAttribute("innerHTML")._NonNull();
    public static string GetThisTagHtml(this IWebElement tag)
    {
        // 1) 最初の ">" を探す
        string outerHtml = tag.GetOuterHtml();
        int closeBracketIndex = outerHtml.IndexOf('>');
        if (closeBracketIndex == -1)
        {
            // 正常なタグ構造ではない (何らかのエラー)
            throw new Exception("Invalid outerHTML: " + outerHtml);
        }

        // 2) 開始タグ部分だけを切り出す => "<div abc>"
        string openingTag = outerHtml.Substring(0, closeBracketIndex + 1);

        return openingTag;
    }
    public static List<IWebElement> GetThisAndParentTags(this IWebElement tag)
    {
        List<IWebElement> ret = new List<IWebElement>();
        IWebElement currentElement = tag;

        List<string> tmpList = new List<string>();

        try
        {
            ret.Add(tag);

            while (true)
            {
                currentElement = currentElement.FindElement(By.XPath(".."));

                if (currentElement == null)
                {
                    break;
                }

                if (currentElement.TagName.Equals("html", StringComparison.OrdinalIgnoreCase))
                {
                    ret.Add(currentElement);
                    break;
                }

                ret.Add(currentElement);
            }
        }
        catch (NoSuchElementException)
        {
        }

        return ret;
    }
    public static string GetThisAndParentTagsDebugStr(this IWebElement tag)
    {
        IWebElement currentElement = tag;

        List<string> tmpList = new List<string>();

        try
        {
            tmpList.Add(currentElement.GetThisTagHtml());

            while (true)
            {
                currentElement = currentElement.FindElement(By.XPath(".."));
                tmpList.Add(currentElement.GetThisTagHtml());

                if (currentElement == null)
                {
                    break;
                }

                if (currentElement.TagName.Equals("html", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }
        }
        catch (NoSuchElementException)
        {
        }

        StringWriter w = new StringWriter();
        w.NewLine = Str.NewLine_Str_Local;
        for (int i = 0; i < tmpList.Count; i++)
        {
            int index = tmpList.Count - i - 1;
            string s = tmpList[index];

            w.WriteLine($"{Str.MakeCharArray(' ', i)}{s}");
        }

        return w.ToString();
    }
    public static void DebugThisAndParentTags(this IWebElement tag)
    {
        tag.GetThisAndParentTagsDebugStr()._Debug();
    }
}

public class WebAutoSettings
{
    public string ChromeExePath = "";
    public string ChromeProfilePath = "";
    public string ChromeDriverExePath = "";
    public int ChromeDebuggerPort = Consts.Ports.WebAutoChromeDebuggerPortDefault;
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

public class WebAutoDownloadedFile
{
    public FilePath? DownloadedFilePath = null!;
    public string SuggestedFileName = null!;
    public Memory<byte> FileBody;
    public string LinkTextStr = null!;
}

public class WebAutoWindow : AsyncService
{
    AsyncLock Lock => this.Auto.WindowLock;

    public WebAuto Auto { get; }
    public ChromeDriver Driver => Auto.Driver;
    public bool AutoCloseOnDispose { get; }

    public string WindowHandle { get; }

    string DownloadTmpDirPath { get; }

    public WebAutoWindow(WebAuto auto, bool tabMode, bool autoCloseOnDispose) : base()
    {
        try
        {
            this.DownloadTmpDirPath = auto.DownloadTmpDirRoot._CombinePath("d_" + Str.GenRandStr().Substring(0, 16).ToLowerInvariant());

            this.Auto = auto;
            this.AutoCloseOnDispose = autoCloseOnDispose;

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

    public byte[] CaptureScreenShotPng()
    {
        var screenshot = ((ITakesScreenshot)Driver).GetScreenshot();

        using var ms = new MemoryStream(screenshot.AsByteArray);

        return ms.ToArray();
    }

    public async Task GoToUrlAsync(string url)
    {
        await this.Driver.Navigate().GoToUrlAsync(url);
    }

    public void SetFocus(IWebElement element)
    {
        ((IJavaScriptExecutor)this.Driver).ExecuteScript("arguments[0].focus();", element);
    }

    public Actions StartActionAt(IWebElement element)
    {
        var action = new Actions(this.Driver);

        action.MoveToElement(element);

        return action;
    }

    public Actions NewActions()
    {
        return new Actions(this.Driver);
    }

    public async Task<IWebElement> WaitAndFindElementAsync(Func<IWebDriver, Task<IWebElement?>> condition, int? timeout = null, CancellationToken cancel = default)
    {
        return await WaitAndFindElementAsync(async driver =>
        {
            var item = await condition(driver);
            if (item == null) return null;
            return item._SingleList();
        }, timeout, cancel);
    }

    public Task<IWebElement> WaitAndFindElementAsync(Func<IWebDriver, IWebElement?> condition, int? timeout = null, CancellationToken cancel = default)
        => WaitAndFindElementAsync(driver => condition(driver)._TR(), timeout, cancel);

    public async Task<IWebElement> WaitAndFindElementAsync(Func<IWebDriver, Task<IEnumerable<IWebElement>?>> condition, int? timeout = null, CancellationToken cancel = default)
    {
        IWebElement? ret = null;

        timeout ??= this.Auto.Settings.FindElementWaitTimeoutMsecs;

        bool ok = await TaskUtil.AwaitWithPollAsync(timeout.Value, 33, async () =>
        {
            if (Driver.Url._IsEmpty())
            {
                return false;
            }
            try
            {
                var candidates = await condition(Driver);

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
    public Task<IWebElement> WaitAndFindElementAsync(Func<IWebDriver, IEnumerable<IWebElement>?> condition, int? timeout = null, CancellationToken cancel = default)
        => WaitAndFindElementAsync(driver => condition(driver)._TR(), timeout, cancel);


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

    class DevTools
    {
        public DevToolsSession Session = null!;
        public OpenQA.Selenium.DevTools.V134.DevToolsSessionDomains Domain = null!;
    }

    ResultOrExeption<DevTools>? CurrentDevTools = null;

    async Task<ResultOrExeption<DevTools>> Internal_GetDevToolsSessionMainAsync()
    {
        await Task.CompletedTask;
        try
        {
            var session = Driver.GetDevToolsSession();
            var domain = session.GetVersionSpecificDomains<OpenQA.Selenium.DevTools.V134.DevToolsSessionDomains>();

            return new ResultOrExeption<DevTools>(new DevTools { Session = session, Domain = domain });
        }
        catch (Exception ex)
        {
            return new ResultOrExeption<DevTools>(ex);
        }
    }

    async Task<DevTools> GetDevToolsSessionAsync()
    {
        if (CurrentDevTools == null) CurrentDevTools = await Internal_GetDevToolsSessionMainAsync();

        return CurrentDevTools!;
    }

    public async Task<WebAutoDownloadedFile> DownloadFileAsync(IWebElement clickToDownload, string clickLinkStr, FilePath? destFilePath = null, bool useSuggestedFileNameAsDestPath = false, int startTimeout = 10 * 1000, int downloadTimeout = 30 * 1000, CancellationToken cancel = default)
    {
        Where();
        // 一時ディレクトリの作成
        string downloadDir = this.DownloadTmpDirPath._CombinePath("x_" + Str.GenRandStr().Substring(0, 16).ToLowerInvariant());

        try
        {
            var devTools = await GetDevToolsSessionAsync();
            var devToolsDomain = devTools.Domain;

            await devToolsDomain.Browser.SetDownloadBehavior(new SetDownloadBehaviorCommandSettings
            {
                Behavior = "allow",
                DownloadPath = downloadDir,
                EventsEnabled = true,
            });

            await Lfs.CreateDirectoryAsync(downloadDir, flags: FileFlags.OnCreateSetCompressionFlag, cancel: cancel);

            string? downloadGuid = null;
            string? suggestedFileName = null;

            bool isDownloadCompleted = false;
            AsyncManualResetEvent startedEvent = new();
            AsyncManualResetEvent completedEvent = new();

            EventHandler<DownloadWillBeginEventArgs> downloadStartEvent = (sender, e) =>
            {
                downloadGuid = e.Guid;
                suggestedFileName = e.SuggestedFilename;
                startedEvent.Set(true);
            };

            EventHandler<DownloadProgressEventArgs> downloadProgressEvent = (sender, e) =>
            {
                if (e.Guid != downloadGuid) return;
                if (e.State == "completed")
                {
                    isDownloadCompleted = true;
                    completedEvent.Set(true);
                }
                else if (e.State == "interrupted")
                {
                    isDownloadCompleted = false;
                    completedEvent.Set(true);
                }
            };

            // ダウンロード関係イベントの設定
            devToolsDomain.Browser.DownloadWillBegin += downloadStartEvent;
            devToolsDomain.Browser.DownloadProgress += downloadProgressEvent;

            try
            {
                this.StartActionAt(clickToDownload).Click().Perform();

                await Task.Yield();

                // 開始まで待機
                await startedEvent.WaitAsync(startTimeout, cancel);

                if (startedEvent.IsSet == false)
                {
                    throw new CoresException("File download did not started.");
                }

                // 完了まで待機
                await completedEvent.WaitAsync(downloadTimeout, cancel);

                if (isDownloadCompleted)
                {
                    suggestedFileName = PPWin.MakeSafeFileName(suggestedFileName._NotEmptyOrDefault("unknown.dat"), false, true, true);

                    var srcFile = (await Lfs.EnumDirectoryAsync(downloadDir, cancel: cancel)).Where(x => x.IsFile).Single();

                    if (destFilePath != null)
                    {
                        var destFilePath2 = destFilePath;

                        if (useSuggestedFileNameAsDestPath)
                        {
                            destFilePath2 = destFilePath2.GetParentDirectory().Combine(suggestedFileName);
                        }

                        await Lfs.CopyFileAsync(srcFile.FullPath, destFilePath2.PathString,
                            new CopyFileParams(flags: destFilePath.Flags.BitAdd(FileFlags.AutoCreateDirectory)), destFileSystem: destFilePath.FileSystem, cancel: cancel);

                        return new WebAutoDownloadedFile { DownloadedFilePath = destFilePath2, SuggestedFileName = suggestedFileName, LinkTextStr = clickLinkStr };
                    }
                    else
                    {
                        return new WebAutoDownloadedFile { DownloadedFilePath = null, SuggestedFileName = suggestedFileName, LinkTextStr = clickLinkStr, FileBody = await Lfs.ReadDataFromFileAsync(srcFile.FullPath, cancel: cancel) };
                    }
                }
                else
                {
                    if (downloadGuid != null)
                    {
                        try
                        {
                            await devToolsDomain.Browser.CancelDownload(new OpenQA.Selenium.DevTools.V134.Browser.CancelDownloadCommandSettings
                            {
                                Guid = downloadGuid,
                            });
                        }
                        catch { }
                    }

                    throw new CoresException("File download aborted.");
                }
            }
            finally
            {
                // ダウンロード関係イベントの削除
                try
                {
                    devToolsDomain.Browser.DownloadWillBegin -= downloadStartEvent;
                    devToolsDomain.Browser.DownloadProgress -= downloadProgressEvent;
                }
                catch (Exception ex)
                {
                    ex._Error();
                }
            }
        }
        finally
        {
            await DeleteDownloadTempDirAsync();
        }
    }

    async Task DeleteDownloadTempDirAsync(CancellationToken cancel = default)
    {
        try
        {
            if (await Lfs.IsDirectoryExistsAsync(this.DownloadTmpDirPath, cancel))
            {
                //await Lfs.DeleteDirectoryAsync(this.DownloadTmpDirPath, true, forcefulUseInternalRecursiveDelete: true);
            }
        }
        catch (Exception ex)
        {
            ex._Error();
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

                        if (this.AutoCloseOnDispose)
                        {
                            Driver.Close();
                        }
                        Driver._DisposeSafe();
                    }
                    catch (Exception ex2)
                    {
                        ex2._Error();
                    }
                }

            }

            await DeleteDownloadTempDirAsync();
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

    public string DownloadTmpDirRoot { get; }

    readonly static AsyncLock StartupLock = new AsyncLock();

    public readonly AsyncLock WindowLock = new AsyncLock();

    public WebAuto(WebAutoSettings settings, CancellationToken cancel = default) : base(cancel)
    {
        try
        {
            this.DownloadTmpDirRoot = Env.MyLocalTempDir._CombinePath("_down_tmp");

            this.Settings = settings;

            int port = settings.ChromeDebuggerPort;

            List<string> chromeArgs = new();
            chromeArgs.Add("--disable-cache");
            chromeArgs.Add("--disable-application-cache");
            chromeArgs.Add("--disable-offline-load-stale-cache");
            chromeArgs.Add("--disk-cache-size=0");
            chromeArgs.Add("--disable-session-crashed-bubble");
            chromeArgs.Add($"--remote-debugging-port={port}");
            chromeArgs.Add($"--user-data-dir={settings.ChromeProfilePath._EnsureQuotation()}");
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


                if (settings.ChromeDriverExePath._IsFilled())
                {
                    string driverExePath = settings.ChromeDriverExePath;

                    this.Service = ChromeDriverService.CreateDefaultService(PP.GetDirectoryName(driverExePath), PP.GetFileName(driverExePath));
                }
                else
                {
                    this.Service = ChromeDriverService.CreateDefaultService();
                }
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

    public WebAutoWindow CreateWindowOrTab(bool tabMode, bool autoCloseOnDispose)
    {
        return new WebAutoWindow(this, tabMode, autoCloseOnDispose);
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

