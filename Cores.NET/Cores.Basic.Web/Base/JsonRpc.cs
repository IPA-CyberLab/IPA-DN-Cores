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
using Microsoft.Extensions.Configuration;
using Castle.DynamicProxy;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Microsoft.AspNetCore.Routing;

using IPA.Cores.Helper.Basic;

namespace IPA.Cores.Basic
{
    class JsonRpcException : Exception
    {
        public JsonRpcError RpcError { get; }
        public JsonRpcException(JsonRpcError err)
            : base($"Code={err.Code}, Message={err.Message.NonNull()}" +
                  (err == null || err.Data == null ? "" : $", Data={err.Data.ObjectToJson(compact: true)}"))
        {
            this.RpcError = err;
        }
    }

    class JsonRpcRequest
    {
        [JsonProperty("jsonrpc")]
        public string Version { get; set; } = "2.0";

        [JsonProperty("method")]
        public string Method { get; set; } = "";

        [JsonProperty("params")]
        public object Params { get; set; } = null;

        [JsonProperty("id")]
        public string Id { get; set; } = null;

        public JsonRpcRequest() { }

        public JsonRpcRequest(string method, object param, string id)
        {
            this.Method = method;
            this.Params = param;
            this.Id = id;
        }
    }

    class JsonRpcResponse<TResult> : JsonRpcResponse
        where TResult : class
    {
        [JsonIgnore]
        public TResult ResultData
        {
            get => Result == null ? null : base.Result.ConvertJsonObject<TResult>();
            set => Result = value;
        }
    }

    class JsonRpcResponseOk : JsonRpcResponse
    {
        [JsonIgnore]
        public override JsonRpcError Error { get => null; set { } }

        [JsonProperty("result", NullValueHandling = NullValueHandling.Include)]
        public override object Result { get; set; } = null;
    }

    class JsonRpcResponseError : JsonRpcResponse
    {
        [JsonIgnore]
        public override object Result { get => null; set { } }

        [JsonProperty("error", NullValueHandling = NullValueHandling.Include)]
        public override JsonRpcError Error { get; set; } = null;
    }

    class JsonRpcResponse
    {
        [JsonProperty("jsonrpc")]
        public virtual string Version { get; set; } = "2.0";

        [JsonProperty("result")]
        public virtual object Result { get; set; } = null;

        [JsonProperty("error")]
        public virtual JsonRpcError Error { get; set; } = null;

        [JsonProperty("id", NullValueHandling = NullValueHandling.Include)]
        public virtual string Id { get; set; } = null;

        [JsonIgnore]
        public virtual bool IsError => this.Error != null;

        [JsonIgnore]
        public virtual bool IsOk => !IsError;

        public virtual void ThrowIfError()
        {
            if (this.IsError) throw new JsonRpcException(this.Error);
        }

        public override string ToString()
        {
            return this.ObjectToJson(compact: true);
        }
    }

    class JsonRpcError
    {
        public JsonRpcError() { }
        public JsonRpcError(int code, string message, object data = null)
        {
            this.Code = code;
            this.Message = message.NonNull();
            if (this.Message.IsEmpty()) this.Message = $"JSON-RPC Error {code}";
            this.Data = data;
        }

        [JsonProperty("code")]
        public int Code { get; set; } = 0;

        [JsonProperty("message")]
        public string Message { get; set; } = null;

        [JsonProperty("data")]
        public object Data { get; set; } = null;
    }

    class RpcInterfaceAttribute : Attribute { }

    class RpcMethodInfo
    {
        public string Name { get; }
        public MethodInfo Method { get; }
        public ParameterInfo[] ParametersByIndex { get; }
        public ParameterInfo ReturnParameter { get; }
        public bool IsTask { get; }
        public Type TaskType { get; }
        public bool IsGenericTask { get; }
        public Type GeneticTaskType { get; }

