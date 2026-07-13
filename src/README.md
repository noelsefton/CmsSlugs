# CmsSlugs

A NuGet package family that resolves `(slug, culture) -> content` from a fast in-memory map
instead of an Optimizely Find query on the hot path. See
[`docs/cmsSlugs-Specification.md`](../docs/cmsSlugs-Specification.md) for the full design.

Supports **Optimizely / EPiServer CMS 11 and CMS 12**. The CMS adapter multi-targets `net472`
(CMS 11, .NET Framework) and `net6.0` (CMS 12, .NET 6+) from one source tree. The core and storage
providers are `netstandard2.0`, consumable by both.

## Projects

| Project | Role | Target |
| --- | --- | --- |
| `CmsSlugs` | Core: `SlugEntry`, `ISlugStore`, key normalization, in-memory store | netstandard2.0 |
| `CmsSlugs.Optimizely` | CMS 11 + 12 adapter: source contract, id codec, event module (+ boot scan), rebuild job, resolver | net472; net6.0 |
| `CmsSlugs.MsSql` | Durable `ISlugStore` over SQL Server (Data as a JSON column) | netstandard2.0 |
| `CmsSlugs.Redis` | Durable `ISlugStore` over Redis (hash per slug) | netstandard2.0 |
| `tests/CmsSlugs.Tests` | Core unit tests (xUnit + NSubstitute + Shouldly) | net8.0 |

Dependency rule: every adapter and provider references **core only**. A CMS adapter never
references a storage provider and vice versa.

## Restore / build

`CmsSlugs.Optimizely` pulls `EPiServer.*` 11.x packages, so restore needs the Optimizely feed —
it is configured in [`NuGet.config`](NuGet.config). Building the net472 adapter requires a Windows
build host (or Mono) with the .NET Framework 4.7.2 targeting pack.

```bash
dotnet restore CmsSlugs.sln
# Build the net6.0 (CMS 12) adapter + the netstandard2.0 libraries anywhere:
dotnet build CmsSlugs.sln -c Release
# The net472 (CMS 11) target needs a Windows build host (or Mono) with the .NET Framework 4.7.2
# targeting pack; MSBuild on Windows builds all target frameworks:
#   msbuild CmsSlugs.sln /p:Configuration=Release
dotnet test tests/CmsSlugs.Tests                 # core tests are net8.0 and run cross-platform
```

## Wiring (Optimizely)

Register services from an `IConfigurableModule`:

```csharp
[InitializableModule]
public class CmsSlugsConfiguration : IConfigurableModule
{
    public void ConfigureContainer(ServiceConfigurationContext context)
    {
        context.Services.AddCmsSlugs();                              // resolver + default in-memory store
        context.Services.AddSingleton<ISlugSource, MySlugSource>();   // your slug-building logic

        // Optional durable store (replaces the in-memory default — register either, not both):
        // context.Services.AddCmsSlugsSqlServer(connectionString);  // CmsSlugs.MsSql
        // context.Services.AddCmsSlugsRedis("localhost:6379");      // CmsSlugs.Redis
    }

    public void Initialize(InitializationEngine context) { }
    public void Uninitialize(InitializationEngine context) { }
}
```

Use the resolver inside your `IPartialRouter` and keep the Find fallback for the boot window:

```csharp
public class SlugPartialRouter : IPartialRouter<PageData, CatalogContentBase>
{
    private readonly SlugResolver _resolver;
    private readonly IContentLoader _contentLoader;

    public SlugPartialRouter(SlugResolver resolver, IContentLoader contentLoader)
        => (_resolver, _contentLoader) = (resolver, contentLoader);

    public object RoutePartial(PageData content, SegmentContext segmentContext)
    {
        var segment = segmentContext.GetNextValue(segmentContext.RemainingPath).Next;
        var culture = segmentContext.Language?.Name ?? ContentLanguage.PreferredCulture.Name;

        if (!_resolver.IsReady)
            return ResolveViaFind(segment, culture);                 // boot window / empty store

        if (_resolver.TryResolve(segment, culture, out var hit))     // hit.Data is available too
            return _contentLoader.Get<CatalogContentBase>(hit.ContentLink);

        return null;                                                 // miss -> 404 or Find
    }

    public PartialRouteData GetPartialVirtualPath(
        CatalogContentBase content, string language, RouteValueDictionary routeValues, RequestContext requestContext)
        => null; // unchanged from your existing implementation
}
```

The `IPartialRouter` snippet above is the **CMS 11** signature (`SegmentContext`). On **CMS 12**
the router interface differs (`RoutePartial(content, UrlResolverContext)` and `ContentLanguage`/
`UrlResolverContext` for the culture), but the CmsSlugs parts are identical: inject `SlugResolver`,
check `IsReady`, call `TryResolve`, and load `hit.ContentLink`.

For a durable store, run the **"Rebuild Slug Index"** scheduled job once after deploy to seed it.
Create the SQL table from [`CmsSlugs.MsSql/schema.sql`](CmsSlugs.MsSql/schema.sql).
