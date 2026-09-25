using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sellora.OrderService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderApprovalAndCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "cancellation_reason",
                table: "customer_order",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancelled_by",
                table: "customer_order",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "confirmed_at",
                table: "customer_order",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "customer_order",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "order_decision",
                columns: table => new
                {
                    order_decision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    actor_user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    actor_role = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status_before = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status_after = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_decision", x => x.order_decision_id);
                    table.CheckConstraint("ck_order_decision_kind", "kind IN ('Approved', 'Rejected', 'CancelledByShop')");
                    table.CheckConstraint("ck_order_decision_rejection_reason", "kind <> 'Rejected' OR (reason IS NOT NULL AND length(trim(reason)) > 0)");
                    table.ForeignKey(
                        name: "fk_order_decision_customer_order",
                        column: x => x.order_id,
                        principalTable: "customer_order",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_order_decision_order_decided",
                table: "order_decision",
                columns: new[] { "order_id", "decided_at" });

            migrationBuilder.Sql("""
                UPDATE customer_order
                SET confirmed_at = CASE
                    WHEN fulfilment_type = 'ScheduledDelivery' THEN order_date
                    ELSE checked_out_at
                END
                WHERE status = 'Confirmed' AND confirmed_at IS NULL;
                """);    
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_decision");

            migrationBuilder.DropColumn(
                name: "cancelled_by",
                table: "customer_order");

            migrationBuilder.DropColumn(
                name: "confirmed_at",
                table: "customer_order");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "customer_order");

            migrationBuilder.AlterColumn<string>(
                name: "cancellation_reason",
                table: "customer_order",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);
        }
    }
}
