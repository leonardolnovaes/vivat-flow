using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tsdt.Api.Identity.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260929210000_AddContracts")]
public partial class AddContracts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Contracts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                QuoteId = table.Column<Guid>(type: "uuid", nullable: false),
                CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                CustomerLegalNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                ApprovedTotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                PaymentType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                InstallmentCount = table.Column<int>(type: "integer", nullable: true),
                StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                PaymentTerms = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedByUserId = table.Column<string>(type: "text", nullable: false),
                UpdatedByUserId = table.Column<string>(type: "text", nullable: false),
                Version = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Contracts", x => x.Id);
                table.CheckConstraint("CK_Contracts_ApprovedTotalAmount_NonNegative", "\"ApprovedTotalAmount\" >= 0");
                table.ForeignKey("FK_Contracts_Customers_CustomerId", x => x.CustomerId, "Customers", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_Contracts_Organizations_OrganizationId", x => x.OrganizationId, "Organizations", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_Contracts_Quotes_QuoteId", x => x.QuoteId, "Quotes", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateTable(
            name: "ContractItems",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false), ContractId = table.Column<Guid>(type: "uuid", nullable: false), QuoteItemId = table.Column<Guid>(type: "uuid", nullable: false), ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                ServiceCodeSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false), ServiceNameSnapshot = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                ServiceLineId = table.Column<Guid>(type: "uuid", nullable: false), ServiceLineCodeSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false), ServiceLineNameSnapshot = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false), DisplayOrder = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table => { table.PrimaryKey("PK_ContractItems", x => x.Id); table.ForeignKey("FK_ContractItems_Contracts_ContractId", x => x.ContractId, "Contracts", "Id", onDelete: ReferentialAction.Restrict); table.ForeignKey("FK_ContractItems_QuoteItems_QuoteItemId", x => x.QuoteItemId, "QuoteItems", "Id", onDelete: ReferentialAction.Restrict); table.ForeignKey("FK_ContractItems_Services_ServiceId", x => x.ServiceId, "Services", "Id", onDelete: ReferentialAction.Restrict); });
        migrationBuilder.CreateTable(
            name: "ContractAuditRecords",
            columns: table => new { Id = table.Column<Guid>(type: "uuid", nullable: false), ContractId = table.Column<Guid>(type: "uuid", nullable: false), ActorUserId = table.Column<string>(type: "text", nullable: false), Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false), OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false), ChangedFields = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true) },
            constraints: table => { table.PrimaryKey("PK_ContractAuditRecords", x => x.Id); table.ForeignKey("FK_ContractAuditRecords_Contracts_ContractId", x => x.ContractId, "Contracts", "Id", onDelete: ReferentialAction.Restrict); });
        migrationBuilder.CreateIndex(name: "IX_Contracts_CustomerId", table: "Contracts", column: "CustomerId");
        migrationBuilder.CreateIndex(name: "IX_Contracts_OrganizationId_QuoteId", table: "Contracts", columns: new[] { "OrganizationId", "QuoteId" }, unique: true, filter: "\"Status\" IN ('Draft', 'Active')");
        migrationBuilder.CreateIndex(name: "IX_Contracts_OrganizationId_Status_UpdatedAtUtc", table: "Contracts", columns: new[] { "OrganizationId", "Status", "UpdatedAtUtc" });
        migrationBuilder.CreateIndex(name: "IX_Contracts_QuoteId", table: "Contracts", column: "QuoteId");
        migrationBuilder.CreateIndex(name: "IX_ContractItems_ContractId_DisplayOrder", table: "ContractItems", columns: new[] { "ContractId", "DisplayOrder" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_ContractItems_QuoteItemId", table: "ContractItems", column: "QuoteItemId"); migrationBuilder.CreateIndex(name: "IX_ContractItems_ServiceId", table: "ContractItems", column: "ServiceId"); migrationBuilder.CreateIndex(name: "IX_ContractAuditRecords_ContractId_OccurredAtUtc", table: "ContractAuditRecords", columns: new[] { "ContractId", "OccurredAtUtc" });
    }
    protected override void Down(MigrationBuilder migrationBuilder) { migrationBuilder.DropTable(name: "ContractAuditRecords"); migrationBuilder.DropTable(name: "ContractItems"); migrationBuilder.DropTable(name: "Contracts"); }
}
