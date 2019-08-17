﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Codes;
using IPA.Cores.Helper.Codes;
using static IPA.Cores.Globals.Codes;

using IPA.Cores.Basic.App.DaemonCenterLib;
using Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation;

namespace DaemonCenter
{
    public class Startup
    {
        readonly HttpServerStartupHelper StartupHelper;
        readonly AspNetLib AspNetLib;

        public Startup(IConfiguration configuration)
        {
            StartupHelper = new HttpServerStartupHelper(configuration);

            AspNetLib = new AspNetLib(configuration);

            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // AspNetLib による設定を追加
            AspNetLib.ConfigureServices(StartupHelper, services);

            // 基本的な設定を追加
            StartupHelper.ConfigureServices(services);

            // リクエスト数制限機能を追加
            services.AddHttpRequestRateLimiter<HttpRequestRateLimiterHashKeys.SrcIPAddress>(_ => { });

            // Cookie 認証機能を追加
            EasyCookieAuth.LoginFormMessage.TrySet("ログインが必要です。");
            EasyCookieAuth.AuthenticationPasswordValidator = StartupHelper.SimpleBasicAuthenticationPasswordValidator;
            EasyCookieAuth.ConfigureServices(services, !StartupHelper.ServerOptions.AutomaticRedirectToHttpsIfPossible);

            // MVC 機能を追加
            services.AddControllersWithViews()
                .AddViewOptions(opt => opt.HtmlHelperOptions.ClientValidationEnabled = false)
                .AddRazorOptions(opt => AspNetLib.ConfigureRazorOptions(opt))
                .AddRazorRuntimeCompilation(opt => AspNetLib.ConfigureRazorRuntimeCompilationOptions(opt))
                .SetCompatibilityVersion(CompatibilityVersion.Version_3_0);

            // シングルトンサービスの注入
            services.AddSingleton(new Server());

            // 全ページ共通コンテキストの注入
            services.AddScoped<PageContext>();
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env, IApplicationLifetime lifetime, Server server)
        {
            // リクエスト数制限
            app.UseHttpRequestRateLimiter<HttpRequestRateLimiterHashKeys.SrcIPAddress>();

            // wwwroot ディレクトリを static ファイルのルートとして追加
            StartupHelper.AddStaticFileProvider(Env.AppRootDir._CombinePath("wwwroot"));

            // AspNetLib による設定を追加
            AspNetLib.Configure(StartupHelper, app, env);

            // 基本的な設定を追加
            StartupHelper.Configure(app, env);

            // エラーページを追加
            if (StartupHelper.IsDevelopmentMode)
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/App/Error");
            }

            // エラーログを追加
            app.UseHttpExceptionLogger();

            // Static ファイルを追加
            app.UseStaticFiles();
            
            // JSON-RPC を追加
            server.RegisterRoutesToHttpServer(app, "/rpc");

            // ルーティングを有効可 (認証を利用する場合は認証前に呼び出す必要がある)
            app.UseRouting();

            // 認証・認可を実施
            app.UseAuthentication();
            app.UseAuthorization();

            // ルートマップを定義
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=App}/{action=Index}/{id?}/{id2?}");
            });

            // クリーンアップ動作を定義
            lifetime.ApplicationStopping.Register(() =>
            {
                server._DisposeSafe();

                AspNetLib._DisposeSafe();
                StartupHelper._DisposeSafe();
            });
        }
    }
}
