#if NET6_0_OR_GREATER
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace CmsSlugs.Optimizely;

/// <summary>Which corner the developer index indicator is pinned to.</summary>
public enum IndicatorCorner
{
    BottomRight,
    BottomLeft,
    TopRight,
    TopLeft
}

/// <summary>Options for <see cref="CmsSlugsDeveloperSetupExtensions.CmsSlugsDeveloperSetup"/>.</summary>
public sealed class CmsSlugsDeveloperOptions
{
    /// <summary>Corner the indicator sits in. Default bottom-right.</summary>
    public IndicatorCorner Corner { get; set; } = IndicatorCorner.BottomRight;

    /// <summary>Path the injected script polls, and the middleware answers, for live status.</summary>
    public string StatusPath { get; set; } = "/__cmsslugs/status";
}

/// <summary>
/// Developer conveniences for CmsSlugs (CMS 12 / ASP.NET Core only).
/// </summary>
public static class CmsSlugsDeveloperSetupExtensions
{
    /// <summary>
    /// Injects a small live index-status indicator into every HTML response (like the Optimizely
    /// trial banner) and serves the status it polls. Intended for development only — call inside a
    /// development branch, early in the pipeline:
    /// <code>if (env.IsDevelopment()) app.DeveloperSetup(o => o.Corner = IndicatorCorner.BottomLeft);</code>
    /// </summary>
    public static IApplicationBuilder CmsSlugsDeveloperSetup(
        this IApplicationBuilder app, Action<CmsSlugsDeveloperOptions>? configure = null)
    {
        var options = new CmsSlugsDeveloperOptions();
        configure?.Invoke(options);
        return app.UseMiddleware<CmsSlugsIndicatorMiddleware>(options);
    }
}

internal sealed class CmsSlugsIndicatorMiddleware
{
    private readonly RequestDelegate _next;
    private readonly CmsSlugsDeveloperOptions _options;

    public CmsSlugsIndicatorMiddleware(RequestDelegate next, CmsSlugsDeveloperOptions options)
    {
        _next = next;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context, ISlugStore store, SlugIndexDiagnostics diagnostics)
    {
        // Live status for the injected poller.
        if (context.Request.Path.Equals(_options.StatusPath, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(StatusJson(store, diagnostics));
            return;
        }

        // Only candidate HTML pages get buffered; leave websockets, static assets and the CMS shell alone.
        if (ShouldBypass(context))
        {
            await _next(context);
            return;
        }

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await _next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        if (IsInjectableHtml(context.Response))
        {
            var html = Encoding.UTF8.GetString(buffer.ToArray());
            var bytes = Encoding.UTF8.GetBytes(Inject(html));
            context.Response.ContentLength = bytes.Length;
            await originalBody.WriteAsync(bytes, 0, bytes.Length);
        }
        else
        {
            var raw = buffer.ToArray();
            await originalBody.WriteAsync(raw, 0, raw.Length);
        }
    }

    private bool ShouldBypass(HttpContext context)
    {
        if (context.WebSockets.IsWebSocketRequest) return true;

        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/episerver", StringComparison.OrdinalIgnoreCase)) return true; // CMS edit UI

        var dot = path.LastIndexOf('.');
        if (dot < 0) return false;
        switch (path.Substring(dot).ToLowerInvariant())
        {
            case ".js": case ".css": case ".map": case ".json": case ".png": case ".jpg":
            case ".jpeg": case ".gif": case ".svg": case ".webp": case ".ico": case ".woff":
            case ".woff2": case ".ttf": case ".eot": case ".mp4": case ".pdf": case ".zip":
                return true;
            default:
                return false;
        }
    }

    private static bool IsInjectableHtml(HttpResponse response)
    {
        if (response.StatusCode != 200) return false;
        if (response.Headers["Content-Encoding"].Count > 0) return false; // compressed; don't touch
        var ct = response.ContentType;
        return ct != null && ct.Contains("text/html", StringComparison.OrdinalIgnoreCase);
    }

    private static string StatusJson(ISlugStore store, SlugIndexDiagnostics d) =>
        "{\"count\":" + store.Count +
        ",\"isReady\":" + (store.IsReady ? "true" : "false") +
        ",\"inProgress\":" + (d.InProgress ? "true" : "false") +
        ",\"processed\":" + d.Processed +
        ",\"total\":" + d.Total + "}";

    private string Inject(string html)
    {
        var snippet = BuildSnippet();
        var idx = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? html.Insert(idx, snippet) : html + snippet;
    }

    private string BuildSnippet()
    {
        var v = _options.Corner is IndicatorCorner.TopLeft or IndicatorCorner.TopRight ? "top" : "bottom";
        var h = _options.Corner is IndicatorCorner.BottomLeft or IndicatorCorner.TopLeft ? "left" : "right";
        return Template
            .Replace("__V__", v)
            .Replace("__H__", h)
            .Replace("__URL__", _options.StatusPath);
    }

    // Verbatim (non-interpolated) so the JS braces need no escaping; tokens are swapped in BuildSnippet.
    private const string Template = @"
<div id=""__cmsslugs-indicator"" style=""position:fixed;__V__:12px;__H__:12px;z-index:2147483647;font:12px/1.4 system-ui,-apple-system,sans-serif;background:#1f2430;color:#e6e6e6;padding:6px 10px;border-radius:8px;box-shadow:0 2px 8px rgba(0,0,0,.35);display:flex;align-items:center;gap:8px;pointer-events:none;user-select:none;opacity:.92"">
<span id=""__cmsslugs-dot"" style=""width:9px;height:9px;border-radius:50%;background:#888;flex:0 0 auto""></span>
<span id=""__cmsslugs-text"">CmsSlugs…</span>
</div>
<script>(function(){var u='__URL__',d=document.getElementById('__cmsslugs-dot'),t=document.getElementById('__cmsslugs-text');function f(s){if(s.inProgress){d.style.background='#e0a83e';return 'Slugs: building '+(s.total?Math.round(100*s.processed/s.total)+'%':(s.processed||0));}if(s.isReady){d.style.background='#3fb950';return 'Slugs: ready ('+(s.count||0).toLocaleString()+')';}d.style.background='#f85149';return 'Slugs: not ready';}function p(){fetch(u,{headers:{Accept:'application/json'}}).then(function(r){return r.json();}).then(function(s){t.textContent=f(s);}).catch(function(){t.textContent='Slugs: n/a';});}p();setInterval(p,1500);})();</script>";
}
#endif
