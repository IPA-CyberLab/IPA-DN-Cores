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

#if CORES_BASIC_HTTPSERVER
// ASP.NET Core 3.0 用の型名を無理やり ASP.NET Core 2.2 でコンパイルするための型エイリアスの設定
using IWebHostEnvironment = Microsoft.AspNetCore.Hosting.IHostingEnvironment;
using IHostApplicationLifetime = Microsoft.AspNetCore.Hosting.IApplicationLifetime;
#endif

namespace IPA.Cores.Basic;

public class JsonRpcHttpServer : JsonRpcServer
{
    public string RpcBaseAbsoluteUrlPath = ""; // "/rpc/" のような絶対パス。末尾に / を含む。

    public JsonRpcHttpServer(JsonRpcServerApi api, JsonRpcServerConfig? cfg = null) : base(api, cfg) { }

    public virtual async Task GetRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData)
    {
        CancellationToken cancel = request._GetRequestCancellationToken();

        try
        {
            string rpcMethod = routeData.Values._GetStr("rpc_method");
            if (rpcMethod._IsEmpty())
            {
                var baseUri = request.GetEncodedUrl()._ParseUrl()._CombineUrl(this.RpcBaseAbsoluteUrlPath);

                string helpStr = "";

                if (this.Config.PrintHelp)
                {
                    helpStr = GenerateHelpString(baseUri);
                }

                await response._SendStringContentsAsync($"This is a JSON-RPC server.\r\nAPI: {Api.GetType().AssemblyQualifiedName}\r\nNow: {DateTimeOffset.Now._ToDtStr(withNanoSecs: true)}\r\n\r\n{helpStr}", cancel: cancel, normalizeCrlf: CrlfStyle.Lf);
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

                await ProcessHttpRequestMain(request, response, in_str, Consts.MimeTypes.TextUtf8, Consts.HttpStatusCodes.InternalServerError, true);
            }
        }
        catch (Exception ex)
        {
            await response._SendStringContentsAsync("Request Error: " + ex.ToString(), cancel: cancel, statusCode: Consts.HttpStatusCodes.InternalServerError, normalizeCrlf: CrlfStyle.Lf);
        }
    }

    public virtual async Task PostRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData)
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

    protected virtual async Task ProcessHttpRequestMain(HttpRequest request, HttpResponse response, string inStr, string responseContentsType = Consts.MimeTypes.Json, int httpStatusWhenError = Consts.HttpStatusCodes.Ok, bool simpleResultWhenOk = false)
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
                conn.LocalIpAddress!._UnmapIPv4().ToString(), conn.LocalPort,
                conn.RemoteIpAddress!._UnmapIPv4().ToString(), conn.RemotePort,
                requestHeaders,
                suppliedUsername,
                suppliedPassword);

            var callResults = await this.CallMethods(inStr, client_info, simpleResultWhenOk);

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

    public void RegisterRoutesToHttpServer(IApplicationBuilder appBuilder, string path = "/rpc")
    {
        path = path._NonNullTrim();

        if (path.StartsWith("/") == false) throw new CoresLibException($"Invalid absolute path: '{path}'");
        if (path.Length >= 2 && path.EndsWith("/")) throw new CoresLibException($"Path must not end with '/'.");

        RouteBuilder rb = new RouteBuilder(appBuilder);

        rb.MapGet(path, GetRequestHandler);
        rb.MapGet(path + "/{rpc_method}", GetRequestHandler);
        rb.MapGet(path + "/{rpc_method}/{rpc_param}", GetRequestHandler);
        rb.MapPost(path, PostRequestHandler);

        IRouter router = rb.Build();
        appBuilder.UseRouter(router);

        this.RpcBaseAbsoluteUrlPath = path;
        if (this.RpcBaseAbsoluteUrlPath.EndsWith("/") == false) this.RpcBaseAbsoluteUrlPath += "/";
    }

    // ヘルプ文字列を生成する
    readonly FastCache<Uri, string> helpStringCache = new FastCache<Uri, string>();
    string GenerateHelpString(Uri rpcBaseUri)
    {
        return helpStringCache.GetOrCreate(rpcBaseUri, x => GenerateHelpStringCore(x))!;
    }
    string GenerateHelpStringCore(Uri rpcBaseUri)
    {
        var methodList = this.Api.EnumMethodsForHelp();

        StringWriter w = new StringWriter();

        int methodIndex = 0;

        w.WriteLine();
        w.WriteLine();
        w.WriteLine(@"/***********************************************************************
**
** HTTP RPC Help Guide
** 
** You can call this RPC API with either HTTP Query String or JSON-RPC.
**
***********************************************************************/
");
        w.WriteLine();
        w.WriteLine();

        w.WriteLine($"List of all {methodList.Count} RPC Methods:");

        foreach (var m in methodList)
        {
            methodIndex++;

            string helpStr = $"RPC Method #{methodIndex}: {m.Name}({(m.ParametersHelpList.Select(x => x.Name + ": " + x.TypeName + (x.Mandatory ? "" : " = " + x.DefaultValue._ObjectToJson(compact: true) ))._Combine(", "))}): {m.RetValueTypeName};";

            if (m.Description._IsFilled()) helpStr += " // " + m.Description;

            w.WriteLine();
            w.WriteLine($"- {helpStr}");
        }

        w.WriteLine();
        w.WriteLine();
        w.WriteLine();

        methodIndex = 0;

        foreach (var m in methodList)
        {
            methodIndex++;

            w.WriteLine("======================================================================");

            string helpStr = $"RPC Method #{methodIndex}: {m.Name}({(m.ParametersHelpList.Select(x => x.Name + ": " + x.TypeName + (x.Mandatory ? "" : " = " + x.DefaultValue._ObjectToJson(compact: true)))._Combine(", "))}): {m.RetValueTypeName};";

            w.WriteLine($"- {helpStr}");

            w.WriteLine("======================================================================");
            w.WriteLine();
            w.WriteLine($"RPC Method Name: {m.Name}");
            w.WriteLine();

            if (m.Description._IsFilled())
            {
                w.WriteLine($"RPC Description: {m.Description}");
                w.WriteLine();
            }

            w.WriteLine($"RPC Authenication Required: {m.RequireAuth._ToBoolYesNoStr()}");
            w.WriteLine();

            var pl = m.ParametersHelpList;

            var sampleRequestJsonData = Json.NewJsonObject();

            QueryStringList qsList = new QueryStringList();

            if (pl.Count == 0)
            {
                w.WriteLine($"No RPC Input Parameters.");
                w.WriteLine();
            }
            else
            {
                w.WriteLine($"RPC Input: {pl.Count} Parameters");

                for (int i = 0; i < pl.Count; i++)
                {
                    var pp = pl[i];
                    string? qsSampleOrDefaultValue = null;

                    w.WriteLine($"  Parameter #{i + 1}: {pp.Name}".TrimEnd());
                    w.WriteLine($"    Name: {pp.Name}");
                    if (pp.Description._IsFilled())
                    {
                        w.WriteLine($"    Description: {pp.Description}");
                    }
                    if (pp.IsPrimitiveType)
                    {
                        w.WriteLine($"    Input Data Type: Primitive Value - {pp.TypeName}");
                    }
                    else
                    {
                        w.WriteLine($"    Input Data Type: JSON Data - {pp.TypeName}");
                    }
                    if (pp.SampleValueOneLineStr._IsFilled())
                    {
                        if (pp.IsPrimitiveType)
                        {
                            w.WriteLine($"    Input Data Example: {pp.SampleValueOneLineStr}");
                        }
                        else
                        {
                            w.WriteLine($"    Input Data Example (JSON):");
                            w.WriteLine($"    --------------------");
                            w.Write(pp.SampleValueMultiLineStr._PrependIndent(4));
                            w.WriteLine($"    --------------------");
                        }
                        qsSampleOrDefaultValue = pp.SampleValueOneLineStr;
                    }
                    w.WriteLine($"    Mandatory: {pp.Mandatory._ToBoolYesNoStr()}");
                    if (pp.Mandatory == false)
                    {
                        w.WriteLine($"    Default Value: {pp.DefaultValue._ObjectToJson(includeNull: true, compact: true)}");
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

                    sampleRequestJsonData.TryAdd(pp.Name, JToken.FromObject(pp.SampleValueObject));

                    w.WriteLine();
                }

            }

            if (m.HasRetValue == false)
            {
                w.WriteLine("No RPC Result Value.");
                w.WriteLine();
            }
            else
            {
                w.WriteLine("RPC Result Value:");
                if (m.IsRetValuePrimitiveType)
                {
                    w.WriteLine($"  Output Data Type: Primitive Value - {m.RetValueTypeName}");
                }
                else
                {
                    w.WriteLine($"  Output Data Type: JSON Data - {m.RetValueTypeName}");
                }

                if (m.RetValueSampleValueJsonMultilineStr._IsFilled())
                {
                    w.WriteLine("  Sample Result Output Data:");
                    w.WriteLine("  --------------------");
                    w.Write($"{m.RetValueSampleValueJsonMultilineStr}"._PrependIndent(2));
                    w.WriteLine("  --------------------");
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

                w.WriteLine("--- RPC Call Sample: HTTP GET with Query String Sample ---");

                w.WriteLine($"# HTTP GET Target URL (You can test it with your web browser easily):");
                w.WriteLine($"{urlDir}");
                w.WriteLine();
                w.WriteLine($"# wget command line sample on Linux bash (retcode = 0 when successful):");
                w.WriteLine($"wget --content-on-error --no-verbose -O - --no-check-certificate {urlDir._EscapeBashArg()}");
                w.WriteLine();
                w.WriteLine($"# curl command line sample on Linux bash (retcode = 0 when successful):");
                w.WriteLine($"curl --get --globoff --fail -k --raw --verbose {urlDir._EscapeBashArg()}");
                w.WriteLine();
                w.WriteLine();

                JsonRpcRequestForHelp reqSample = new JsonRpcRequestForHelp
                {
                    Version = "2.0",
                    Method = m.Name,
                    Params = sampleRequestJsonData,
                };

                w.WriteLine("--- RPC Call Sample: HTTP POST with JSON-RPC Sample ---");
                w.WriteLine($"# HTTP POST Target URL:");
                w.WriteLine($"{rpcBaseUri}");
                w.WriteLine();
                w.WriteLine($"# curl JSON-RPC call command line sample on Linux bash:");
                w.WriteLine($"cat <<\\EOF | curl --request POST --globoff --fail -k --raw --verbose --data @- '{rpcBaseUri}'");
                w.Write(reqSample._ObjectToJson(includeNull: true, compact: false)._NormalizeCrlf(ensureLastLineCrlf: true));
                w.WriteLine("EOF");
                w.WriteLine();

                w.WriteLine("# JSON-RPC call response sample (when successful):");
                var okSample = new JsonRpcResponseForHelp_Ok
                {
                    Version = "2.0",
                    Result = m.RetValueSampleValueObject,
                };
                w.WriteLine($"--------------------");
                w.Write(okSample._ObjectToJson(includeNull: true, compact: false)._NormalizeCrlf(ensureLastLineCrlf: true));
                w.WriteLine($"--------------------");
                w.WriteLine();

                w.WriteLine("# JSON-RPC call response sample (when error):");
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
                w.WriteLine($"--------------------");
                w.Write(errorSample._ObjectToJson(includeNull: true, compact: false)._NormalizeCrlf(ensureLastLineCrlf: true));
                w.WriteLine($"--------------------");
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
                urlWithBasicAuth = urlWithBasicAuth._AddCredentialOnUrl("USERNAME_HERE", "PASSWORD_HERE");

                string urlWithQsAuth = rpcBaseUri._CombineUrlDir(m.Name).ToString();
                urlWithQsAuth += $"?rpc_auth_username=USERNAME_HERE&rpc_auth_password=PASSWORD_HERE";
                if (qsList.Any())
                {
                    urlWithQsAuth += "&" + qsList.ToString(null, urlEncodeParam);
                }

                w.WriteLine("--- RPC Call Sample: HTTP GET with Query String Sample ---");
                w.WriteLine($"# HTTP GET Target URL (You can test it with your web browser easily):");
                w.WriteLine("# (with HTTP Basic Authentication)");
                w.WriteLine($"{urlWithBasicAuth}");
                w.WriteLine("# (with HTTP Query String Authentication)");
                w.WriteLine($"{urlWithQsAuth}");
                w.WriteLine();
                w.WriteLine($"# wget command line sample on Linux bash (retcode = 0 when successful):");
                w.WriteLine("# (wget with HTTP Basic Authentication)");
                w.WriteLine($"wget --content-on-error --no-verbose -O - --no-check-certificate {urlWithBasicAuth._EscapeBashArg()}");
                w.WriteLine("# (wget with HTTP Query String Authentication)");
                w.WriteLine($"wget --content-on-error --no-verbose -O - --no-check-certificate {urlWithQsAuth._EscapeBashArg()}");
                w.WriteLine("# (wget with HTTP Header Authentication)");
                w.WriteLine($"wget --content-on-error --no-verbose -O - --no-check-certificate --header 'X-RPC-Auth-Username: USERNAME_HERE' --header 'X-RPC-Auth-Password: PASSWORD_HERE' {urlSimple._EscapeBashArg()}");
                w.WriteLine();
                w.WriteLine($"# curl command line sample on Linux bash (retcode = 0 when successful):");
                w.WriteLine("# (curl with HTTP Basic Authentication)");
                w.WriteLine($"curl --get --globoff --fail -k --raw --verbose {urlWithBasicAuth._EscapeBashArg()}");
                w.WriteLine("# (curl with HTTP Query String Authentication)");
                w.WriteLine($"curl --get --globoff --fail -k --raw --verbose {urlWithQsAuth._EscapeBashArg()}");
                w.WriteLine("# (curl with HTTP Header Authentication)");
                w.WriteLine($"curl --get --globoff --fail -k --raw --verbose --header 'X-RPC-Auth-Username: USERNAME_HERE' --header 'X-RPC-Auth-Password: PASSWORD_HERE' {urlSimple._EscapeBashArg()}");
                w.WriteLine();
                w.WriteLine();

                JsonRpcRequestForHelp reqSample = new JsonRpcRequestForHelp
                {
                    Version = "2.0",
                    Method = m.Name,
                    Params = sampleRequestJsonData,
                };

                w.WriteLine("--- RPC Call Sample: HTTP POST with JSON-RPC Sample ---");
                w.WriteLine($"# HTTP POST Target URL (You can test it with your web browser easily):");
                w.WriteLine($"{urlWithBasicAuth}");
                w.WriteLine();
                w.WriteLine($"# curl JSON-RPC call command line sample on Linux bash:");
                w.WriteLine($"cat <<\\EOF | curl --request POST --globoff --fail -k --raw --verbose --data @- '{rpcBaseUri}'");
                w.Write(reqSample._ObjectToJson(includeNull: true, compact: false)._NormalizeCrlf(ensureLastLineCrlf: true));
                w.WriteLine("EOF");
                w.WriteLine();

                w.WriteLine("# JSON-RPC call response sample (when successful):");
                var okSample = new JsonRpcResponseForHelp_Ok
                {
                    Version = "2.0",
                    Result = m.RetValueSampleValueObject,
                };
                w.WriteLine($"--------------------");
                w.Write(okSample._ObjectToJson(includeNull: true, compact: false)._NormalizeCrlf(ensureLastLineCrlf: true));
                w.WriteLine($"--------------------");
                w.WriteLine();

                w.WriteLine("# JSON-RPC call response sample (when error):");
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
                w.WriteLine($"--------------------");
                w.Write(errorSample._ObjectToJson(includeNull: true, compact: false)._NormalizeCrlf(ensureLastLineCrlf: true));
                w.WriteLine($"--------------------");
                w.WriteLine();
            }


            w.WriteLine();
            w.WriteLine();
            w.WriteLine();
        }

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

