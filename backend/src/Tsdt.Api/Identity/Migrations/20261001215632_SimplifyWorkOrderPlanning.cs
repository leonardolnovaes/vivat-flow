using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tsdt.Api.Identity.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyWorkOrderPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_OrganizationId_ContractId",
                table: "WorkOrders");

            migrationBuilder.AddColumn<DateOnly>(
                name: "ScheduledEndDate",
                table: "WorkOrders",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "ScheduledEndTime",
                table: "WorkOrders",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ScheduledStartDate",
                table: "WorkOrders",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "ScheduledStartTime",
                table: "WorkOrders",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActorNameSnapshot",
                table: "WorkOrderAuditRecords",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "WorkOrderAuditRecords",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            // Preserve legacy instants as explicit Sao Paulo local dates and times before dropping columns.
            migrationBuilder.Sql("""
                UPDATE "WorkOrders" SET
                    "ScheduledStartDate" = ("ScheduledStart" AT TIME ZONE 'America/Sao_Paulo')::date,
                    "ScheduledStartTime" = ("ScheduledStart" AT TIME ZONE 'America/Sao_Paulo')::time,
                    "ScheduledEndDate" = ("ScheduledEnd" AT TIME ZONE 'America/Sao_Paulo')::date,
                    "ScheduledEndTime" = ("ScheduledEnd" AT TIME ZONE 'America/Sao_Paulo')::time;
                UPDATE "WorkOrders" SET "Status" = 'Completed'
                    WHERE "Status" IN ('AwaitingClosure', 'Closed');
                UPDATE "WorkOrderAuditRecords" AS audit SET "ActorNameSnapshot" = users."FullName"
                    FROM "AspNetUsers" AS users, "WorkOrders" AS orders
                    WHERE audit."WorkOrderId" = orders."Id" AND audit."ActorUserId" = users."Id"
                        AND users."OrganizationId" = orders."OrganizationId";
                UPDATE "WorkOrderAuditRecords" SET "ActorNameSnapshot" =
                    CASE WHEN "ActorUserId" = '' THEN 'Sistema' ELSE 'Usuário não disponível' END
                    WHERE "ActorNameSnapshot" IS NULL;
                """);

            migrationBuilder.DropColumn(
                name: "ScheduledEnd",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "ScheduledStart",
                table: "WorkOrders");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_OrganizationId_ContractId",
                table: "WorkOrders",
                columns: new[] { "OrganizationId", "ContractId" },
                unique: true,
                filter: "\"ContractId\" IS NOT NULL AND \"Status\" IN ('Draft', 'Scheduled', 'InProgress')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The old model cannot represent date-only planning or successive completed orders safely.
            throw new NotSupportedException("Restore a database backup to revert this planning/lifecycle migration.");
        }
    }
}
