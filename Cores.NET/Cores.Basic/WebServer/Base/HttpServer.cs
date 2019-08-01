﻿// IPA Cores.NET
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
using System.Linq;


using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.AspNetCore.Routing;
using IPA.Cores.ClientApi.Acme;
using System.Security.Claims;
using System.Runtime.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;

namespace IPA.Cores.Basic
{
    public class HttpServerStartupConfig
    {
    }

    public class HttpServerSimpleBasicAuthDatabase
    {
        public Dictionary<string, string> UsernameAndPassword = new Dictionary<string, string>(StrComparer.IgnoreCaseComparer);

        public HttpServerSimpleBasicAuthDatabase() { }

        public HttpServerSimpleBasicAuthDatabase(EnsureSpecial withRandom)
        {
            this.UsernameAndPassword.Add("user1", Str.GenRandPassword());
            this.UsernameAndPassword.Add("user2", Str.GenRandPassword());
        }

        public bool Authenticate(string username, string password)
        {
            if (UsernameAndPassword.TryGetValue(username, out string pw))
            {
                if (password == pw)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public class HttpServerAnyAuthenticationRequired : AuthorizationHandler<HttpServerAnyAuthenticationRequired>, IAuthorizationRequirement
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            HttpServerAnyAuthenticationRequired requirement)
        {
            if (context.User.Identity.IsAuthenticated)
            {
                context.Succeed(requirement);
            }
            else
            {
                context.Fail();
            }
            return Task.FromResult(0);
        }
    }

    public class HttpServerStartupHelper : IDisposable
    {
        public IConfiguration Configuration { get; }
        public HttpServerOptions ServerOptions { get; }
        public HttpServerStartupConfig StartupConfig { get; }
        public object Param { get; }
        public CancellationToken CancelToken { get; }

        public bool IsDevelopmentMode { get; private set; }

        readonly List<IFileProvider> StaticFileProviderList = new List<IFileProvider>();

        public Func<string, string, Task<bool>> SimpleBasicAuthenticationPasswordValidator { get; set; } = null;

        public HttpServerStartupHelper(IConfiguration configuration)
        {
            this.Configuration = configuration;

            this.Param = GlobalObjectExchange.Withdraw(this.Configuration["coreutil_param_token"]);
            this.CancelToken = (CancellationToken)GlobalObjectExchange.Withdraw(this.Configuration["coreutil_cancel_token"]);

            this.ServerOptions = this.Configuration["coreutil_ServerBuilderConfig"]._JsonToObject<HttpServerOptions>();
            this.StartupConfig = new HttpServerStartupConfig();

            Hive.LocalAppSettingsEx["WebServer"].AccessData(true,
                k =>
                {
                    this.IsDevelopmentMode = k.GetBool("IsDevelopmentMode", false);

                    if (this.ServerOptions.UseSimpleBasicAuthentication || this.ServerOptions.HoldSimpleBasicAuthenticationDatabase)
                    {
                        k.Get("SimpleBasicAuthDatabase", new HttpServerSimpleBasicAuthDatabase(EnsureSpecial.Yes));

                        SimpleBasicAuthenticationPasswordValidator = async (username, password) =>
                        {
                            bool ok = false;

                            Hive.LocalAppSettingsEx[this.ServerOptions.HiveName].AccessData(false, k2 =>
                            {
                                HttpServerSimpleBasicAuthDatabase db = k2.Get<HttpServerSimpleBasicAuthDatabase>("SimpleBasicAuthDatabase");

                                ok = db.Authenticate(username, password);
                            });

                            await Task.CompletedTask;
                            return ok;
                        };
                    }
                });
        }