        public RpcMethodInfo(Type target_class, string method_name)
        {
            MethodInfo method_info = target_class.GetMethod(method_name);
            if (method_info == null)
            {
                throw new JsonRpcException(new JsonRpcError(-32601, "Method not found"));
            }

            var r = method_info.ReturnParameter;
            bool is_task = false;
            if (r.ParameterType == typeof(Task) || r.ParameterType.IsSubclassOf(typeof(Task))) is_task = true;

            if (is_task == false)
            {
                throw new ApplicationException($"The return value of the function '{method_info.Name}' is not a Task.");
            }

            if (is_task)
            {
                this.TaskType = r.ParameterType;
                Type[] generic_types = TaskType.GenericTypeArguments;
                if (generic_types.Length == 1)
                {
                    this.IsGenericTask = true;
                    this.GeneticTaskType = generic_types[0];
                }
                else if (generic_types.Length >= 2)
                {
                    throw new ApplicationException("generic_types.Length >= 2");
                }
            }

            this.IsTask = is_task;
            this.Method = method_info;
            this.Name = method_name;
            this.ReturnParameter = r;
            this.ParametersByIndex = method_info.GetParameters();
        }

        public async Task<object> InvokeMethod(object target_instance, string method_name, JObject param)
        {
            object[] in_params = new object[this.ParametersByIndex.Length];
            if (this.ParametersByIndex.Length == 1 && this.ParametersByIndex[0].ParameterType == typeof(System.Object))
            {
                in_params = new object[1] { param };
            }
            else
            {
                for (int i = 0; i < this.ParametersByIndex.Length; i++)
                {
                    ParameterInfo pi = this.ParametersByIndex[i];
                    if (param != null && param.TryGetValue(pi.Name, out var value))
                        in_params[i] = value.ToObject(pi.ParameterType);
                    else if (pi.HasDefaultValue)
                        in_params[i] = pi.DefaultValue;
                    else throw new ArgumentException($"The parameter '{pi.Name}' is missing.");
                }
            }

            object retobj = this.Method.Invoke(target_instance, in_params);

            if (this.IsTask == false)
                return Task.FromResult<object>(retobj);
            else
            {
                Type t = retobj.GetType();
                Task task = (Task)retobj;

                //Dbg.WhereThread();

                await task;

                //Dbg.WhereThread();

                var prop_mi = t.GetProperty("Result");
                object retvalue = prop_mi.GetValue(retobj);

                return retvalue;
            }
        }
    }

    abstract class JsonRpcServerApi
    {
        public Type RpcInterface { get; }

        public JsonRpcServerApi()
        {
            this.RpcInterface = get_rpc_interface();
        }

        protected JsonRpcClientInfo ClientInfo { get => TaskVar<JsonRpcClientInfo>.Value; }

        public CancellationTokenSource CancelSource { get; } = new CancellationTokenSource();
        public CancellationToken CancelToken { get => this.CancelSource.Token; }

        Dictionary<string, RpcMethodInfo> method_info_cache = new Dictionary<string, RpcMethodInfo>();
        public RpcMethodInfo GetMethodInfo(string method_name)
        {
            RpcMethodInfo m = null;
            lock (method_info_cache)
            {
                if (method_info_cache.ContainsKey(method_name) == false)
                {
                    m = get_method_info_main(method_name);
                    method_info_cache.Add(method_name, m);
                }
                else
                    m = method_info_cache[method_name];
            }
            return m;
        }
        RpcMethodInfo get_method_info_main(string method_name)
        {
            RpcMethodInfo mi = new RpcMethodInfo(this.GetType(), method_name);
            if (this.RpcInterface.GetMethod(mi.Name) == null)
            {
                throw new ApplicationException($"The method '{method_name}' is not defined on the interface '{this.RpcInterface.Name}'.");
            }
            return mi;
        }

        public Task<object> InvokeMethod(string method_name, JObject param, RpcMethodInfo method_info = null)
        {
            if (method_info == null) method_info = GetMethodInfo(method_name);
            return method_info.InvokeMethod(this, method_name, param);
        }

