namespace Tsdt.Api.Identity;

public static class TenantOwnershipRules
{
    public static bool IsTenantMemberAllowed(bool isPlatformAdministrator, Guid? organizationId) =>
        !isPlatformAdministrator && organizationId.HasValue;

    public static bool IsOwnedBy(Guid organizationId, Guid resourceOrganizationId) =>
        organizationId != Guid.Empty && organizationId == resourceOrganizationId;
}
