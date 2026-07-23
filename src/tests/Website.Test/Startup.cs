using EPiServer.Cms.Shell;
using EPiServer.Cms.UI.AspNetIdentity;
using EPiServer.Scheduler;
using EPiServer.ServiceLocation;
using EPiServer.Web.Routing;
using CmsSlugs.Optimizely;
using Mediachase.Commerce.Anonymous;

namespace Website.Test;

public class Startup(IWebHostEnvironment webHostingEnvironment, IConfiguration configuration)
{
    public void ConfigureServices(IServiceCollection services)
    {
        if (webHostingEnvironment.IsDevelopment())
        {
            AppDomain.CurrentDomain.SetData("DataDirectory", Path.Combine(webHostingEnvironment.ContentRootPath, "App_Data"));

            services.Configure<SchedulerOptions>(options => options.Enabled = false);
        }

        // Base CmsSlugs index options from appsettings ("CmsSlugs" section).
        services.Configure<SlugIndexOptions>(configuration.GetSection("CmsSlugs"));

        // Code overrides win over appsettings (PostConfigure) and only change the values you set:
        // services.ConfigureCmsSlugs(o => o.MaxDegreeOfParallelism = 16);

        services
            .AddCmsAspNetIdentity<ApplicationUser>()
            .AddCommerce()
            .AddAdminUserRegistration()
            .AddEmbeddedLocalization<Startup>();
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();

            // Live CmsSlugs index-status badge injected into every page (dev only). Early in the
            // pipeline so it wraps the whole response. Corner is configurable.
            app.CmsSlugsDeveloperSetup(o => o.Corner = IndicatorCorner.BottomRight);
        }

        app.UseAnonymousId();

        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        app.UseEndpoints(endpoints =>
        {
            // CmsSlugs diagnostics (/cmsslugs/status + /cmsslugs/routers) now ship in the
            // CmsSlugs.Optimizely package as an opt-in extension.
            endpoints.CmsSlugsMapDiagnosticRoutes();

            endpoints.MapContent();

            // A bare "/{slug}" under the start page matches no child content, so CMS content
            // routing treats it as a miss and never hands a remaining path to the partial router.
            // Resolve it here instead, straight through the CmsSlugs SlugResolver under test.
            endpoints.MapFallback(async (HttpContext ctx) =>
            {
                var resolver = ctx.RequestServices.GetRequiredService<CmsSlugs.Optimizely.SlugResolver>();
                var loader = ctx.RequestServices.GetRequiredService<EPiServer.IContentLoader>();
                var slug = ctx.Request.Path.Value?.Trim('/') ?? string.Empty;

                if (!string.IsNullOrEmpty(slug)
                    && resolver.TryResolve(slug, "en", out var hit)
                    && loader.TryGet<EPiServer.Core.IContent>(hit.ContentLink, out var content))
                {
                    await ctx.Response.WriteAsync(
                        $"Resolved via CmsSlugs (root fallback): {content.Name} [{hit.ContentLink}]");
                    return;
                }

                ctx.Response.StatusCode = 404;
                await ctx.Response.WriteAsync($"CmsSlugs: no slug matched '{ctx.Request.Path}'");
            });
        });
    }
}
