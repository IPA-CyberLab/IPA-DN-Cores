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
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using System.Net;
//using System.Net.Http;
//using System.Net.Http.Headers;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Basic.HttpClientCore;

namespace IPA.Cores.Basic
{
    static partial class CoresConfig
    {
        public static partial class HttpClientSettings
        {
            public static readonly Copenhagen<int> MaxConnectionPerServer = 512;
            public static readonly Copenhagen<int> PooledConnectionLifeTime = 3 * 1000;
        }
    }

    class WebResponseException : Exception
    {
        public WebResponseException(string message) : base(message) { }
    }

    abstract class WebResponseBasic
    {
        public abstract void CheckError();
    }

    class WebSendRecvRequest : IDisposable
    {
        public WebApiMethods Method { get; }
        public string Url { get; }
        public CancellationToken Cancel { get; }
        public string UploadContentType { get; }
        public Stream UploadStream { get; }

        public WebSendRecvRequest(WebApiMethods method, string url, CancellationToken cancel = default,
            string uploadContentType = "application/octet-stream", Stream uploadStream = null)
        {
            this.Method = method;
            this.Url = url;
            this.Cancel = cancel;
            this.UploadContentType = uploadContentType.FilledOrDefault("application/octet-stream");
        }

        public void Dispose() => Dispose(true);
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;
            this.UploadStream.DisposeSafe();
        }
    }

    class WebSendRecvResponse : IDisposable
    {
        public HttpContent DownloadContent { get; }
        public HttpResponseMessage HttpResponseMessage { get; }
        public string DownloadContentType { get; }
        public long? DownloadContentLength { get; }
        public Stream DownloadStream { get; }

        public WebSendRecvResponse(HttpResponseMessage response, Stream downloadStream)
        {
            this.HttpResponseMessage = response;
            this.DownloadContent = response.Content;

            this.DownloadContentType = this.DownloadContent.Headers.ContentType?.MediaType.NonNullTrim();

            if (this.DownloadContent.TryComputeLength(out long length))
                this.DownloadContentLength = length;
            else
                this.DownloadContentLength = response.Content.Headers.ContentLength;

            DownloadStream = downloadStream;
        }

        public void Dispose() => Dispose(true);
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;
            this.HttpResponseMessage.DisposeSafe();
            this.DownloadContent.DisposeSafe();
            this.DownloadStream.DisposeSafe();
        }
    }

    partial class WebRet
    {
        public string Url { get; }
        public string ContentType { get; }
        public byte[] Data { get; }
        public string MediaType { get; }
        public string CharSet { get; }
        public Encoding DefaultEncoding { get; } = null;
        public WebApi Api { get; }

        public WebRet(WebApi api, string url, string contentType, byte[] data)
        {
            this.Api = api;
            this.Url = url.NonNull();
            this.ContentType = contentType.NonNull();

            try
            {
                var ct = new System.Net.Mime.ContentType(this.ContentType);
                this.MediaType = ct.MediaType.NonNull();
                this.CharSet = ct.CharSet.NonNull();
            }
            catch
            {
                this.MediaType = this.ContentType;
                this.CharSet = "";
            }

            try
            {
                if (this.CharSet.IsFilled())
                {
                    this.DefaultEncoding = Encoding.GetEncoding(this.CharSet);
                }
            }
            catch
            {
            }

            if (this.DefaultEncoding == null)
            {
                this.DefaultEncoding = api.RequestEncoding;
            }

            this.Data = data.NonNull();

            if (this.Api.DebugPrintResponse)
            {
                this.DebugObject();
            }
        }

        public override string ToString() => this.Data.GetString(this.DefaultEncoding);
        public string ToString(Encoding encoding) => this.Data.GetString(encoding);
    }

    enum WebApiMethods
    {
        GET,
        DELETE,
        POST,
        PUT,
    }

    partial class WebApi : IDisposable
    {
        static GlobalInitializer gInit = new GlobalInitializer();

        public const int DefaultTimeoutMsecs = 60 * 1000;
        public int TimeoutMsecs { get => (int)Client.Timeout.TotalMilliseconds; set => Client.Timeout = new TimeSpan(0, 0, 0, 0, value); }

        public const long DefaultMaxRecvSize = 100 * 1024 * 1024;
        public long MaxRecvSize { get => this.Client.MaxResponseContentBufferSize; set => this.Client.MaxResponseContentBufferSize = value; }
        public bool SslAcceptAnyCerts { get; set; } = false;
        public bool UseProxy { get; set; } = true;
        public List<string> SslAcceptCertSHA1HashList { get; set; } = new List<string>();
        public Encoding RequestEncoding { get; set; } = Str.Utf8Encoding;

        public bool DebugPrintResponse { get; set; } = false;

        public SortedList<string, string> RequestHeaders = new SortedList<string, string>();

        SocketsHttpHandler ClientHandler;

        public X509CertificateCollection ClientCerts { get => this.ClientHandler.SslOptions.ClientCertificates; }

        public HttpClient Client { get; private set; }

        public WebApi()
        {
            this.ClientHandler = new SocketsHttpHandler();

            this.ClientHandler.AllowAutoRedirect = true;
            this.ClientHandler.MaxAutomaticRedirections = 10;
            this.ClientHandler.MaxConnectionsPerServer = CoresConfig.HttpClientSettings.MaxConnectionPerServer;
            this.ClientHandler.PooledConnectionLifetime = Util.ConvertTimeSpan((ulong)CoresConfig.HttpClientSettings.PooledConnectionLifeTime.Value);

            this.Client = new HttpClient(this.ClientHandler, true);
            this.MaxRecvSize = WebApi.DefaultMaxRecvSize;
            this.TimeoutMsecs = WebApi.DefaultTimeoutMsecs;
        }

        public void Dispose() => Dispose(true);
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;
            this.Client.DisposeSafe();
            this.Client = null;
        }


        public string BuildQueryString(params (string name, string value)[] queryList)
        {
            StringWriter w = new StringWriter();
            int count = 0;
            if (queryList != null)
            {
                foreach (var t in queryList)
                {
                    if (count != 0)
                    {
                        w.Write("&");
                    }
                    w.Write($"{t.name.EncodeUrl(this.RequestEncoding)}={t.value.EncodeUrl(this.RequestEncoding)}");
                    count++;
                }
            }
            return w.ToString();
        }

        public void AddHeader(string name, string value)
        {
            if (name.IsEmpty()) return;
            lock (this.RequestHeaders)
            {
                if (value.IsEmpty() == false)
                {
                    if (this.RequestHeaders.ContainsKey(name))
                        this.RequestHeaders[name] = value;
                    else
                        this.RequestHeaders.Add(name, value);
                }
                else
                {
                    if (this.RequestHeaders.ContainsKey(name))
                        this.RequestHeaders.Remove(name);
                }
            }
        }

        virtual protected HttpRequestMessage CreateWebRequest(WebApiMethods method, string url, params (string name, string value)[] queryList)
        {
            string qs = "";

            if (method == WebApiMethods.GET || method == WebApiMethods.DELETE)
            {
                qs = BuildQueryString(queryList);
                if (qs.IsEmpty() == false)
                {
                    url = url + "?" + qs;
                }
            }

            HttpRequestMessage requestMessage = new HttpRequestMessage(new HttpMethod(method.ToString()), url);

            CacheControlHeaderValue cacheControl = new CacheControlHeaderValue();
            cacheControl.NoStore = true;
            cacheControl.NoCache = true;
            requestMessage.Headers.CacheControl = cacheControl;

            try
            {
                if (this.SslAcceptAnyCerts)
                {
                    this.ClientHandler.SslOptions.RemoteCertificateValidationCallback = (message, cert, chain, errors) => true;
                }
                else if (this.SslAcceptCertSHA1HashList != null && SslAcceptCertSHA1HashList.Count >= 1)
                {
                    this.ClientHandler.SslOptions.RemoteCertificateValidationCallback = (message, cert, chain, errors) =>
                    {
                        foreach (var s in this.SslAcceptCertSHA1HashList)
                            if (cert.GetCertHashString().IsSamei(s)) return true;
                        return false;
                    };
                }
            }
            catch { }

            try
            {
                this.ClientHandler.UseProxy = this.UseProxy;
            }
            catch { }

            foreach (string name in this.RequestHeaders.Keys)
            {
                string value = this.RequestHeaders[name];
                requestMessage.Headers.Add(name, value);
            }

            return requestMessage;
        }

        public static void ThrowIfError(HttpResponseMessage res)
        {
            res.EnsureSuccessStatusCode();
        }

        public async Task<WebRet> SimpleQueryAsync(WebApiMethods method, string url, CancellationToken cancel = default, string postContentType = "application/x-www-form-urlencoded", params (string name, string value)[] queryList)
        {
            if (postContentType.IsEmpty()) postContentType = "application/x-www-form-urlencoded";
            HttpRequestMessage r = CreateWebRequest(method, url, queryList);

            if (method == WebApiMethods.POST || method == WebApiMethods.PUT)
            {
                string qs = BuildQueryString(queryList);

                r.Content = new StringContent(qs, this.RequestEncoding, postContentType);
            }

            using (HttpResponseMessage res = await this.Client.SendAsync(r, HttpCompletionOption.ResponseContentRead, cancel))
            {
                ThrowIfError(res);
                byte[] data = await res.Content.ReadAsByteArrayAsync();
                return new WebRet(this, url, res.Content.Headers.TryGetContentType(), data);
            }
        }


        public async Task<WebRet> SimplePostDataAsync(string url, byte[] postData, CancellationToken cancel = default, string postContentType = "application/json")
        {
            if (postContentType.IsEmpty()) postContentType = "application/json";
            HttpRequestMessage r = CreateWebRequest(WebApiMethods.POST, url, null);

            r.Content = new ByteArrayContent(postData);
            r.Content.Headers.ContentType = new MediaTypeHeaderValue(postContentType);

            using (HttpResponseMessage res = await this.Client.SendAsync(r, HttpCompletionOption.ResponseContentRead, cancel))
            {
                ThrowIfError(res);
                byte[] data = await res.Content.ReadAsByteArrayAsync();
                string type = res.Content.Headers.TryGetContentType();
                return new WebRet(this, url, res.Content.Headers.TryGetContentType(), data);
            }
        }


        public virtual async Task<WebRet> SimplePostJsonAsync(WebApiMethods method, string url, string jsonString, CancellationToken cancel = default)
        {
            if (!(method == WebApiMethods.POST || method == WebApiMethods.PUT)) throw new ArgumentException($"Invalid method: {method.ToString()}");

            HttpRequestMessage r = CreateWebRequest(method, url, null);

            byte[] upload_data = jsonString.GetBytes(this.RequestEncoding);

            r.Content = new ByteArrayContent(upload_data);
            r.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            using (HttpResponseMessage res = await this.Client.SendAsync(r, HttpCompletionOption.ResponseContentRead, cancel))
            {
                ThrowIfError(res);
                byte[] data = await res.Content.ReadAsByteArrayAsync();
                return new WebRet(this, url, res.Content.Headers.TryGetContentType(), data);
            }
        }

        public virtual async Task<WebSendRecvResponse> HttpSendRecvDataAsync(WebSendRecvRequest request)
        {
            HttpRequestMessage r = CreateWebRequest(request.Method, request.Url);

            HttpResponseMessage res = await this.Client.SendAsync(r, HttpCompletionOption.ResponseHeadersRead);
            try
            {
                ThrowIfError(res);

                return new WebSendRecvResponse(res, await res.Content.ReadAsStreamAsync());
            }
            catch
            {
                res.DisposeSafe();
                throw;
            }
        }
    }
}

