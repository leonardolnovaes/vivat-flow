using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Tsdt.Api.Contracts;
using Tsdt.Api.Identity;
using Tsdt.Api.Identity.Migrations;
using Tsdt.Api.Platform;

namespace Tsdt.Tests.Platform;

[Trait("Category", "Unit")]
public sealed class OrganizationFeatureMigrationTests
{
    [Fact]
    public void Migration_creates_unique_entitlements_and_backfills_the_current_catalog_for_every_existing_organization()
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        new Probe().Run(builder);

        var auditColumn = Assert.Single(builder.Operations.OfType<AddColumnOperation>());
        Assert.Equal("FeatureKey", auditColumn.Name);
        Assert.Equal("OrganizationAuditRecords", auditColumn.Table);
        Assert.Equal("character varying(80)", auditColumn.ColumnType);
        Assert.Equal(80, auditColumn.MaxLength);
        Assert.True(auditColumn.IsNullable);

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

    [Fact]
    public void Audit_feature_key_belongs_to_organization_audit_in_runtime_and_snapshot_models()
    {
        using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);
        var runtimeModel = context.Model;

        var snapshotType = typeof(ApplicationDbContext).Assembly.GetType(
            "Tsdt.Api.Identity.Migrations.ApplicationDbContextModelSnapshot",
            throwOnError: true)!;
        var snapshot = (ModelSnapshot)Activator.CreateInstance(snapshotType, nonPublic: true)!;

        AssertAuditFeatureKey(
            runtimeModel.FindEntityType(typeof(OrganizationAuditRecord))!,
            "OrganizationAuditRecords",
            "FeatureKey",
            columnType: null);
        AssertAuditFeatureKeyIsAbsent(runtimeModel.FindEntityType(typeof(ContractAuditRecord))!, "ContractAuditRecords");
        AssertAuditFeatureKey(
            snapshot.Model.FindEntityType(typeof(OrganizationAuditRecord).FullName!)!,
            "OrganizationAuditRecords",
            "FeatureKey",
            "character varying(80)");
        AssertAuditFeatureKeyIsAbsent(snapshot.Model.FindEntityType(typeof(ContractAuditRecord).FullName!)!, "ContractAuditRecords");
    }

    private static void AssertAuditFeatureKey(IEntityType entityType, string tableName, string propertyName, string? columnType)
    {
        Assert.Equal(tableName, entityType.GetTableName());
        var property = entityType.FindProperty(propertyName);
        Assert.NotNull(property);
        Assert.True(property!.IsNullable);
        Assert.Equal(80, property.GetMaxLength());
        if (columnType is not null)
            Assert.Equal(columnType, property.GetColumnType());
    }

    private static void AssertAuditFeatureKeyIsAbsent(IEntityType entityType, string tableName)
    {
        Assert.Equal(tableName, entityType.GetTableName());
        Assert.Null(entityType.FindProperty("FeatureKey"));
    }

    private sealed class Probe : AddOrganizationFeatureEntitlements
    {
        public void Run(MigrationBuilder builder) => Up(builder);
    }
}
