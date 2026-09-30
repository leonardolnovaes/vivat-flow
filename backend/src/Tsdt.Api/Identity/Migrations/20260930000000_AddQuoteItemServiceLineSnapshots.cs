using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tsdt.Api.Identity.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260930000000_AddQuoteItemServiceLineSnapshots")]
public partial class AddQuoteItemServiceLineSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(name: "ServiceLineIdSnapshot", table: "QuoteItems", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<string>(name: "ServiceLineCodeSnapshot", table: "QuoteItems", type: "character varying(50)", maxLength: 50, nullable: true);
        migrationBuilder.AddColumn<string>(name: "ServiceLineNameSnapshot", table: "QuoteItems", type: "character varying(160)", maxLength: 160, nullable: true);
        migrationBuilder.Sql("""
            UPDATE "QuoteItems" AS item
            SET "ServiceLineIdSnapshot" = service."ServiceLineId",
                "ServiceLineCodeSnapshot" = line."Code",
                "ServiceLineNameSnapshot" = line."Name"
            FROM "Services" AS service
            INNER JOIN "ServiceLines" AS line ON line."Id" = service."ServiceLineId"
            WHERE item."ServiceId" = service."Id";
            """);
        migrationBuilder.AlterColumn<Guid>(name: "ServiceLineIdSnapshot", table: "QuoteItems", type: "uuid", nullable: false, oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);
        migrationBuilder.AlterColumn<string>(name: "ServiceLineCodeSnapshot", table: "QuoteItems", type: "character varying(50)", maxLength: 50, nullable: false, oldClrType: typeof(string), oldType: "character varying(50)", oldMaxLength: 50, oldNullable: true);
        migrationBuilder.AlterColumn<string>(name: "ServiceLineNameSnapshot", table: "QuoteItems", type: "character varying(160)", maxLength: 160, nullable: false, oldClrType: typeof(string), oldType: "character varying(160)", oldMaxLength: 160, oldNullable: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder) { migrationBuilder.DropColumn(name: "ServiceLineIdSnapshot", table: "QuoteItems"); migrationBuilder.DropColumn(name: "ServiceLineCodeSnapshot", table: "QuoteItems"); migrationBuilder.DropColumn(name: "ServiceLineNameSnapshot", table: "QuoteItems"); }
}
