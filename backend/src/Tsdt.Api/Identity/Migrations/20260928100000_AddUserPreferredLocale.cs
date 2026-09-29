using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tsdt.Api.Identity.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260928100000_AddUserPreferredLocale")]
public partial class AddUserPreferredLocale : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<string>(
        name: "PreferredLocale", table: "AspNetUsers", type: "character varying(5)", maxLength: 5, nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(
        name: "PreferredLocale", table: "AspNetUsers");
}
