using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tsdt.Api.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkOrderNumberCounters",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    LastNumber = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderNumberCounters", x => new { x.OrganizationId, x.Year });
                    table.CheckConstraint("CK_WorkOrderNumberCounters_Range", "\"LastNumber\" >= 1 AND \"LastNumber\" <= 999999");
                    table.ForeignKey(
                        name: "FK_WorkOrderNumberCounters_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SourceType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    QuoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContractId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerLegalNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ServiceAddressSnapshot = table.Column<string>(type: "character varying(700)", maxLength: 700, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    AssignedUserId = table.Column<string>(type: "text", nullable: true),
                    AssignedUserNameSnapshot = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    AssignedUserEmailSnapshot = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    ScheduledStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ScheduledEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExecutionCompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OperationalNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CompletionNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrders", x => x.Id);
                    table.CheckConstraint("CK_WorkOrders_Source", "(\"SourceType\" = 'Quote' AND \"ContractId\" IS NULL) OR (\"SourceType\" = 'Contract' AND \"ContractId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_WorkOrders_AspNetUsers_AssignedUserId",
                        column: x => x.AssignedUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrders_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrders_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrders_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrders_Quotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "Quotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderAuditRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ChangedFields = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderAuditRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderAuditRecords_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuoteItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContractItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceCodeSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ServiceNameSnapshot = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ServiceLineIdSnapshot = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceLineCodeSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ServiceLineNameSnapshot = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderItems_ContractItems_ContractItemId",
                        column: x => x.ContractItemId,
                        principalTable: "ContractItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderItems_QuoteItems_QuoteItemId",
                        column: x => x.QuoteItemId,
                        principalTable: "QuoteItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderItems_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderItems_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderAuditRecords_WorkOrderId_OccurredAtUtc",
                table: "WorkOrderAuditRecords",
                columns: new[] { "WorkOrderId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderItems_ContractItemId",
                table: "WorkOrderItems",
                column: "ContractItemId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderItems_QuoteItemId",
                table: "WorkOrderItems",
                column: "QuoteItemId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderItems_ServiceId",
                table: "WorkOrderItems",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderItems_WorkOrderId_DisplayOrder",
                table: "WorkOrderItems",
                columns: new[] { "WorkOrderId", "DisplayOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_AssignedUserId",
                table: "WorkOrders",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_ContractId",
                table: "WorkOrders",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_CustomerId",
                table: "WorkOrders",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_OrganizationId_AssignedUserId_UpdatedAtUtc",
                table: "WorkOrders",
                columns: new[] { "OrganizationId", "AssignedUserId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_OrganizationId_ContractId",
                table: "WorkOrders",
                columns: new[] { "OrganizationId", "ContractId" },
                filter: "\"ContractId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_OrganizationId_Number",
                table: "WorkOrders",
                columns: new[] { "OrganizationId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_OrganizationId_QuoteId",
                table: "WorkOrders",
                columns: new[] { "OrganizationId", "QuoteId" },
                unique: true,
                filter: "\"Status\" <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_OrganizationId_Status_UpdatedAtUtc",
                table: "WorkOrders",
                columns: new[] { "OrganizationId", "Status", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_OrganizationId_UpdatedAtUtc_Id",
                table: "WorkOrders",
                columns: new[] { "OrganizationId", "UpdatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_QuoteId",
                table: "WorkOrders",
                column: "QuoteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkOrderAuditRecords");

            migrationBuilder.DropTable(
                name: "WorkOrderItems");

            migrationBuilder.DropTable(
                name: "WorkOrderNumberCounters");

            migrationBuilder.DropTable(
                name: "WorkOrders");
        }
    }
}
