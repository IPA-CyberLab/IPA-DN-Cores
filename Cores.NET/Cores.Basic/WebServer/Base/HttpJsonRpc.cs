// IPA Cores.NET
// 
// Copyright (c) 2018- IPA CyberLab.
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

#if CORES_BASIC_WEBAPP || CORES_BASIC_HTTPSERVER

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http.Extensions;
using Castle.DynamicProxy;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Microsoft.Extensions.Hosting;
using System.IO;
using System.Web;

#if CORES_BASIC_HTTPSERVER
// ASP.NET Core 3.0 用の型名を無理やり ASP.NET Core 2.2 でコンパイルするための型エイリアスの設定
using IWebHostEnvironment = Microsoft.AspNetCore.Hosting.IHostingEnvironment;
using IHostApplicationLifetime = Microsoft.AspNetCore.Hosting.IApplicationLifetime;
#endif

namespace IPA.Cores.Basic;

public class JsonRpcHttpServer : JsonRpcServer
{
    public static bool HideErrorDetails = false; // 詳細エラーを隠すかどうかのフラグ。手抜きグローバル変数

    public string RpcBaseAbsoluteUrlPath = ""; // "/rpc/" のような絶対パス。末尾に / を含む。
    public string WebFormBaseAbsoluteUrlPath = ""; // "/webform/" のような絶対パス。末尾に / を含む。
    public string ConfigFormBaseAbsoluteUrlPath = ""; // "/configform/" のような絶対パス。末尾に / を含む。
    public string ObjEditBaseAbsoluteUrlPath = ""; // "/objedit/" のような絶対パス。末尾に / を含む。

    readonly string WebFormSecretKey = Str.GenRandStr();

    public JsonRpcHttpServer(JsonRpcServerApi api, JsonRpcServerConfig? cfg = null) : base(api, cfg) { }

    enum AdminFormsOperation
    {
        ConfigForm = 0,
    }

