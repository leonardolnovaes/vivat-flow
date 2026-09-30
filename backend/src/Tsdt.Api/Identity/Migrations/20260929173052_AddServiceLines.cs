using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tsdt.Api.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServiceLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceLines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationServiceLines",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceLineId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationServiceLines", x => new { x.OrganizationId, x.ServiceLineId });
                    table.ForeignKey(
                        name: "FK_OrganizationServiceLines_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrganizationServiceLines_ServiceLines_ServiceLineId",
                        column: x => x.ServiceLineId,
                        principalTable: "ServiceLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""
                INSERT INTO "ServiceLines" ("Id", "Code", "Name", "IsActive")
                SELECT '11111111-1111-1111-1111-111111111111', 'SST', 'Segurança e Saúde no Trabalho', true
                WHERE NOT EXISTS (SELECT 1 FROM "ServiceLines" WHERE "Code" = 'SST');
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "ServiceLineId",
                table: "Services",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "Services"
                SET "ServiceLineId" = (SELECT "Id" FROM "ServiceLines" WHERE "Code" = 'SST')
                WHERE "ServiceLineId" IS NULL;

                INSERT INTO "OrganizationServiceLines" ("OrganizationId", "ServiceLineId")
                SELECT DISTINCT service."OrganizationId", line."Id"
                FROM "Services" AS service
                CROSS JOIN "ServiceLines" AS line
                WHERE line."Code" = 'SST'
                ON CONFLICT DO NOTHING;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "ServiceLineId",
                table: "Services",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Services_ServiceLineId",
                table: "Services",
                column: "ServiceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationServiceLines_ServiceLineId",
                table: "OrganizationServiceLines",
                column: "ServiceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceLines_Code",
                table: "ServiceLines",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Services_ServiceLines_ServiceLineId",
                table: "Services",
                column: "ServiceLineId",
                principalTable: "ServiceLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Services_ServiceLines_ServiceLineId",
                table: "Services");

            migrationBuilder.DropTable(
                name: "OrganizationServiceLines");

            migrationBuilder.DropTable(
                name: "ServiceLines");

            migrationBuilder.DropIndex(
                name: "IX_Services_ServiceLineId",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "ServiceLineId",
                table: "Services");
        }
    }
}
