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
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Web;
using IPA.Cores.Helper.Web;
using static IPA.Cores.Globals.Web;
using Castle.DynamicProxy.Generators;
using Microsoft.AspNetCore.Diagnostics;

namespace IPA.Cores.Helper.Web
{
    public class AspNetCookieOptions : INormalizable
    {
        public string Domain { get; set; } = "";
        public bool NoJavaScript { get; set; } = false;
        public bool HttpsOnly { get; set; } = false;
        public int Days { get; set; } = Consts.Numbers.MaxCookieDays;
        public string Path { get; set; } = "/";

        public void Normalize()
        {
            if (Days < 0) Days = 0;
            if (Days > Consts.Numbers.MaxCookieDays) Days = Consts.Numbers.MaxCookieDays;

            if (Path._IsEmpty()) Path = "/";
            Domain = Domain._NonNullTrim();
        }

        public AspNetCookieOptions(int days = Consts.Numbers.MaxCookieDays, bool httpsOnly = false, bool noJs = false, string path = "/", string domain = "")
        {
            this.Days = days;
            this.HttpsOnly = httpsOnly;
            this.NoJavaScript = noJs;
            this.Path = path;
            this.Domain = domain;

            this.Normalize();
        }

        public CookieOptions ToCookieOptions()
        {
            this.Normalize();

            CookieOptions ret = new CookieOptions
            {
                Domain = this.Domain,
                Expires = this.Days <= 0 ? (DateTimeOffset ?)null: DateTimeOffset.Now.AddDays(this.Days),
                HttpOnly = this.NoJavaScript,
                Secure = this.HttpsOnly,
                Path = this.Path,
                SameSite = SameSiteMode.Lax,
            };

            return ret;
        }
    }

    public static partial class AspNetExtensions
    {
        public static string GenerateAbsoluteUrl(this ControllerBase c, string actionName)
        {
            string url = Str.BuildHttpUrl(c.Request.Scheme, c.Request.Host.Host, c.Request.Host.Port ?? 0, c.Url.Action(actionName));

            return url;
        }

        // JSON.NET を用いた JSON 応答の生成
        public static IActionResult _AspNetJsonResult(this object obj, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, bool base64url = false, Type? type = null)
        {
            string jsonStr = obj._ObjectToJson(includeNull, escapeHtml, maxDepth, compact, referenceHandling, base64url, type);

            return new ContentResult()
            {
                Content = jsonStr,
                ContentType = Consts.MimeTypes.Json,
                StatusCode = 200,
            };
        }

        public static IMvcBuilder ConfigureMvcWithAspNetLib(this IMvcBuilder mvc, AspNetLib lib)
            => lib.ConfigureAspNetLibMvc(mvc);

        public static HttpActionResult GetHttpActionResult(this HttpResult h)
            => new HttpActionResult(h);

        public static TextActionResult _AspNetTextActionResult(this string str, string contentType = Consts.MimeTypes.TextUtf8, int statusCode = Consts.HttpStatusCodes.Ok, Encoding? encoding = null, IReadOnlyList<KeyValuePair<string, string>>? additionalHeaders = null)
            => new TextActionResult(str, contentType, statusCode, encoding, additionalHeaders);


        // エラーハンドラページで利用できる、最後のエラーの取得
        public static Exception _GetLastError(this Controller controller)
        {
            try
            {
                var errorContext = controller.HttpContext.Features.Get<IExceptionHandlerFeature>();

                return errorContext.Error;
            }
            catch
            {
                throw new CoresLibException("Unknown exception: Failed to Get IExceptionHandlerFeature.");
            }
        }

        // タグ関係
        public static string _BoolToChecked(this bool b) => b ? " checked" : "";

        // --- Cookie 関係 ---

        public static void _EasySaveCookie<T>(this HttpResponse response, string cookieName, T value, AspNetCookieOptions? options = null)
        {
            if (options == null) options = new AspNetCookieOptions();

            cookieName = Consts.Strings.EasyCookieNamePrefix + cookieName;

            string valueStr = EasyCookieUtil.SerializeObject(value);

            if (valueStr._IsEmpty())
            {
                response.Cookies.Delete(cookieName);
            }
            else
            {
                response.Cookies.Append(cookieName, valueStr, options.ToCookieOptions());
            }
        }

        [return: MaybeNull]
        public static T _EasyLoadCookie<T>(this HttpRequest request, string cookieName)
        {
            cookieName = Consts.Strings.EasyCookieNamePrefix + cookieName;

            string valueStr = request.Cookies[cookieName];

            return EasyCookieUtil.DeserializeObject<T>(valueStr);
        }

        public static void _EasySaveCookie<T>(this HttpContext context, string cookieName, T value, AspNetCookieOptions? options = null)
            => context.Response._EasySaveCookie(cookieName, value, options);

        [return: MaybeNull]
        public static T _EasyLoadCookie<T>(this HttpContext context, string cookieName)
            => context.Request._EasyLoadCookie<T>(cookieName);

        public static void _EasySaveCookie<T>(this Controller controller, string cookieName, T value, AspNetCookieOptions? options = null)
            => controller.HttpContext._EasySaveCookie(cookieName, value, options);

        [return: MaybeNull]
        public static T _EasyLoadCookie<T>(this Controller controller, string cookieName)
            => controller.HttpContext._EasyLoadCookie<T>(cookieName);
    }
}

namespace IPA.Cores.Web
{
    // 単純なテキストを返すクラス
    public class TextActionResult : HttpActionResult
    {
        public TextActionResult(string str, string contentType = Consts.MimeTypes.TextUtf8, int statusCode = Consts.HttpStatusCodes.Ok, Encoding? encoding = null, IReadOnlyList<KeyValuePair<string, string>>? additionalHeaders = null)
            : base(new HttpStringResult(str, contentType, statusCode, encoding, additionalHeaders))
        {
        }
    }

    // HttpResult を元にして ASP.NET MVC の IActionResult インスタンスを生成するクラス
    public class HttpActionResult : IActionResult
    {
        public HttpResult HttpResult { get; }

        public HttpActionResult(HttpResult httpResult)
        {
            if (httpResult == null) throw new ArgumentNullException(nameof(httpResult));

            this.HttpResult = httpResult;
        }

        public async Task ExecuteResultAsync(ActionContext context)
        {
            using (this.HttpResult)
            {
                await context.HttpContext.Response._SendHttpResultAsync(this.HttpResult, context.HttpContext._GetRequestCancellationToken());
            }
        }
    }
}

namespace IPA.Cores.Globals
{
    public static partial class Web
    {
    }
}

