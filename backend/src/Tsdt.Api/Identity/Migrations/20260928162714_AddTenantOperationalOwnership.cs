using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tsdt.Api.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantOperationalOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "Customers", "Services", "Quotes" })
                migrationBuilder.AddColumn<Guid>(name: "OrganizationId", table: table, type: "uuid", nullable: true);
            migrationBuilder.Sql("""
                DO $$ DECLARE legacy_organization_id uuid; BEGIN
                    IF EXISTS (SELECT 1 FROM "Customers" WHERE "OrganizationId" IS NULL)
                        OR EXISTS (SELECT 1 FROM "Services" WHERE "OrganizationId" IS NULL)
                        OR EXISTS (SELECT 1 FROM "Quotes" WHERE "OrganizationId" IS NULL) THEN
                        SELECT "Id" INTO legacy_organization_id FROM "Organizations" WHERE "Slug" = 'organizacao-inicial';
                        IF legacy_organization_id IS NULL THEN
                            INSERT INTO "Organizations" ("Id", "Name", "Slug", "Status", "CreatedAtUtc", "UpdatedAtUtc")
                            VALUES (md5(random()::text || clock_timestamp()::text)::uuid, 'Organização inicial', 'organizacao-inicial', 'Active', now(), now())
                            RETURNING "Id" INTO legacy_organization_id;
                        END IF;
                        UPDATE "Customers" SET "OrganizationId" = legacy_organization_id WHERE "OrganizationId" IS NULL;
                        UPDATE "Services" SET "OrganizationId" = legacy_organization_id WHERE "OrganizationId" IS NULL;
                        UPDATE "Quotes" SET "OrganizationId" = legacy_organization_id WHERE "OrganizationId" IS NULL;
                    END IF;
                END $$;
                """);
            foreach (var table in new[] { "Customers", "Services", "Quotes" })
                migrationBuilder.AlterColumn<Guid>(name: "OrganizationId", table: table, type: "uuid", nullable: false, oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);
            migrationBuilder.DropIndex(name: "IX_Customers_Cnpj", table: "Customers"); migrationBuilder.DropIndex(name: "IX_Customers_IsActive_LegalName", table: "Customers"); migrationBuilder.DropIndex(name: "IX_Services_Code", table: "Services"); migrationBuilder.DropIndex(name: "IX_Services_IsActive_Name", table: "Services"); migrationBuilder.DropIndex(name: "IX_Quotes_Number", table: "Quotes"); migrationBuilder.DropIndex(name: "IX_Quotes_Status_UpdatedAtUtc", table: "Quotes"); migrationBuilder.DropIndex(name: "IX_Quotes_CustomerId_CreatedAtUtc", table: "Quotes");
            migrationBuilder.CreateIndex(name: "IX_Customers_OrganizationId_Cnpj", table: "Customers", columns: new[] { "OrganizationId", "Cnpj" }, unique: true); migrationBuilder.CreateIndex(name: "IX_Customers_OrganizationId_IsActive_LegalName", table: "Customers", columns: new[] { "OrganizationId", "IsActive", "LegalName" }); migrationBuilder.CreateIndex(name: "IX_Services_OrganizationId_Code", table: "Services", columns: new[] { "OrganizationId", "Code" }, unique: true); migrationBuilder.CreateIndex(name: "IX_Services_OrganizationId_IsActive_Name", table: "Services", columns: new[] { "OrganizationId", "IsActive", "Name" }); migrationBuilder.CreateIndex(name: "IX_Quotes_OrganizationId_Number", table: "Quotes", columns: new[] { "OrganizationId", "Number" }, unique: true); migrationBuilder.CreateIndex(name: "IX_Quotes_OrganizationId_Status_UpdatedAtUtc", table: "Quotes", columns: new[] { "OrganizationId", "Status", "UpdatedAtUtc" }); migrationBuilder.CreateIndex(name: "IX_Quotes_OrganizationId_CustomerId_CreatedAtUtc", table: "Quotes", columns: new[] { "OrganizationId", "CustomerId", "CreatedAtUtc" });
            foreach (var table in new[] { "Customers", "Services", "Quotes" }) migrationBuilder.AddForeignKey(name: $"FK_{table}_Organizations_OrganizationId", table: table, column: "OrganizationId", principalTable: "Organizations", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Tenant ownership migration is intentionally irreversible.");
        }
    }
}
