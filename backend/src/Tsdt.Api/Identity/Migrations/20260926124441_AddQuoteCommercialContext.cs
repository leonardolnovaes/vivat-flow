using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tsdt.Api.Identity.Migrations;

public partial class AddQuoteCommercialContext : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "EmployeeCount", table: "Quotes", type: "integer", nullable: true);
        migrationBuilder.AddColumn<int>(name: "RiskDegree", table: "Quotes", type: "integer", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "ServiceUnitId", table: "Quotes", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<string>(name: "ServiceAddressSnapshot", table: "Quotes", type: "character varying(700)", maxLength: 700, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "EmployeeCount", table: "Quotes");
        migrationBuilder.DropColumn(name: "RiskDegree", table: "Quotes");
        migrationBuilder.DropColumn(name: "ServiceUnitId", table: "Quotes");
        migrationBuilder.DropColumn(name: "ServiceAddressSnapshot", table: "Quotes");
    }
}