        protected Type get_rpc_interface()
        {
            Type ret = null;
            Type t = this.GetType();
            var ints = t.GetTypeInfo().GetInterfaces();
            int num = 0;
            foreach (var f in ints)
                if (f.GetCustomAttribute<RpcInterfaceAttribute>() != null)
                {
                    ret = f;
                    num++;
                }
            if (num == 0) throw new ApplicationException($"The class '{t.Name}' has no interface with the RpcInterface attribute.");
            if (num >= 2) throw new ApplicationException($"The class '{t.Name}' has two or mode interfaces with the RpcInterface attribute.");
            return ret;
        }

        public virtual object StartCall(JsonRpcClientInfo client_info) { return null; }

        public virtual async Task<object> StartCallAsync(JsonRpcClientInfo client_info, object param) => await Task.FromResult<object>(null);

        public virtual void FinishCall(object param) { }

        public virtual async Task FinishCallAsync(object param) => await Task.CompletedTask;
    }

    abstract class JsonRpcServer
    {
        public JsonRpcServerApi Api { get; }
        public JsonRpcServerConfig Config { get; }
        public CancellationToken CancelToken { get => this.Api.CancelToken; }

        public JsonRpcServer(JsonRpcServerApi api, JsonRpcServerConfig cfg, CancellationToken cancel_token)
        {
            this.Api = api;
            TaskUtil.ChainCancellationTokensToCancellationTokenSource(this.Api.CancelSource, true, cancel_token);
            this.Config = cfg;
        }

        public async Task<JsonRpcResponse> CallMethod(JsonRpcRequest req)
        {
            try
            {
                this.CancelToken.ThrowIfCancellationRequested();
                RpcMethodInfo method = this.Api.GetMethodInfo(req.Method);
                JObject in_obj;

                if (req.Params is JObject)
                {
                    in_obj = (JObject)req.Params;
                }
                else
                {
                    in_obj = new JObject();
                }

                try
                {
                    object ret_obj = await this.Api.InvokeMethod(req.Method, in_obj, method);
                    return new JsonRpcResponseOk()
                    {
                        Id = req.Id,
                        Error = null,
                        Result = ret_obj,
                    };
                }
                catch (System.Reflection.TargetInvocationException ex)
                {
                    throw ex.InnerException;
                }
            }
            catch (JsonRpcException ex)
            {
                return new JsonRpcResponseError()
                {
                    Id = req.Id,
                    Error = ex.RpcError,
                    Result = null,
                };
            }
            catch (Exception ex)
            {
                if (ex.InnerException != null) ex = ex.InnerException;
                return new JsonRpcResponseError()
                {
                    Id = req.Id,
                    Error = new JsonRpcError(-32603, ex.Message, ex.ToString()),
                    Result = null,
                };
            }
        }

        public async Task<string> CallMethods(string in_str, JsonRpcClientInfo client_info)
        {
            bool is_single = false;
            List<JsonRpcRequest> request_list = new List<JsonRpcRequest>();
            try
            {
                if (in_str.StartsWith("{"))
                {
                    is_single = true;
                    JsonRpcRequest r = in_str.JsonToObject<JsonRpcRequest>();
                    request_list.Add(r);
                }
                else
                {
                    JsonRpcRequest[] rr = in_str.JsonToObject<JsonRpcRequest[]>();
                    request_list = new List<JsonRpcRequest>(rr);
                }
            }
            catch
            {
                throw new JsonRpcException(new JsonRpcError(-32700, "Parse error"));
            }

            List<JsonRpcResponse> response_list = new List<JsonRpcResponse>();

            TaskVar.Set<JsonRpcClientInfo>(client_info);
            try
            {
                object param1 = this.Api.StartCall(client_info);
                try
                {
                    object param2 = await this.Api.StartCallAsync(client_info, param1);
                    try
                    {
                        foreach (JsonRpcRequest req in request_list)
                        {
                            try
                            {
                                JsonRpcResponse res = await CallMethod(req);
                                if (req.Id != null) response_list.Add(res);
                            }
                            catch (Exception ex)
                            {
                                JsonRpcException json_ex;
                                if (ex is JsonRpcException) json_ex = ex as JsonRpcException;
                                else json_ex = new JsonRpcException(new JsonRpcError(-32603, ex.Message, ex.ToString()));
                                JsonRpcResponseError res = new JsonRpcResponseError()
                                {
                                    Id = req.Id,
                                    Error = json_ex.RpcError,
                                    Result = null,
                                };
                                if (req.Id != null) response_list.Add(res);
                            }
                        }
                    }
                    finally
                    {
                        await this.Api.FinishCallAsync(param2);
                    }
                }
                finally
                {
                    this.Api.FinishCall(param1);
                }
            }
            finally
            {
                TaskVar.Set<JsonRpcClientInfo>(null);
            }

            if (is_single)
            {
                if (response_list.Count >= 1)
                    return response_list[0].ObjectToJson();
                else
                    return "";
            }
            else
                return response_list.ObjectToJson();
        }
    }

