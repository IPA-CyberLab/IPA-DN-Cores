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

#if CORES_BASIC_WEBSERVER

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

namespace IPA.Cores.Basic
{

    public class LogBrowserHttpServerOptions
    {
        public DirectoryPath RootDir { get; }
        public string SystemTitle { get; }

        public LogBrowserHttpServerOptions(DirectoryPath rootDir, string systemTitle = Consts.Strings.LogBrowserDefaultSystemTitle)
        {
            this.SystemTitle = systemTitle._FilledOrDefault(Consts.Strings.LogBrowserDefaultSystemTitle);
            this.RootDir = rootDir;
        }
    }

    public class LogBrowserHttpServerBuilder : HttpServerStartupBase
    {
        public LogBrowserHttpServerOptions Options => (LogBrowserHttpServerOptions)this.Param;

        public ChrootFileSystem RootFs;

        public static HttpServer<LogBrowserHttpServerBuilder> StartServer(HttpServerOptions httpCfg, LogBrowserHttpServerOptions options, CancellationToken cancel = default)
            => new HttpServer<LogBrowserHttpServerBuilder>(httpCfg, options, cancel);

        public LogBrowserHttpServerBuilder(IConfiguration configuration) : base(configuration)
        {
        }

        protected override void ConfigureImpl_BeforeHelper(HttpServerStartupConfig cfg, IApplicationBuilder app, IHostingEnvironment env, IApplicationLifetime lifetime)
        {
        }

        protected override void ConfigureImpl_AfterHelper(HttpServerStartupConfig cfg, IApplicationBuilder app, IHostingEnvironment env, IApplicationLifetime lifetime)
        {
            this.RootFs = new ChrootFileSystem(new ChrootFileSystemParam(Options.RootDir.FileSystem, Options.RootDir.PathString, FileSystemMode.ReadOnly));

            RouteBuilder rb = new RouteBuilder(app);

            rb.MapGet("{*path}", GetRequestHandler);

            IRouter router = rb.Build();
            app.UseRouter(router);

            lifetime.ApplicationStopping.Register(() =>
            {
                this.RootFs._DisposeSafe();
            });
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

                breadCrumbsHtml.WriteLine($"<li{classStr}><a href=\"{crumb.PathString._EncodeUrlPath()}\"><span class=\"icon\"><i class=\"{iconInfo.Item1} {iconInfo.Item2}\" aria-hidden=\"true\"></i></span><b>{printName._EncodeHtml(true)}</b></a></li>");
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
                dirHtml.WriteLine($"<th><a href=\"{absolutePath._EncodeUrlPath()}\"><span class=\"icon\"><i class=\"{iconInfo.Item1} {iconInfo.Item2}\"></i></span>{printName._EncodeHtml(true)}</a></th>");

                string sizeStr = Str.GetFileSizeStr(e.Size);
                if (e.IsDirectory)
                {
                    sizeStr = "<Dir>";
                }

                dirHtml.WriteLine($"<td align=\"right\">{sizeStr._EncodeHtml()}</td>");
                dirHtml.WriteLine($"<td>{e.LastWriteTime.LocalDateTime._ToDtStr()}</td>");
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

        public async Task GetRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData)
        {
            CancellationToken cancel = request._GetRequestCancellationToken();

            try
            {
                string path = routeData.Values._GetStrOrEmpty("path");

                if (path.StartsWith("/") == false) path = "/" + path;

                path = PathParser.Linux.NormalizeUnixStylePathWithRemovingRelativeDirectoryElements(path);

                if (RootFs.IsDirectoryExists(path, cancel))
                {
                    // Directory
                    string htmlBody = BuildDirectoryHtml(new DirectoryPath(path, RootFs));

                    await response._SendStringContents(htmlBody, contentsType: Consts.MimeTypes.HtmlUtf8, cancel: cancel);
                }
                else if (RootFs.IsFileExists(path, cancel))
                {
                    // File
                    string extension = RootFs.PathParser.GetExtension(path);
                    string mimeType = MasterData.ExtensionToMime.Get(extension);

                    using (FileObject file = await RootFs.OpenAsync(path, cancel: cancel))
                    {
                        await response._SendFileContents(file, 0, null, mimeType, this.CancelToken);
                    }
                }
                else
                {
                    // Not found
                    response.StatusCode = 404;
                    await response._SendStringContents($"404 File not found", cancel: cancel);
                }
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                await response._SendStringContents($"HTTP Status Code: {response.StatusCode}\r\n" + ex.ToString(), cancel: cancel);
            }
        }
    }
}

#endif // CORES_BASIC_WEBSERVER

