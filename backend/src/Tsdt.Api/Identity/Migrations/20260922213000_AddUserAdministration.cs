using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Tsdt.Api.Identity.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260922213000_AddUserAdministration")]
public partial class AddUserAdministration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "FullName",
            table: "AspNetUsers",
            type: "text",
            nullable: false,
            defaultValue: "");

        migrationBuilder.CreateTable(
            name: "UserAdministrationAuditRecords",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ActorUserId = table.Column<string>(type: "text", nullable: false),
                TargetUserId = table.Column<string>(type: "text", nullable: false),
                Action = table.Column<string>(type: "text", nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                OldRole = table.Column<string>(type: "text", nullable: true),
                NewRole = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_UserAdministrationAuditRecords", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_UserAdministrationAuditRecords_TargetUserId",
            table: "UserAdministrationAuditRecords",
            column: "TargetUserId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "UserAdministrationAuditRecords");
        migrationBuilder.DropColumn(name: "FullName", table: "AspNetUsers");
    }
}
