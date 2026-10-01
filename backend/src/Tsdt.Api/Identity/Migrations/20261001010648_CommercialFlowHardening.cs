using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tsdt.Api.Identity.Migrations
{
    /// <inheritdoc />
    public partial class CommercialFlowHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_OrganizationId_QuoteId",
                table: "WorkOrders");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ScheduledEnd",
                table: "QuoteVisits",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Contracts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "OneOff");

            migrationBuilder.CreateTable(
                name: "QuoteApprovalRecipients",
                columns: table => new
                {
                    QuoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerContactId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteApprovalRecipients", x => new { x.QuoteId, x.CustomerContactId });
                    table.ForeignKey(
                        name: "FK_QuoteApprovalRecipients_CustomerContacts_CustomerContactId",
                        column: x => x.CustomerContactId,
                        principalTable: "CustomerContacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuoteApprovalRecipients_Quotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "Quotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_OrganizationId_QuoteId",
                table: "WorkOrders",
                columns: new[] { "OrganizationId", "QuoteId" },
                unique: true,
                filter: "\"Status\" IN ('Draft', 'Scheduled', 'InProgress', 'AwaitingClosure')");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteApprovalRecipients_CustomerContactId",
                table: "QuoteApprovalRecipients",
                column: "CustomerContactId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuoteApprovalRecipients");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_OrganizationId_QuoteId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Contracts");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ScheduledEnd",
                table: "QuoteVisits",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_OrganizationId_QuoteId",
                table: "WorkOrders",
                columns: new[] { "OrganizationId", "QuoteId" },
                unique: true,
                filter: "\"Status\" <> 'Cancelled'");
        }
    }
}
