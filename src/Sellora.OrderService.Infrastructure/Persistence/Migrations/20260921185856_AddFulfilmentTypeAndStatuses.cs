using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sellora.OrderService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFulfilmentTypeAndStatuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "fulfilment_type",
                table: "customer_order",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_customer_order_company_shop_status",
                table: "customer_order",
                columns: new[] { "company_id", "shop_id", "status" });
            migrationBuilder.Sql("UPDATE customer_order SET status = 'Confirmed' WHERE status = 'Submitted';");    
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_customer_order_company_shop_status",
                table: "customer_order");

            migrationBuilder.DropColumn(
                name: "fulfilment_type",
                table: "customer_order");
        }
    }
}
