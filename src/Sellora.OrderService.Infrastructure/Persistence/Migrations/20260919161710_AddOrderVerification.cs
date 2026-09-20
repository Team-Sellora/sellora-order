using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sellora.OrderService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "inventory_owner_id",
                table: "customer_order",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "reservation_id",
                table: "customer_order",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "order_verification_step",
                columns: table => new
                {
                    order_verification_step_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    passed = table.Column<bool>(type: "boolean", nullable: false),
                    detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_verification_step", x => x.order_verification_step_id);
                    table.ForeignKey(
                        name: "fk_order_verification_step_customer_order",
                        column: x => x.order_id,
                        principalTable: "customer_order",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_customer_order_reservation",
                table: "customer_order",
                column: "reservation_id");

            migrationBuilder.CreateIndex(
                name: "uq_order_verification_step_order_step",
                table: "order_verification_step",
                columns: new[] { "order_id", "step" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_verification_step");

            migrationBuilder.DropIndex(
                name: "ix_customer_order_reservation",
                table: "customer_order");

            migrationBuilder.DropColumn(
                name: "inventory_owner_id",
                table: "customer_order");

            migrationBuilder.DropColumn(
                name: "reservation_id",
                table: "customer_order");
        }
    }
}
