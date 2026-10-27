using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sellora.OrderService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxSequenceColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_message_key_order",
                table: "outbox_message");

            migrationBuilder.CreateSequence(
                name: "outbox_message_sequence_seq");

            migrationBuilder.AddColumn<long>(
                name: "sequence",
                table: "outbox_message",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_key_order",
                table: "outbox_message",
                columns: new[] { "message_key", "sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_message_key_order",
                table: "outbox_message");

            migrationBuilder.DropColumn(
                name: "sequence",
                table: "outbox_message");

            migrationBuilder.DropSequence(
                name: "outbox_message_sequence_seq");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_key_order",
                table: "outbox_message",
                columns: new[] { "message_key", "occurred_at", "ordinal" });
        }
    }
}