    class JsonRpcHttpServer : JsonRpcServer
    {
        public JsonRpcHttpServer(JsonRpcServerApi api, JsonRpcServerConfig cfg, CancellationToken cancel_token) : base(api, cfg, cancel_token) { }

        public virtual async Task GetRequestHandler(HttpRequest request, HttpResponse response, RouteData route_data)
        {
            try
            {
                string rpc_method = route_data.Values.GetStrOrEmpty("rpc_method");
                if (rpc_method.IsEmpty())
                {
                    await response.SendStringContents($"This is a JSON-RPC server.\r\nAPI: {Api.GetType().AssemblyQualifiedName}\r\nNow: {DateTime.Now.ToDtStr(with_nanosecs: true)}", cancel: this.CancelToken);
                }
                else
                {
                    string args = route_data.Values.GetStrOrEmpty("rpc_param");

                    if (args.IsEmpty())
                    {
                        JObject jo = new JObject();

                        foreach (string key in request.Query.Keys)
                        {
                            string value = request.Query[key];

                            jo.Add(key, JToken.FromObject(value));
                        }

                        args = jo.ObjectToJson(compact: true);
                    }

                    string id = "GET-" + Str.NewGuid();
                    string in_str = "{'jsonrpc':'2.0','method':'" + rpc_method + "','params':" + args + ",'id':'" + id + "'}";

                    await process_http_request_main(request, response, in_str, "text/plain; charset=UTF-8");
                }
            }
            catch (Exception ex)
            {
                await response.SendStringContents(ex.ToString(), cancel: this.CancelToken);
            }
        }

        public virtual async Task PostRequestHandler(HttpRequest request, HttpResponse response, RouteData route_data)
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

        protected virtual async Task process_http_request_main(HttpRequest request, HttpResponse response, string in_str, string response_contents_type = "application/json")
        {
            string ret_str = "";
            try
            {
                SortedDictionary<string, string> headers = new SortedDictionary<string, string>();
                foreach (string header_name in request.Headers.Keys)
                {
                    if (request.Headers.TryGetValue(header_name, out var val))
                    {
                        headers.Add(header_name, val.ToString());
                    }
                }

                var conn = request.HttpContext.Connection;
                JsonRpcClientInfo client_info = new JsonRpcClientInfo(this, conn.LocalIpAddress.ToString(), conn.LocalPort,
                    conn.RemoteIpAddress.ToString(), conn.RemotePort,
                    headers);

                //string in_str = request.Body.ReadToEnd().GetString_UTF8();
                //string in_str = (request.Body.ReadToEnd(this.Config.MaxRequestBodyLen)).GetString_UTF8();
                //Dbg.WriteLine("in_str: " + in_str);

                ret_str = await this.CallMethods(in_str, client_info);
            }
            catch (Exception ex)
            {
                JsonRpcException json_ex;
                if (ex is JsonRpcException) json_ex = ex as JsonRpcException;
                else json_ex = new JsonRpcException(new JsonRpcError(1234, ex.Message, ex.ToString()));

                ret_str = new JsonRpcResponseError()
                {
                    Error = json_ex.RpcError,
                    Id = null,
                    Result = null,
                }.ObjectToJson();
            }

            //Dbg.WriteLine("ret_str: " + ret_str);

            await response.SendStringContents(ret_str, response_contents_type, cancel: this.CancelToken);
        }

