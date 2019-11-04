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
using System.IO;
using System.Net;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Hosting;

#if CORES_BASIC_HTTPSERVER
// ASP.NET Core 3.0 用の型名を無理やり ASP.NET Core 2.2 でコンパイルするための型エイリアスの設定
using IWebHostEnvironment = Microsoft.AspNetCore.Hosting.IHostingEnvironment;
using IHostApplicationLifetime = Microsoft.AspNetCore.Hosting.IApplicationLifetime;
#endif

namespace IPA.Cores.Basic
{
    // LogBrowser を含んだ HttpServer のオプション
    public class LogBrowserHttpServerOptions
    {
        public LogBrowserOptions LogBrowserOptions { get; }
        public string AbsolutePrefixPath { get; }

        public LogBrowserHttpServerOptions(LogBrowserOptions options, string absolutePrefixPath)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            this.LogBrowserOptions = options;
            this.AbsolutePrefixPath = absolutePrefixPath;
        }
    }

    // LogBrowser のオプション
    public class LogBrowserOptions
    {
        public DirectoryPath RootDir { get; }
        public string SystemTitle { get; }
        public long TailSize { get; }
        public Func<IPAddress, bool> ClientIpAcl { get; }

        public LogBrowserOptions(DirectoryPath rootDir,
            string systemTitle = Consts.Strings.LogBrowserDefaultSystemTitle,
            long tailSize = Consts.Numbers.LogBrowserDefaultTailSize,
            Func<IPAddress, bool>? clientIpAcl = null)
        {
            this.SystemTitle = systemTitle._FilledOrDefault(Consts.Strings.LogBrowserDefaultSystemTitle);
            this.RootDir = rootDir;
            this.TailSize = tailSize._Max(1);

            // デフォルト ACL はすべて通す
            if (clientIpAcl == null) clientIpAcl = (ip) => true;

            this.ClientIpAcl = clientIpAcl;
        }
    }

    // 汎用的に任意の Kestrel App から利用できる LogBrowser
    public class LogBrowser : AsyncService
    {
        public LogBrowserOptions Options { get; }

        public ChrootFileSystem RootFs;

        public string AbsolutePathPrefix { get; }

        public LogBrowser(LogBrowserOptions options, string absolutePathPrefix)
        {
            if (absolutePathPrefix._IsFilled())
            {
                if (absolutePathPrefix.StartsWith("/") == false)
                {
                    throw new ArgumentException("Must be an absolute URL.", nameof(absolutePathPrefix));
                }
            }
            this.AbsolutePathPrefix = absolutePathPrefix._NonNull();

            this.Options = options;

            this.RootFs = new ChrootFileSystem(new ChrootFileSystemParam(Options.RootDir.FileSystem, Options.RootDir.PathString, FileSystemMode.ReadOnly));
        }
        
        public async Task GetRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData)
        {
            CancellationToken cancel = request._GetRequestCancellationToken();

            using (HttpResult result = await ProcessRequestAsync(request.HttpContext.Connection.RemoteIpAddress._UnmapIPv4(),
                request._GetRequestPathAndQueryString(),
                cancel))
            {
                await response._SendHttpResultAsync(result, cancel);
            }
        }

        public async Task<HttpResult> ProcessRequestAsync(IPAddress clientIpAddress, string requestPathAndQueryString, CancellationToken cancel = default)
        {
            clientIpAddress = clientIpAddress._UnmapIPv4();

            try
            {
                // クライアント IP による ACL のチェック
                if (Options.ClientIpAcl(clientIpAddress) == false)
                {
                    // ACL error
                    return new HttpStringResult("403 Forbidden", statusCode: 403);
                }

                // URL のチェック
                requestPathAndQueryString._ParseUrl(out Uri uri, out QueryStringList qsList);

                string relativePath;

                if (this.AbsolutePathPrefix._IsFilled())
                {
                    // AbsolutePathPrefix の検査
                    if (uri.AbsolutePath._TryTrimStartWith(out relativePath, StringComparison.OrdinalIgnoreCase, this.AbsolutePathPrefix) == false)
                    {
                        // Not found
                        return new HttpStringResult("404 Not Found", statusCode: 404);
                    }
                }
                else
                {
                    relativePath = uri.AbsolutePath;
                }

                if (relativePath.StartsWith("/") == false) relativePath = "/" + relativePath;

                relativePath = PathParser.Linux.NormalizeUnixStylePathWithRemovingRelativeDirectoryElements(relativePath);

                if (RootFs.IsDirectoryExists(relativePath, cancel))
                {
                    // Directory
                    string htmlBody = BuildDirectoryHtml(new DirectoryPath(relativePath, RootFs));

                    return new HttpStringResult(htmlBody, contentType: Consts.MimeTypes.HtmlUtf8);
                }
                else if (RootFs.IsFileExists(relativePath, cancel))
                {
                    // File
                    string extension = RootFs.PathParser.GetExtension(relativePath);
                    string mimeType = MasterData.ExtensionToMime.Get(extension);

                    FileObject file = await RootFs.OpenAsync(relativePath, cancel: cancel);
                    try
                    {
                        long fileSize = file.Size;

                        long head = qsList._GetStrFirst("head")._ToInt()._NonNegative();
                        long tail = qsList._GetStrFirst("tail")._ToInt()._NonNegative();

                        if (head != 0 && tail != 0) throw new ApplicationException("You can specify either head or tail.");

                        head = head._Min(fileSize);
                        tail = tail._Min(fileSize);

                        long readStart = 0;
                        long readSize = fileSize;

                        if (head != 0)
                        {
                            readStart = 0;
                            readSize = head;
                        }
                        else if (tail != 0)
                        {
                            readStart = fileSize - tail;
                            readSize = tail;
                        }

                        if (tail != 0)
                        {
                            mimeType = Consts.MimeTypes.Text;
                        }

                        byte[] preData = new byte[0];

                        if (readSize != 0 && fileSize >= 3)
                        {
                            try
                            {
                                // 元のファイルの先頭に BOM が付いていて、先頭をスキップする場合は、
                                // 応答データに先頭にも BOM を付ける
                                byte[] bom = new byte[3];
                                if (await file.ReadRandomAsync(0, bom, cancel) == 3)
                                {
                                    if (Str.BOM_UTF_8._MemEquals(bom))
                                    {
                                        preData = bom;
                                    }
                                }
                            }
                            catch { }
                        }

                        return new HttpFileResult(file, readStart, readSize, mimeType, preData: preData);
                    }
                    catch
                    {
                        file._DisposeSafe();
                        throw;
                    }
                }
                else
                {
                    // Not found
                    return new HttpStringResult("404 Not Found", statusCode: 404);
                }
            }
            catch (Exception ex)
            {
                return new HttpStringResult($"HTTP Status Code: 500\r\n" + ex.ToString(), statusCode: 500);
            }
        }

        string BuildDirectoryHtml(DirectoryPath dir)
        {
            string body = CoresRes["LogBrowser/Html/Directory.html"].String;

            // Enum dir list
            List<FileSystemEntity> list = dir.EnumDirectory(flags: EnumDirectoryFlags.NoGetPhysicalSize | EnumDirectoryFlags.IncludeParentDirectory).ToList();

            // Bread crumbs
            var breadCrumbList = dir.GetBreadCrumbList();
            StringWriter breadCrumbsHtml = new StringWriter();

            foreach (var crumb in breadCrumbList)
            {
                var iconInfo = MasterData.GetFasIconFromExtension(Consts.MimeTypes.DirectoryOpening);
                string printName = crumb.GetThisDirectoryName() + "/";
                string classStr = "";
                if (breadCrumbList.LastOrDefault() == crumb)
                {
                    classStr = " class=\"is-active\"";
                }

                breadCrumbsHtml.WriteLine($"<li{classStr}><a href=\"{AbsolutePathPrefix}{crumb.PathString._EncodeUrlPath()}\"><span class=\"icon\"><i class=\"{iconInfo.Item1} {iconInfo.Item2}\" aria-hidden=\"true\"></i></span><b>{printName._EncodeHtml(true)}</b></a></li>");
            }

            // File contents
            StringWriter dirHtml = new StringWriter();

            var fileListSorted = list
                .Where(x => x.Name.IndexOf('\"') == -1)
                .OrderByDescending(x => x.IsCurrentDirectory)
                .ThenByDescending(x => x.IsParentDirectory)
                .ThenByDescending(x => x.IsDirectory)
                .ThenBy(x => x.Name, StrComparer.IgnoreCaseComparer);

            foreach (FileSystemEntity e in fileListSorted)
            {
                string absolutePath = e.FullPath;
                if (e.IsDirectory && absolutePath.Last() != '/')
                {
                    absolutePath += "/";
                }

                string extension = RootFs.PathParser.GetExtension(absolutePath);
                if (e.IsDirectory)
                {
                    extension = Consts.MimeTypes.Directory;
                    if (e.IsCurrentOrParentDirectory)
                    {
                        extension = Consts.MimeTypes.DirectoryOpening;
                    }
                }

                string printName = e.Name;
                if (e.IsDirectory)
                {
                    printName = $"{e.Name}/";
                }

                var iconInfo = MasterData.GetFasIconFromExtension(extension);

                dirHtml.WriteLine("<tr>");
                dirHtml.WriteLine("<th>");

                if (e.IsDirectory == false)
                {
                    dirHtml.WriteLine($@"<a href=""{AbsolutePathPrefix}{absolutePath._EncodeUrlPath()}?tail={Options.TailSize}""><i class=""far fa-eye""></i></a>&nbsp;");
                }

                dirHtml.WriteLine($@"<a href=""{AbsolutePathPrefix}{absolutePath._EncodeUrlPath()}""><span class=""icon\""><i class=""{iconInfo.Item1} {iconInfo.Item2}""></i></span> {printName._EncodeHtml(true)}</a>");

                dirHtml.WriteLine("</th>");

                string sizeStr = Str.GetFileSizeStr(e.Size);
                if (e.IsDirectory)
                {
                    sizeStr = "<Dir>";
                }

                dirHtml.WriteLine($"<td align=\"right\">{sizeStr._EncodeHtml()}</td>");
                dirHtml.WriteLine($"<td>{e.LastWriteTime.LocalDateTime._ToDtStr()}</td>");

                dirHtml.WriteLine("</tr>");
            }

            body = body._ReplaceStrWithReplaceClass(new
            {
                __FULLPATH__ = RootFs.PathParser.AppendDirectorySeparatorTail(dir.PathString)._EncodeHtml(true),
                __TITLE__ = $"{this.Options.SystemTitle} - {RootFs.PathParser.AppendDirectorySeparatorTail(dir.PathString)}"._EncodeHtml(true),
                __TITLE2__ = $"{this.Options.SystemTitle} - {RootFs.PathParser.AppendDirectorySeparatorTail(dir.PathString)}"._EncodeHtml(true),
                __BREADCRUMB__ = breadCrumbsHtml,
                __FILENAMES__ = dirHtml,
            });

            return body;
        }

        protected override void DisposeImpl(Exception? ex)
        {
            try
            {
                this.RootFs._DisposeSafe();
            }
            finally
            {
                base.DisposeImpl(ex);
            }
        }
    }

    // LogBrowser 用の新しい Kestrel 専用サーバーを立てるためのスタートアップクラス
    public class LogBrowserHttpServerBuilder : HttpServerStartupBase
    {
        public LogBrowserHttpServerOptions Options => (LogBrowserHttpServerOptions)this.Param!;

        public LogBrowser? Impl = null;

        public static HttpServer<LogBrowserHttpServerBuilder> StartServer(HttpServerOptions httpCfg, LogBrowserHttpServerOptions options, CancellationToken cancel = default)
            => new HttpServer<LogBrowserHttpServerBuilder>(httpCfg, options, cancel);

        public LogBrowserHttpServerBuilder(IConfiguration configuration) : base(configuration)
        {
        }

        protected override void ConfigureImpl_BeforeHelper(HttpServerStartupConfig cfg, IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime)
        {
        }

        protected override void ConfigureImpl_AfterHelper(HttpServerStartupConfig cfg, IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime)
        {
            this.Impl = new LogBrowser(this.Options.LogBrowserOptions, this.Options.AbsolutePrefixPath);

            RouteBuilder rb = new RouteBuilder(app);

            if (this.Impl.AbsolutePathPrefix._IsEmpty())
            {
                // 通常
                rb.MapGet("{*path}", Impl.GetRequestHandler);
            }
            else
            {
                // URL Secret を設定
                rb.MapGet(this.Impl.AbsolutePathPrefix + "/{*path}", Impl.GetRequestHandler);
            }

            IRouter router = rb.Build();
            app.UseRouter(router);

            lifetime.ApplicationStopping.Register(() =>
            {
                this.Impl._DisposeSafe();
            });
        }
    }
}

#endif // CORES_BASIC_WEBAPP

