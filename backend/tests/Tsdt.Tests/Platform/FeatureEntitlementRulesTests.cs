using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tsdt.Api.Identity;
using Tsdt.Api.Platform;
using Tsdt.Tests.Identity;

namespace Tsdt.Tests.Platform;

[Trait("Category", "Unit")]
public sealed class FeatureEntitlementRulesTests
{
    [Fact]
    public void Catalog_has_unique_canonical_keys_and_rejects_unknown_keys()
    {
        Assert.True(FeatureCatalog.IsValid());
        Assert.Equal(7, FeatureCatalog.AllKeys.Count);
        Assert.Equal(FeatureCatalog.AllKeys.Count, FeatureCatalog.AllKeys.Distinct(StringComparer.Ordinal).Count());
        Assert.False(FeatureCatalog.TryGet("custom-feature", out _));
    }

    [Theory]
    [InlineData(FeatureCatalog.Quotes, FeatureCatalog.Customers, FeatureCatalog.Services)]
    [InlineData(FeatureCatalog.Contracts, FeatureCatalog.Quotes, null)]
    [InlineData(FeatureCatalog.WorkOrders, FeatureCatalog.Contracts, null)]
    [InlineData(FeatureCatalog.Schedule, FeatureCatalog.WorkOrders, null)]
    [InlineData(FeatureCatalog.Documents, FeatureCatalog.Customers, null)]
    public void Enable_requires_the_catalog_dependencies(string feature, string first, string? second)
    {
        var enabled = new HashSet<string>(StringComparer.Ordinal);
        var missing = FeatureEntitlementRules.MissingDependencies(feature, enabled);
        Assert.Contains(first, missing);
        if (second is not null) Assert.Contains(second, missing);

        enabled.Add(first);
        if (second is not null) enabled.Add(second);
        Assert.Empty(FeatureEntitlementRules.MissingDependencies(feature, enabled));
    }

    [Fact]
    public void Customers_and_services_have_no_prerequisites()
    {
        Assert.Empty(FeatureEntitlementRules.MissingDependencies(FeatureCatalog.Customers, new HashSet<string>()));
        Assert.Empty(FeatureEntitlementRules.MissingDependencies(FeatureCatalog.Services, new HashSet<string>()));
    }

    [Fact]
    public void Disabling_a_prerequisite_reports_each_enabled_dependent()
    {
        var enabled = new HashSet<string>([FeatureCatalog.Quotes, FeatureCatalog.Documents], StringComparer.Ordinal);
        Assert.Equal([FeatureCatalog.Quotes, FeatureCatalog.Documents], FeatureEntitlementRules.EnabledDependents(FeatureCatalog.Customers, enabled));
        Assert.Equal([FeatureCatalog.Quotes], FeatureEntitlementRules.EnabledDependents(FeatureCatalog.Services, enabled));
        Assert.Equal([FeatureCatalog.WorkOrders], FeatureEntitlementRules.EnabledDependents(FeatureCatalog.Contracts, new HashSet<string>([FeatureCatalog.WorkOrders], StringComparer.Ordinal)));
    }

    [Fact]
    public async Task Entitlement_checks_are_feature_specific_and_scoped_to_the_requested_organization()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var client = IdentityTestClient.Create(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var first = new Organization { Id = Guid.NewGuid(), Name = "Feature A", Slug = $"feature-a-{Guid.NewGuid():N}" };
            var second = new Organization { Id = Guid.NewGuid(), Name = "Feature B", Slug = $"feature-b-{Guid.NewGuid():N}" };
            db.Organizations.AddRange(first, second);
            db.OrganizationFeatures.Add(new OrganizationFeature { OrganizationId = first.Id, FeatureKey = FeatureCatalog.Customers });
            await db.SaveChangesAsync();

            var access = new FeatureEntitlementService(db);
            Assert.True(await access.IsEnabledAsync(first.Id, FeatureCatalog.Customers));
            Assert.False(await access.IsEnabledAsync(first.Id, FeatureCatalog.Services));
            Assert.False(await access.IsEnabledAsync(second.Id, FeatureCatalog.Customers));
        }
    }
}
