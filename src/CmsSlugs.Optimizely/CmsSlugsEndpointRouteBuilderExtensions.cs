#if NET6_0_OR_GREATER
using System.Collections.Generic;
using System.Linq;
using EPiServer.Core.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CmsSlugs.Optimizely;

/// <summary>
/// Opt-in CmsSlugs diagnostics endpoints (CMS 12 / ASP.NET Core only). Register them explicitly:
/// <code>
/// app.UseEndpoints(endpoints =>
/// {
///     endpoints.MapCmsSlugsDiagnostics();
///     endpoints.MapContent();
/// });
/// </code>
/// </summary>
public static class CmsSlugsEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps two GET endpoints under <paramref name="prefix"/> (default <c>/cmsslugs</c>):
    /// <c>/status</c> — index size, readiness, last-build timing, and an optional
    /// <c>?slug=&amp;culture=</c> sample resolve; and <c>/routers</c> — the registered partial routers.
    /// </summary>
    public static IEndpointRouteBuilder CmsSlugsMapDiagnosticRoutes(
        this IEndpointRouteBuilder endpoints, string prefix = "/cmsslugs")
    {
        endpoints.MapGet(prefix + "/status",
            (HttpContext http, ISlugStore store, SlugResolver resolver, SlugIndexDiagnostics diagnostics) =>
            {
                var slug = http.Request.Query["slug"].ToString();
                var culture = http.Request.Query["culture"].ToString();

                object? sample = null;
                if (!string.IsNullOrEmpty(slug)
                    && resolver.TryResolve(slug, string.IsNullOrEmpty(culture) ? "en" : culture, out var hit))
                {
                    sample = new { contentLink = hit.ContentLink.ToString(), data = hit.Data };
                }

                return Results.Json(new
                {
                    count = store.Count,
                    isReady = store.IsReady,
                    inProgress = diagnostics.InProgress,
                    processed = diagnostics.Processed,
                    total = diagnostics.Total,
                    lastBuildStartedUtc = diagnostics.LastBuildStartedUtc,
                    lastBuildCompletedUtc = diagnostics.LastBuildCompletedUtc,
                    lastBuildEntryCount = diagnostics.LastBuildEntryCount,
                    lastBuildSeconds = diagnostics.LastBuildDuration?.TotalSeconds,
                    lastBuildDuration = diagnostics.LastBuildDuration?.ToString(),
                    sample
                });
            });

        endpoints.MapGet(prefix + "/routers",
            (IEnumerable<IPartialRouter> routers) =>
                Results.Json(routers.Select(r => r.GetType().FullName).ToArray()));

        return endpoints;
    }
}
#endif
