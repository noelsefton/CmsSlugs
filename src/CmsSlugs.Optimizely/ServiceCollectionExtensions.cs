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
    public static IServiceCollection AddCmsSlugs(this IServiceCollection services)
    {
        // Default store; a storage provider package (MsSql/Redis) registers its own first and wins.
        services.TryAddSingleton<ISlugStore, InMemorySlugStore>();
        services.TryAddSingleton<SlugResolver>();
        return services;
    }
}
