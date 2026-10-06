using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tsdt.Api.Identity.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261006120000_AddOrganizationAuditTargetUser")]
public partial class AddOrganizationAuditTargetUser : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "TargetUserId",
            table: "OrganizationAuditRecords",
            type: "character varying(450)",
            maxLength: 450,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "TargetUserId",
            table: "OrganizationAuditRecords");
    }
}
