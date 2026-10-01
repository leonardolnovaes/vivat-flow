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

    private sealed class MigrationProbe : PostMergeReviewHardening
    {
        public void Run(MigrationBuilder builder) => Up(builder);
    }
}
