using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Tsdt.Api.Identity.Migrations;
using Tsdt.Api.Quotes;

namespace Tsdt.Tests.Quotes;

public sealed class QuoteApprovalRecipientSnapshotMigrationTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void Legacy_recipient_snapshot_allows_a_missing_email()
    {
        var recipient = new QuoteApprovalRecipient { NameSnapshot = "Historical contact", EmailSnapshot = null };

        Assert.Null(recipient.EmailSnapshot);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Migration_keeps_legacy_email_snapshot_nullable()
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        new MigrationProbe().Run(builder);

        var emailColumn = Assert.Single(builder.Operations.OfType<AddColumnOperation>(), operation => operation.Table == "QuoteApprovalRecipients" && operation.Name == "EmailSnapshot");
        Assert.True(emailColumn.IsNullable);
        Assert.DoesNotContain(builder.Operations.OfType<AlterColumnOperation>(), operation => operation.Table == "QuoteApprovalRecipients" && operation.Name == "EmailSnapshot" && !operation.IsNullable);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Migration_enforces_one_unfinished_work_order_per_contract()
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        new MigrationProbe().Run(builder);

        var index = Assert.Single(builder.Operations.OfType<CreateIndexOperation>(), operation => operation.Table == "WorkOrders" && operation.Name == "IX_WorkOrders_OrganizationId_ContractId");
        Assert.True(index.IsUnique);
        Assert.Equal("\"ContractId\" IS NOT NULL AND \"Status\" IN ('Draft', 'Scheduled', 'InProgress', 'AwaitingClosure')", index.Filter);
    }

    private sealed class MigrationProbe : PostMergeReviewHardening
    {
        public void Run(MigrationBuilder builder) => Up(builder);
    }
}