        public void RegisterToHttpServer(IApplicationBuilder app, string path = "rpc")
        {
            RouteBuilder rb = new RouteBuilder(app);

            rb.MapGet(path, GetRequestHandler);
            rb.MapGet(path + "/{rpc_method}", GetRequestHandler);
            rb.MapGet(path + "/{rpc_method}/{rpc_param}", GetRequestHandler);
            rb.MapPost(path, PostRequestHandler);

            IRouter router = rb.Build();
            app.UseRouter(router);
        }
    }

    class JsonRpcServerConfig
    {
        public int MaxRequestBodyLen { get; set; } = 100 * 1024 * 1024;
    }

    class JsonHttpRpcListener : HttpServerImplementation
    {
        public JsonRpcHttpServer JsonServer { get; }

        public JsonHttpRpcListener(IConfiguration configuration) : base(configuration)
        {
            (JsonRpcServerConfig rpc_cfg, JsonRpcServerApi api) p = ((JsonRpcServerConfig rpc_cfg, JsonRpcServerApi api))this.Param;

            JsonServer = new JsonRpcHttpServer(p.api, p.rpc_cfg, this.CancelToken);
        }

        public static HttpServer<JsonHttpRpcListener> StartServer(HttpServerBuilderConfig http_cfg, JsonRpcServerConfig rpc_server_cfg, JsonRpcServerApi rpc_api)
            => new HttpServer<JsonHttpRpcListener>(http_cfg, (rpc_server_cfg, rpc_api));

        public override void SetupStartupConfig(HttpServerStartupConfig cfg, IApplicationBuilder app, IHostingEnvironment env)
            => this.JsonServer.RegisterToHttpServer(app);
    }

    abstract class JsonRpcClient
    {
        List<(JsonRpcRequest request, JsonRpcResponse response, Type response_data_type)> st_call_queue = new List<(JsonRpcRequest request, JsonRpcResponse response, Type response_data_type)>();

        public void ST_CallClear() => st_call_queue.Clear();

        protected JsonRpcClient()
        {
            mt_batch = new BatchQueue<MT_QueueItem>(mt_batch_process_proc, 10);
        }

        public JsonRpcResponse<TResponse> ST_CallAdd<TResponse>(string method, object param) where TResponse : class
        {
            var ret = new JsonRpcResponse<TResponse>();

            var add_item = (new JsonRpcRequest(method, param, Str.NewGuid()), ret, typeof(TResponse));

            st_call_queue.Add(add_item);

            return ret;
        }

        public JsonRpcResponse ST_CallAdd(string method, object param, Type result_type)
        {
            JsonRpcResponse ret = (JsonRpcResponse)Activator.CreateInstance(typeof(JsonRpcResponse<>).MakeGenericType(result_type));

            var add_item = (new JsonRpcRequest(method, param, Str.NewGuid()), ret, result_type);

            st_call_queue.Add(add_item);

            return ret;
        }

