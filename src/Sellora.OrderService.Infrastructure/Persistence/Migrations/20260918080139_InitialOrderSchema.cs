using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sellora.OrderService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialOrderSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_order",
                columns: table => new
                {
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_rep_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    territory_id = table.Column<Guid>(type: "uuid", nullable: false),
                    province_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    order_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    order_reference = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_order", x => x.order_id);
                    table.CheckConstraint("ck_customer_order_subtotal_non_negative", "subtotal >= 0");
                    table.CheckConstraint("ck_customer_order_total_non_negative", "total >= 0");
                });

            migrationBuilder.CreateTable(
                name: "order_line",
                columns: table => new
                {
                    order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_name_snapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price_snapshot = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    line_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_line", x => x.order_line_id);
                    table.CheckConstraint("ck_order_line_quantity_positive", "quantity > 0");
                    table.CheckConstraint("ck_order_line_unit_price_non_negative", "unit_price_snapshot >= 0");
                    table.ForeignKey(
                        name: "fk_order_line_customer_order",
                        column: x => x.order_id,
                        principalTable: "customer_order",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_customer_order_company_agency_date",
                table: "customer_order",
                columns: new[] { "company_id", "agency_id", "order_date" });

            migrationBuilder.CreateIndex(
                name: "ix_customer_order_company_province_date",
                table: "customer_order",
                columns: new[] { "company_id", "province_id", "order_date" });

            migrationBuilder.CreateIndex(
                name: "ix_customer_order_company_rep_date",
                table: "customer_order",
                columns: new[] { "company_id", "sales_rep_id", "order_date" });

            migrationBuilder.CreateIndex(
                name: "ix_customer_order_company_shop_date",
                table: "customer_order",
                columns: new[] { "company_id", "shop_id", "order_date" });

            migrationBuilder.CreateIndex(
                name: "uq_customer_order_company_reference",
                table: "customer_order",
                columns: new[] { "company_id", "order_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_order_line_order_product",
                table: "order_line",
                columns: new[] { "order_id", "product_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_line");

            migrationBuilder.DropTable(
                name: "customer_order");
        }
    }
}
