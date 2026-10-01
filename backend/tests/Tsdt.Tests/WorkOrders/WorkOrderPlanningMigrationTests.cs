using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Tsdt.Api.Identity.Migrations;
using Xunit;

namespace Tsdt.Tests.WorkOrders;

[Trait("Category", "Unit")]
public sealed class WorkOrderPlanningMigrationTests
{
    [Fact]
    public void Migration_preserves_schedule_and_tenant_safe_actor_names_before_removing_legacy_columns()
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        new Probe().Run(builder);
        var sql = Assert.Single(builder.Operations.OfType<SqlOperation>());
        Assert.Contains("AT TIME ZONE 'America/Sao_Paulo'", sql.Sql);
        Assert.Contains("WHERE \"Status\" IN ('AwaitingClosure', 'Closed')", sql.Sql);
        Assert.Contains("users.\"OrganizationId\" = orders.\"OrganizationId\"", sql.Sql);
        var operations = builder.Operations.ToList();
        Assert.True(operations.IndexOf(sql) < operations.FindIndex(operation => operation is DropColumnOperation));
        var index = Assert.Single(builder.Operations.OfType<CreateIndexOperation>());
        Assert.True(index.IsUnique);
        Assert.DoesNotContain("Completed", index.Filter!);
        Assert.DoesNotContain("AwaitingClosure", index.Filter!);
    }

    private sealed class Probe : SimplifyWorkOrderPlanning { public void Run(MigrationBuilder builder) => Up(builder); }
}