        public async Task ST_CallAll(bool throw_each_error = false)
        {
            if (st_call_queue.Count == 0) return;

            try
            {
                string req = "";
                bool is_single = false;

                Dictionary<string, (JsonRpcRequest request, JsonRpcResponse response, Type response_data_type)> requests_table = new Dictionary<string, (JsonRpcRequest request, JsonRpcResponse response, Type response_data_type)>();
                List<JsonRpcRequest> requests = new List<JsonRpcRequest>();

                foreach (var o in this.st_call_queue)
                {
                    requests_table.Add(o.request.Id, (o.request, o.response, o.response_data_type));
                    requests.Add(o.request);
                }

                //if (requests_table.Count >= 2)
                //    Dbg.WriteLine($"Num_Requests_per_call: {requests_table.Count}");

                if (requests_table.Count == 1)
                {
                    req = requests[0].ObjectToJson(compact: true);
                }
                else
                {
                    req = requests.ObjectToJson(compact: true);
                }

                //req.Debug();

                string ret = await SendRequestAndGetResponse(req);

                if (ret.StartsWith("{")) is_single = true;

                List<JsonRpcResponse> ret_list = new List<JsonRpcResponse>();

                if (is_single)
                {
                    JsonRpcResponse r = ret.JsonToObject<JsonRpcResponse>();
                    ret_list.Add(r);
                }
                else
                {
                    JsonRpcResponse[] r = ret.JsonToObject<JsonRpcResponse[]>();
                    ret_list = new List<JsonRpcResponse>(r);
                }

                foreach (var res in ret_list)
                {
                    if (res.Id.IsFilled())
                    {
                        if (requests_table.ContainsKey(res.Id))
                        {
                            var q = requests_table[res.Id];

                            q.response.Error = res.Error;
                            q.response.Id = res.Id;
                            q.response.Result = res.Result;
                            q.response.Version = res.Version;
                        }
                    }
                }

                if (throw_each_error)
                    foreach (var r in ret_list)
                    {
                        r.ThrowIfError();
                    }
            }
            finally
            {
                ST_CallClear();
            }
        }

        public async Task<JsonRpcResponse<TResponse>> ST_CallOne<TResponse>(string method, object param, bool throw_error = false) where TResponse : class
        {
            ST_CallClear();
            try
            {
                JsonRpcResponse<TResponse> res = ST_CallAdd<TResponse>(method, param);
                await ST_CallAll(throw_error);
                return res;
            }
            finally
            {
                ST_CallClear();
            }
        }

        class MT_QueueItem
        {
            public string Method;
            public object Param;
            public Type ResultType;
            public JsonRpcResponse Response;
        }

        object lock_mt_queue = new object();
        List<MT_QueueItem> mt_queue = new List<MT_QueueItem>();
        SemaphoreSlim mt_semaphore = new SemaphoreSlim(1, 1);

        BatchQueue<MT_QueueItem> mt_batch;

        void mt_batch_process_proc(BatchQueueItem<MT_QueueItem>[] items)
        {
            try
            {
                foreach (var q in items)
                {
                    var item = q.UserItem;
                    item.Response = ST_CallAdd(item.Method, item.Param, item.ResultType);
                }

                ST_CallAll(false).Wait();
            }
            catch (Exception ex)
            {
                //ex.ToString().Print();
                foreach (var q in items)
                {
                    var item = q.UserItem;
                    item.Response.Error = new JsonRpcError(-1, ex.ToString());
                }
            }
            finally
            {
                foreach (var q in items)
                {
                    q.SetCompleted();
                }
            }
        }

        public async Task<JsonRpcResponse<TResponse>> MT_Call<TResponse>(string method, object param, bool throw_error = false) where TResponse : class
        {
            var response = new JsonRpcResponse<TResponse>();

            MT_QueueItem q = new MT_QueueItem()
            {
                Method = method,
                Param = param,
                ResultType = typeof(TResponse),
                Response = null,
            };

            var b = mt_batch.Add(q);

            await b.CompletedEvent.WaitAsync();

            return (JsonRpcResponse<TResponse>)q.Response;
        }

        public abstract Task<string> SendRequestAndGetResponse(string req);

        public abstract int TimeoutMsecs { get; set; }

        public abstract void AddHeader(string name, string value);

        class ProxyInterceptor : IInterceptor
        {
            public JsonRpcClient RpcClient { get; }

            public ProxyInterceptor(JsonRpcClient rpc_client)
            {
                this.RpcClient = rpc_client;
            }

