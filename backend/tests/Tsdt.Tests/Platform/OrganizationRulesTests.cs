using Tsdt.Api.Platform;

namespace Tsdt.Tests.Platform;

[Trait("Category", "Unit")]
public sealed class OrganizationRulesTests
{
    [Theory]
    [InlineData(" Example Tenant ", "example tenant")]
    [InlineData("MIXED-Case", "mixed-case")]
    public void NormalizeSlug_trims_and_lowercases(string value, string expected) => Assert.Equal(expected, OrganizationRules.NormalizeSlug(value));

    [Theory]
    [InlineData("example-tenant")]
    [InlineData("tenant2")]
    public void IsValidSlug_accepts_normalized_identifier(string value) => Assert.True(OrganizationRules.IsValidSlug(value));

    [Theory]
    [InlineData("")]
    [InlineData("two--hyphens")]
    [InlineData("with spaces")]
    [InlineData("with_underscore")]
    public void IsValidSlug_rejects_invalid_identifier(string value) => Assert.False(OrganizationRules.IsValidSlug(value));

    [Theory]
    [InlineData(OrganizationStatus.Active, OrganizationStatus.Suspended, true)]
    [InlineData(OrganizationStatus.Suspended, OrganizationStatus.Active, true)]
    [InlineData(OrganizationStatus.Active, OrganizationStatus.Deactivated, true)]
    [InlineData(OrganizationStatus.Suspended, OrganizationStatus.Deactivated, true)]
    [InlineData(OrganizationStatus.Deactivated, OrganizationStatus.Active, false)]
    [InlineData(OrganizationStatus.Deactivated, OrganizationStatus.Suspended, false)]
    public void Lifecycle_allows_only_supported_transitions(OrganizationStatus current, OrganizationStatus target, bool expected) => Assert.Equal(expected, OrganizationLifecycle.CanTransition(current, target));

    [Fact]
    public void Lifecycle_makes_repeated_transition_idempotent() => Assert.True(OrganizationLifecycle.CanTransition(OrganizationStatus.Suspended, OrganizationStatus.Suspended));

    [Theory]
    [InlineData(false, true, OrganizationStatus.Active, true)]
    [InlineData(false, true, OrganizationStatus.Suspended, false)]
    [InlineData(false, false, OrganizationStatus.Active, false)]
    [InlineData(true, false, null, true)]
    public void Tenant_access_requires_active_organization_unless_platform_administrator(bool platformAdmin, bool hasOrganization, OrganizationStatus? status, bool expected) =>
        Assert.Equal(expected, OrganizationAccess.IsTenantAccessAllowed(platformAdmin, hasOrganization ? Guid.NewGuid() : null, status));

    [Theory]
    [InlineData(OrganizationStatus.Active, true)]
    [InlineData(OrganizationStatus.Suspended, false)]
    [InlineData(OrganizationStatus.Deactivated, false)]
    public void Tenant_administrator_provisioning_requires_an_active_organization(OrganizationStatus status, bool expected) =>
        Assert.Equal(expected, OrganizationRules.CanProvisionTenantAdministrator(status));
}
