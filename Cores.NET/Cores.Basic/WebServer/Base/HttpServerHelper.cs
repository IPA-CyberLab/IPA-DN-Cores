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
using System.Text;
using System.IO;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Collections.Generic;
using Microsoft.Extensions.Primitives;
using System.Linq;

namespace IPA.Cores.Helper.Basic
{
    public static class WebServerHelper
    {
        public static CancellationToken _GetRequestCancellationToken(this HttpResponse h) => h.HttpContext.RequestAborted;
        public static CancellationToken _GetRequestCancellationToken(this HttpRequest h) => h.HttpContext.RequestAborted;
        public static CancellationToken _GetRequestCancellationToken(this HttpContext h) => h.RequestAborted;

        public static async Task _SendHttpResultAsync(this HttpResponse h, HttpResult result, CancellationToken cancel = default)
        {
            if (result.Offset != 0)
            {
                result.Stream.Seek(result.Offset, SeekOrigin.Begin);
            }

            await h._SendStreamContents(result.Stream, result.Length, result.ContentType, cancel);
        }

        public static Task _SendStringContents(this HttpResponse h, string body, string contentsType = Consts.MimeTypes.TextUtf8, Encoding encoding = null, CancellationToken cancel = default(CancellationToken))
        {
            if (encoding == null) encoding = Str.Utf8Encoding;
            byte[] ret_data = encoding.GetBytes(body);

            h.ContentType = contentsType;
            h.ContentLength = ret_data.Length;
            return h.Body.WriteAsync(ret_data, 0, ret_data.Length, cancel);
        }

        public static async Task<string> _RecvStringContents(this HttpRequest h, int maxRequestBodyLen = int.MaxValue, Encoding encoding = null, CancellationToken cancel = default(CancellationToken))
        {
            if (encoding == null) encoding = Str.Utf8Encoding;

            return (await h.Body._ReadToEndAsync(maxRequestBodyLen, cancel))._GetString_UTF8();
        }

        public static async Task _SendFileContents(this HttpResponse h, FileBase file, long offset, long? count, string contentsType = Consts.MimeTypes.OctetStream, CancellationToken cancel = default)
        {
            CheckStreamRange(offset, count, file.Size);

            using (FileStream srcStream = file.GetStream(false))
            {
                if (offset > 0)
                {
                    srcStream.Seek(offset, SeekOrigin.Begin);
                }

                await h._SendStreamContents(srcStream, count, contentsType, cancel);
            }
        }

        public static async Task _SendStreamContents(this HttpResponse h, Stream sourceStream, long? count, string contentsType = Consts.MimeTypes.OctetStream, CancellationToken cancel = default)
        {
            h.ContentType = contentsType;
            h.ContentLength = count;

            await StreamCopyOperation.CopyToAsync(sourceStream, h.Body, count, cancel);
        }

        // From: https://github.com/aspnet/HttpAbstractions/blob/31a836c9f35987c736161bf6e3f763517da8d504/src/Microsoft.AspNetCore.Http.Extensions/SendFileResponseExtensions.cs
        // Copyright (c) .NET Foundation. All rights reserved.
        // Licensed under the Apache License, Version 2.0.
        // License: https://github.com/aspnet/HttpAbstractions/blob/31a836c9f35987c736161bf6e3f763517da8d504/LICENSE.txt
        static void CheckStreamRange(long offset, long? count, long fileLength)
        {
            if (offset < 0 || offset > fileLength)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), offset, string.Empty);
            }
            if (count.HasValue &&
                (count.GetValueOrDefault() < 0 || count.GetValueOrDefault() > fileLength - offset))
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, string.Empty);
            }
        }

        public static string _GetRequestPathAndQueryString(this HttpRequest r)
        {
            string path = r.Path.ToString()._NonNull();
            string qs = r.QueryString.ToString()._NonNull();

            if (qs._IsNullOrZeroLen() == false)
            {
                return path + qs;
            }
            else
            {
                return path;
            }
        }

        public static string _GetQueryStringFirst(this HttpRequest r, string key, string defaultStr = "", StringComparison comparison = StringComparison.OrdinalIgnoreCase, bool autoTrim = true)
            => _GetQueryStringFirst(r.Query, key, defaultStr, comparison, autoTrim);

        public static string _GetQueryStringFirst(this IQueryCollection d, string key, string defaultStr = "", StringComparison comparison = StringComparison.OrdinalIgnoreCase, bool autoTrim = true)
        {
            if (key._IsEmpty()) throw new ArgumentNullException(nameof(key));

            if (d == null) return defaultStr;

            var matchList = d.Where(x => x.Key._IsSameTrim(key, comparison));
            if (matchList.Any() == false) return defaultStr;

            StringValues values = matchList.First().Value;

            string ret = values.Where(x => (autoTrim ? x._IsFilled() : x != null)).FirstOrDefault();

            if (ret._IsEmpty())
            {
                return defaultStr;
            }

            if (autoTrim)
            {
                ret = ret._NonNullTrim();
            }

            return ret;
        }
    }
}

#endif // CORES_BASIC_WEBAPP

