using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sellora.OrderService.Domain.Entities;

namespace Sellora.OrderService.Infrastructure.Persistence.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public const string ReferenceUniqueIndex = "uq_customer_order_company_reference";

    public void Configure(EntityTypeBuilder<Order> builder)
    {
        // "order" is a reserved word in PostgreSQL.
        builder.ToTable("customer_order", table =>
        {
            table.HasCheckConstraint("ck_customer_order_subtotal_non_negative", "subtotal >= 0");
            table.HasCheckConstraint("ck_customer_order_total_non_negative", "total >= 0");
        });

        builder.HasKey(order => order.OrderId).HasName("pk_customer_order");

        builder.Property(order => order.OrderId).HasColumnName("order_id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(order => order.CompanyId).HasColumnName("company_id").HasColumnType("uuid").IsRequired();
        builder.Property(order => order.ShopId).HasColumnName("shop_id").HasColumnType("uuid").IsRequired();
        builder.Property(order => order.SalesRepId).HasColumnName("sales_rep_id").HasColumnType("uuid").IsRequired();
        builder.Property(order => order.AgencyId).HasColumnName("agency_id").HasColumnType("uuid").IsRequired();
        builder.Property(order => order.TerritoryId).HasColumnName("territory_id").HasColumnType("uuid").IsRequired();
        builder.Property(order => order.ProvinceId).HasColumnName("province_id").HasColumnType("uuid").IsRequired();

        builder.Property(order => order.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(order => order.OrderDate)
            .HasColumnName("order_date")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(order => order.OrderReference)
            .HasColumnName("order_reference")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(order => order.Subtotal).HasColumnName("subtotal").HasPrecision(18, 2).IsRequired();
        builder.Property(order => order.Total).HasColumnName("total").HasPrecision(18, 2).IsRequired();

        builder.HasMany(order => order.Lines)
            .WithOne()
            .HasForeignKey(line => line.OrderId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_order_line_customer_order");

        builder.Navigation(order => order.Lines)
            .HasField("_lines")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(order => new { order.CompanyId, order.OrderReference })
            .IsUnique()
            .HasDatabaseName(ReferenceUniqueIndex);

        // One index per scoping path used by GET /api/orders.
        builder.HasIndex(order => new { order.CompanyId, order.SalesRepId, order.OrderDate })
            .HasDatabaseName("ix_customer_order_company_rep_date");
        builder.HasIndex(order => new { order.CompanyId, order.AgencyId, order.OrderDate })
            .HasDatabaseName("ix_customer_order_company_agency_date");
        builder.HasIndex(order => new { order.CompanyId, order.ProvinceId, order.OrderDate })
            .HasDatabaseName("ix_customer_order_company_province_date");
        builder.HasIndex(order => new { order.CompanyId, order.ShopId, order.OrderDate })
            .HasDatabaseName("ix_customer_order_company_shop_date");
    }
}

public sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.ToTable("order_line", table =>
        {
            table.HasCheckConstraint("ck_order_line_quantity_positive", "quantity > 0");
            table.HasCheckConstraint("ck_order_line_unit_price_non_negative", "unit_price_snapshot >= 0");
        });

        builder.HasKey(line => line.OrderLineId).HasName("pk_order_line");

        builder.Property(line => line.OrderLineId).HasColumnName("order_line_id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(line => line.OrderId).HasColumnName("order_id").HasColumnType("uuid").IsRequired();
        builder.Property(line => line.ProductId).HasColumnName("product_id").HasColumnType("uuid").IsRequired();
        builder.Property(line => line.ProductNameSnapshot).HasColumnName("product_name_snapshot").HasMaxLength(200).IsRequired();
        builder.Property(line => line.Quantity).HasColumnName("quantity").IsRequired();
        builder.Property(line => line.UnitPriceSnapshot).HasColumnName("unit_price_snapshot").HasPrecision(18, 2).IsRequired();
        builder.Property(line => line.LineTotal).HasColumnName("line_total").HasPrecision(18, 2).IsRequired();

        // Database-level backstop for the "no duplicate product" rule.
        builder.HasIndex(line => new { line.OrderId, line.ProductId })
            .IsUnique()
            .HasDatabaseName("uq_order_line_order_product");
    }
}