        public virtual void ConfigureServices(IServiceCollection services)
        {
            services.AddHttpExceptionLogger(opt => { });

            if (this.ServerOptions.AutomaticRedirectToHttpsIfPossible)
            {
                services.AddEnforceHttps(opt =>
                {
                });
            }

            services.AddRouting();


            if (ServerOptions.UseSimpleBasicAuthentication)
            {
                if (ServerOptions.RequireBasicAuthenticationToAllRequests)
                {
                    services.AddAuthorization(options =>
                    {
                        options.AddPolicy(nameof(HttpServerAnyAuthenticationRequired), policy => policy.Requirements.Add(new HttpServerAnyAuthenticationRequired()));
                    });
                }

                // Simple BASIC authentication
                services.AddAuthentication(BasicAuthDefaults.AuthenticationScheme)
                    .AddBasic(options =>
                    {
                        options.AllowInsecureProtocol = true;
                        options.Realm = this.ServerOptions.SimpleBasicAuthenticationRealm._FilledOrDefault("Auth");

                        options.Events = new BasicAuthEvents
                        {
                            OnValidateCredentials = async (context) =>
                            {
                                if (await this.SimpleBasicAuthenticationPasswordValidator(context.Username, context.Password))
                                {
                                    var claims = new[]
                                    {
                                    new Claim(
                                        ClaimTypes.NameIdentifier,
                                        context.Username,
                                        ClaimValueTypes.String,
                                        context.Options.ClaimsIssuer),

                                    new Claim(
                                        ClaimTypes.Name,
                                        context.Username,
                                        ClaimValueTypes.String,
                                        context.Options.ClaimsIssuer)
                                    };

                                    context.Principal = new ClaimsPrincipal(
                                        new ClaimsIdentity(claims, context.Scheme.Name));

                                    context.Success();
                                }
                            }
                        };
                    });
            }
        }

        public void AddStaticFileProvider(string physicalDirectoryPath)
        {
            FileSystemBasedProvider provider = Lfs.CreateFileProvider(physicalDirectoryPath);

            this.AddStaticFileProvider(provider);
        }

        public void AddStaticFileProvider(IFileProvider provider)
        {
            if (provider != null)
                StaticFileProviderList.Add(provider);
        }

        public void AddStaticFileProvider(IEnumerable<IFileProvider> provider)
        {
            if (provider != null)
                provider._DoForEach(x => AddStaticFileProvider(x));
        }

        public virtual void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (this.ServerOptions.AutomaticRedirectToHttpsIfPossible)
            {
                app.UseEnforceHttps();
            }

            app.UseStatusCodePages();

            app.UseWebServerLogger();

#if CORES_BASIC_JSON
#if CORES_BASIC_SECURITY
            if (this.ServerOptions.UseGlobalCertVault && this.ServerOptions.HasHttpPort80)
            {
                // Add the ACME HTTP-based challenge responder
                RouteBuilder rb = new RouteBuilder(app);

                rb.MapGet("/.well-known/acme-challenge/{token}", AcmeGetChallengeFileRequestHandler);

                IRouter router = rb.Build();
                app.UseRouter(router);
            }
#endif  // CORES_BASIC_JSON
#endif  // CORES_BASIC_SECURITY;

            if (this.ServerOptions.MustIncludeHostnameStrList.Count >= 1)
            {
                app.Use(async (context, next) =>
                {
                    if (this.ServerOptions.MustIncludeHostnameStrList.Select(x => x._NonNullTrim()).Where(x => x._IsFilled() && (context?.Request?.Host.Host?._InStr(x, true) ?? false)).Any())
                    {
                        await next();
                    }
                    else
                    {
                        context.Response.StatusCode = 404;
                        await context.Response._SendStringContents("Server not found");
                    }
                });
            }

            if (ServerOptions.UseSimpleBasicAuthentication)
            {
                // Simple Basic Authentication
                app.UseAuthentication();

                if (ServerOptions.RequireBasicAuthenticationToAllRequests)
                {
                    app.Use(async (context, next) =>
                    {
                        AuthorizationResult allowed = await context.RequestServices.GetRequiredService<IAuthorizationService>().AuthorizeAsync(context.User, null, nameof(HttpServerAnyAuthenticationRequired));
                        if (allowed.Succeeded)
                        {
                            await next();
                        }
                        else
                        {
                            await context.RequestServices.GetRequiredService<IAuthenticationService>().ChallengeAsync(context, BasicAuthDefaults.AuthenticationScheme, new AuthenticationProperties());
                        }
                    });
                }
            }

