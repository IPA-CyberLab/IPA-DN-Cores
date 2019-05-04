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

namespace IPA.Cores.Basic
{
    class JsonRpcHttpServer : JsonRpcServer
    {
        public JsonRpcHttpServer(JsonRpcServerApi api, JsonRpcServerConfig cfg) : base(api, cfg) { }

        public virtual async Task GetRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData)
        {
            try
            {
                string rpcMethod = routeData.Values.GetStrOrEmpty("rpc_method");
                if (rpcMethod.IsEmpty())
                {
                    await response.SendStringContents($"This is a JSON-RPC server.\r\nAPI: {Api.GetType().AssemblyQualifiedName}\r\nNow: {DateTime.Now.ToDtStr(withNanoSecs: true)}", cancel: this.CancelToken);
                }
                else
                {
                    string args = routeData.Values.GetStrOrEmpty("rpc_param");

                    if (args.IsEmpty())
                    {
                        JObject jObj = new JObject();

                        foreach (string key in request.Query.Keys)
                        {
                            string value = request.Query[key];

                            jObj.Add(key, JToken.FromObject(value));
                        }

                        args = jObj.ObjectToJson(compact: true);
                    }

                    string id = "GET-" + Str.NewGuid();
                    string in_str = "{'jsonrpc':'2.0','method':'" + rpcMethod + "','params':" + args + ",'id':'" + id + "'}";

                    await process_http_request_main(request, response, in_str, "text/plain; charset=UTF-8");
                }
            }
            catch (Exception ex)
            {
                await response.SendStringContents(ex.ToString(), cancel: this.CancelToken);
            }
        }

        public virtual async Task PostRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData)
        {
            try
            {
                string in_str = await request.RecvStringContents(this.Config.MaxRequestBodyLen, cancel: this.CancelToken);

                await process_http_request_main(request, response, in_str);
            }
            catch (Exception ex)
            {
                await response.SendStringContents(ex.ToString(), cancel: this.CancelToken);
            }
        }

        protected virtual async Task process_http_request_main(HttpRequest request, HttpResponse response, string inStr, string responseContentsType = "application/json")
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
                JsonRpcClientInfo client_info = new JsonRpcClientInfo(this, conn.LocalIpAddress.UnmapIPv4().ToString(), conn.LocalPort,
                    conn.RemoteIpAddress.UnmapIPv4().ToString(), conn.RemotePort,
                    headers);

                //string in_str = request.Body.ReadToEnd().GetString_UTF8();
                //string in_str = (request.Body.ReadToEnd(this.Config.MaxRequestBodyLen)).GetString_UTF8();
                //Dbg.WriteLine("in_str: " + in_str);

                ret_str = await this.CallMethods(inStr, client_info);
            }
            catch (Exception ex)
            {
                JsonRpcException json_ex;
                if (ex is JsonRpcException) json_ex = ex as JsonRpcException;
                else json_ex = new JsonRpcException(new JsonRpcError(1234, ex.GetSingleException().Message, ex.ToString()));

                ret_str = new JsonRpcResponseError()
                {
                    Error = json_ex.RpcError,
                    Id = null,
                    Result = null,
                }.ObjectToJson();
            }

            //Dbg.WriteLine("ret_str: " + ret_str);

            await response.SendStringContents(ret_str, responseContentsType, cancel: this.CancelToken);
        }

        public void RegisterRoutesToHttpServer(IApplicationBuilder appBuilder, string path = "rpc")
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

    class JsonRpcHttpServerBuilder : HttpServerBuilderBase
    {
        public JsonRpcHttpServer JsonServer { get; }

        private JsonRpcHttpServerBuilder(IConfiguration configuration) : base(configuration)
        {
            (JsonRpcServerConfig rpcCfg, JsonRpcServerApi api) p = ((JsonRpcServerConfig rpcCfg, JsonRpcServerApi api))this.Param;

            JsonServer = new JsonRpcHttpServer(p.api, p.rpcCfg);
        }

        public static HttpServer<JsonRpcHttpServerBuilder> StartServer(HttpServerOptions httpCfg, JsonRpcServerConfig rpcServerCfg, JsonRpcServerApi rpcApi, CancellationToken cancel = default)
            => new HttpServer<JsonRpcHttpServerBuilder>(httpCfg, (rpcServerCfg, rpcApi), cancel);

        protected override void ConfigureImpl(HttpServerStartupConfig cfg, IApplicationBuilder app, IHostingEnvironment env)
            => this.JsonServer.RegisterRoutesToHttpServer(app);
    }
}
