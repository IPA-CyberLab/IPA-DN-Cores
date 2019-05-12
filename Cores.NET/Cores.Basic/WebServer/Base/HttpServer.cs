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
using System.Collections.Generic;
using System.Security.Authentication;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http.Extensions;
using Newtonsoft.Json;


using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace IPA.Cores.Basic
{
    class HttpServerStartupConfig
    {
    }

    abstract class HttpServerStartupBase
    {
        public IConfiguration Configuration { get; }
        HttpServerOptions BuilderConfig;
        HttpServerStartupConfig StartupConfig;
        protected object Param;
        public CancellationToken CancelToken { get; }

        public HttpServerStartupBase(IConfiguration configuration)
        {
            this.Configuration = configuration;

            this.Param = GlobalObjectExchange.Withdraw(this.Configuration["coreutil_param_token"]);
            this.CancelToken = (CancellationToken)GlobalObjectExchange.Withdraw(this.Configuration["coreutil_cancel_token"]);

            this.BuilderConfig = this.Configuration["coreutil_ServerBuilderConfig"]._JsonToObject<HttpServerOptions>();
            this.StartupConfig = new HttpServerStartupConfig();
        }

        public virtual void ConfigureServices(IServiceCollection services)
        {
            services.AddRouting();
        }

        protected abstract void ConfigureImpl(HttpServerStartupConfig cfg, IApplicationBuilder app, IHostingEnvironment env);

        public virtual void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            ConfigureImpl(this.StartupConfig, app, env);

            if (BuilderConfig.UseStaticFiles) app.UseStaticFiles();
            if (BuilderConfig.ShowDetailError) app.UseDeveloperExceptionPage();
            app.UseStatusCodePages();
            app.UseWebServerLogger();
        }
    }

    class HttpServerOptions
    {
        public List<int> HttpPortsList { get; set; } = new List<int>(new int[] { 88, 8080 });
        public List<int> HttpsPortsList { get; set; } = new List<int>(new int[] { 8081 });

        public string ContentsRoot { get; set; } = Env.AppRootDir;
        public string WwwRoot { get; set; } = Env.AppRootDir._CombinePath("wwwroot");
        public bool LocalHostOnly { get; set; } = false;
        public bool IPv4Only { get; set; } = false;
        public bool DebugKestrelToConsole { get; set; } = false;
        public bool DebugKestrelToLog { get; set; } = true;
        public bool UseStaticFiles { get; set; } = true;
        public bool ShowDetailError { get; set; } = true;

        [JsonIgnore]
        public CertSelectorCallback ServerCertSelector { get; set; } = null;
        [JsonIgnore]
        public TcpIpSystem TcpIpSystem { get; set; } = LocalNet;

        public IWebHostBuilder GetWebHostBuilder<TStartup>(object sslCertSelectorParam = null) where TStartup: class
        {
            return new WebHostBuilder()
                .UseKestrelWithStack(opt => ConfigureKestrelServerOptions(opt, sslCertSelectorParam))
                //.UseKestrel(opt => ConfigureKestrelServerOptions(opt, sslCertSelectorParam))
                .UseWebRoot(WwwRoot)
                .UseContentRoot(ContentsRoot)
                .ConfigureAppConfiguration((hostingContext, config) =>
                {

                })
                .ConfigureLogging((hostingContext, logging) =>
                {
                    if (this.DebugKestrelToConsole)
                    {
                        logging.AddConsole();
                    }

                    if (this.DebugKestrelToLog)
                    {
                        logging.AddProvider(new MsLoggerProvider());
                    }
                })
                .UseStartup<TStartup>();
        }

        public void ConfigureKestrelServerOptions(KestrelServerOptions opt, object sslCertSelectorParam)
        {
            if (this.LocalHostOnly)
            {
                foreach (int port in this.HttpPortsList) opt.ListenLocalhost(port);
                foreach (int port in this.HttpsPortsList) opt.ListenLocalhost(port, lo => EnableHttps(lo));
            }
            else if (this.IPv4Only)
            {
                foreach (int port in this.HttpPortsList) opt.Listen(IPAddress.Any, port);
                foreach (int port in this.HttpsPortsList) opt.Listen(IPAddress.Any, port, lo => EnableHttps(lo));
            }
            else
            {
                foreach (int port in this.HttpPortsList) opt.ListenAnyIP(port);
                foreach (int port in this.HttpsPortsList) opt.ListenAnyIP(port, lo => EnableHttps(lo));
            }

            void EnableHttps(ListenOptions listenOptions)
            {
                listenOptions.UseHttps(httpsOptions =>
                {
                    httpsOptions.SslProtocols = SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12;
                    if (this.ServerCertSelector != null)
                    {
                        httpsOptions.ServerCertificateSelector = ((ctx, sni) => this.ServerCertSelector(sslCertSelectorParam, sni));
                    }
                });
            }
        }
    }

    sealed class HttpServer<THttpServerBuilder> : AsyncService
        where THttpServerBuilder : class
    {
        readonly HttpServerOptions Options;
        readonly Task HostTask;

        readonly string ParamToken;
        readonly string CancelToken;

        public HttpServer(HttpServerOptions options, object param = null, CancellationToken cancel = default) : base(cancel)
        {
            try
            {
                this.Options = options;

                BasicFile.MakeDirIfNotExists(Options.WwwRoot);
                BasicFile.MakeDirIfNotExists(Options.ContentsRoot);

                ParamToken = GlobalObjectExchange.Deposit(param);
                CancelToken = GlobalObjectExchange.Deposit(this.GrandCancel);

                try
                {
                    var dict = new Dictionary<string, string>
                    {
                        {"coreutil_ServerBuilderConfig", this.Options._ObjectToJson() },
                        {"coreutil_param_token", ParamToken },
                        {"coreutil_cancel_token", CancelToken },
                    };

                    IConfiguration iconf = new ConfigurationBuilder()
                        .AddInMemoryCollection(dict)
                        .Build();

                    var host = Options.GetWebHostBuilder<THttpServerBuilder>(param)
                        .UseConfiguration(iconf)
                        .Build();

                    HostTask = host.RunAsync(this.CancelWatcher.CancelToken);
                }
                catch
                {
                    GlobalObjectExchange.TryWithdraw(ParamToken);
                    ParamToken = null;

                    GlobalObjectExchange.TryWithdraw(CancelToken);
                    CancelToken = null;
                    throw;
                }
            }
            catch
            {
                this._DisposeSafe();
                throw;
            }
        }

        protected override async Task CleanupImplAsync(Exception ex)
        {
            await HostTask;

            GlobalObjectExchange.TryWithdraw(ParamToken);
            GlobalObjectExchange.TryWithdraw(CancelToken);
        }
    }
}

#endif // CORES_BASIC_WEBSERVER

