using EPiServer;
using EPiServer.Core;
using EPiServer.Framework;
using EPiServer.Framework.Initialization;
using EPiServer.Security;
using EPiServer.ServiceLocation;
using Mediachase.Commerce.Catalog;

namespace Website.Test.Infrastructure;

/// <summary>
/// One-shot fix for a Commerce DB where the "Set catalog root access rights" migration step never
/// ran: the catalog root ACL is empty, so the Catalog UI throws AccessDeniedException
/// ("Failed loading content ... -1073741823__CatalogContent"). Writes the same entries the
/// migration step would (admin roles full access, Everyone read), plus WebAdmins so the CLI-created
/// admin user works without role mapping. Safe to run repeatedly; remove once the catalog loads.
/// See https://world.optimizely.com/forum/developer-forum/commerce-14/thread-container/2023/6/getting-failed-loading-content-when-opening-commerce-catalog-on-site-where-commerce-has-been-installed-after-cms/
/// </summary>
[InitializableModule]
[ModuleDependency(typeof(EPiServer.Commerce.Initialization.InitializationModule))]
public sealed class CatalogRootAccessFixModule : IInitializableModule
{
    public void Initialize(InitializationEngine context)
    {
        var loader = context.Locate.Advanced.GetInstance<IContentLoader>();
        var referenceConverter = context.Locate.Advanced.GetInstance<ReferenceConverter>();
        var securityRepository = context.Locate.Advanced.GetInstance<IContentSecurityRepository>();

        if (!loader.TryGet(referenceConverter.GetRootLink(), out IContent content))
            return;
        if (content is not IContentSecurable securable)
            return;

        var acl = (IContentSecurityDescriptor)securable.GetContentSecurityDescriptor().CreateWritableClone();
        acl.AddEntry(new AccessControlEntry("CommerceAdmins", AccessLevel.FullAccess, SecurityEntityType.Role));
        acl.AddEntry(new AccessControlEntry("Administrators", AccessLevel.FullAccess, SecurityEntityType.Role));
        acl.AddEntry(new AccessControlEntry("WebAdmins", AccessLevel.FullAccess, SecurityEntityType.Role));
        acl.AddEntry(new AccessControlEntry(EveryoneRole.RoleName, AccessLevel.Read, SecurityEntityType.Role));
        securityRepository.Save(content.ContentLink, acl, SecuritySaveType.Replace);
    }

    public void Uninitialize(InitializationEngine context) { }
}
