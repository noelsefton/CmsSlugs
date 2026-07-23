using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CmsSlugs.Optimizely;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers CmsSlugs for Optimizely: the resolver and — unless a storage provider has already
    /// registered one — the in-memory <see cref="ISlugStore"/>. The boot scan runs from the event
    /// module's initialization. The consumer must register their own <see cref="ISlugSource"/>.
    /// Call from an <c>IConfigurableModule.ConfigureContainer</c> via <c>context.Services</c>.
    /// </summary>
    public static IServiceCollection AddCmsSlugs(this IServiceCollection services, Action<SlugIndexOptions>? configure = null)
    {
        // Default store; a storage provider package (MsSql/Redis) registers its own first and wins.
        services.TryAddSingleton<ISlugStore, InMemorySlugStore>();
        services.TryAddSingleton<SlugResolver>();
        services.TryAddSingleton<SlugIndexDiagnostics>();
        services.AddOptions<SlugIndexOptions>();   // makes IOptions<SlugIndexOptions> resolvable

        // Anything passed here is treated as a code override (PostConfigure => wins over appsettings).
        if (configure is not null)
            services.PostConfigure(configure);

        return services;
    }

    /// <summary>
    /// Override index options from code. Runs as a PostConfigure, so it takes effect AFTER any
    /// appsettings binding (<c>Configure&lt;SlugIndexOptions&gt;(config.GetSection("CmsSlugs"))</c>)
    /// regardless of call order, and only changes the values you set. Call multiple times if needed.
    /// </summary>
    public static IServiceCollection ConfigureCmsSlugs(this IServiceCollection services, Action<SlugIndexOptions> configure)
    {
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        services.AddOptions<SlugIndexOptions>();
        services.PostConfigure(configure);
        return services;
    }
}
