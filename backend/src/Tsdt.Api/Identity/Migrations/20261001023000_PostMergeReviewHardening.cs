using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tsdt.Api.Identity.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261001023000_PostMergeReviewHardening")]
public partial class PostMergeReviewHardening : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_WorkOrders_OrganizationId_ContractId",
            table: "WorkOrders");

        migrationBuilder.DropIndex(
            name: "IX_WorkOrders_OrganizationId_QuoteId",
            table: "WorkOrders");

        migrationBuilder.CreateIndex(
            name: "IX_WorkOrders_OrganizationId_ContractId",
            table: "WorkOrders",
            columns: new[] { "OrganizationId", "ContractId" },
            unique: true,
            filter: "\"ContractId\" IS NOT NULL AND \"Status\" IN ('Draft', 'Scheduled', 'InProgress', 'AwaitingClosure')");

        migrationBuilder.CreateIndex(
            name: "IX_WorkOrders_OrganizationId_QuoteId",
            table: "WorkOrders",
            columns: new[] { "OrganizationId", "QuoteId" });

        migrationBuilder.AddColumn<string>(
            name: "NameSnapshot",
            table: "QuoteApprovalRecipients",
            type: "character varying(120)",
            maxLength: 120,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "EmailSnapshot",
            table: "QuoteApprovalRecipients",
            type: "character varying(254)",
            maxLength: 254,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PhoneSnapshot",
            table: "QuoteApprovalRecipients",
            type: "character varying(11)",
            maxLength: 11,
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE "QuoteApprovalRecipients" AS recipient
            SET "NameSnapshot" = contact."Name",
                "EmailSnapshot" = contact."Email",
                "PhoneSnapshot" = contact."Phone"
            FROM "CustomerContacts" AS contact
            WHERE contact."Id" = recipient."CustomerContactId";
            """);

        migrationBuilder.AlterColumn<string>(
            name: "NameSnapshot",
            table: "QuoteApprovalRecipients",
            type: "character varying(120)",
            maxLength: 120,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(120)",
            oldMaxLength: 120,
            oldNullable: true);

        migrationBuilder.DropColumn(
            name: "Type",
            table: "Contracts");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM "WorkOrders"
                    WHERE "Status" IN ('Draft', 'Scheduled', 'InProgress', 'AwaitingClosure')
                    GROUP BY "OrganizationId", "QuoteId"
                    HAVING COUNT(*) > 1
                ) THEN
                    RAISE EXCEPTION 'Cannot restore quote-scoped Work Order uniqueness while multiple unfinished Work Orders exist for a quote.';
                END IF;
            END $$;
            """);

        migrationBuilder.DropIndex(
            name: "IX_WorkOrders_OrganizationId_ContractId",
            table: "WorkOrders");

        migrationBuilder.DropIndex(
            name: "IX_WorkOrders_OrganizationId_QuoteId",
            table: "WorkOrders");

        migrationBuilder.CreateIndex(
            name: "IX_WorkOrders_OrganizationId_ContractId",
            table: "WorkOrders",
            columns: new[] { "OrganizationId", "ContractId" },
            filter: "\"ContractId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_WorkOrders_OrganizationId_QuoteId",
            table: "WorkOrders",
            columns: new[] { "OrganizationId", "QuoteId" },
            unique: true,
            filter: "\"Status\" IN ('Draft', 'Scheduled', 'InProgress', 'AwaitingClosure')");

        migrationBuilder.AddColumn<string>(
            name: "Type",
            table: "Contracts",
            type: "character varying(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "Pontual");

        migrationBuilder.DropColumn(
            name: "EmailSnapshot",
            table: "QuoteApprovalRecipients");

        migrationBuilder.DropColumn(
            name: "NameSnapshot",
            table: "QuoteApprovalRecipients");

        migrationBuilder.DropColumn(
            name: "PhoneSnapshot",
            table: "QuoteApprovalRecipients");
    }
}
