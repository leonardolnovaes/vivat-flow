using Tsdt.Api.Identity;

namespace Tsdt.Tests.Identity;

[Trait("Category", "Unit")]
public sealed class TenantOwnershipRulesTests
{
    [Fact]
    public void Tenant_member_requires_an_organization_and_cannot_be_a_platform_administrator()
    {
        Assert.True(TenantOwnershipRules.IsTenantMemberAllowed(false, Guid.NewGuid()));
        Assert.False(TenantOwnershipRules.IsTenantMemberAllowed(false, null));
        Assert.False(TenantOwnershipRules.IsTenantMemberAllowed(true, Guid.NewGuid()));
    }

    [Fact]
    public void Resource_access_requires_the_same_organization()
    {
        var organizationId = Guid.NewGuid();
        Assert.True(TenantOwnershipRules.IsOwnedBy(organizationId, organizationId));
        Assert.False(TenantOwnershipRules.IsOwnedBy(organizationId, Guid.NewGuid()));
        Assert.False(TenantOwnershipRules.IsOwnedBy(Guid.Empty, organizationId));
    }
}
