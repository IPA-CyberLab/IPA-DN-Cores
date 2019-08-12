using System;
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

namespace AspNetCore1
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
            AspNetLib.ConfigureServices(StartupHelper, services);

            StartupHelper.ConfigureServices(services);

            services.AddHttpRequestRateLimiter<HttpRequestRateLimiterHashKeys.SrcIPAddress>(opt =>
            {
            });

            //services.Configure<CookiePolicyOptions>(options =>
            //{
            //    // This lambda determines whether user consent for non-essential cookies is needed for a given request.
            //    options.CheckConsentNeeded = context => true;
            //    options.MinimumSameSitePolicy = SameSiteMode.None;
            //});

            services.AddMvc()
                .AddViewOptions(opt =>
                {
                    opt.HtmlHelperOptions.ClientValidationEnabled = false;
                })
                .AddRazorOptions(opt =>
                {
                    AspNetLib.ConfigureRazorOptions(opt);
                })
                .SetCompatibilityVersion(CompatibilityVersion.Version_2_1);
        }
        
        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, IApplicationLifetime lifetime)
        {
            app.UseHttpRequestRateLimiter<HttpRequestRateLimiterHashKeys.SrcIPAddress>();

            // wwwroot directory of this project
            StartupHelper.AddStaticFileProvider(Env.AppRootDir._CombinePath("wwwroot"));

            AspNetLib.Configure(StartupHelper, app, env);

            StartupHelper.Configure(app, env);
            
            if (StartupHelper.IsDevelopmentMode)
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }

            app.UseHttpExceptionLogger();

            app.UseStaticFiles();
            //app.UseCookiePolicy();

            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");
            });

            lifetime.ApplicationStopping.Register(() =>
            {
                AspNetLib._DisposeSafe();
                StartupHelper._DisposeSafe();
            });
        }
    }
}
