using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace CmsSlugs.Redis;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Redis store as the active <see cref="ISlugStore"/>, replacing the default
    /// in-memory store, using a connection string.
    /// </summary>
    public static IServiceCollection AddCmsSlugsRedis(this IServiceCollection services, string configuration, RedisStoreOptionsBuilder? options = null)
    {
        var multiplexer = ConnectionMultiplexer.Connect(configuration);
        return services.AddCmsSlugsRedis(multiplexer, options);
    }

    /// <summary>
    /// Registers the Redis store as the active <see cref="ISlugStore"/> using an existing multiplexer.
    /// </summary>
    public static IServiceCollection AddCmsSlugsRedis(this IServiceCollection services, IConnectionMultiplexer connection, RedisStoreOptionsBuilder? options = null)
    {
        var opts = new RedisSlugStoreOptions();
        options?.Invoke(opts);

        services.RemoveAll<ISlugStore>();
        services.AddSingleton<ISlugStore>(_ => new RedisSlugStore(connection, opts));
        return services;
    }
}

/// <summary>Delegate to configure <see cref="RedisSlugStoreOptions"/>.</summary>
public delegate void RedisStoreOptionsBuilder(RedisSlugStoreOptions options);
