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

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using System.Net;
using System.ComponentModel.DataAnnotations;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

//using System.Net.Http;
//using System.Net.Http.Headers;
using IPA.Cores.Basic.HttpClientCore;
using System.Security.Cryptography;
using System.Linq;
using Org.BouncyCastle.Ocsp;

#pragma warning disable CA2235 // Mark all non-serializable fields

namespace IPA.Cores.Basic
{
    public static partial class CoresConfig
    {
        public static partial class DefaultHttpClientSettings
        {
            public static readonly Copenhagen<int> MaxConnectionPerServer = 512;
            public static readonly Copenhagen<int> PooledConnectionLifeTime = 5 * 60 * 1000;
            public static readonly Copenhagen<int> Timeout = 60 * 1000;
            public static readonly Copenhagen<int> MaxRecvSize = 100 * 1024 * 1024;
            public static readonly Copenhagen<bool> UseProxy = true;
        }
    }

    public class WebResponseException : Exception
    {
        public WebResponseException(string message) : base(message) { }
    }

    public interface IValidatable
    {
        void Validate();
    }

    public static partial class IErrorCheckableHelper
    {
        // IErrorCheckable から IErrorCheckableHelper の実装に必要な Validate() 関数の応答を返す
        public static IEnumerable<ValidationResult> _Validate(this IValidatable targetObject, ValidationContext validationContext)
        {
            try
            {
                if (targetObject is INormalizable norm) norm.Normalize();

                targetObject.Validate();
            }
            catch (Exception ex)
            {
                ValidationResult err = new ValidationResult(ex.Message);

                return err._SingleArray();
            }

            return EmptyEnumerable<ValidationResult>.Empty;
        }
    }

    public class WebSendRecvRequest : IDisposable
    {
        public WebMethods Method { get; }
        public string Url { get; }
        public CancellationToken Cancel { get; }
        public string UploadContentType { get; }
        public Stream? UploadStream { get; }
        public long? RangeStart { get; }
        public long? RangeLength { get; }

        public WebSendRecvRequest(WebMethods method, string url, CancellationToken cancel = default,
            string uploadContentType = Consts.MimeTypes.OctetStream, Stream? uploadStream = null,
            long? rangeStart = null, long? rangeLength = null)
        {
            if (rangeStart == null && rangeLength != null) throw new ArgumentOutOfRangeException("rangeStart == null && rangeLength != null");
            if ((rangeStart ?? 0) < 0) throw new ArgumentOutOfRangeException(nameof(rangeStart));
            if ((rangeLength ?? 1) <= 0) throw new ArgumentOutOfRangeException(nameof(rangeLength));

            this.RangeStart = rangeStart;
            this.RangeLength = rangeLength;

            checked
            {
                Limbo.SInt64 = this.RangeStart ?? 0 + this.RangeLength ?? 0;
            }

            this.Method = method;
            this.Url = url;
            this.Cancel = cancel;
            this.UploadContentType = uploadContentType._FilledOrDefault(Consts.MimeTypes.OctetStream);
            this.UploadStream = uploadStream;
        }

