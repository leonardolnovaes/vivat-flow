namespace Tsdt.Api.Identity;

public static class TenantContext
{
    internal const string OrganizationItemKey = "TenantOrganizationId";

    public static Guid OrganizationId(HttpContext context) =>
        context.Items.TryGetValue(OrganizationItemKey, out var value) && value is Guid organizationId
            ? organizationId
            : throw new UnauthorizedAccessException("A tenant organization is required.");
}