            public void Intercept(IInvocation v)
            {
                JObject o = new JObject();
                var in_params = v.Method.GetParameters();
                if (v.Arguments.Length != in_params.Length) throw new ApplicationException("v.Arguments.Length != in_params.Length");
                for (int i = 0; i < in_params.Length; i++)
                {
                    var p = in_params[i];
                    o.Add(p.Name, JToken.FromObject(v.Arguments[i]));
                }
                Task<JsonRpcResponse<object>> call_ret = RpcClient.MT_Call<object>(v.Method.Name, o, true);

                //ret.Wait();

                //Dbg.WhereThread(v.Method.Name);
                Task<object> ret = get_response_object_async(call_ret);

                var return_type = v.Method.ReturnType;
                if (return_type.IsGenericType == false) throw new ApplicationException($"The return type of the method '{v.Method.Name}' is not a Task<>.");
                if (return_type.BaseType != typeof(Task)) throw new ApplicationException($"The return type of the method '{v.Method.Name}' is not a Task<>.");

                var generic_args = return_type.GetGenericArguments();
                if (generic_args.Length != 1) throw new ApplicationException($"The return type of the method '{v.Method.Name}' is not a Task<>.");
                var task_return_type = generic_args[0];

                v.ReturnValue = TaskUtil.ConvertTask(ret, typeof(object), task_return_type);
                //Dbg.WhereThread(v.Method.Name);
            }

            async Task<object> get_response_object_async(Task<JsonRpcResponse<object>> o)
            {
                //Dbg.WhereThread();
                await o;
                o.Result.ThrowIfError();
                //Dbg.WhereThread();
                return o.Result.ResultData;
            }
        }

        public virtual TRpcInterface GenerateRpcInterface<TRpcInterface>() where TRpcInterface : class
        {
            ProxyGenerator g = new ProxyGenerator();
            ProxyInterceptor ic = new ProxyInterceptor(this);

            return g.CreateInterfaceProxyWithoutTarget<TRpcInterface>(ic);
        }
    }

    class JsonRpcHttpClient : JsonRpcClient, IDisposable
    {
        public WebApi WebApi { get; private set; } = new WebApi();
        public string ApiBaseUrl { get; set; }

        public override int TimeoutMsecs { get => WebApi.TimeoutMsecs; set => WebApi.TimeoutMsecs = value; }
        public override void AddHeader(string name, string value) => WebApi.AddHeader(name, value);

        public JsonRpcHttpClient(string api_url)
        {
            this.ApiBaseUrl = api_url;
        }

        public override async Task<string> SendRequestAndGetResponse(string req)
        {
            WebRet ret = await this.WebApi.RequestWithPostData(this.ApiBaseUrl, req.GetBytes_UTF8(), "application/json");

            return ret.ToString();
        }

        Once dispose_once;
        public void Dispose()
        {
            if (dispose_once.IsFirstCall())
            {
                this.WebApi.Dispose();
                this.WebApi = null;
            }
        }
    }

    class JsonRpcHttpClient<TRpcInterface> : JsonRpcHttpClient
        where TRpcInterface : class
    {
        public TRpcInterface Call { get; }
        public JsonRpcHttpClient(string api_url) : base(api_url)
        {
            this.Call = this.GenerateRpcInterface<TRpcInterface>();
        }
    }

    class JsonRpcClientInfo
    {
        public string LocalIP { get; }
        public int LocalPort { get; }
        public string RemoteIP { get; }
        public int RemotePort { get; }
        public DateTime ConnectedDateTime { get; }
        public SortedDictionary<string, string> Headers { get; }
        public JsonRpcServer RpcServer { get; }

        public JsonRpcClientInfo(JsonRpcServer rpc_server, string local_ip, int local_port, string remote_ip, int remote_port, SortedDictionary<string, string> headers)
        {
            this.RpcServer = rpc_server;
            this.ConnectedDateTime = DateTime.Now;
            this.LocalIP = local_ip.NonNull();
            this.LocalPort = local_port;
            this.RemoteIP = remote_ip.NonNull();
            this.RemotePort = remote_port;
            this.Headers = headers;
        }

        public override string ToString()
        {
            return $"{this.RemoteIP}:{this.RemotePort}";
        }
    }
}
