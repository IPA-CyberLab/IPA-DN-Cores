﻿// IPA Cores.NET
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

#if CORES_BASIC_JSON

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Reflection;
using Castle.DynamicProxy;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    public static partial class CoresConfig
    {
        public static partial class JsonRpcServerSettings
        {
            public static readonly Copenhagen<int> DefaultMaxRequestBodyLen = 100 * 1024 * 1024; // 100 MB
        }
    }

    public class JsonRpcException : Exception
    {
        public JsonRpcError RpcError { get; }
        public JsonRpcException(JsonRpcError err)
            : base($"Code={err.Code}, Message={err.Message._NonNull()}" +
                  (err == null || err.Data == null ? "" : $", Data={err.Data._ObjectToJson(compact: true)}"))
        {
            this.RpcError = err;
        }
    }

    public class JsonRpcRequest
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

    public class JsonRpcResponse<TResult> : JsonRpcResponse
        where TResult : class
    {
        [JsonIgnore]
        public TResult ResultData
        {
            get => Result == null ? null : base.Result._ConvertJsonObject<TResult>();
            set => Result = value;
        }
    }

    public class JsonRpcResponseOk : JsonRpcResponse
    {
        [JsonIgnore]
        public override JsonRpcError Error { get => null; set { } }

        [JsonProperty("result", NullValueHandling = NullValueHandling.Include)]
        public override object Result { get; set; } = null;
    }

    public class JsonRpcResponseError : JsonRpcResponse
    {
        [JsonIgnore]
        public override object Result { get => null; set { } }

        [JsonProperty("error", NullValueHandling = NullValueHandling.Include)]
        public override JsonRpcError Error { get; set; } = null;
    }

    public class JsonRpcResponse
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
            return this._ObjectToJson(compact: true);
        }
    }

    public class JsonRpcError
    {
        public JsonRpcError() { }
        public JsonRpcError(int code, string message, object data = null)
        {
            this.Code = code;
            this.Message = message._NonNull();
            if (this.Message._IsEmpty()) this.Message = $"JSON-RPC Error {code}";
            this.Data = data;
        }

        [JsonProperty("code")]
        public int Code { get; set; } = 0;

        [JsonProperty("message")]
        public string Message { get; set; } = null;

        [JsonProperty("data")]
        public object Data { get; set; } = null;
    }

    public class RpcInterfaceAttribute : Attribute { }

    public class RpcMethodInfo
    {
        public string Name { get; }
        public MethodInfo Method { get; }
        public ParameterInfo[] ParametersByIndex { get; }
        public ParameterInfo ReturnParameter { get; }
        public bool IsTask { get; }
        public Type TaskType { get; }
        public bool IsGenericTask { get; }
        public Type GeneticTaskType { get; }

        public RpcMethodInfo(Type targetClass, string methodName)
        {
            MethodInfo methodInfo = targetClass.GetMethod(methodName);
            if (methodInfo == null)
            {
                throw new JsonRpcException(new JsonRpcError(-32601, "Method not found"));
            }

            var r = methodInfo.ReturnParameter;
            bool isTask = false;
            if (r.ParameterType._IsSubClassOfOrSame(typeof(Task))) isTask = true;

            if (isTask == false)
            {
                throw new ApplicationException($"The return value of the function '{methodInfo.Name}' is not a Task.");
            }

            if (isTask)
            {
                this.TaskType = r.ParameterType;
                Type[] genericTypes = TaskType.GenericTypeArguments;
                if (genericTypes.Length == 1)
                {
                    this.IsGenericTask = true;
                    this.GeneticTaskType = genericTypes[0];
                }
                else if (genericTypes.Length >= 2)
                {
                    throw new ApplicationException("generic_types.Length >= 2");
                }
            }

            this.IsTask = isTask;
            this.Method = methodInfo;
            this.Name = methodName;
            this.ReturnParameter = r;
            this.ParametersByIndex = methodInfo.GetParameters();
        }

        public async Task<object> InvokeMethod(object targetInstance, string methodName, JObject param)
        {
            object[] inParams = new object[this.ParametersByIndex.Length];
            if (this.ParametersByIndex.Length == 1 && this.ParametersByIndex[0].ParameterType == typeof(System.Object))
            {
                inParams = new object[1] { param };
            }
            else
            {
                for (int i = 0; i < this.ParametersByIndex.Length; i++)
                {
                    ParameterInfo pi = this.ParametersByIndex[i];
                    if (param != null && param.TryGetValue(pi.Name, out var value))
                        inParams[i] = value.ToObject(pi.ParameterType);
                    else if (pi.HasDefaultValue)
                        inParams[i] = pi.DefaultValue;
                    else throw new ArgumentException($"The parameter '{pi.Name}' is missing.");
                }
            }

            object retobj = this.Method.Invoke(targetInstance, inParams);

            if (this.IsTask == false)
                return Task.FromResult<object>(retobj);
            else
            {
                Type t = retobj.GetType();
                Task task = (Task)retobj;

                //Dbg.WhereThread();

                await task;

                //Dbg.WhereThread();

                var propertyMethodInfo = t.GetProperty("Result");
                object retvalue = propertyMethodInfo.GetValue(retobj);

                return retvalue;
            }
        }
    }

    public abstract class JsonRpcServerApi : AsyncService
    {
        public Type RpcInterface { get; }

        public JsonRpcServerApi(CancellationToken cancel = default) : base(cancel)
        {
            this.RpcInterface = GetRpcInterface();
        }

        protected JsonRpcClientInfo ClientInfo { get => TaskVar<JsonRpcClientInfo>.Value; }

        Dictionary<string, RpcMethodInfo> MethodInfoCache = new Dictionary<string, RpcMethodInfo>();
        public RpcMethodInfo GetMethodInfo(string methodName)
        {
            RpcMethodInfo m = null;
            lock (MethodInfoCache)
            {
                if (MethodInfoCache.ContainsKey(methodName) == false)
                {
                    m = GetMethodInfoMain(methodName);
                    MethodInfoCache.Add(methodName, m);
                }
                else
                    m = MethodInfoCache[methodName];
            }
            return m;
        }
        RpcMethodInfo GetMethodInfoMain(string methodName)
        {
            RpcMethodInfo mi = new RpcMethodInfo(this.GetType(), methodName);
            if (this.RpcInterface.GetMethod(mi.Name) == null)
            {
                throw new ApplicationException($"The method '{methodName}' is not defined on the interface '{this.RpcInterface.Name}'.");
            }
            return mi;
        }

        public Task<object> InvokeMethod(string methodName, JObject param, RpcMethodInfo methodInfo = null)
        {
            if (methodInfo == null) methodInfo = GetMethodInfo(methodName);
            return methodInfo.InvokeMethod(this, methodName, param);
        }

        protected Type GetRpcInterface()
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

        public virtual object StartCall(JsonRpcClientInfo clientInfo) { return null; }

        public virtual async Task<object> StartCallAsync(JsonRpcClientInfo clientInfo, object param) => await Task.FromResult<object>(null);

        public virtual void FinishCall(object param) { }

        public virtual Task FinishCallAsync(object param) => Task.CompletedTask;
    }

    public abstract class JsonRpcServer
    {
        public JsonRpcServerApi Api { get; }
        public JsonRpcServerConfig Config { get; }
        public CancellationToken CancelToken { get => this.Api.GrandCancel; }

        public JsonRpcServer(JsonRpcServerApi api, JsonRpcServerConfig cfg)
        {
            this.Api = api;
            this.Config = cfg;
        }

        public async Task<JsonRpcResponse> CallMethod(JsonRpcRequest req)
        {
            try
            {
                this.CancelToken.ThrowIfCancellationRequested();
                RpcMethodInfo method = this.Api.GetMethodInfo(req.Method);
                JObject inObj;

                if (req.Params is JObject)
                {
                    inObj = (JObject)req.Params;
                }
                else
                {
                    inObj = new JObject();
                }

                try
                {
                    object retObj = await this.Api.InvokeMethod(req.Method, inObj, method);
                    if (method.IsGenericTask == false)
                    {
                        retObj = null;
                    }
                    return new JsonRpcResponseOk()
                    {
                        Id = req.Id,
                        Error = null,
                        Result = retObj,
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
                ex = ex._GetSingleException();
                return new JsonRpcResponseError()
                {
                    Id = req.Id,
                    Error = new JsonRpcError(-32603, ex.Message, ex.ToString()),
                    Result = null,
                };
            }
        }

        public async Task<string> CallMethods(string inStr, JsonRpcClientInfo clientInfo)
        {
            bool is_single = false;
            List<JsonRpcRequest> requestList = new List<JsonRpcRequest>();
            try
            {
                if (inStr.StartsWith("{"))
                {
                    is_single = true;
                    JsonRpcRequest r = inStr._JsonToObject<JsonRpcRequest>();
                    requestList.Add(r);
                }
                else
                {
                    JsonRpcRequest[] rr = inStr._JsonToObject<JsonRpcRequest[]>();
                    requestList = new List<JsonRpcRequest>(rr);
                }
            }
            catch
            {
                throw new JsonRpcException(new JsonRpcError(-32700, "Parse error"));
            }

            List<JsonRpcResponse> response_list = new List<JsonRpcResponse>();

            TaskVar.Set<JsonRpcClientInfo>(clientInfo);
            try
            {
                object param1 = this.Api.StartCall(clientInfo);
                try
                {
                    object param2 = await this.Api.StartCallAsync(clientInfo, param1);
                    try
                    {
                        foreach (JsonRpcRequest req in requestList)
                        {
                            LogDefJsonRpc log = new LogDefJsonRpc()
                            {
                                EndPoints = new LogDefIPEndPoints()
                                {
                                    LocalIP = clientInfo.LocalIP,
                                    LocalPort = clientInfo.LocalPort,
                                    RemoteIP = clientInfo.RemoteIP,
                                    RemotePort = clientInfo.RemotePort,
                                },
                                ConnectedDateTime = clientInfo.ConnectedDateTime,
                                RpcMethodName = req.Method,
                            };

                            try
                            {
                                JsonRpcResponse res = await CallMethod(req);
                                if (req.Id != null) response_list.Add(res);

                                log.RpcResultOk = res.IsOk;
                                if (res.IsOk == false)
                                {
                                    log.RpcError = res.Error;
                                }
                            }
                            catch (Exception ex)
                            {
                                JsonRpcException jsonException;
                                if (ex is JsonRpcException) jsonException = ex as JsonRpcException;
                                else jsonException = new JsonRpcException(new JsonRpcError(-32603, ex._GetSingleException().Message, ex.ToString()));
                                JsonRpcResponseError res = new JsonRpcResponseError()
                                {
                                    Id = req.Id,
                                    Error = jsonException.RpcError,
                                    Result = null,
                                };
                                if (req.Id != null) response_list.Add(res);

                                log.RpcResultOk = false;
                                log.RpcError = res.Error;
                            }

                            log._PostAccessLog(LogTag.JsonRpcRequestProcessor, copyToDebug: log.RpcResultOk == false);
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
                    return response_list[0]._ObjectToJson();
                else
                    return "";
            }
            else
                return response_list._ObjectToJson();
        }
    }

    public class JsonRpcServerConfig
    {
        public int MaxRequestBodyLen { get; set; } = CoresConfig.JsonRpcServerSettings.DefaultMaxRequestBodyLen.Value;
    }

    public abstract class JsonRpcClient
    {
        List<(JsonRpcRequest request, JsonRpcResponse response, Type responseDataType)> StCallQueue = new List<(JsonRpcRequest request, JsonRpcResponse response, Type responseDataType)>();

        public void ST_CallClear() => StCallQueue.Clear();

        protected JsonRpcClient()
        {
            MtBatch = new BatchQueue<MT_QueueItem>(MtBatchProcessProc, 10);
        }

        public JsonRpcResponse<TResponse> ST_CallAdd<TResponse>(string method, object param) where TResponse : class
        {
            var ret = new JsonRpcResponse<TResponse>();

            var add_item = (new JsonRpcRequest(method, param, Str.NewGuid()), ret, typeof(TResponse));

            StCallQueue.Add(add_item);

            return ret;
        }

        public JsonRpcResponse ST_CallAdd(string method, object param, Type resultType)
        {
            JsonRpcResponse ret = (JsonRpcResponse)Activator.CreateInstance(typeof(JsonRpcResponse<>).MakeGenericType(resultType));

            var add_item = (new JsonRpcRequest(method, param, Str.NewGuid()), ret, resultType);

            StCallQueue.Add(add_item);

            return ret;
        }

        public async Task ST_CallAll(bool throwEachError = false)
        {
            if (StCallQueue.Count == 0) return;

            try
            {
                string req = "";
                bool isSingle = false;

                Dictionary<string, (JsonRpcRequest request, JsonRpcResponse response, Type response_data_type)> requestsTable = new Dictionary<string, (JsonRpcRequest request, JsonRpcResponse response, Type response_data_type)>();
                List<JsonRpcRequest> requests = new List<JsonRpcRequest>();

                foreach (var o in this.StCallQueue)
                {
                    requestsTable.Add(o.request.Id, (o.request, o.response, o.responseDataType));
                    requests.Add(o.request);
                }

                //if (requests_table.Count >= 2)
                //    Dbg.WriteLine($"Num_Requests_per_call: {requests_table.Count}");

                if (requestsTable.Count == 1)
                {
                    req = requests[0]._ObjectToJson(compact: true);
                }
                else
                {
                    req = requests._ObjectToJson(compact: true);
                }

                //req.Debug();

                string ret = await SendRequestAndGetResponseImplAsync(req);

                if (ret.StartsWith("{")) isSingle = true;

                List<JsonRpcResponse> retList = new List<JsonRpcResponse>();

                if (isSingle)
                {
                    JsonRpcResponse r = ret._JsonToObject<JsonRpcResponse>();
                    retList.Add(r);
                }
                else
                {
                    JsonRpcResponse[] r = ret._JsonToObject<JsonRpcResponse[]>();
                    retList = new List<JsonRpcResponse>(r);
                }

                foreach (var res in retList)
                {
                    if (res.Id._IsFilled())
                    {
                        if (requestsTable.ContainsKey(res.Id))
                        {
                            var q = requestsTable[res.Id];

                            q.response.Error = res.Error;
                            q.response.Id = res.Id;
                            q.response.Result = res.Result;
                            q.response.Version = res.Version;
                        }
                    }
                }

                if (retList.Count == 1)
                {
                    foreach (var res in retList)
                    {
                        if (res.IsError)
                        {
                            if (res.Id._IsEmpty())
                            {
                                foreach (var q in requestsTable.Values)
                                {
                                    q.response.Error = res.Error;
                                    q.response.Id = res.Id;
                                    q.response.Result = res.Result;
                                    q.response.Version = res.Version;
                                }

                                break;
                            }
                        }
                    }
                }

                if (throwEachError)
                    foreach (var r in retList)
                    {
                        r.ThrowIfError();
                    }
            }
            finally
            {
                ST_CallClear();
            }
        }

        public async Task<JsonRpcResponse<TResponse>> ST_CallOne<TResponse>(string method, object param, bool throwError = false) where TResponse : class
        {
            ST_CallClear();
            try
            {
                JsonRpcResponse<TResponse> res = ST_CallAdd<TResponse>(method, param);
                await ST_CallAll(throwError);
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

        CriticalSection LockMtQueue = new CriticalSection();
        List<MT_QueueItem> MtQueue = new List<MT_QueueItem>();
        SemaphoreSlim MtSemaphore = new SemaphoreSlim(1, 1);

        BatchQueue<MT_QueueItem> MtBatch;

        void MtBatchProcessProc(BatchQueueItem<MT_QueueItem>[] items)
        {
            try
            {
                foreach (var q in items)
                {
                    var item = q.UserItem;
                    item.Response = ST_CallAdd(item.Method, item.Param, item.ResultType);
                }

                ST_CallAll(false)._GetResult();
            }
            catch (Exception ex)
            {
                //ex.ToString().Print();
                foreach (var q in items)
                {
                    var item = q.UserItem;
                    item.Response.Error = new JsonRpcError(-1, ex._GetSingleException().ToString());
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

        public async Task<JsonRpcResponse<TResponse>> MT_Call<TResponse>(string method, object param, bool throwError = false) where TResponse : class
        {
            var response = new JsonRpcResponse<TResponse>();

            MT_QueueItem q = new MT_QueueItem()
            {
                Method = method,
                Param = param,
                ResultType = typeof(TResponse),
                Response = null,
            };

            var b = MtBatch.Add(q);

            await b.CompletedEvent.WaitAsync();

            return (JsonRpcResponse<TResponse>)q.Response;
        }

        public abstract Task<string> SendRequestAndGetResponseImplAsync(string req, CancellationToken cancel = default);

        public abstract int TimeoutMsecs { get; set; }

        public abstract void AddHeader(string name, string value);

        class ProxyInterceptor : IInterceptor
        {
            public JsonRpcClient RpcClient { get; }

            public ProxyInterceptor(JsonRpcClient rpcClient)
            {
                this.RpcClient = rpcClient;
            }

            public void Intercept(IInvocation invocation)
            {
                JObject o = new JObject();
                var inParams = invocation.Method.GetParameters();
                if (invocation.Arguments.Length != inParams.Length) throw new ApplicationException("v.Arguments.Length != in_params.Length");
                for (int i = 0; i < inParams.Length; i++)
                {
                    var p = inParams[i];
                    o.Add(p.Name, JToken.FromObject(invocation.Arguments[i]));
                }
                Task<JsonRpcResponse<object>> callRet = RpcClient.MT_Call<object>(invocation.Method.Name, o, true);

                //ret.Wait();

                //Dbg.WhereThread(v.Method.Name);
                Task<object> ret = GetResponseObjectAsync(callRet);

                var returnType = invocation.Method.ReturnType;

                if (returnType == typeof(Task))
                {
                    invocation.ReturnValue = ret;
                }
                else
                {
                    if (returnType.IsGenericType == false) throw new ApplicationException($"The return type of the method '{invocation.Method.Name}' is not a Task<>.");
                    if (returnType.BaseType != typeof(Task)) throw new ApplicationException($"The return type of the method '{invocation.Method.Name}' is not a Task<>.");

                    var genericArgs = returnType.GetGenericArguments();
                    if (genericArgs.Length != 1) throw new ApplicationException($"The return type of the method '{invocation.Method.Name}' is not a Task<>.");
                    var taskReturnType = genericArgs[0];

                    invocation.ReturnValue = TaskUtil.ConvertTask(ret, typeof(object), taskReturnType);
                    //Dbg.WhereThread(v.Method.Name);
                }
            }

            async Task<object> GetResponseObjectAsync(Task<JsonRpcResponse<object>> o)
            {
                //Dbg.WhereThread();
                await o;
                var result = o._GetResult();
                result.ThrowIfError();
                //Dbg.WhereThread();
                return result.ResultData;
            }
        }

        public virtual TRpcInterface GenerateRpcInterface<TRpcInterface>() where TRpcInterface : class
        {
            ProxyGenerator g = new ProxyGenerator();
            ProxyInterceptor ic = new ProxyInterceptor(this);

            return g.CreateInterfaceProxyWithoutTarget<TRpcInterface>(ic);
        }
    }

    public class JsonRpcHttpClient : JsonRpcClient, IDisposable
    {
        public WebApi WebApi { get; private set; }
        public string ApiBaseUrl { get; set; }

        public override int TimeoutMsecs { get => WebApi.TimeoutMsecs; set => WebApi.TimeoutMsecs = value; }
        public override void AddHeader(string name, string value) => WebApi.AddHeader(name, value);

        public JsonRpcHttpClient(string apiUrl, WebApiOptions webApiOptions = null)
        {
            this.ApiBaseUrl = apiUrl;
            this.WebApi = new WebApi(webApiOptions);
        }

        public override async Task<string> SendRequestAndGetResponseImplAsync(string req, CancellationToken cancel = default)
        {
            WebRet ret = await this.WebApi.SimplePostDataAsync(this.ApiBaseUrl, req._GetBytes_UTF8(), cancel, Consts.MimeTypes.Json);

            return ret.ToString();
        }

        Once DisposedFlag;
        public void Dispose()
        {
            if (DisposedFlag.IsFirstCall())
            {
                this.WebApi.Dispose();
                this.WebApi = null;
            }
        }
    }

    public class JsonRpcHttpClient<TRpcInterface> : JsonRpcHttpClient
        where TRpcInterface : class
    {
        public TRpcInterface Call { get; }
        public JsonRpcHttpClient(string apiUrl, WebApiOptions webApiOptions = null) : base(apiUrl, webApiOptions)
        {
            this.Call = this.GenerateRpcInterface<TRpcInterface>();
        }
    }

    public class JsonRpcClientInfo
    {
        public string LocalIP { get; }
        public int LocalPort { get; }
        public string RemoteIP { get; }
        public int RemotePort { get; }
        public DateTimeOffset ConnectedDateTime { get; }
        public SortedDictionary<string, string> Headers { get; }
        public JsonRpcServer RpcServer { get; }
        public object Param1 { get; set; }
        public object Param2 { get; set; }
        public object Param3 { get; set; }

        public JsonRpcClientInfo(JsonRpcServer rpcServer, string localIp, int localPort, string remoteIp, int remotePort, SortedDictionary<string, string> headers)
        {
            this.RpcServer = rpcServer;
            this.ConnectedDateTime = DateTimeOffset.Now;
            this.LocalIP = localIp._NonNull();
            this.LocalPort = localPort;
            this.RemoteIP = remoteIp._NonNull();
            this.RemotePort = remotePort;
            this.Headers = headers;
        }

        public override string ToString()
        {
            return $"{this.RemoteIP}:{this.RemotePort}";
        }
    }
}

#endif // CORES_BASIC_JSON

