using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tsdt.Api.Identity.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261006150000_AddOrganizationFeatureEntitlements")]
public partial class AddOrganizationFeatureEntitlements : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "FeatureKey",
            table: "OrganizationAuditRecords",
            type: "character varying(80)",
            maxLength: 80,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "OrganizationFeatures",
            columns: table => new
            {
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                FeatureKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OrganizationFeatures", item => new { item.OrganizationId, item.FeatureKey });
                table.ForeignKey(
                    name: "FK_OrganizationFeatures_Organizations_OrganizationId",
                    column: item => item.OrganizationId,
                    principalTable: "Organizations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.Sql("""
            INSERT INTO "OrganizationFeatures" ("OrganizationId", "FeatureKey")
            SELECT organization."Id", feature."FeatureKey"
            FROM "Organizations" AS organization
            CROSS JOIN (VALUES
                ('customers'),
                ('services'),
                ('quotes'),
                ('contracts'),
                ('work-orders'),
                ('schedule'),
                ('documents')
            ) AS feature("FeatureKey");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "OrganizationFeatures");
        migrationBuilder.DropColumn(name: "FeatureKey", table: "OrganizationAuditRecords");
    }
}