        public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;
            this.UploadStream._DisposeSafe();
        }
    }

    public class WebSendRecvResponse : IDisposable
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

            this.DownloadContentType = (this.DownloadContent.Headers.ContentType?.MediaType)._NonNullTrim();

            if (this.DownloadContent.TryComputeLength(out long length))
                this.DownloadContentLength = length;
            else
                this.DownloadContentLength = response.Content.Headers.ContentLength;

            DownloadStream = downloadStream;
        }

        public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;
            this.HttpResponseMessage._DisposeSafe();
            this.DownloadContent._DisposeSafe();
            this.DownloadStream._DisposeSafe();
        }
    }

    public class WebUserRet<TUser> : SystemAndUser<WebRet, TUser>
    {
        public WebUserRet(WebRet web, TUser user) : base(web, user)
        {
        }
    }

    public partial class WebRet
    {
        public const string MediaTypeJson = Consts.MimeTypes.Json;

        public string Url { get; }
        public string ContentType { get; }
        public byte[] Data { get; }
        public string MediaType { get; }
        public string CharSet { get; }
        public Encoding DefaultEncoding { get; } = null!;
        public WebApi Api { get; }
        public HttpResponseHeaders Headers { get; }

        public WebRet(WebApi api, string url, string contentType, byte[] data, HttpResponseHeaders headers)
        {
            this.Api = api;
            this.Url = url._NonNull();
            this.ContentType = contentType._NonNull();
            this.Headers = headers;

            try
            {
                var ct = new System.Net.Mime.ContentType(this.ContentType);
                this.MediaType = ct.MediaType._NonNull();
                this.CharSet = ct.CharSet._NonNull();
            }
            catch
            {
                this.MediaType = this.ContentType;
                this.CharSet = "";
            }

            try
            {
                if (this.CharSet._IsFilled())
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

            this.Data = data._NonNull();

            if (this.Api.DebugPrintResponse)
            {
                this._DebugObject();
            }
        }

        public override string ToString() => this.Data._GetString(this.DefaultEncoding);
        public string ToString(Encoding encoding) => this.Data._GetString(encoding);

        public WebUserRet<TUser> CreateUserRet<TUser>(TUser userData) => new WebUserRet<TUser>(this, userData);
    }

    [Flags]
    public enum WebMethods
    {
        GET,
        DELETE,
        POST,
        PUT,
        HEAD,
    }

    [Flags]
    public enum WebMethodBits
    {
        GET = 1,
        DELETE = 2,
        POST = 4,
        PUT = 8,
        HEAD = 16,
    }

    [Serializable]
    public class WebApiSettings
    {
        public int Timeout = CoresConfig.DefaultHttpClientSettings.Timeout;
        public int MaxRecvSize = CoresConfig.DefaultHttpClientSettings.MaxRecvSize;
        public int MaxConnectionPerServer = CoresConfig.DefaultHttpClientSettings.MaxConnectionPerServer;
        public int PooledConnectionLifeTime = CoresConfig.DefaultHttpClientSettings.PooledConnectionLifeTime;
        public bool UseProxy = CoresConfig.DefaultHttpClientSettings.UseProxy;
        public bool DisableKeepAlive = false;

        public bool AllowAutoRedirect = true;
        public int MaxAutomaticRedirections = 10;

        public bool SslAcceptAnyCerts = false;
        public List<string> SslAcceptCertSHAHashList = new List<string>();

        public bool DebugPrintResponse = false;
    }

    public class WebApiOptions
    {
        public WebApiSettings Settings { get; }
        public TcpIpSystem TcpIp { get; }

        public WebApiOptions(WebApiSettings? settings = null, TcpIpSystem? tcpIp = null)
        {
            if (settings == null) settings = new WebApiSettings();
            if (tcpIp == null) tcpIp = LocalNet;

            this.Settings = settings;
            this.TcpIp = tcpIp;
        }
    }

    public partial class WebApi : IDisposable
    {
        WebApiSettings Settings;

        public int TimeoutMsecs { get => (int)Client.Timeout.TotalMilliseconds; set => Client.Timeout = new TimeSpan(0, 0, 0, 0, value); }
        public long MaxRecvSize { get => this.Client.MaxResponseContentBufferSize; set => this.Client.MaxResponseContentBufferSize = value; }

        public Encoding RequestEncoding { get; set; } = Encoding.UTF8;

        public SortedList<string, string> RequestHeaders = new SortedList<string, string>();

        SocketsHttpHandler ClientHandler;

        public X509CertificateCollection ClientCerts { get => this.ClientHandler.SslOptions.ClientCertificates; }

        public HttpClient Client { get; private set; }

        public bool DebugPrintResponse => this.Settings.DebugPrintResponse;

        public WebApi(WebApiOptions? options = null)
        {
            if (options == null) options = new WebApiOptions();

            this.Settings = options.Settings._CloneDeep();

            this.ClientHandler = new SocketsHttpHandler(options.TcpIp);

            this.ClientHandler.AllowAutoRedirect = this.Settings.AllowAutoRedirect;
            this.ClientHandler.MaxAutomaticRedirections = this.Settings.MaxAutomaticRedirections;
            this.ClientHandler.MaxConnectionsPerServer = this.Settings.MaxConnectionPerServer;
            this.ClientHandler.PooledConnectionLifetime = Util.ConvertTimeSpan(this.Settings.PooledConnectionLifeTime);

            this.Client = new HttpClient(this.ClientHandler, true);
            this.MaxRecvSize = this.Settings.MaxRecvSize;
            this.TimeoutMsecs = this.Settings.Timeout;
        }

        public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;
            this.Client._DisposeSafe();
            this.Client = null!;
        }


        public string BuildQueryString(params (string name, string? value)[]? queryList)
        {
            StringWriter w = new StringWriter();
            int count = 0;
            if (queryList != null)
            {
                foreach (var t in queryList)
                {
                    if (t.value != null)
                    {
                        if (count != 0)
                        {
                            w.Write("&");
                        }
                        w.Write($"{t.name._EncodeUrl(this.RequestEncoding)}={t.value._EncodeUrl(this.RequestEncoding)}");
                        count++;
                    }
                }
            }
            return w.ToString();
        }

        public void AddHeader(string name, string value)
        {
            if (name._IsEmpty()) return;
            lock (this.RequestHeaders)
            {
                if (value._IsEmpty() == false)
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

        virtual protected HttpRequestMessage CreateWebRequest(WebMethods method, string url, params (string name, string? value)[]? queryList)
        {
            string qs = "";

            if (method == WebMethods.GET || method == WebMethods.DELETE || method == WebMethods.HEAD)
            {
                qs = BuildQueryString(queryList);
                if (qs._IsEmpty() == false)
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
                if (this.Settings.SslAcceptAnyCerts)
                {
                    this.ClientHandler.SslOptions.RemoteCertificateValidationCallback = (message, cert, chain, errors) => true;
                }
                else if (this.Settings.SslAcceptCertSHAHashList != null && this.Settings.SslAcceptCertSHAHashList.Count >= 1)
                {
                    this.ClientHandler.SslOptions.RemoteCertificateValidationCallback = (message, cert, chain, errors) =>
                    {
                        foreach (var s in this.Settings.SslAcceptCertSHAHashList.Select(x => x._NormalizeHexString()))
                        {
                            if (cert.GetCertHashString(HashAlgorithmName.SHA1)._IsSamei(s)) return true;
                            if (cert.GetCertHashString(HashAlgorithmName.SHA256)._IsSamei(s)) return true;
                            if (cert.GetCertHashString(HashAlgorithmName.SHA512)._IsSamei(s)) return true;
                        }
                        return false;
                    };
                }
            }
            catch { }

            try
            {
                if (this.ClientHandler.UseProxy != this.Settings.UseProxy)
                {
                    this.ClientHandler.UseProxy = this.Settings.UseProxy;
                }
            }
            catch { }

            foreach (string name in this.RequestHeaders.Keys)
            {
                string value = this.RequestHeaders[name];
                requestMessage.Headers.Add(name, value);
            }

            if (this.Settings.DisableKeepAlive)
            {
                try
                {
                    requestMessage.Headers.Add("Connection", "close");
                }
                catch { }
            }

            return requestMessage;
        }

        public virtual async Task ThrowIfErrorAsync(HttpResponseMessage res)
        {
            if (res.IsSuccessStatusCode) return;

            string details = "";

            try
            {
                byte[] data = await res.Content.ReadAsByteArrayAsync();

                details = data._GetString_UTF8()._OneLine(" ");
            }
            catch { }

            string errStr = string.Format("Response status code does not indicate success: {0} ({1}).", (int)res.StatusCode, res.ReasonPhrase);

            if (details != null)
            {
                errStr += " Details: " + details._TruncStr(1024);
            }

            throw new HttpRequestException(errStr);
        }

        public virtual async Task<WebRet> SimpleQueryAsync(WebMethods method, string url, CancellationToken cancel = default, string? postContentType = Consts.MimeTypes.FormUrlEncoded, params (string name, string? value)[] queryList)
        {
            if (postContentType._IsEmpty()) postContentType = Consts.MimeTypes.FormUrlEncoded;
            HttpRequestMessage r = CreateWebRequest(method, url, queryList);

            if (method == WebMethods.POST || method == WebMethods.PUT)
            {
                string qs = BuildQueryString(queryList);

                r.Content = new StringContent(qs, this.RequestEncoding, postContentType);
            }

            using (HttpResponseMessage res = await this.Client.SendAsync(r, HttpCompletionOption.ResponseContentRead, cancel))
            {
                await ThrowIfErrorAsync(res);
                byte[] data = await res.Content.ReadAsByteArrayAsync();
                return new WebRet(this, url, res.Content.Headers._TryGetContentType(), data, res.Headers);
            }
        }


        public virtual async Task<WebRet> SimplePostDataAsync(string url, byte[] postData, CancellationToken cancel = default, string postContentType = Consts.MimeTypes.Json)
        {
            if (postContentType._IsEmpty()) postContentType = Consts.MimeTypes.Json;
            HttpRequestMessage r = CreateWebRequest(WebMethods.POST, url);

            r.Content = new ByteArrayContent(postData);
            r.Content.Headers.ContentType = new MediaTypeHeaderValue(postContentType);

            using (HttpResponseMessage res = await this.Client.SendAsync(r, HttpCompletionOption.ResponseContentRead, cancel))
            {
                await ThrowIfErrorAsync(res);
                byte[] data = await res.Content.ReadAsByteArrayAsync();
                string type = res.Content.Headers._TryGetContentType();
                return new WebRet(this, url, res.Content.Headers._TryGetContentType(), data, res.Headers);
            }
        }


        public virtual async Task<WebRet> SimplePostJsonAsync(WebMethods method, string url, string jsonString, CancellationToken cancel = default, string postContentType = Consts.MimeTypes.Json)
        {
            if (postContentType._IsEmpty()) postContentType = Consts.MimeTypes.Json;

            if (!(method == WebMethods.POST || method == WebMethods.PUT)) throw new ArgumentException($"Invalid method: {method.ToString()}");

            HttpRequestMessage r = CreateWebRequest(method, url);

            byte[] upload_data = jsonString._GetBytes(this.RequestEncoding);

            r.Content = new ByteArrayContent(upload_data);
            r.Content.Headers.ContentType = new MediaTypeHeaderValue(postContentType);

            using (HttpResponseMessage res = await this.Client.SendAsync(r, HttpCompletionOption.ResponseContentRead, cancel))
            {
                await ThrowIfErrorAsync(res);
                byte[] data = await res.Content.ReadAsByteArrayAsync();
                return new WebRet(this, url, res.Content.Headers._TryGetContentType(), data, res.Headers);
            }
        }

        public virtual async Task<WebSendRecvResponse> HttpSendRecvDataAsync(WebSendRecvRequest request)
        {
            HttpRequestMessage r = CreateWebRequest(request.Method, request.Url);

            if (request.RangeStart != null)
            {
                // 一部分のみ指定してダウンロード
                r.Headers.Range = new RangeHeaderValue(request.RangeStart, request.RangeLength.HasValue ? request.RangeStart + request.RangeLength - 1 : null);
            }

            if (request.Method.EqualsAny(WebMethods.POST, WebMethods.PUT))
            {
                if (request.UploadStream == null)
                    throw new ArgumentException("request.UploadStream == null");

                r.Content = new StreamContent(request.UploadStream);
                r.Content.Headers.ContentType = new MediaTypeHeaderValue(request.UploadContentType);
            }
            else
            {
                if (request.UploadStream != null)
                    throw new ArgumentException($"request.UploadStream != null, but the specified method is {request.Method}.");
            }

            HttpResponseMessage res = await this.Client.SendAsync(r, HttpCompletionOption.ResponseHeadersRead, request.Cancel);
            try
            {
                await ThrowIfErrorAsync(res);

                return new WebSendRecvResponse(res, await res.Content.ReadAsStreamAsync());
            }
            catch
            {
                res._DisposeSafe();
                throw;
            }
        }
    }

    // 任意の Stream の一部を HTTP 応答するクラス
    public partial class HttpResult : IDisposable
    {
        public int StatusCode { get; }
        public string? ContentType { get; }

        public ReadOnlyMemory<byte> PreData { get; }
        public ReadOnlyMemory<byte> PostData { get; }

        public Stream Stream { get; }
        public long Offset { get; }
        public long? Length { get; }

        public bool DisposeStream { get; }

        public HttpResult(Stream stream, long offset, long? length, string? contentType = Consts.MimeTypes.TextUtf8, int statusCode = Consts.HttpStatusCodes.Ok, bool disposeStream = true,
            ReadOnlyMemory<byte> preData = default, ReadOnlyMemory<byte> postData = default)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            if ((length ?? long.MaxValue) < 0) throw new ArgumentOutOfRangeException(nameof(length));
            if (statusCode == 0) throw new ArgumentOutOfRangeException(nameof(statusCode));

            contentType = contentType._NullIfEmpty();

            this.Stream = stream;
            this.Offset = offset;
            this.Length = length;
            this.ContentType = contentType;
            this.StatusCode = statusCode;
            this.PreData = preData;
            this.PostData = postData;

            this.DisposeStream = disposeStream;
        }

        public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;

            if (this.DisposeStream)
            {
                this.Stream._DisposeSafe();
            }
        }
    }

    // すでに用意されたメモリ配列を HTTP 応答するクラス
    public class HttpMemoryResult : HttpResult
    {
        public HttpMemoryResult(ReadOnlySpan<byte> data, string contentType = Consts.MimeTypes.OctetStream, int statusCode = Consts.HttpStatusCodes.Ok)
            : base(data._ToMemoryStream(), 0, data.Length, contentType, statusCode) { }
    }

    // すでに用意された文字列データを HTTP 応答するクラス
    public class HttpStringResult : HttpMemoryResult
    {
        public HttpStringResult(string str, string contentType = Consts.MimeTypes.TextUtf8, int statusCode = Consts.HttpStatusCodes.Ok, Encoding? encoding = null)
            : base(str._NonNull()._GetBytes(encoding ?? Str.Utf8Encoding), contentType, statusCode) { }
    }

    // 指定されたファイルを HTTP 応答するクラス
    public class HttpFileResult : HttpResult
    {
        public HttpFileResult(FileBase file, long offset, long? length, string contentType = Consts.MimeTypes.OctetStream, int statusCode = Consts.HttpStatusCodes.Ok, bool disposeFile = true,
            ReadOnlyMemory<byte> preData = default, ReadOnlyMemory<byte> postData = default)
            : base(file.GetStream(disposeFile), offset, length, contentType, statusCode, true, preData, postData) { }
    }
}

