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

#if CORES_BASIC_WEBSERVER

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Microsoft.AspNetCore.Http;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Helper.Basic
{
    static class WebServerHelper
    {
        public static Task _SendStringContents(this HttpResponse h, string body, string contentsType = "text/plain; charset=UTF-8", Encoding encoding = null, CancellationToken cancel = default(CancellationToken))
        {
            if (encoding == null) encoding = Str.Utf8Encoding;
            h.ContentType = contentsType;
            byte[] ret_data = encoding.GetBytes(body);
            return h.Body.WriteAsync(ret_data, 0, ret_data.Length, cancel);
        }

        public static async Task<string> _RecvStringContents(this HttpRequest h, int maxRequestBodyLen = int.MaxValue, Encoding encoding = null, CancellationToken cancel = default(CancellationToken))
        {
            if (encoding == null) encoding = Str.Utf8Encoding;
            return (await h.Body._ReadToEndAsync(maxRequestBodyLen, cancel))._GetString_UTF8();
        }
    }

    static partial class StandardMainFunctions
    {
        public static class AspNet
        {
            public static void DoMain<TStartup>(HttpServerOptions httpServerOptions = null) where TStartup : class
            {
                if (httpServerOptions == null)
                    httpServerOptions = new HttpServerOptions();

                CoresLibrary.Main.Init();

                try
                {
                    using (HttpServer<TStartup> httpServer = new HttpServer<TStartup>(httpServerOptions))
                    {
                        Con.ReadLine("Enter to exit>");
                    }
                }
                finally
                {
                    if (CoresLibrary.Main.Free().LeakCheckerResult.HasLeak && Dbg.IsConsoleDebugMode)
                    {
                        Console.ReadKey();
                    }
                }
            }
        }
    }
}

#endif // CORES_BASIC_WEBSERVER

