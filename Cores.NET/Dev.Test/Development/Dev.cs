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
// 開発中のクラスの一時置き場

#if true

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;

using IPA.Cores.Basic;
using IPA.Cores.Basic.DnsLib;
using IPA.Cores.Basic.Internal;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Net.Sockets;

using Newtonsoft.Json;
using System.Data;
using System.Reflection;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace IPA.Cores.Basic;

public enum S3FsClientFlags
{
    None = 0,
    AmazonS3 = 1,
}

public class S3FsClientConfig : INormalizable
{
    public S3FsClientFlags Flags = S3FsClientFlags.None;

    public string BaseUrl = "";

    public string AccessKey = "";
    public string SecretKey = "";
    public string BucketName = "";

    public void Normalize()
    {
    }
}

public class S3FsRequest
{
    public S3FsClientConfig Config = null!;
    public WebMethods Method = WebMethods.GET;
    public string ContentMD5 = "";
    public string ContentType = Consts.MimeTypes.OctetStream;
    public DateTimeOffset Date = Util.ZeroDateTimeOffsetValue;

    public string VirtualPath = "";
    public string SubResource = "";

    public long? RangeStart = null;
    public long? RangeLength = null;

    public StrDictionary<List<string>> Headers = new StrDictionary<List<string>>(StrCmpi);

    public string GenerateAuthorizationHeader()
    {
        return $"AWS {this.Config.AccessKey}:{GenerateSignature()}";
    }

    public string GenerateSignature()
    {
        // https://docs.aws.amazon.com/ja_jp/AmazonS3/latest/userguide/RESTAuthentication.html#ConstructingTheAuthenticationHeader
        string strToSign = GenerateStringToSign();

        byte[] signBytes = Secure.HMacSHA1(this.Config.SecretKey._GetBytes_UTF8(), strToSign._GetBytes_UTF8());

        return Str.Base64Encode(signBytes);
    }

    public string GenerateStringToSign()
    {
        // https://docs.aws.amazon.com/ja_jp/AmazonS3/latest/userguide/RESTAuthentication.html#ConstructingTheAuthenticationHeader
        StringWriter w = new StringWriter();
        w.NewLine = Str.NewLine_Str_Unix;

        w.WriteLine(this.Method.ToString());
        w.WriteLine(this.ContentMD5);

        if (this.Method == WebMethods.POST || this.Method == WebMethods.PUT)
        {
            w.WriteLine(this.ContentType);
        }
        else
        {
            w.WriteLine();
        }

        w.WriteLine(this.Date.ToUniversalTime().ToString("r"));

        if (this.Config.Flags.Bit(S3FsClientFlags.AmazonS3))
        {
            foreach (var kv in this.Headers.OrderBy(x => x.Key.ToLowerInvariant()))
            {
                string key = kv.Key;
                var valueList = kv.Value;

                if (key.StartsWith("x-amz-", StringComparison.OrdinalIgnoreCase))
                {
                    if (key._IsSamei("x-amz-date") == false)
                    {
                        string key2 = key.ToLowerInvariant();

                        string txt = key2 + ":" + valueList._Combine(",");

                        w.WriteLine(txt);
                    }
                }
            }
        }

        string canonicalizedResource = "";

        var baseUri = this.Config.BaseUrl._ParseUrl();
        if (baseUri.AbsolutePath == "" || baseUri.AbsolutePath == "/")
        {
            canonicalizedResource = "/" + this.Config.BucketName;
        }

        canonicalizedResource += this.VirtualPath;

        if (this.SubResource._IsFilled())
        {
            if (this.SubResource.StartsWith("?") == false)
            {
                canonicalizedResource += "?";
            }
            canonicalizedResource += this.SubResource;
        }

        w.Write(canonicalizedResource);

        return w.ToString();
    }
}

public class S3FsClient : WebFsClient
{
    public S3FsClientConfig Config;
    public WebApi Web;

    public S3FsClient(S3FsClientConfig config, WebFsClientSettings? settings = null) : base(settings)
    {
        try
        {
            this.Config = config;

            this.Config.Normalize();

            this.Web = new WebApi(this.Settings.WebOptions);
        }
        catch (Exception ex)
        {
            this._DisposeSafe(ex);
            throw;
        }
    }

