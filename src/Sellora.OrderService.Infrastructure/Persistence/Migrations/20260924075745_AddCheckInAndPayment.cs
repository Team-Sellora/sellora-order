using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sellora.OrderService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckInAndPayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancellation_reason",
                table: "customer_order",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelled_at",
                table: "customer_order",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "checked_out_at",
                table: "customer_order",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "checkout_latitude",
                table: "customer_order",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "checkout_longitude",
                table: "customer_order",
                type: "double precision",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "order_check_in",
                columns: table => new
                {
                    order_check_in_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_rep_id = table.Column<Guid>(type: "uuid", nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    accuracy_meters = table.Column<double>(type: "double precision", nullable: true),
                    shop_latitude = table.Column<double>(type: "double precision", nullable: false),
                    shop_longitude = table.Column<double>(type: "double precision", nullable: false),
                    distance_meters = table.Column<double>(type: "double precision", nullable: false),
                    radius_meters = table.Column<double>(type: "double precision", nullable: false),
                    accepted = table.Column<bool>(type: "boolean", nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_check_in", x => x.order_check_in_id);
                    table.CheckConstraint("ck_order_check_in_distance", "distance_meters >= 0");
                    table.CheckConstraint("ck_order_check_in_latitude", "latitude BETWEEN -90 AND 90");
                    table.CheckConstraint("ck_order_check_in_longitude", "longitude BETWEEN -180 AND 180");
                    table.ForeignKey(
                        name: "fk_order_check_in_customer_order",
                        column: x => x.order_id,
                        principalTable: "customer_order",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment",
                columns: table => new
                {
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sales_rep_id = table.Column<Guid>(type: "uuid", nullable: false),
                    check_in_id = table.Column<Guid>(type: "uuid", nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    distance_meters = table.Column<double>(type: "double precision", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment", x => x.payment_id);
                    table.CheckConstraint("ck_payment_amount_non_negative", "amount >= 0");
                    table.ForeignKey(
                        name: "fk_payment_customer_order",
                        column: x => x.order_id,
                        principalTable: "customer_order",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_order_check_in",
                        column: x => x.check_in_id,
                        principalTable: "order_check_in",
                        principalColumn: "order_check_in_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_order_check_in_order_rep_recorded",
                table: "order_check_in",
                columns: new[] { "order_id", "sales_rep_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_check_in_id",
                table: "payment",
                column: "check_in_id");

            migrationBuilder.CreateIndex(
                name: "uq_payment_order",
                table: "payment",
                column: "order_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment");

            migrationBuilder.DropTable(
                name: "order_check_in");

            migrationBuilder.DropColumn(
                name: "cancellation_reason",
                table: "customer_order");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                table: "customer_order");

            migrationBuilder.DropColumn(
                name: "checked_out_at",
                table: "customer_order");

            migrationBuilder.DropColumn(
                name: "checkout_latitude",
                table: "customer_order");

            migrationBuilder.DropColumn(
                name: "checkout_longitude",
                table: "customer_order");
        }
    }
}
