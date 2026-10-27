using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sellora.OrderService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderEventsOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "agency_email",
                table: "customer_order",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "agency_name",
                table: "customer_order",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sales_rep_name",
                table: "customer_order",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shop_name",
                table: "customer_order",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shop_owner_email",
                table: "customer_order",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shop_owner_name",
                table: "customer_order",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "outbox_message",
                columns: table => new
                {
                    outbox_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    aggregate_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    aggregate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    event_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    correlation_id = table.Column<string>(type: "text", maxLength: 128, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    schema_version = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_message", x => x.outbox_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_key_order",
                table: "outbox_message",
                columns: new[] { "message_key", "occurred_at", "ordinal" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_pending_relay",
                table: "outbox_message",
                columns: new[] { "published_at", "next_attempt_at", "lease_expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_message");

            migrationBuilder.DropColumn(
                name: "agency_email",
                table: "customer_order");

            migrationBuilder.DropColumn(
                name: "agency_name",
                table: "customer_order");

            migrationBuilder.DropColumn(
                name: "sales_rep_name",
                table: "customer_order");

            migrationBuilder.DropColumn(
                name: "shop_name",
                table: "customer_order");

            migrationBuilder.DropColumn(
                name: "shop_owner_email",
                table: "customer_order");

            migrationBuilder.DropColumn(
                name: "shop_owner_name",
                table: "customer_order");
        }
    }
}
