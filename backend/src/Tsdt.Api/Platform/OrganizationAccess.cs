namespace Tsdt.Api.Platform;

public static class OrganizationAccess
{
    public static bool IsTenantAccessAllowed(bool isPlatformAdministrator, Guid? organizationId, OrganizationStatus? organizationStatus) =>
        isPlatformAdministrator || (organizationId.HasValue && organizationStatus == OrganizationStatus.Active);
}
