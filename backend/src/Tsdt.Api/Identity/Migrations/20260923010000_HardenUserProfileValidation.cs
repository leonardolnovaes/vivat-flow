using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tsdt.Api.Identity.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260923010000_HardenUserProfileValidation")]
public partial class HardenUserProfileValidation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(name: "FullName", table: "AspNetUsers", type: "character varying(120)", maxLength: 120, nullable: false, oldClrType: typeof(string), oldType: "text");
        migrationBuilder.AlterColumn<string>(name: "Email", table: "AspNetUsers", type: "character varying(254)", maxLength: 254, nullable: true, oldClrType: typeof(string), oldType: "character varying(256)", oldMaxLength: 256, oldNullable: true);
        migrationBuilder.AlterColumn<string>(name: "NormalizedEmail", table: "AspNetUsers", type: "character varying(254)", maxLength: 254, nullable: true, oldClrType: typeof(string), oldType: "character varying(256)", oldMaxLength: 256, oldNullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(name: "FullName", table: "AspNetUsers", type: "text", nullable: false, oldClrType: typeof(string), oldType: "character varying(120)", oldMaxLength: 120);
        migrationBuilder.AlterColumn<string>(name: "Email", table: "AspNetUsers", type: "character varying(256)", maxLength: 256, nullable: true, oldClrType: typeof(string), oldType: "character varying(254)", oldMaxLength: 254, oldNullable: true);
        migrationBuilder.AlterColumn<string>(name: "NormalizedEmail", table: "AspNetUsers", type: "character varying(256)", maxLength: 256, nullable: true, oldClrType: typeof(string), oldType: "character varying(254)", oldMaxLength: 254, oldNullable: true);
    }
}
