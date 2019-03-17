using System;
using System.Threading;
using System.Threading.Tasks;
using System.Data;
using System.Data.Sql;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using System.Text;
using System.Configuration;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Authentication;
using System.Web;
using System.IO;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Net;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


using IPA.Cores.Helper.Basic;

namespace IPA.Cores.Basic
{
    class HttpServerStartupConfig
    {
    }

    abstract class HttpServerImplementation
    {
        public IConfiguration Configuration { get; }
        HttpServerBuilderConfig builder_config;
        HttpServerStartupConfig startup_config;
        protected object Param;
        public CancellationToken CancelToken { get; }

        public HttpServerImplementation(IConfiguration configuration)
        {
            this.Configuration = configuration;

            this.Param = GlobalObjectExchange.Withdraw(this.Configuration["coreutil_param_token"]);
            this.CancelToken = (CancellationToken)GlobalObjectExchange.Withdraw(this.Configuration["coreutil_cancel_token"]);

            this.builder_config = this.Configuration["coreutil_ServerBuilderConfig"].JsonToObject<HttpServerBuilderConfig>();
            this.startup_config = new HttpServerStartupConfig();
        }

        public virtual void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_1);
        }

        public abstract void SetupStartupConfig(HttpServerStartupConfig cfg, IApplicationBuilder app, IHostingEnvironment env);

        public virtual void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            SetupStartupConfig(this.startup_config, app, env);

            if (builder_config.UseStaticFiles) app.UseStaticFiles();
            if (builder_config.ShowDetailError) app.UseDeveloperExceptionPage();
            app.UseStatusCodePages();
        }
    }

    class HttpServerBuilderConfig
    {
        public List<int> HttpPortsList = new List<int>(new int[] { 88, 8080 });
        public List<int> HttpsPortsList = new List<int>(new int[] { 8081 });

        public string ContentsRoot = Env.AppRootDir.CombinePath("wwwroot");
        public bool LocalHostOnly = false;
        public bool IPv4Only = false;
        public bool DebugToConsole = true;
        public bool UseStaticFiles = true;
        public bool ShowDetailError = true;
    }

    class HttpServer<THttpServerStartup> where THttpServerStartup : HttpServerImplementation
    {
        HttpServerBuilderConfig config;
        CancellationTokenSource cancel = new CancellationTokenSource();
        Task hosttask;

        public HttpServer(HttpServerBuilderConfig cfg, object param)
        {
            this.config = cfg;

            IO.MakeDirIfNotExists(config.ContentsRoot);

            string param_token = GlobalObjectExchange.Deposit(param);
            string cancel_token = GlobalObjectExchange.Deposit(cancel.Token);
            try
            {
                var dict = new Dictionary<string, string>
                {
                    {"coreutil_ServerBuilderConfig", this.config.ObjectToJson() },
                    {"coreutil_param_token", param_token },
                    {"coreutil_cancel_token", cancel_token },
                };

                IConfiguration iconf = new ConfigurationBuilder()
                    .AddInMemoryCollection(dict)
                    .Build();

                var h = new WebHostBuilder()
                    .UseKestrel(opt =>
                    {
                        if (config.LocalHostOnly)
                        {
                            foreach (int port in config.HttpPortsList) opt.ListenLocalhost(port);
                            foreach (int port in config.HttpsPortsList) opt.ListenLocalhost(port, lo =>
                            {
                                lo.UseHttps(so =>
                                {
                                    so.SslProtocols = SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12;
                                });
                            });
                        }
                        else if (config.IPv4Only)
                        {
                            foreach (int port in config.HttpPortsList) opt.Listen(IPAddress.Any, port);
                            foreach (int port in config.HttpsPortsList) opt.Listen(IPAddress.Any, port, lo =>
                            {
                                lo.UseHttps(so =>
                                {
                                    so.SslProtocols = SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12;
                                });
                            });
                        }
                        else
                        {
                            foreach (int port in config.HttpPortsList) opt.ListenAnyIP(port);
                            foreach (int port in config.HttpsPortsList) opt.ListenAnyIP(port, lo =>
                            {
                                lo.UseHttps(so =>
                                {
                                    so.SslProtocols = SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12;
                                });
                            });
                        }
                    })
                    .UseWebRoot(config.ContentsRoot)
                    .UseContentRoot(config.ContentsRoot)
                    .ConfigureAppConfiguration((hostingContext, config) =>
                    {

                    })
                    .ConfigureLogging((hostingContext, logging) =>
                    {
                        if (config.DebugToConsole)
                        {
                            logging.AddConsole();
                            logging.AddDebug();
                        }
                    })
                    .UseConfiguration(iconf)
                    .UseStartup<THttpServerStartup>()
                    .Build();

                hosttask = h.RunAsync(cancel.Token);
            }
            catch
            {
                GlobalObjectExchange.TryWithdraw(param_token);
                GlobalObjectExchange.TryWithdraw(cancel_token);
                throw;
            }
        }

        Once stop_flag;

        public void Stop() => this.StopAsync().Wait();

        public async Task StopAsync()
        {
            if (stop_flag.IsFirstCall())
            {
                cancel.TryCancelNoBlock();
            }

            await hosttask;
        }
    }
}
