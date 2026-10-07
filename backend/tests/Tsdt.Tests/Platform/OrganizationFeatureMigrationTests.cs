using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Tsdt.Api.Identity.Migrations;

namespace Tsdt.Tests.Platform;

[Trait("Category", "Unit")]
public sealed class OrganizationFeatureMigrationTests
{
    [Fact]
    public void Migration_creates_unique_entitlements_and_backfills_the_current_catalog_for_every_existing_organization()
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        new Probe().Run(builder);

        var table = Assert.Single(builder.Operations.OfType<CreateTableOperation>());
        Assert.Equal("OrganizationFeatures", table.Name);
        Assert.Equal(["OrganizationId", "FeatureKey"], table.PrimaryKey!.Columns);
        Assert.Equal(80, Assert.Single(table.Columns, column => column.Name == "FeatureKey").MaxLength);
        var foreignKey = Assert.Single(table.ForeignKeys);
        Assert.Equal("Organizations", foreignKey.PrincipalTable);
        Assert.Equal(ReferentialAction.Cascade, foreignKey.OnDelete);

        var backfill = Assert.Single(builder.Operations.OfType<SqlOperation>());
        Assert.Contains("CROSS JOIN", backfill.Sql);
        Assert.Contains("FROM \"Organizations\"", backfill.Sql);
        foreach (var key in new[] { "customers", "services", "quotes", "contracts", "work-orders", "schedule", "documents" })
            Assert.Contains($"('{key}')", backfill.Sql);
    }

    private sealed class Probe : AddOrganizationFeatureEntitlements
    {
        public void Run(MigrationBuilder builder) => Up(builder);
    }
}