    // 任意の絶対パスを、S3 内で通用する仮想パスに変換する (主にパスのチェックをしてディレクトリ区切り記号を正規化するだけ)
    public static string NormalizeVirtualPath(string path, bool isDirectory)
    {
        path = PPLinux.NormalizeDirectorySeparatorAndCheckIfAbsolutePath(path);

        if (path.StartsWith("/") == false)
        {
            throw new CoresLibException($"targetPath '{path}' doesn't start with '/'.");
        }

        if (isDirectory == false)
        {
            if (path.Length <= 1 || path.EndsWith("/"))
            {
                throw new CoresLibException($"targetPath '{path}' is invalid as filename.");
            }
        }
        else
        {
            if (path.EndsWith("/") == false)
            {
                path += "/";
            }
        }

        return path;
    }

    // 任意の絶対パスを、S3 用の URL に変換する
    public string GenerateUrlFromVirtualPath(string path, bool isDirectory)
    {
        path = NormalizeVirtualPath(path, isDirectory);

        if (path.StartsWith("/") == false)
        {
            throw new CoresLibException($"Invalid path: '{path}'");
        }

        path = path.Substring(1);

        string baseUrlTmp = this.Config.BaseUrl;

        if (baseUrlTmp.EndsWith("/") == false)
        {
            baseUrlTmp += "/";
        }

        var baseUri = baseUrlTmp._ParseUrl();

        var ret = baseUri._CombineUrl(path);

        return ret.ToString();
    }

    // リクエストの送付
    public async Task<WebSendRecvResponse> RequestAsync(S3FsRequest req, string url, Stream? uploadStream = null, CancellationToken cancel = default)
    {
        WebRequestOptions options = new WebRequestOptions();

        options.RequestHeaders.Add("Date", req.Date.ToUniversalTime().ToString("r")._SingleList());
        options.RequestHeaders.Add("Authorization", req.GenerateAuthorizationHeader()._SingleList());

        foreach (var kv in req.Headers)
        {
            options.RequestHeaders.Add(kv.Key, kv.Value.ToList());
        }

        WebSendRecvRequest webReq = new WebSendRecvRequest(req.Method, url, cancel, req.ContentType, uploadStream, options: options, rangeStart: req.RangeStart, rangeLength: req.RangeLength);

        var webRes = await this.Web.HttpSendRecvDataAsync(webReq);

        return webRes;
    }

    public async Task Test1Async(CancellationToken cancel = default)
    {
        string path = "/Hello.txt";

        var x = GenerateUrlFromVirtualPath(path, false);

        S3FsRequest req = new S3FsRequest
        {
            Config = this.Config,
            Method = WebMethods.GET,
            Date = DtOffsetNow,
            VirtualPath = NormalizeVirtualPath(path, false),
        };

        string url = GenerateUrlFromVirtualPath(path, false);

        await using var res = await this.RequestAsync(req, url, null, cancel);

        var data = await res.DownloadStream._ReadToEndAsync();

        res.HttpResponseMessage.Content.Headers._Print();

        data._GetString_UTF8()._Print();
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.Web._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

public class WebFsClientSettings
{
    public WebApiOptions WebOptions { get; }

    public WebFsClientSettings(WebApiOptions? webOptions = null)
    {
        if (webOptions == null)
        {
            webOptions = new WebApiOptions(doNotUseTcpStack: true);
        }

        this.WebOptions = webOptions;
    }
}

public abstract class WebFsClient : AsyncService
{
    public WebFsClientSettings Settings { get; }

    public WebFsClient(WebFsClientSettings? settings = null)
    {
        try
        {
            if (settings == null) settings = new WebFsClientSettings();

            this.Settings = settings;
        }
        catch (Exception ex)
        {
            this._DisposeSafe(ex);
            throw;
        }
    }

    protected override Task CleanupImplAsync(Exception? ex)
    {
        return base.CleanupImplAsync(ex);
    }
}



#endif

