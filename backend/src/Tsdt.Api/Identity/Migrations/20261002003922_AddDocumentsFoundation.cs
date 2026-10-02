using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tsdt.Api.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentsFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Customers_OrganizationId_Id",
                table: "Customers",
                columns: new[] { "OrganizationId", "Id" });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    StorageKey = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ContextType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ContextId = table.Column<Guid>(type: "uuid", nullable: true),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UploadedByUserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                    table.CheckConstraint("CK_Documents_Context_Pair", "(\"ContextType\" IS NULL AND \"ContextId\" IS NULL) OR (\"ContextType\" IS NOT NULL AND \"ContextId\" IS NOT NULL)");
                    table.CheckConstraint("CK_Documents_SizeBytes_Positive", "\"SizeBytes\" > 0");
                    table.ForeignKey(
                        name: "FK_Documents_Customers_OrganizationId_CustomerId",
                        columns: x => new { x.OrganizationId, x.CustomerId },
                        principalTable: "Customers",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentAuditRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ContextType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ContextId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentAuditRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentAuditRecords_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAuditRecords_DocumentId",
                table: "DocumentAuditRecords",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAuditRecords_OrganizationId_CustomerId_OccurredAtUtc",
                table: "DocumentAuditRecords",
                columns: new[] { "OrganizationId", "CustomerId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_OrganizationId_ContextType_ContextId",
                table: "Documents",
                columns: new[] { "OrganizationId", "ContextType", "ContextId" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_OrganizationId_CustomerId_UploadedAtUtc",
                table: "Documents",
                columns: new[] { "OrganizationId", "CustomerId", "UploadedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_StorageKey",
                table: "Documents",
                column: "StorageKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentAuditRecords");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Customers_OrganizationId_Id",
                table: "Customers");
        }
    }
}
