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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Castle.DynamicProxy;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Microsoft.Extensions.Hosting;

#if CORES_BASIC_HTTPSERVER
// ASP.NET Core 3.0 用の型名を無理やり ASP.NET Core 2.2 でコンパイルするための型エイリアスの設定
using IWebHostEnvironment = Microsoft.AspNetCore.Hosting.IHostingEnvironment;
using IHostApplicationLifetime = Microsoft.AspNetCore.Hosting.IApplicationLifetime;
#endif

namespace IPA.Cores.Basic
{
    public class JsonRpcHttpServer : JsonRpcServer
    {
        public JsonRpcHttpServer(JsonRpcServerApi api, JsonRpcServerConfig? cfg = null) : base(api, cfg) { }

        public virtual async Task GetRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData)
        {
            CancellationToken cancel = request._GetRequestCancellationToken();

            try
            {
                string rpcMethod = routeData.Values._GetStr("rpc_method");
                if (rpcMethod._IsEmpty())
                {
                    await response._SendStringContents($"This is a JSON-RPC server.\r\nAPI: {Api.GetType().AssemblyQualifiedName}\r\nNow: {DateTime.Now._ToDtStr(withNanoSecs: true)}", cancel: cancel);
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

                            jObj.Add(key, JToken.FromObject(value));
                        }

                        args = jObj._ObjectToJson(compact: true);
                    }

                    string id = "GET-" + Str.NewGuid();
                    string in_str = "{'jsonrpc':'2.0','method':'" + rpcMethod + "','params':" + args + ",'id':'" + id + "'}";

                    await ProcessHttpRequestMain(request, response, in_str, Consts.MimeTypes.TextUtf8);
                }
            }
            catch (Exception ex)
            {
                await response._SendStringContents(ex.ToString(), cancel: cancel);
            }
        }

        public virtual async Task PostRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData)
        {
            try
            {
                string in_str = await request._RecvStringContents(this.Config.MaxRequestBodyLen, cancel: request._GetRequestCancellationToken());

                await ProcessHttpRequestMain(request, response, in_str);
            }
            catch (Exception ex)
            {
                await response._SendStringContents(ex.ToString(), cancel: request._GetRequestCancellationToken());
            }
        }

        protected virtual async Task ProcessHttpRequestMain(HttpRequest request, HttpResponse response, string inStr, string responseContentsType = Consts.MimeTypes.Json)
        {
            string ret_str = "";
            try
            {
                SortedDictionary<string, string> headers = new SortedDictionary<string, string>();
                foreach (string headerName in request.Headers.Keys)
                {
                    if (request.Headers.TryGetValue(headerName, out var val))
                    {
                        headers.Add(headerName, val.ToString());
                    }
                }

                var conn = request.HttpContext.Connection;
                JsonRpcClientInfo client_info = new JsonRpcClientInfo(this, conn.LocalIpAddress._UnmapIPv4().ToString(), conn.LocalPort,
                    conn.RemoteIpAddress._UnmapIPv4().ToString(), conn.RemotePort,
                    headers);

                //string in_str = request.Body.ReadToEnd().GetString_UTF8();
                //string in_str = (request.Body.ReadToEnd(this.Config.MaxRequestBodyLen)).GetString_UTF8();
                //Dbg.WriteLine("in_str: " + in_str);

                ret_str = await this.CallMethods(inStr, client_info);
            }
            catch (Exception ex)
            {
                JsonRpcException json_ex;
                if (ex is JsonRpcException) json_ex = (JsonRpcException)ex;
                else json_ex = new JsonRpcException(new JsonRpcError(1234, ex._GetSingleException().Message, ex.ToString()));

                ret_str = new JsonRpcResponseError()
                {
                    Error = json_ex.RpcError,
                    Id = null,
                    Result = null,
                }._ObjectToJson();
            }

            //Dbg.WriteLine("ret_str: " + ret_str);

            await response._SendStringContents(ret_str, responseContentsType, cancel: request._GetRequestCancellationToken());
        }

        public void RegisterRoutesToHttpServer(IApplicationBuilder appBuilder, string path = "/rpc")
        {
            RouteBuilder rb = new RouteBuilder(appBuilder);

            rb.MapGet(path, GetRequestHandler);
            rb.MapGet(path + "/{rpc_method}", GetRequestHandler);
            rb.MapGet(path + "/{rpc_method}/{rpc_param}", GetRequestHandler);
            rb.MapPost(path, PostRequestHandler);

            IRouter router = rb.Build();
            appBuilder.UseRouter(router);
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
}

#endif // CORES_BASIC_WEBAPP