            if (ServerOptions.UseStaticFiles)
            {
                StaticFileOptions sfo = new StaticFileOptions
                {
                    FileProvider = new CompositeFileProvider(this.StaticFileProviderList),
                };

                app.UseStaticFiles(sfo);
            }
        }

#if CORES_BASIC_JSON
#if CORES_BASIC_SECURITY

        public virtual async Task AcmeGetChallengeFileRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData)
        {
            try
            {
                AcmeAccount currentAccount = GlobalCertVault.GetAcmeAccountForChallengeResponse();
                string retStr;

                if (currentAccount == null)
                {
                    retStr = "Error: GlobalCertVault.GetAcmeAccountForChallengeResponse() == null";
                }
                else
                {
                    string token = routeData.Values._GetStr("token");

                    retStr = currentAccount.ProcessChallengeRequest(token);
                }

                await response._SendStringContents(retStr, Consts.MimeTypes.OctetStream);
            }
            catch (Exception ex)
            {
                ex._Debug();
                throw;
            }
        }

        public void Dispose() => Dispose(true);
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;

            foreach (IFileProvider provider in StaticFileProviderList)
            {
                if (provider is IDisposable target) target._DisposeSafe();
            }
        }

#endif  // CORES_BASIC_JSON
#endif  // CORES_BASIC_SECURITY;

    }

    public abstract class HttpServerStartupBase
    {
        public HttpServerStartupHelper Helper { get; }
        public IConfiguration Configuration { get; }

        public HttpServerOptions ServerOptions => Helper.ServerOptions;
        public HttpServerStartupConfig StartupConfig => Helper.StartupConfig;
        public object Param => Helper.Param;
        public CancellationToken CancelToken => Helper.CancelToken;

        protected abstract void ConfigureImpl_BeforeHelper(HttpServerStartupConfig cfg, IApplicationBuilder app, IHostingEnvironment env, IApplicationLifetime lifetime);

        protected abstract void ConfigureImpl_AfterHelper(HttpServerStartupConfig cfg, IApplicationBuilder app, IHostingEnvironment env, IApplicationLifetime lifetime);

        public HttpServerStartupBase(IConfiguration configuration)
        {
            this.Configuration = configuration;

            this.Helper = new HttpServerStartupHelper(configuration);
        }

        public virtual void ConfigureServices(IServiceCollection services)
        {
            Helper.ConfigureServices(services);
        }

        public virtual void Configure(IApplicationBuilder app, IHostingEnvironment env, IApplicationLifetime lifetime)
        {
            ConfigureImpl_BeforeHelper(Helper.StartupConfig, app, env, lifetime);

            Helper.Configure(app, env);

            ConfigureImpl_AfterHelper(Helper.StartupConfig, app, env, lifetime);
        }
    }

    public class HttpServerOptions
    {
        public List<int> HttpPortsList { get; set; } = new List<int>(new int[] { 88, 8080 });
        public List<int> HttpsPortsList { get; set; } = new List<int>(new int[] { 8081 });

        public List<string> MustIncludeHostnameStrList { get; set; } = new List<string>();

        public string ContentsRoot { get; set; } = Env.AppRootDir;
        public string WwwRoot { get; set; } = Env.AppRootDir._CombinePath("wwwroot");
        public bool LocalHostOnly { get; set; } = false;
        public bool IPv4Only { get; set; } = false;
        public bool DebugKestrelToConsole { get; set; } = false;
        public bool DebugKestrelToLog { get; set; } = false;
        public bool UseStaticFiles { get; set; } = true;
        public bool ShowDetailError { get; set; } = true;
        public bool UseKestrelWithIPACoreStack { get; set; } = true;

        public bool RequireBasicAuthenticationToAllRequests { get; set; } = false;

        public bool UseSimpleBasicAuthentication { get; set; } = false;
        public bool HoldSimpleBasicAuthenticationDatabase { get; set; } = false;
        public string SimpleBasicAuthenticationRealm { get; set; } = "Basic Authentication";
        public bool AutomaticRedirectToHttpsIfPossible { get; set; } = true;

        public bool HideKestrelServerHeader { get; set; } = true;

        public string HiveName { get; set; } = Consts.HiveNames.DefaultWebServer;

#if CORES_BASIC_JSON
#if CORES_BASIC_SECURITY
        public bool UseGlobalCertVault { get; set; } = true;

        [JsonIgnore]
        public CertificateStore GlobalCertVaultDefauleCert { get; set; } = null;
#endif  // CORES_BASIC_JSON
#endif  // CORES_BASIC_SECURITY;

        [JsonIgnore]
        public bool HasHttpPort80 => this.HttpPortsList.Where(x => x == 80).Any();

        [JsonIgnore]
        public CertSelectorCallback ServerCertSelector { get; set; } = null;

        [JsonIgnore]
        public TcpIpSystem TcpIp { get; set; } = null;

        public IWebHostBuilder GetWebHostBuilder<TStartup>(object sslCertSelectorParam = null) where TStartup : class
        {
            IWebHostBuilder baseWebHost = null;

            if (this.UseKestrelWithIPACoreStack)
            {
                baseWebHost = new WebHostBuilder().UseKestrelWithStack(opt => ConfigureKestrelServerOptions(opt, sslCertSelectorParam));
            }
            else
            {
                baseWebHost = new WebHostBuilder().UseKestrel(opt => ConfigureKestrelServerOptions(opt, sslCertSelectorParam));
            }

            return baseWebHost
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
            opt.AddServerHeader = !this.HideKestrelServerHeader;

            KestrelServerWithStackOptions withStackOpt = opt as KestrelServerWithStackOptions;

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

            if (withStackOpt != null)
            {
                // Kestrel with Stack
                withStackOpt.TcpIp = this.TcpIp ?? LocalNet;
            }

            void EnableHttps(ListenOptions listenOptions)
            {
                listenOptions.UseHttps(httpsOptions =>
                {
                    httpsOptions.SslProtocols = SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12;

                    bool useGlobalCertVault = false;

#if CORES_BASIC_JSON
#if CORES_BASIC_SECURITY
                    useGlobalCertVault = this.UseGlobalCertVault;
#endif  // CORES_BASIC_JSON
#endif  // CORES_BASIC_SECURITY;

                    if (useGlobalCertVault == false)
                    {
                        if (this.ServerCertSelector != null)
                        {
                            httpsOptions.ServerCertificateSelector = ((ctx, sni) => this.ServerCertSelector(sslCertSelectorParam, sni));
                        }
                    }

#if CORES_BASIC_JSON
#if CORES_BASIC_SECURITY
                    if (useGlobalCertVault)
                    {
                        if (this.GlobalCertVaultDefauleCert != null)
                        {
                            GlobalCertVault.SetDefaultCertificate(this.GlobalCertVaultDefauleCert);
                        }

                        httpsOptions.ServerCertificateSelector = ((ctx, sni) => (X509Certificate2)GlobalCertVault.GetGlobalCertVault().X509CertificateSelector(sni, !this.HasHttpPort80).NativeCertificate);
                    }
#endif  // CORES_BASIC_JSON
#endif  // CORES_BASIC_SECURITY;

                });
            }
        }
    }

    public sealed class HttpServer<THttpServerBuilder> : AsyncService
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

                bool isDevelopmentMode = false;

                Hive.LocalAppSettingsEx[this.Options.HiveName].AccessData(true,
                    k =>
                    {
                        isDevelopmentMode = k.GetBool("IsDevelopmentMode", false);
                    });

                Lfs.CreateDirectory(Options.WwwRoot);
                Lfs.CreateDirectory(Options.ContentsRoot);

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
                        .CaptureStartupErrors(true)
                        .UseSetting("detailedErrors", isDevelopmentMode.ToString())
                        .Build();

                    HostTask = host.RunAsync(this.CancelWatcher.CancelToken);
                }
                catch
                {
                    GlobalObjectExchange.TryWithdraw(ParamToken, out _);
                    ParamToken = null;

                    GlobalObjectExchange.TryWithdraw(CancelToken, out _);
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

            GlobalObjectExchange.TryWithdraw(ParamToken, out _);
            GlobalObjectExchange.TryWithdraw(CancelToken, out _);
        }
    }
}

#endif // CORES_BASIC_WEBSERVER

