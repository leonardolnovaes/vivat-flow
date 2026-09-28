using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tsdt.Api.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddQuoteApprovalWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovalRecipientEmail",
                table: "Quotes",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovalRecipientName",
                table: "Quotes",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClientResponseAt",
                table: "Quotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientResponseNotes",
                table: "Quotes",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClientResponseType",
                table: "Quotes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SentForApprovalAt",
                table: "Quotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ValidUntil",
                table: "Quotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_SentForApprovalAt",
                table: "Quotes",
                column: "SentForApprovalAt");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_ValidUntil",
                table: "Quotes",
                column: "ValidUntil");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Quotes_SentForApprovalAt",
                table: "Quotes");

            migrationBuilder.DropIndex(
                name: "IX_Quotes_ValidUntil",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "ApprovalRecipientEmail",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "ApprovalRecipientName",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "ClientResponseAt",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "ClientResponseNotes",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "ClientResponseType",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "SentForApprovalAt",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "ValidUntil",
                table: "Quotes");
        }
    }
}
