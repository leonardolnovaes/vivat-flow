using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tsdt.Api.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddQuoteAssignmentAndVisits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "ResponsibleUserId", table: "Quotes", type: "text", nullable: true);
            migrationBuilder.CreateIndex(name: "IX_Quotes_ResponsibleUserId", table: "Quotes", column: "ResponsibleUserId");
            migrationBuilder.AddForeignKey(name: "FK_Quotes_AspNetUsers_ResponsibleUserId", table: "Quotes", column: "ResponsibleUserId", principalTable: "AspNetUsers", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.CreateTable(name: "QuoteVisits", columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false), QuoteId = table.Column<Guid>(type: "uuid", nullable: false), AssignedUserId = table.Column<string>(type: "text", nullable: false), ScheduledStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false), ScheduledEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false), CustomerUnitId = table.Column<Guid>(type: "uuid", nullable: true), LocationSnapshot = table.Column<string>(type: "character varying(700)", maxLength: 700, nullable: true), Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true), Status = table.Column<int>(type: "integer", nullable: false), CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false), CreatedByUserId = table.Column<string>(type: "text", nullable: false), UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false), UpdatedByUserId = table.Column<string>(type: "text", nullable: false)
            }, constraints: table => { table.PrimaryKey("PK_QuoteVisits", x => x.Id); table.ForeignKey("FK_QuoteVisits_AspNetUsers_AssignedUserId", x => x.AssignedUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict); table.ForeignKey("FK_QuoteVisits_Quotes_QuoteId", x => x.QuoteId, "Quotes", "Id", onDelete: ReferentialAction.Restrict); });
            migrationBuilder.CreateIndex(name: "IX_QuoteVisits_AssignedUserId", table: "QuoteVisits", column: "AssignedUserId"); migrationBuilder.CreateIndex(name: "IX_QuoteVisits_QuoteId", table: "QuoteVisits", column: "QuoteId"); migrationBuilder.CreateIndex(name: "IX_QuoteVisits_ScheduledStart", table: "QuoteVisits", column: "ScheduledStart");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "QuoteVisits"); migrationBuilder.DropForeignKey(name: "FK_Quotes_AspNetUsers_ResponsibleUserId", table: "Quotes"); migrationBuilder.DropIndex(name: "IX_Quotes_ResponsibleUserId", table: "Quotes"); migrationBuilder.DropColumn(name: "ResponsibleUserId", table: "Quotes");
        }
    }
}
