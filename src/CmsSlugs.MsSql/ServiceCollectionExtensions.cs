using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CmsSlugs.MsSql;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQL Server store as the active <see cref="ISlugStore"/>, replacing the default
    /// in-memory store. Call before <c>AddCmsSlugs()</c> or any time — it removes any prior registration.
    /// </summary>
    public static IServiceCollection AddCmsSlugsSqlServer(this IServiceCollection services, string connectionString)
        => services.AddCmsSlugsSqlServer(o => o.ConnectionString = connectionString);

    public static IServiceCollection AddCmsSlugsSqlServer(this IServiceCollection services, Action<SqlServerSlugStoreOptions> configure)
    {
        var options = new SqlServerSlugStoreOptions();
        configure(options);

        services.RemoveAll<ISlugStore>();
        services.AddSingleton<ISlugStore>(_ => new SqlServerSlugStore(options));
        return services;
    }
}