    // /configform の GET ハンドラ
    public virtual async Task ConfigForm_GetRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData)
    {
        await ConfigForm_CommonRequestHandler(request, response, routeData, WebMethods.GET);
    }

    // /configform の POST ハンドラ
    public virtual async Task ConfigForm_PostRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData)
    {
        await ConfigForm_CommonRequestHandler(request, response, routeData, WebMethods.POST);
    }

    // /configform の共通ハンドラ
    public virtual async Task ConfigForm_CommonRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData, WebMethods method)
    {
        await AdminForms_CommonAsync(request, response, routeData, "Admin Config Page", AdminFormsOperation.ConfigForm, method,
            async (w, postData, c) =>
            {
                string configBody = "";

                string msg = "";

                if (method == WebMethods.POST)
                {
                    string str = postData._GetString_UTF8();
                    var postCollection = HttpUtility.ParseQueryString(str);
                    string secret = postCollection._GetStr("_secret");
                    if (secret != this.WebFormSecretKey)
                    {
                        msg = "Error! Invalid web form update.";
                    }
                    else
                    {
                        configBody = postCollection._GetStr("configbody");
                        await this.Config.HadbBasedServicePoint!.AdminForm_SetDynamicConfigAsync(configBody, c);

                        msg = "The settings you specified have been properly applied to the server database.";
                    }
                }

                configBody = await this.Config.HadbBasedServicePoint!.AdminForm_GetDynamicConfigAsync(c);

                if (msg._IsFilled()) w.WriteLine($"<p><B><font color=green>{msg}</font></B></p>");

                w.WriteLine($"<form action='{this.ConfigFormBaseAbsoluteUrlPath}' method='post'>");

                w.WriteLine($"<input name='_secret' type='hidden' value='{this.WebFormSecretKey}'/>");

                w.WriteLine("<p>");
                w.WriteLine("<textarea name='configbody' spellcheck='false' rows='2' cols='20' id='configbody' style='color:#222222;font-family:Consolas;font-size:10pt;height:702px;width:95%;padding: 10px 10px 10px 10px;'>");
                w.WriteLine(configBody._EncodeHtmlCodeBlock());
                w.WriteLine("</textarea>");
                w.WriteLine("</p>");

                if (msg._IsFilled()) w.WriteLine($"<p><B><font color=green>{msg}</font></B></p>");

                w.WriteLine("<input class='button is-link' type='submit' style='font-weight: bold' value='Update Now (Be Careful!)'>");

                w.WriteLine("</form>");
            });
    }

    async Task AdminForms_CommonAsync(HttpRequest request, HttpResponse response, RouteData routeData, string title, AdminFormsOperation operation, WebMethods method,
        Func<StringWriter, ReadOnlyMemory<byte>, CancellationToken, Task> bodyWriter)
    {
        ReadOnlyMemory<byte> postData = default;
        CancellationToken cancel = request._GetRequestCancellationToken();

        if (method == WebMethods.POST)
        {
            postData = await request.Body._ReadToEndAsync(10_000_000, cancel);
        }

        // Basic 認証または Query String における認証クレデンシャルが提供されているかどうか調べる
        string suppliedUsername = "";
        string suppliedPassword = "";
        var basicAuthHeader = BasicAuthImpl.ParseBasicAuthenticationHeader(request.Headers._GetStrFirst("Authorization"));
        if (basicAuthHeader != null)
        {
            suppliedUsername = basicAuthHeader.Item1;
            suppliedPassword = basicAuthHeader.Item2;
        }
        var conn = request.HttpContext.Connection;

        SortedDictionary<string, string> requestHeaders = new SortedDictionary<string, string>();
        foreach (string headerName in request.Headers.Keys)
        {
            if (request.Headers.TryGetValue(headerName, out var val))
            {
                requestHeaders.Add(headerName, val.ToString());
            }
        }

        JsonRpcClientInfo clientInfo = new JsonRpcClientInfo(
            request.Scheme,
            request.Host.ToString(),
            request.GetDisplayUrl(),
            conn.LocalIpAddress!._UnmapIPv4().ToString(), conn.LocalPort,
            conn.RemoteIpAddress!._UnmapIPv4().ToString(), conn.RemotePort,
            requestHeaders,
            suppliedUsername,
            suppliedPassword);

        string basicAuthRealm = "Auth for " + this.ConfigFormBaseAbsoluteUrlPath;

        TaskVar.Set<JsonRpcClientInfo>(clientInfo);
        try
        {
            if (this.Config.HadbBasedServicePoint == null)
            {
                throw new CoresException("this.Config.HadbBasedServicePoint is not set.");
            }
            else
            {
                await this.Config.HadbBasedServicePoint.Basic_Require_AdminBasicAuthAsync(basicAuthRealm);
            }

            StringWriter w = new StringWriter();

            WebForm_WriteHtmlHeader(w, $"{this.Config.HelpServerFriendlyName._FilledOrDefault(Api.GetType().Name)} - {title}");

            w.WriteLine(@"
    <div class='box'>
        <div class='content'>
");

            w.WriteLine($"<h2 class='title is-4'>" + $"{this.Config.HelpServerFriendlyName._FilledOrDefault(Api.GetType().Name)} - {title}" + "</h2>");

            await bodyWriter(w, postData, cancel);

            w.WriteLine(@"
        </div>
    </div>
");

            WebForm_WriteHtmlFooter(w);

            await response._SendStringContentsAsync(w.ToString(), contentsType: Consts.MimeTypes.HtmlUtf8, cancel: cancel, normalizeCrlf: CrlfStyle.CrLf);

        }
        catch (JsonRpcAuthErrorException)
        {
            // Basic 認証の要求
            KeyValueList<string, string> basicAuthResponseHeaders = new KeyValueList<string, string>();
            basicAuthResponseHeaders.Add(Consts.HttpHeaders.WWWAuthenticate, $"Basic realm=\"{basicAuthRealm}\"");

            await using var basicAuthRequireResult = new HttpStringResult("Basic Auth Required", contentType: Consts.MimeTypes.TextUtf8, statusCode: Consts.HttpStatusCodes.Unauthorized, additionalHeaders: basicAuthResponseHeaders);

            await response._SendHttpResultAsync(basicAuthRequireResult, cancel: request._GetRequestCancellationToken());

            return;
        }
        finally
        {
            TaskVar.Set<JsonRpcClientInfo>(null);
        }

    }

    // /webform の GET ハンドラ
    public virtual async Task WebForm_GetRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData)
    {
        CancellationToken cancel = request._GetRequestCancellationToken();
        try
        {
            string rpcMethod = routeData.Values._GetStr("rpc_method");
            if (rpcMethod._IsEmpty())
            {
                // 目次ページの表示 (TODO)
                var baseUri = request.GetEncodedUrl()._ParseUrl()._CombineUrl(this.RpcBaseAbsoluteUrlPath);

                await response._SendStringContentsAsync(WebForm_GenerateHtmlHelpString(baseUri), contentsType: Consts.MimeTypes.HtmlUtf8, cancel: cancel, normalizeCrlf: CrlfStyle.CrLf);
            }
            else
            {
                var mi = this.Api.GetMethodInfo(rpcMethod);

                // API ごとのページの表示
                string suppliedUsername = "";
                string suppliedPassword = "";

                // Basic 認証または Query String における認証クレデンシャルが提供されているかどうか調べる
                var basicAuthHeader = BasicAuthImpl.ParseBasicAuthenticationHeader(request.Headers._GetStrFirst("Authorization"));
                if (basicAuthHeader != null)
                {
                    suppliedUsername = basicAuthHeader.Item1;
                    suppliedPassword = basicAuthHeader.Item2;
                }

                JObject jObj = new JObject();

                bool isCall = false;

                SortedDictionary<string, string> requestHeaders = new SortedDictionary<string, string>();
                foreach (string headerName in request.Headers.Keys)
                {
                    if (request.Headers.TryGetValue(headerName, out var val))
                    {
                        requestHeaders.Add(headerName, val.ToString());
                    }
                }

                foreach (string key in request.Query.Keys)
                {
                    string value = request.Query[key];

                    if (key._IsSamei("_call"))
                    {
                        if (value._ToBool())
                        {
                            isCall = true;
                        }
                    }

                    string valueTrim = value.Trim();
                    if (valueTrim.StartsWith("{") && valueTrim.EndsWith("}"))
                    {
                        // JSON Data
                        jObj.Add(key, value._JsonToObject<JObject>());
                    }
                    else
                    {
                        // Primitive Date
                        jObj.Add(key, JToken.FromObject(value));
                    }
                }

                if (isCall == false)
                {
                    if (mi.ParametersByIndex.Length == 0)
                    {
                        // 1 つもパラメータ指定がない場合は Web フォームを作成せず直接呼び出す
                        isCall = true;
                    }
                }

                string args = jObj._ObjectToJson(compact: true);

                //string id = "GET-" + Str.NewGuid().ToUpperInvariant();
                string in_str = "{'jsonrpc':'2.0','method':'" + rpcMethod + "','params':" + args + "}";

                string body;

                var conn = request.HttpContext.Connection;

                JsonRpcClientInfo client_info = new JsonRpcClientInfo(
                    request.Scheme,
                    request.Host.ToString(),
                    request.GetDisplayUrl(),
                    conn.LocalIpAddress!._UnmapIPv4().ToString(), conn.LocalPort,
                    conn.RemoteIpAddress!._UnmapIPv4().ToString(), conn.RemotePort,
                    requestHeaders,
                    suppliedUsername,
                    suppliedPassword);

                if (isCall == false)
                {
                    // Web フォームの画面表示
                    body = WebForm_GenerateApiInputFormPage(rpcMethod);
                }
                else
                {
                    long tickStart = Time.HighResTick64;

                    var timeStamp = DtOffsetNow;

                    // JSON-RPC の実際の実行と結果の取得
                    var callResults = await this.CallMethods(in_str, client_info, true, StringComparison.OrdinalIgnoreCase);

                    if (HideErrorDetails) callResults.RemoveErrorDetailsFromResultString();

                    long tickEnd = Time.HighResTick64;

                    long tookTick = tickEnd - tickStart;

                    if (callResults.Error_AuthRequired)
                    {
                        // Basic 認証の要求
                        KeyValueList<string, string> basicAuthResponseHeaders = new KeyValueList<string, string>();
                        string realm = $"User Authentication for {this.RpcBaseAbsoluteUrlPath}";
                        if (callResults.Error_AuthRequiredRealmName._IsFilled())
                        {
                            realm += $" ({realm._MakeVerySafeAsciiOnlyNonSpaceFileName()})";
                        }
                        basicAuthResponseHeaders.Add(Consts.HttpHeaders.WWWAuthenticate, $"Basic realm=\"{realm}\"");

                        await using var basicAuthRequireResult = new HttpStringResult(callResults.ResultString, contentType: Consts.MimeTypes.TextUtf8, statusCode: Consts.HttpStatusCodes.Unauthorized, additionalHeaders: basicAuthResponseHeaders);

                        await response._SendHttpResultAsync(basicAuthRequireResult, cancel: request._GetRequestCancellationToken());

                        return;
                    }

                    body = WebForm_GenerateApiResultPage(rpcMethod, callResults, request.Host.Host, timeStamp, tookTick);
                }

                await response._SendStringContentsAsync(body, contentsType: Consts.MimeTypes.HtmlUtf8, cancel: cancel, normalizeCrlf: CrlfStyle.CrLf);

                //await ProcessHttpRequestMain(request, response, in_str, Consts.MimeTypes.TextUtf8, Consts.HttpStatusCodes.InternalServerError, true, paramsStrComparison: StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            await response._SendStringContentsAsync("Request Error: " + ex.ToString(), cancel: cancel, statusCode: Consts.HttpStatusCodes.InternalServerError, normalizeCrlf: CrlfStyle.Lf);
        }
    }

    string WebForm_GenerateApiResultPage(string methodName, JsonRpcCallResult callResults, string httpHostHeader, DateTimeOffset timeStamp, long tookTick)
    {
        StringWriter w = new StringWriter();
        var mi = this.Api.GetMethodInfo(methodName);

        var bomUtfData = callResults.ResultString._GetBytes_UTF8(bom: true);

        string title = $"{mi.Name}() API Returned OK. Returned {bomUtfData.Length._ToString3()} bytes JSON Result Data.";

        if (callResults.AllError && callResults.SingleErrorMessage._IsFilled())
        {
            title = $"{mi.Name}() API Returned An Error.";
        }

        WebForm_WriteHtmlHeader(w, title);


        var now = DtOffsetNow;

        string bomUtfDownloadFileName = $"JSON_{httpHostHeader}_{now._ToYymmddStr(yearTwoDigits: true)}_{now._ToHhmmssStr()}_{mi.Name}_API_Result"._MakeVerySafeAsciiOnlyNonSpaceFileName() + ".json";

        w.WriteLine(Str.GenerateHtmlDownloadJavaScript(bomUtfData, Consts.MimeTypes.Binary, bomUtfDownloadFileName));

        w.WriteLine(@"
    <div class='box'>
        <div class='content'>
");

        w.WriteLine($"<h2 class='title is-4'>" + title._EncodeHtml() + "</h2>");

        if (callResults.AllError)
        {
            w.WriteLine($@"
<article class='message is-danger'>
  <div class='message-body'>
    <strong>Error Message:</strong><BR>{callResults.SingleErrorMessage._EncodeHtml()}
  </div>
</article>");

            w.WriteLine($"<a class='button is-danger is-rounded' style='font-weight: bold' href='javascript:history.go(-1)'><i class='fas fa-arrow-circle-left'></i>&nbsp;Back to the Previous Web Form and Try Again</a>");
            w.WriteLine("<p>　</p>");
        }

        w.WriteLine($"<p><b>The JSON result data are as follows.</b> You may see the <a href='{this.RpcBaseAbsoluteUrlPath}#{mi.Name}' target='_blank'><b>API Reference Manual of the {mi.Name}() API</a></b>.</p>");

        w.WriteLine($"<a class='button is-primary' id='download' style='font-weight: bold' href='#' download='{bomUtfDownloadFileName}' onclick='handleDownload()'><i class='far fa-folder-open'></i>&nbsp;Download JSON Result Data ({bomUtfData.Length._ToString3()} bytes)</a>");

        if (mi.ParametersByIndex.Length == 0)
        {
            w.WriteLine($"<a class='button is-info' style='font-weight: bold' href='{this.WebFormBaseAbsoluteUrlPath}{mi.Name}/'><i class='fas fa-sync'></i>&nbsp;Refresh {mi.Name}() API Result</a>");
        }

        w.WriteLine($"<a class='button is-success' style='font-weight: bold' href='{this.WebFormBaseAbsoluteUrlPath}'><i class='fab fa-wpforms'></i>&nbsp;Return to Web Form API Index</a>");


        w.WriteLine($"<p><i>Timestamp: {timeStamp._ToDtStr(true)}, Took time: {TimeSpan.FromMilliseconds(tookTick)._ToTsStr(true)}.</i></p>");

        w.Write("<pre><code class='language-json'>");

        w.Write(callResults.ResultString._EncodeHtmlCodeBlock().ToString());

        w.WriteLine("</code></pre>");

        if (mi.ParametersByIndex.Length >= 1)
        {
            w.WriteLine($"<a class='button is-info' style='font-weight: bold' href='{this.WebFormBaseAbsoluteUrlPath}{mi.Name}/'><i class='fab fa-wpforms'></i>&nbsp;Open Another {mi.Name}() API Web Form</a>");
        }
        else
        {
            w.WriteLine($"<a class='button is-info' style='font-weight: bold' href='{this.WebFormBaseAbsoluteUrlPath}{mi.Name}/'><i class='fas fa-sync'></i>&nbsp;Refresh {mi.Name}() API Result</a>");
        }

        w.WriteLine($"<a class='button is-success' style='font-weight: bold' href='{this.WebFormBaseAbsoluteUrlPath}'><i class='fab fa-wpforms'></i>&nbsp;Return to Web Form API Index</a>");

        w.WriteLine("<p>　</p>");


        w.WriteLine("<hr />");

        w.WriteLine($"<p><b><a href='{this.WebFormBaseAbsoluteUrlPath}'><i class='fab fa-wpforms'></i> API Web Form Index</a></b> > <b><a href='{this.WebFormBaseAbsoluteUrlPath}{mi.Name}/'><i class='fab fa-wpforms'></i> {mi.Name}() API Web Form</a></b></p>");

        if (this.Config.PrintHelp)
        {
            w.WriteLine($"<p><b><a href='{this.RpcBaseAbsoluteUrlPath}'><i class='fas fa-book-open'></i> API Reference Document Index</a></b> > <b><a href='{this.RpcBaseAbsoluteUrlPath}#{mi.Name}'><i class='fas fa-book-open'></i> {mi.Name}() API Document</a></b></p>");
        }

        w.WriteLine(@"
        <p>　</p>
        <hr />
 ");

        w.WriteLine(@"
        </div>
    </div>
");

        WebForm_WriteHtmlFooter(w);

        return w.ToString();
    }

    string WebForm_GenerateApiInputFormPage(string methodName)
    {
        StringWriter w = new StringWriter();
        var mi = this.Api.GetMethodInfo(methodName);

        WebForm_WriteHtmlHeader(w, $"{mi.Name} - {this.Config.HelpServerFriendlyName._FilledOrDefault(Api.GetType().Name)} Web API Form");

        w.WriteLine($"<form action='{this.WebFormBaseAbsoluteUrlPath}{mi.Name}/' method='get'>");
        w.WriteLine($"<input name='_call' type='hidden' value='1'/>");

        w.WriteLine(@"
    <div class='box'>
        <div class='content'>
");

        w.WriteLine($"<h2 class='title is-4'>" + $"{this.Config.HelpServerFriendlyName._FilledOrDefault(Api.GetType().Name)} Web API Form - {mi.Name}() API" + "</h2>");

        w.WriteLine($"<h3 class='title is-5'>" + $"{mi.Name}() API: {mi.Description._EncodeHtml()}" + "</h3>");

        string requireAuthStr = "";

        if (mi.RequireAuth)
        {
            requireAuthStr = "<i class='fas fa-key'></i> ";
        }

        w.WriteLine($"<p><b><a href='{this.WebFormBaseAbsoluteUrlPath}'><i class='fab fa-wpforms'></i> API Web Form Index</a></b> > <b><a href='{this.WebFormBaseAbsoluteUrlPath}{mi.Name}/'><i class='fab fa-wpforms'></i> {requireAuthStr} {mi.Name}() API Web Form</a></b></p>");

        int index = 0;

        foreach (var p in mi.ParametersHelpList)
        {
            index++;

            string sampleStr = p.SampleValueOneLineStr._IsEmpty() ? "" : $"<p>Input Example: <code>{p.SampleValueOneLineStr._RemoveQuotation()._EncodeHtmlCodeBlock()}</code></p>";

            string defaultValueStr = p.DefaultValue._ObjectToJson(compact: true);
            if (p.DefaultValue == null) defaultValueStr = "";

            string controlStr = $@"
                    <div class='field'>
                        <p class='control'>
                            <input class='input is-info text-box single-line' name='{p.Name}' type='text' value='{defaultValueStr._RemoveQuotation()._EncodeHtml()}' />
                            {sampleStr}
<p>{p.Description._EncodeHtml()}</p>
                        </p>
                    </div>
";

            if (p.IsEnumType)
            {
                StringWriter optionsStr = new StringWriter();

                optionsStr.WriteLine($"<option value='__Not_Selected__' selected>▼ Select Item</option>");

                foreach (var kv in p.EnumValuesList!)
                {
                    optionsStr.WriteLine($"<option value='{kv.Key}'>{kv.Value}</option>");
                }

                controlStr = $@"
                <div class='field-body'>
                    <div class='field is-narrow'>
                        <p class='control'>
                                <select name='{p.Name}' class='input is-info'>
{optionsStr}
                                </select>
<p>{p.Description._EncodeHtml()}</p>
{sampleStr}
                        </p>
                    </div>
                </div>
";
            }

            w.WriteLine($@"
            <div class='field is-horizontal'>
                <div class='field-label is-normal'>
                    <label class='label'><i class='fas fa-keyboard'></i> #{index} {p.Name}:<BR>({p.TypeName})</label>
                </div>
                <div class='field-body'>
                    {controlStr}
                </div>
            </div>");
        }

        w.WriteLine($@"
            <div class='field is-horizontal'>
                <div class='field-label'>
                    <!-- Left empty for spacing -->
                </div>
                <div class='field-body'>
                    <div class='field'>
                        <div class='control'>
                            <input class='button is-link' type='submit' style='font-weight: bold' value='Call this API with Parameters'>
                            <p>　</p>
");


        if (this.Config.PrintHelp)
        {
            w.WriteLine("<p><b>You may see this API reference before calling the API.</b></p>");
            w.WriteLine($"<p><b><a href='{this.RpcBaseAbsoluteUrlPath}'><i class='fas fa-book-open'></i> API Reference Document Index</a></b> > <b><a href='{this.RpcBaseAbsoluteUrlPath}#{mi.Name}'><i class='fas fa-book-open'></i> {mi.Name}() API Document</a></b></p>");
        }

        w.WriteLine(@"
                        </div>
                    </div>
                </div>
            </div>");

        w.WriteLine("<p>　</p>");

        w.WriteLine(@"
        </div>
    </div>
");

        w.WriteLine("</form>");

        WebForm_WriteHtmlFooter(w);

        return w.ToString();
    }

    protected virtual void WebForm_WriteHtmlHeader(StringWriter w, string title)
    {
        string additionalStyle = @"
<style type='text/css'>
/* Please see documentation at https://docs.microsoft.com/aspnet/core/client-side/bundling-and-minification\ 
for details on configuring this project to bundle and minify static web assets. */
body {
    padding-top: 0px;
    padding-bottom: 0px;
}

/* Wrapping element */
/* Set some basic padding to keep content from hitting the edges */
.body-content {
    padding-left: 15px;
    padding-right: 15px;
}

/* Carousel */
.carousel-caption p {
    font-size: 20px;
    line-height: 1.4;
}

/* Make .svg files in the carousel display properly in older browsers */
.carousel-inner .item img[src$='.svg'] {
    width: 100%;
}

/* QR code generator */
#qrCode {
    margin: 15px;
}

/* Hide/rearrange for smaller screens */
@media screen and (max-width: 767px) {
    /* Hide captions */
    .carousel-caption {
        display: none;
    }
}

.footer {
    padding: 1rem 1rem 1rem;
}

pre[class*='language-'] {
    background: #f6f8fa;
}

/* Solve the conflict between Bulma and Prism */
code[class*='language-'] .tag,
code[class*='language-'] .number {
    align-items: stretch;
    background-color: transparent;
    border-radius: 0;
    display: inline;
    font-size: 1em;
    height: auto;
    justify-content: flex-start;
    line-height: normal;
    padding: 0;
    white-space: pre;
    margin-right: 0;
    min-width: auto;
    text-align: left;
    vertical-align: baseline;
}

code[class*=""language-""], pre[class*=""language-""] {
    white-space: pre-wrap !important;
    word-break: break-word !important;
}

</style>
";

        w.WriteLine($@"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8' />
    <meta name='viewport' content='width=device-width, initial-scale=1.0' />
    <title>{title._EncodeHtml()}</title>

    <link rel='stylesheet' href='https://cdnjs.cloudflare.com/ajax/libs/bulma/0.7.5/css/bulma.css' integrity='sha256-ujE/ZUB6CMZmyJSgQjXGCF4sRRneOimQplBVLu8OU5w=' crossorigin='anonymous' />
    <link rel='stylesheet' href='https://cdn.jsdelivr.net/npm/bulma-extensions@6.2.7/dist/css/bulma-extensions.min.css' integrity='sha256-RuPsE2zPsNWVhhvpOcFlMaZ1JrOYp2uxbFmOLBYtidc=' crossorigin='anonymous'>
    <link rel='stylesheet' href='https://cdnjs.cloudflare.com/ajax/libs/font-awesome/5.14.0/css/all.min.css' integrity='sha512-1PKOgIY59xJ8Co8+NE6FZ+LOAZKjy+KY8iq0G4B3CyeY6wYHN3yt9PW0XpSriVlkMXe40PTKnXrLnZ9+fkDaog==' crossorigin='anonymous' />
    <link rel='stylesheet' href='https://cdnjs.cloudflare.com/ajax/libs/prism/1.16.0/themes/prism.css' integrity='sha256-Vl2/8UdUJhoDlkCr9CEJmv77kiuh4yxMF7gP1OYe6EA=' crossorigin='anonymous' />
{additionalStyle}
</head>
<body>");
    }

    protected virtual void WebForm_WriteHtmlFooter(StringWriter w)
    {
        w.WriteLine(@"

    <script src='https://cdn.jsdelivr.net/npm/bulma-extensions@6.2.7/dist/js/bulma-extensions.js' integrity='sha256-02UMNoxzmxWzx36g1Y4tr93G0oHuz+khCNMBbilTBAg=' crossorigin='anonymous'></script>
    <script src='https://cdnjs.cloudflare.com/ajax/libs/vue/2.6.10/vue.js' integrity='sha256-ufGElb3TnOtzl5E4c/qQnZFGP+FYEZj5kbSEdJNrw0A=' crossorigin='anonymous'></script>
    <script src='https://cdn.jsdelivr.net/npm/buefy@0.7.10/dist/buefy.js' integrity='sha256-jzFK32XQpxIXsHY1dSlIXAerpfvTaKKBLrOGX76W9AU=' crossorigin='anonymous'></script>
    <script src='https://cdnjs.cloudflare.com/ajax/libs/axios/0.19.0/axios.js' integrity='sha256-XmdRbTre/3RulhYk/cOBUMpYlaAp2Rpo/s556u0OIKk=' crossorigin='anonymous'></script>
    <script src='https://cdnjs.cloudflare.com/ajax/libs/lodash.js/4.17.15/lodash.js' integrity='sha256-kzv+r6dLqmz7iYuR2OdwUgl4X5RVsoENBzigdF5cxtU=' crossorigin='anonymous'></script>
    <script src='https://cdnjs.cloudflare.com/ajax/libs/moment.js/2.24.0/moment.js' integrity='sha256-H9jAz//QLkDOy/nzE9G4aYijQtkLt9FvGmdUTwBk6gs=' crossorigin='anonymous'></script>
    <script src='https://cdnjs.cloudflare.com/ajax/libs/prism/1.16.0/prism.js' integrity='sha256-DIDyRYnz+KgK09kOQq3WVsIv1dcMpTZyuWimu3JMCj8=' crossorigin='anonymous'></script>
    <script src='https://cdnjs.cloudflare.com/ajax/libs/prism/1.16.0/components/prism-json.js' integrity='sha256-xRwOqwAdoQU7w2xCV4YQpPH5TaJMEMtzHExglTQTUZ4=' crossorigin='anonymous'></script>
    <script src='https://cdnjs.cloudflare.com/ajax/libs/prism/1.16.0/components/prism-bash.js' integrity='sha256-R8B0GSG+S1gSO1tPkcl2Y+s3qryWdPQDHOy4JxZtuJI=' crossorigin='anonymous'></script>
</body>
</html>
");
    }

    // /rpc の GET ハンドラ
    public virtual async Task Rpc_GetRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData)
    {
        CancellationToken cancel = request._GetRequestCancellationToken();

        try
        {
            string rpcMethod = routeData.Values._GetStr("rpc_method");
            if (rpcMethod._IsEmpty())
            {
                var baseUri = request.GetEncodedUrl()._ParseUrl()._CombineUrl(this.RpcBaseAbsoluteUrlPath);

                if (this.Config.PrintHelp)
                {
                    await response._SendStringContentsAsync(Rpc_GenerateHtmlHelpString(baseUri), contentsType: Consts.MimeTypes.HtmlUtf8, cancel: cancel, normalizeCrlf: CrlfStyle.CrLf);
                }
                else
                {
                    await response._SendStringContentsAsync($"This is a JSON-RPC server.\r\nAPI: {Api.GetType().AssemblyQualifiedName}\r\nNow: {DateTimeOffset.Now._ToDtStr(withNanoSecs: true)}\r\n", cancel: cancel, normalizeCrlf: CrlfStyle.Lf);
                }
            }
            else
            {
                string args = routeData.Values._GetStr("rpc_param");

                if (args._IsEmpty())
                {
                    JObject jObj = new JObject();

                    foreach (string key in request.Query.Keys)
                    {
                        string value = request.Query[key];

                        string valueTrim = value.Trim();
                        if (valueTrim.StartsWith("{") && valueTrim.EndsWith("}"))
                        {
                            // JSON Data
                            jObj.Add(key, value._JsonToObject<JObject>());
                        }
                        else
                        {
                            // Primitive Date
                            jObj.Add(key, JToken.FromObject(value));
                        }
                    }

                    args = jObj._ObjectToJson(compact: true);
                }

                //string id = "GET-" + Str.NewGuid().ToUpperInvariant();
                string in_str = "{'jsonrpc':'2.0','method':'" + rpcMethod + "','params':" + args + "}";

                await ProcessHttpRequestMain(request, response, in_str, Consts.MimeTypes.TextUtf8, Consts.HttpStatusCodes.InternalServerError, true, paramsStrComparison: StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            await response._SendStringContentsAsync("Request Error: " + ex.ToString(), cancel: cancel, statusCode: Consts.HttpStatusCodes.InternalServerError, normalizeCrlf: CrlfStyle.Lf);
        }
    }

    // /rpc の POST ハンドラ
    public virtual async Task Rpc_PostRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData)
    {
        try
        {
            string in_str = await request._RecvStringContentsAsync(this.Config.MaxRequestBodyLen, cancel: request._GetRequestCancellationToken());

            await ProcessHttpRequestMain(request, response, in_str);
        }
        catch (Exception ex)
        {
            await response._SendStringContentsAsync(ex.ToString(), cancel: request._GetRequestCancellationToken(), normalizeCrlf: CrlfStyle.Lf);
        }
    }

    protected virtual async Task ProcessHttpRequestMain(HttpRequest request, HttpResponse response, string inStr, string responseContentsType = Consts.MimeTypes.Json, int httpStatusWhenError = Consts.HttpStatusCodes.Ok, bool simpleResultWhenOk = false, StringComparison paramsStrComparison = StringComparison.Ordinal)
    {
        int statusCode = Consts.HttpStatusCodes.Ok;

        string retStr = "";
        try
        {
            SortedDictionary<string, string> requestHeaders = new SortedDictionary<string, string>();
            foreach (string headerName in request.Headers.Keys)
            {
                if (request.Headers.TryGetValue(headerName, out var val))
                {
                    requestHeaders.Add(headerName, val.ToString());
                }
            }

            string suppliedUsername = "";
            string suppliedPassword = "";

            // ヘッダにおける認証クレデンシャルが提供されているかどうか調べる
            suppliedUsername = request.Headers._GetStrFirst("X-RPC-Auth-Username");
            suppliedPassword = request.Headers._GetStrFirst("X-RPC-Auth-Password");

            // Query String における認証クレデンシャルが提供されているかどうか調べる
            if (suppliedUsername._IsEmpty() || suppliedPassword._IsEmpty())
            {
                suppliedUsername = request._GetQueryStringFirst("rpc_auth_username");
                suppliedPassword = request._GetQueryStringFirst("rpc_auth_password");
            }

            if (suppliedUsername._IsEmpty() || suppliedPassword._IsEmpty())
            {
                // Basic 認証または Query String における認証クレデンシャルが提供されているかどうか調べる
                var basicAuthHeader = BasicAuthImpl.ParseBasicAuthenticationHeader(request.Headers._GetStrFirst("Authorization"));
                if (basicAuthHeader != null)
                {
                    suppliedUsername = basicAuthHeader.Item1;
                    suppliedPassword = basicAuthHeader.Item2;
                }
            }

            var conn = request.HttpContext.Connection;

            JsonRpcClientInfo client_info = new JsonRpcClientInfo(
                request.Scheme,
                request.Host.ToString(),
                request.GetDisplayUrl(),
                conn.LocalIpAddress!._UnmapIPv4().ToString(), conn.LocalPort,
                conn.RemoteIpAddress!._UnmapIPv4().ToString(), conn.RemotePort,
                requestHeaders,
                suppliedUsername,
                suppliedPassword);

            var callResults = await this.CallMethods(inStr, client_info, simpleResultWhenOk, paramsStrComparison);

            if (HideErrorDetails) callResults.RemoveErrorDetailsFromResultString();

            retStr = callResults.ResultString;

            if (callResults.Error_AuthRequired)
            {
                // Basic 認証の要求
                KeyValueList<string, string> basicAuthResponseHeaders = new KeyValueList<string, string>();
                string realm = $"User Authentication for {this.RpcBaseAbsoluteUrlPath}";
                if (callResults.Error_AuthRequiredRealmName._IsFilled())
                {
                    realm += $" ({realm._MakeVerySafeAsciiOnlyNonSpaceFileName()})";
                }
                basicAuthResponseHeaders.Add(Consts.HttpHeaders.WWWAuthenticate, $"Basic realm=\"{realm}\"");

                await using var basicAuthRequireResult = new HttpStringResult(retStr, contentType: Consts.MimeTypes.TextUtf8, statusCode: Consts.HttpStatusCodes.Unauthorized, additionalHeaders: basicAuthResponseHeaders);

                await response._SendHttpResultAsync(basicAuthRequireResult, cancel: request._GetRequestCancellationToken());

                return;
            }

            if (callResults.AllError)
            {
                statusCode = httpStatusWhenError;
            }
        }
        catch (Exception ex)
        {
            JsonRpcException json_ex;
            if (ex is JsonRpcException) json_ex = (JsonRpcException)ex;
            else json_ex = new JsonRpcException(new JsonRpcError(1234, ex._GetSingleException().Message, ex.ToString()));

            retStr = new JsonRpcResponseError()
            {
                Error = json_ex.RpcError,
                Id = null,
                Result = null,
            }._ObjectToJson();

            statusCode = httpStatusWhenError;
        }

        await response._SendStringContentsAsync(retStr, responseContentsType, cancel: request._GetRequestCancellationToken(), statusCode: statusCode, normalizeCrlf: CrlfStyle.Lf);
    }

    public void RegisterRoutesToHttpServer(IApplicationBuilder appBuilder, string rpcPath = "/rpc", string webFormPath = "/webform", string configFormPath = "/configform", string objEditPath = "/objedit")
    {
        rpcPath = rpcPath._NonNullTrim();
        if (rpcPath.StartsWith("/") == false) throw new CoresLibException($"Invalid absolute path: '{rpcPath}'");
        if (rpcPath.Length >= 2 && rpcPath.EndsWith("/")) throw new CoresLibException($"Path must not end with '/'.");

        webFormPath = webFormPath._NonNullTrim();
        if (webFormPath.StartsWith("/") == false) throw new CoresLibException($"Invalid absolute path: '{webFormPath}'");
        if (webFormPath.Length >= 2 && webFormPath.EndsWith("/")) throw new CoresLibException($"Path must not end with '/'.");

        configFormPath = configFormPath._NonNullTrim();
        if (configFormPath.StartsWith("/") == false) throw new CoresLibException($"Invalid absolute path: '{configFormPath}'");
        if (configFormPath.Length >= 2 && configFormPath.EndsWith("/")) throw new CoresLibException($"Path must not end with '/'.");

        objEditPath = objEditPath._NonNullTrim();
        if (objEditPath.StartsWith("/") == false) throw new CoresLibException($"Invalid absolute path: '{objEditPath}'");
        if (objEditPath.Length >= 2 && objEditPath.EndsWith("/")) throw new CoresLibException($"Path must not end with '/'.");

        RouteBuilder rb = new RouteBuilder(appBuilder);

        rb.MapGet(rpcPath, Rpc_GetRequestHandler);
        rb.MapGet(rpcPath + "/{rpc_method}", Rpc_GetRequestHandler);
        rb.MapGet(rpcPath + "/{rpc_method}/{rpc_param}", Rpc_GetRequestHandler);
        rb.MapPost(rpcPath, Rpc_PostRequestHandler);

        rb.MapGet(webFormPath, WebForm_GetRequestHandler);
        rb.MapGet(webFormPath + "/{rpc_method}", WebForm_GetRequestHandler);

        rb.MapGet(configFormPath, ConfigForm_GetRequestHandler);
        rb.MapPost(configFormPath, ConfigForm_PostRequestHandler);

        rb.MapGet(configFormPath + "/{rpc_method}", WebForm_GetRequestHandler);

        IRouter router = rb.Build();
        appBuilder.UseRouter(router);

        this.RpcBaseAbsoluteUrlPath = rpcPath;
        if (this.RpcBaseAbsoluteUrlPath.EndsWith("/") == false) this.RpcBaseAbsoluteUrlPath += "/";

        this.WebFormBaseAbsoluteUrlPath = webFormPath;
        if (this.WebFormBaseAbsoluteUrlPath.EndsWith("/") == false) this.WebFormBaseAbsoluteUrlPath += "/";

        this.ConfigFormBaseAbsoluteUrlPath = configFormPath;
        if (this.ConfigFormBaseAbsoluteUrlPath.EndsWith("/") == false) this.ConfigFormBaseAbsoluteUrlPath += "/";

        this.ObjEditBaseAbsoluteUrlPath = objEditPath;
        if (this.ObjEditBaseAbsoluteUrlPath.EndsWith("/") == false) this.ObjEditBaseAbsoluteUrlPath += "/";
    }



    // Web Form ヘルプ文字列を生成する
    readonly FastCache<Uri, string> webFormHelpStringCache = new FastCache<Uri, string>();
    string WebForm_GenerateHtmlHelpString(Uri webFormBaseUri)
    {
        return webFormHelpStringCache.GetOrCreate(webFormBaseUri, x => WebForm_GenerateHtmlHelpStringCore(x))!;
    }
    string WebForm_GenerateHtmlHelpStringCore(Uri webFormBaseUri)
    {
        var methodList = this.Api.EnumMethodsForHelp();

        StringWriter w = new StringWriter();

        int methodIndex = 0;


        WebForm_WriteHtmlHeader(w, $"{this.Config.HelpServerFriendlyName._FilledOrDefault(Api.GetType().Name)} - JSON-RPC Server API Web Form Index");

        w.WriteLine($@"
    <div class='container is-fluid'>

<div class='box'>
    <div class='content'>
<h2 class='title is-4'>{this.Config.HelpServerFriendlyName._FilledOrDefault(Api.GetType().Name)} - JSON-RPC Server API Web Form Index</h2>
        
<h4 class='title is-5'>List of all {methodList.Count} API Web Forms:</h4>
");

        w.WriteLine("<ul>");

        foreach (var m in methodList)
        {
            methodIndex++;

            string requireAuthStr = "";

            if (m.RequireAuth)
            {
                requireAuthStr = "<i class='fas fa-key'></i> ";
            }

            string titleStr = $"<a href='{this.WebFormBaseAbsoluteUrlPath}{m.Name}/'><b><i class='fab fa-wpforms'></i> {requireAuthStr}API Web Form #{methodIndex}: {m.Name}() API</b></a>{(m.Description._IsFilled() ? " <BR>" : "")} <b>{m.Description._EncodeHtml()}</b>".Trim();

            //w.WriteLine();
            //w.WriteLine($"- {helpStr}");
            w.WriteLine($"<li>{titleStr}<BR><BR></li>");
        }

        w.WriteLine("</ul>");

        w.WriteLine();
        w.WriteLine();
        w.WriteLine();

        w.WriteLine(@"
        <p>　</p>
    </div>
");

        this.WebForm_WriteHtmlFooter(w);

        return w.ToString();
    }



    // RPC API ヘルプ文字列を生成する
    readonly FastCache<Uri, string> rpcHelpStringCache = new FastCache<Uri, string>();
    string Rpc_GenerateHtmlHelpString(Uri rpcBaseUri)
    {
        return rpcHelpStringCache.GetOrCreate(rpcBaseUri, x => Rpc_GenerateHtmlHelpStringCore(x))!;
    }
    string Rpc_GenerateHtmlHelpStringCore(Uri rpcBaseUri)
    {
        var methodList = this.Api.EnumMethodsForHelp();

        StringWriter w = new StringWriter();

        int methodIndex = 0;


        WebForm_WriteHtmlHeader(w, $"{this.Config.HelpServerFriendlyName._FilledOrDefault(Api.GetType().Name)} - JSON-RPC Server API Reference Document Index");

        w.WriteLine($@"
    <div class='container is-fluid'>

<div class='box'>
    <div class='content'>
<h2 class='title is-4'>{this.Config.HelpServerFriendlyName._FilledOrDefault(Api.GetType().Name)} - JSON-RPC Server API Reference Document Index</h2>
        
<h4 class='title is-5'>List of all {methodList.Count} RPC-API Methods:</h4>
");

        w.WriteLine("<ul>");

        foreach (var m in methodList)
        {
            methodIndex++;

            string titleStr = $"<a href='#{m.Name}'><b>RPC Method #{methodIndex}: {m.Name}() API</b></a>{(m.Description._IsFilled() ? "<BR>" : "")} <b>{m.Description._EncodeHtml()}</b>".Trim();

            //w.WriteLine();
            //w.WriteLine($"- {helpStr}");
            w.WriteLine($"<li>{titleStr}<BR><BR></li>");
        }

        w.WriteLine("</ul>");

        w.WriteLine();
        w.WriteLine();
        w.WriteLine();

        methodIndex = 0;

        foreach (var m in methodList)
        {
            var mi = m;

            methodIndex++;

            w.WriteLine($"<hr id={m.Name}>");

            string titleStr = $"RPC Method #{methodIndex}: {m.Name}() API{(m.Description._IsFilled() ? ":" : "")} {m.Description._EncodeHtml()}".Trim();

            w.WriteLine($"<h4 class='title is-5'>{titleStr}</h4>");

            string requireAuthStr = "";

            if (mi.RequireAuth)
            {
                requireAuthStr = "<i class='fas fa-key'></i> ";
            }

            w.WriteLine($"<a class='button is-info' style='font-weight: bold' href='{this.WebFormBaseAbsoluteUrlPath}{mi.Name}/'><i class='fab fa-wpforms'></i>&nbsp;{requireAuthStr}&nbsp;Call this {mi.Name}() API with Web Form</a>");

            w.WriteLine();
            w.WriteLine("<p>　</p>");
            w.WriteLine($"<p>RPC Method Name: <b><code class='language-shell'>{m.Name}()</code></b></p>");
            w.WriteLine($"<p>RPC Definition: <b><code class='language-shell'>{m.Name}({(m.ParametersHelpList.Select(x => x.Name + ": " + x.TypeName + (x.Mandatory ? "" : " = " + x.DefaultValue._ObjectToJson(compact: true)))._Combine(", "))}): {m.RetValueTypeName};</code></b></p>");
            w.WriteLine();

            if (m.Description._IsFilled())
            {
                w.WriteLine($"<p>RPC Description: <b>{m.Description._EncodeHtml()}</b></p>");
                w.WriteLine();
            }

            w.WriteLine($"<p>RPC Authenication Required: {m.RequireAuth._ToBoolYesNoStr()}</p>");
            w.WriteLine();

            var pl = m.ParametersHelpList;

            var sampleRequestJsonData = Json.NewJsonObject();

            QueryStringList qsList = new QueryStringList();

            if (pl.Count == 0)
            {
                w.WriteLine($"<p>No RPC Input Parameters.</p>");
                w.WriteLine();
            }
            else
            {
                w.WriteLine($"<p>RPC Input: {pl.Count} Parameters</p>");

                for (int i = 0; i < pl.Count; i++)
                {
                    var pp = pl[i];
                    string? qsSampleOrDefaultValue = null;

                    w.WriteLine("<p>" + $"Parameter #{i + 1}: <b><code class='language-shell'>{pp.Name}</code></b>".TrimEnd() + "</p>");
                    w.WriteLine("<ul>");
                    if (pp.Description._IsFilled())
                    {
                        w.WriteLine($"<li>Description: {pp.Description._EncodeHtml()}</li>");
                    }
                    if (pp.IsPrimitiveType)
                    {
                        if (pp.IsEnumType == false)
                        {
                            w.WriteLine($"<li>Input Data Type: <code class='language-shell'>Primitive Value - {pp.TypeName}</code></li>");
                        }
                        else
                        {
                            StringWriter enumWriter = new StringWriter();
                            enumWriter.WriteLine($"enum {pp.Type.Name} {{");
                            foreach (var enumItem in Str.GetEnumValuesList(pp.Type).OrderBy(x => x.Value))
                            {
                                enumWriter.WriteLine($"    {enumItem.Key}: {Convert.ToUInt64(enumItem.Value)},");
                            }
                            enumWriter.WriteLine("}");
                            w.WriteLine($"<li>Input Data Type: Enumeration Value - {pp.TypeName}<BR>");
                            w.Write($"<pre><code class='language-json'>{enumWriter.ToString()._EncodeHtmlCodeBlock()}</code></pre>");
                            w.WriteLine($"</li>");
                        }
                    }
                    else
                    {
                        w.WriteLine($"<li>Input Data Type: JSON Data - <code class='language-shell'>{pp.TypeName}</code></li>");
                    }

                    if (pp.SampleValueOneLineStr._IsFilled())
                    {
                        if (pp.IsPrimitiveType)
                        {
                            w.WriteLine($"<li>Input Data Example: <code>{pp.SampleValueOneLineStr._EncodeHtmlCodeBlock()}</code></li>");
                        }
                        else
                        {
                            w.WriteLine($"<li>Input Data Example (JSON):<BR>");
                            w.Write($"<pre><code class='language-json'>{pp.SampleValueMultiLineStr._EncodeHtmlCodeBlock()}</code></pre>");
                            w.WriteLine($"</li>");
                        }
                        qsSampleOrDefaultValue = pp.SampleValueOneLineStr;
                    }
                    w.WriteLine($"<li>Mandatory: {pp.Mandatory._ToBoolYesNoStr()}</li>");
                    if (pp.Mandatory == false)
                    {
                        w.WriteLine($"<li>Default Value: <code>{pp.DefaultValue._ObjectToJson(includeNull: true, compact: true)._EncodeHtml()}</code></li>");
                        if (qsSampleOrDefaultValue == null)
                        {
                            qsSampleOrDefaultValue = pp.DefaultValue._ObjectToJson(includeNull: true, compact: true);
                        }
                    }

                    if (qsSampleOrDefaultValue == null)
                    {
                        if (pp.IsPrimitiveType)
                        {
                            qsSampleOrDefaultValue = $"value_{i + 1}";
                        }
                        else
                        {
                            qsSampleOrDefaultValue = "{JSON_Input_Value_for_No_{i + 1}_Here}";
                        }
                    }

                    // Query String の値は文字列であっても "" で囲む必要がない
                    if (qsSampleOrDefaultValue.Length >= 2 && qsSampleOrDefaultValue.StartsWith("\"") && qsSampleOrDefaultValue.EndsWith("\""))
                    {
                        qsSampleOrDefaultValue = qsSampleOrDefaultValue.Substring(1, qsSampleOrDefaultValue.Length - 2);
                    }

                    qsList.Add(pp.Name, qsSampleOrDefaultValue);

                    JsonSerializerSettings settings = new JsonSerializerSettings();
                    Json.AddStandardSettingsToJsonConverter(settings, JsonFlags.AllEnumToStr);
                    JsonSerializer serializer = JsonSerializer.Create(settings);

                    sampleRequestJsonData.TryAdd(pp.Name, JToken.FromObject(pp.SampleValueObject, serializer));

                    w.WriteLine("</ul>");
                    w.WriteLine();
                }

            }

            if (m.HasRetValue == false)
            {
                w.WriteLine("<p>No RPC Result Value.</p>");
                w.WriteLine();
            }
            else
            {
                w.WriteLine("<p><b>RPC Result Value:</b></p>");
                if (m.IsRetValuePrimitiveType)
                {
                    w.WriteLine($"<p>Output Data Type: Primitive Value - {m.RetValueTypeName}</p>");
                }
                else
                {
                    w.WriteLine($"<p>Output Data Type: JSON Data - {m.RetValueTypeName}</p>");
                }

                if (m.RetValueSampleValueJsonMultilineStr._IsFilled())
                {
                    w.WriteLine("<p>Sample Result Output Data:</p>");

                    w.Write($"<pre><code class='language-json'>{m.RetValueSampleValueJsonMultilineStr._EncodeHtmlCodeBlock()}</code></pre>");
                }

                w.WriteLine("");
            }

            w.WriteLine();

            var urlEncodeParam = new UrlEncodeParam("\"'{}:=,");

            if (m.RequireAuth == false)
            {
                string urlDir = rpcBaseUri._CombineUrlDir(m.Name).ToString();
                if (qsList.Any())
                {
                    urlDir += "?" + qsList.ToString(null, urlEncodeParam);
                }

                w.WriteLine("<p>　</p>");
                w.WriteLine($"<h5 class='title is-6'>RPC Call Sample: HTTP GET with Query String Sample</h5>");
                //w.WriteLine("--- RPC Call Sample: HTTP GET with Query String Sample ---");

                StringWriter tmp = new StringWriter();
                tmp.WriteLine($"# HTTP GET Target URL (You can test it with your web browser easily):");
                tmp.WriteLine($"{urlDir}");
                tmp.WriteLine();
                tmp.WriteLine($"# wget command line sample on Linux bash (retcode = 0 when successful):");
                tmp.WriteLine($"wget --content-on-error --no-verbose -O - --no-check-certificate {urlDir._EscapeBashArg()}");
                tmp.WriteLine();
                tmp.WriteLine($"# curl command line sample on Linux bash (retcode = 0 when successful):");
                tmp.WriteLine($"curl --get --globoff --fail -k --raw --verbose {urlDir._EscapeBashArg()}");
                tmp.WriteLine();

                w.WriteLine($"<pre><code class='language-shell'>{tmp.ToString()._EncodeHtmlCodeBlock()}</code></pre>");

                JsonRpcRequestForHelp reqSample = new JsonRpcRequestForHelp
                {
                    Version = "2.0",
                    Method = m.Name,
                    Params = sampleRequestJsonData,
                };

                w.WriteLine($"<h5 class='title is-6'>RPC Call Sample: HTTP POST with JSON-RPC Sample:</h5>");

                tmp = new StringWriter();
                tmp.WriteLine($"# HTTP POST Target URL:");
                tmp.WriteLine($"{rpcBaseUri}");
                tmp.WriteLine();
                tmp.WriteLine($"# curl JSON-RPC call command line sample on Linux bash:");
                tmp.WriteLine($"cat <<\\EOF | curl --request POST --globoff --fail -k --raw --verbose --data @- '{rpcBaseUri}'");
                tmp.Write(reqSample._ObjectToJson(includeNull: true, compact: false)._NormalizeCrlf(ensureLastLineCrlf: true));
                tmp.WriteLine("EOF");
                tmp.WriteLine();

                w.WriteLine($"<pre><code class='language-shell'>{tmp.ToString()._EncodeHtmlCodeBlock()}</code></pre>");

                //w.WriteLine("# JSON-RPC call response sample (when successful):");
                w.WriteLine($"<h5 class='title is-6'>JSON-RPC call response sample (when successful):</h5>");
                var okSample = new JsonRpcResponseForHelp_Ok
                {
                    Version = "2.0",
                    Result = m.RetValueSampleValueObject,
                };
                //w.WriteLine($"--------------------");
                //w.Write(okSample._ObjectToJson(includeNull: true, compact: false)._NormalizeCrlf(ensureLastLineCrlf: true));
                w.Write($"<pre><code class='language-json'>{okSample._ObjectToJson(includeNull: true, compact: false)._NormalizeCrlf(ensureLastLineCrlf: true)._EncodeHtmlCodeBlock()}</code></pre>");
                //w.WriteLine($"--------------------");
                w.WriteLine();

                w.WriteLine($"<h5 class='title is-6'>JSON-RPC call response sample (when error):</h5>");
                //w.WriteLine("# JSON-RPC call response sample (when error):");
                var errorSample = new JsonRpcResponseForHelp_Error
                {
                    Version = "2.0",
                    Error = new JsonRpcError
                    {
                        Code = -32603,
                        Message = "Sample Error",
                        Data = "Sample Error Detail Data\nThis is a sample error data.",
                    },
                };
                w.Write($"<pre><code class='language-json'>{errorSample._ObjectToJson(includeNull: true, compact: false)._NormalizeCrlf(ensureLastLineCrlf: true)._EncodeHtmlCodeBlock()}</code></pre>");
                //w.WriteLine($"--------------------");
                //w.Write(errorSample._ObjectToJson(includeNull: true, compact: false)._NormalizeCrlf(ensureLastLineCrlf: true));
                //w.WriteLine($"--------------------");
                w.WriteLine();
            }
            else
            {
                string urlSimple = rpcBaseUri._CombineUrlDir(m.Name).ToString();
                if (qsList.Any())
                {
                    urlSimple += "?" + qsList.ToString(null, urlEncodeParam);
                }

                string urlWithBasicAuth = rpcBaseUri._CombineUrlDir(m.Name).ToString();
                if (qsList.Any())
                {
                    urlWithBasicAuth += "?" + qsList.ToString(null, urlEncodeParam);
                }
                urlWithBasicAuth = urlWithBasicAuth._AddCredentialOnUrl(Consts.Strings.DefaultAdminUsername, Consts.Strings.DefaultAdminPassword);

                string urlWithQsAuth = rpcBaseUri._CombineUrlDir(m.Name).ToString();
                urlWithQsAuth += $"?rpc_auth_username={Consts.Strings.DefaultAdminUsername}&rpc_auth_password={Consts.Strings.DefaultAdminPassword}";
                if (qsList.Any())
                {
                    urlWithQsAuth += "&" + qsList.ToString(null, urlEncodeParam);
                }

                //w.WriteLine("--- RPC Call Sample: HTTP GET with Query String Sample ---");
                w.WriteLine("<p>　</p>");
                w.WriteLine($"<h5 class='title is-6'>RPC Call Sample: HTTP GET with Query String Sample</h5>");

                StringWriter tmp = new StringWriter();
                tmp.WriteLine($"# HTTP GET Target URL (You can test it with your web browser easily):");
                tmp.WriteLine("# (with HTTP Basic Authentication)");
                tmp.WriteLine($"{urlWithBasicAuth}");
                tmp.WriteLine("# (with HTTP Query String Authentication)");
                tmp.WriteLine($"{urlWithQsAuth}");
                tmp.WriteLine();
                tmp.WriteLine($"# wget command line sample on Linux bash (retcode = 0 when successful):");
                tmp.WriteLine("# (wget with HTTP Basic Authentication)");
                tmp.WriteLine($"wget --content-on-error --no-verbose -O - --no-check-certificate {urlWithBasicAuth._EscapeBashArg()}");
                tmp.WriteLine("# (wget with HTTP Query String Authentication)");
                tmp.WriteLine($"wget --content-on-error --no-verbose -O - --no-check-certificate {urlWithQsAuth._EscapeBashArg()}");
                tmp.WriteLine("# (wget with HTTP Header Authentication)");
                tmp.WriteLine($"wget --content-on-error --no-verbose -O - --no-check-certificate --header 'X-RPC-Auth-Username: {Consts.Strings.DefaultAdminUsername}' --header 'X-RPC-Auth-Password: {Consts.Strings.DefaultAdminPassword}' {urlSimple._EscapeBashArg()}");
                tmp.WriteLine();
                tmp.WriteLine($"# curl command line sample on Linux bash (retcode = 0 when successful):");
                tmp.WriteLine("# (curl with HTTP Basic Authentication)");
                tmp.WriteLine($"curl --get --globoff --fail -k --raw --verbose {urlWithBasicAuth._EscapeBashArg()}");
                tmp.WriteLine("# (curl with HTTP Query String Authentication)");
                tmp.WriteLine($"curl --get --globoff --fail -k --raw --verbose {urlWithQsAuth._EscapeBashArg()}");
                tmp.WriteLine("# (curl with HTTP Header Authentication)");
                tmp.WriteLine($"curl --get --globoff --fail -k --raw --verbose --header 'X-RPC-Auth-Username: {Consts.Strings.DefaultAdminUsername}' --header 'X-RPC-Auth-Password: {Consts.Strings.DefaultAdminPassword}' {urlSimple._EscapeBashArg()}");
                tmp.WriteLine();

                w.WriteLine($"<pre><code class='language-shell'>{tmp.ToString()._EncodeHtmlCodeBlock()}</code></pre>");

                JsonRpcRequestForHelp reqSample = new JsonRpcRequestForHelp
                {
                    Version = "2.0",
                    Method = m.Name,
                    Params = sampleRequestJsonData,
                };

                //w.WriteLine("--- RPC Call Sample: HTTP POST with JSON-RPC Sample ---");
                w.WriteLine($"<h5 class='title is-6'>RPC Call Sample: HTTP POST with JSON-RPC Sample:</h5>");

                tmp = new StringWriter();
                tmp.WriteLine($"# HTTP POST Target URL (You can test it with your web browser easily):");
                tmp.WriteLine($"{urlWithBasicAuth}");
                tmp.WriteLine();
                tmp.WriteLine($"# curl JSON-RPC call command line sample on Linux bash:");
                tmp.WriteLine($"cat <<\\EOF | curl --request POST --globoff --fail -k --raw --verbose --data @- '{rpcBaseUri}'");
                tmp.Write(reqSample._ObjectToJson(includeNull: true, compact: false)._NormalizeCrlf(ensureLastLineCrlf: true));
                tmp.WriteLine("EOF");
                tmp.WriteLine();

                w.WriteLine($"<pre><code class='language-shell'>{tmp.ToString()._EncodeHtmlCodeBlock()}</code></pre>");

                //w.WriteLine("# JSON-RPC call response sample (when successful):");
                w.WriteLine($"<h5 class='title is-6'>JSON-RPC call response sample (when successful):</h5>");
                var okSample = new JsonRpcResponseForHelp_Ok
                {
                    Version = "2.0",
                    Result = m.RetValueSampleValueObject,
                };
                //w.WriteLine($"--------------------");
                //w.Write(okSample._ObjectToJson(includeNull: true, compact: false)._NormalizeCrlf(ensureLastLineCrlf: true));
                w.Write($"<pre><code class='language-json'>{okSample._ObjectToJson(includeNull: true, compact: false)._NormalizeCrlf(ensureLastLineCrlf: true)._EncodeHtmlCodeBlock()}</code></pre>");
                //w.WriteLine($"--------------------");
                w.WriteLine();

                w.WriteLine($"<h5 class='title is-6'>JSON-RPC call response sample (when error):</h5>");
                var errorSample = new JsonRpcResponseForHelp_Error
                {
                    Version = "2.0",
                    Error = new JsonRpcError
                    {
                        Code = -32603,
                        Message = "Sample Error",
                        Data = "Sample Error Detail Data\nThis is a sample error data.",
                    },
                };
                //w.WriteLine($"--------------------");
                //w.Write(errorSample._ObjectToJson(includeNull: true, compact: false)._NormalizeCrlf(ensureLastLineCrlf: true));
                //w.WriteLine($"--------------------");
                w.Write($"<pre><code class='language-json'>{errorSample._ObjectToJson(includeNull: true, compact: false)._NormalizeCrlf(ensureLastLineCrlf: true)._EncodeHtmlCodeBlock()}</code></pre>");
                w.WriteLine();
            }

            w.WriteLine($"<a href='#'><b><i class='fas fa-arrow-up'></i> Return to the API Index</b></a>");

            w.WriteLine();
            w.WriteLine();
            w.WriteLine();
        }

        w.WriteLine(@"
        <p>　</p>
    </div>
");

        this.WebForm_WriteHtmlFooter(w);

        return w.ToString();
    }
}

public class JsonRpcHttpServerBuilder : HttpServerStartupBase
{
    public JsonRpcHttpServer JsonServer { get; }

    public JsonRpcHttpServerBuilder(IConfiguration configuration) : base(configuration)
    {
        (JsonRpcServerConfig rpcCfg, JsonRpcServerApi api) p = ((JsonRpcServerConfig rpcCfg, JsonRpcServerApi api))this.Param!;

        JsonServer = new JsonRpcHttpServer(p.api, p.rpcCfg);
    }

    public static HttpServer<JsonRpcHttpServerBuilder> StartServer(HttpServerOptions httpCfg, JsonRpcServerConfig rpcServerCfg, JsonRpcServerApi rpcApi, CancellationToken cancel = default)
        => new HttpServer<JsonRpcHttpServerBuilder>(httpCfg, (rpcServerCfg, rpcApi), cancel);

    protected override void ConfigureImpl_BeforeHelper(HttpServerStartupConfig cfg, IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime)
    {
    }

    protected override void ConfigureImpl_AfterHelper(HttpServerStartupConfig cfg, IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime)
    {
        this.JsonServer.RegisterRoutesToHttpServer(app);
    }
}

#endif // CORES_BASIC_WEBAPP

