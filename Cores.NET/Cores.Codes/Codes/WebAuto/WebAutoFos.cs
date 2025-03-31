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

public class WebAutoFosSettings
{
    public string BaseUrl = "";
}

public class WebAutoFos : AsyncService
{
    public WebAuto Auto { get; }
    public WebAutoFosSettings FosSettings {get;}
    public WebAutoWindow Window { get; }
    public ChromeDriver Driver => Auto.Driver;

    public WebAutoFos(WebAutoFosSettings fosSettings, WebAutoSettings webAutoSettings, CancellationToken cancel = default) : base(cancel)
    {
        try
        {
            this.FosSettings = fosSettings;

            this.Auto = new WebAuto(webAutoSettings, this.GrandCancel);

            this.Window = this.Auto.CreateWindowOrTab(true);
        }
        catch (Exception ex)
        {
            this._DisposeSafe(ex);
            throw;
        }
    }

    public async Task TestAsync(CancellationToken cancel = default)
    {
        await Window.GoToUrlAsync(FosSettings.BaseUrl);

        // model-switcher-o1-pro           o1 pro mode
        // model-switcher-o3-mini-high     o3-mini-high
        // model-switcher-o3-mini          o3-mini
        // model-switcher-o1               o1
        // model-switcher-gpt-4-5          GPT-4.5
        // model-switcher-gpt-4o           GPT-4o


        var aiModelSelectBox = await Window.WaitAndFindElementAsync(e => e.FindElements(By.CssSelector("button[data-testid*='model-switcher-dropdown-button']")), cancel: cancel);
        Window.StartActionAt(aiModelSelectBox).Click().Perform();

        //var x = Driver.FindElements(By.PartialLinkText("o1 pro mode"));
        //var o1ProModeItem = await Window.WaitAndFindElementAsync(e => e.FindElements(By.XPath("//*[contains(text(), 'o1 pro mode')]")));
        var o1ProModeItem = await Window.WaitAndFindElementAsync(e => e.FindElements(By.XPath("//div[@data-radix-popper-content-wrapper]//div[@role='menuitem' and @data-testid='model-switcher-o1-pro']")), cancel: cancel);
        Window.StartActionAt(o1ProModeItem).Click().Perform();

        Where();

        await Window.WaitUntilAsync(driver =>
        {
            string url = driver.Url;
            return url.EndsWith("/?model=o1-pro", StrCmpi);
        }, cancel: cancel);

        Where();


        DoNothing();
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.Window._DisposeSafeAsync();
            await this.Auto._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

#endif

