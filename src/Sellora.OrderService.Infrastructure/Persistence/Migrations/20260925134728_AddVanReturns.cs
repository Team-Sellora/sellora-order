using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sellora.OrderService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVanReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "van_return",
                columns: table => new
                {
                    van_return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_reference = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    sales_rep_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_rep_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    agency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    van_inventory_owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    declared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    declared_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    acceptance_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_van_return", x => x.van_return_id);
                    table.CheckConstraint("ck_van_return_status", "status IN ('Declared', 'Accepted')");
                });

            migrationBuilder.CreateTable(
                name: "van_return_line",
                columns: table => new
                {
                    van_return_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    van_return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_name_snapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    declared_quantity = table.Column<int>(type: "integer", nullable: false),
                    counted_quantity = table.Column<int>(type: "integer", nullable: true),
                    variance = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_van_return_line", x => x.van_return_line_id);
                    table.CheckConstraint("ck_van_return_line_counted", "counted_quantity IS NULL OR (counted_quantity >= 0 AND counted_quantity <= declared_quantity)");
                    table.CheckConstraint("ck_van_return_line_declared", "declared_quantity > 0");
                    table.CheckConstraint("ck_van_return_line_variance", "(counted_quantity IS NULL AND variance IS NULL) OR variance = declared_quantity - counted_quantity");
                    table.ForeignKey(
                        name: "fk_van_return_line_van_return",
                        column: x => x.van_return_id,
                        principalTable: "van_return",
                        principalColumn: "van_return_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_van_return_agency_status",
                table: "van_return",
                columns: new[] { "company_id", "agency_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_van_return_sales_rep",
                table: "van_return",
                columns: new[] { "company_id", "sales_rep_id" });

            migrationBuilder.CreateIndex(
                name: "uq_van_return_company_reference",
                table: "van_return",
                columns: new[] { "company_id", "return_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_van_return_line_product",
                table: "van_return_line",
                columns: new[] { "van_return_id", "product_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "van_return_line");

            migrationBuilder.DropTable(
                name: "van_return");
        }
    }
}
