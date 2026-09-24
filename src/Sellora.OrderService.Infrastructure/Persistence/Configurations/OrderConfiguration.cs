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

        builder.Property(order => order.FulfilmentType)
            .HasColumnName("fulfilment_type")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

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

        builder.Property(order => order.ReservationId).HasColumnName("reservation_id").HasColumnType("uuid").IsRequired();
        builder.Property(order => order.InventoryOwnerId).HasColumnName("inventory_owner_id").HasColumnType("uuid").IsRequired();

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

        builder.HasMany(order => order.VerificationSteps)
            .WithOne()
            .HasForeignKey(step => step.OrderId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_order_verification_step_customer_order");

        builder.Navigation(order => order.VerificationSteps)
            .HasField("_verificationSteps")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // US-E4-3: where and when the sale was completed.
        builder.Property(order => order.CheckoutLatitude).HasColumnName("checkout_latitude");
        builder.Property(order => order.CheckoutLongitude).HasColumnName("checkout_longitude");
        builder.Property(order => order.CheckedOutAt).HasColumnName("checked_out_at").HasColumnType("timestamp with time zone");
        builder.Property(order => order.CancelledAt).HasColumnName("cancelled_at").HasColumnType("timestamp with time zone");
        builder.Property(order => order.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(200);

        // US-E4-4: contact snapshot carried by every order event.
        builder.Property(order => order.ShopName).HasColumnName("shop_name").HasMaxLength(200);
        builder.Property(order => order.ShopOwnerName).HasColumnName("shop_owner_name").HasMaxLength(200);
        builder.Property(order => order.ShopOwnerEmail).HasColumnName("shop_owner_email").HasMaxLength(320);
        builder.Property(order => order.AgencyName).HasColumnName("agency_name").HasMaxLength(200);
        builder.Property(order => order.AgencyEmail).HasColumnName("agency_email").HasMaxLength(320);
        builder.Property(order => order.SalesRepName).HasColumnName("sales_rep_name").HasMaxLength(200);

        builder.HasMany(order => order.CheckIns)
            .WithOne()
            .HasForeignKey(checkIn => checkIn.OrderId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_order_check_in_customer_order");

        builder.Navigation(order => order.CheckIns)
            .HasField("_checkIns")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne(order => order.Payment)
            .WithOne()
            .HasForeignKey<Payment>(payment => payment.OrderId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_payment_customer_order");

        builder.HasIndex(order => order.ReservationId)
            .HasDatabaseName("ix_customer_order_reservation");

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

        // The credit check sums a shop's outstanding orders by status.
        builder.HasIndex(order => new { order.CompanyId, order.ShopId, order.Status })
            .HasDatabaseName("ix_customer_order_company_shop_status");
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

public sealed class OrderVerificationStepConfiguration : IEntityTypeConfiguration<OrderVerificationStep>
{
    public void Configure(EntityTypeBuilder<OrderVerificationStep> builder)
    {
        builder.ToTable("order_verification_step");

        builder.HasKey(step => step.OrderVerificationStepId).HasName("pk_order_verification_step");

        builder.Property(step => step.OrderVerificationStepId).HasColumnName("order_verification_step_id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(step => step.OrderId).HasColumnName("order_id").HasColumnType("uuid").IsRequired();
        builder.Property(step => step.Step).HasColumnName("step").HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(step => step.Passed).HasColumnName("passed").IsRequired();
        builder.Property(step => step.Detail).HasColumnName("detail").HasMaxLength(1000).IsRequired();
        builder.Property(step => step.RecordedAt).HasColumnName("recorded_at").HasColumnType("timestamp with time zone").IsRequired();

        builder.HasIndex(step => new { step.OrderId, step.Step })
            .IsUnique()
            .HasDatabaseName("uq_order_verification_step_order_step");
    }
}

public sealed class OrderCheckInConfiguration : IEntityTypeConfiguration<OrderCheckIn>
{
    public void Configure(EntityTypeBuilder<OrderCheckIn> builder)
    {
        builder.ToTable("order_check_in", table =>
        {
            table.HasCheckConstraint("ck_order_check_in_latitude", "latitude BETWEEN -90 AND 90");
            table.HasCheckConstraint("ck_order_check_in_longitude", "longitude BETWEEN -180 AND 180");
            table.HasCheckConstraint("ck_order_check_in_distance", "distance_meters >= 0");
        });

        builder.HasKey(checkIn => checkIn.OrderCheckInId).HasName("pk_order_check_in");

        builder.Property(checkIn => checkIn.OrderCheckInId).HasColumnName("order_check_in_id").ValueGeneratedNever();
        builder.Property(checkIn => checkIn.OrderId).HasColumnName("order_id").IsRequired();
        builder.Property(checkIn => checkIn.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(checkIn => checkIn.SalesRepId).HasColumnName("sales_rep_id").IsRequired();
        builder.Property(checkIn => checkIn.Latitude).HasColumnName("latitude").IsRequired();
        builder.Property(checkIn => checkIn.Longitude).HasColumnName("longitude").IsRequired();
        builder.Property(checkIn => checkIn.AccuracyMeters).HasColumnName("accuracy_meters");
        builder.Property(checkIn => checkIn.ShopLatitude).HasColumnName("shop_latitude").IsRequired();
        builder.Property(checkIn => checkIn.ShopLongitude).HasColumnName("shop_longitude").IsRequired();
        builder.Property(checkIn => checkIn.DistanceMeters).HasColumnName("distance_meters").IsRequired();
        builder.Property(checkIn => checkIn.RadiusMeters).HasColumnName("radius_meters").IsRequired();
        builder.Property(checkIn => checkIn.Accepted).HasColumnName("accepted").IsRequired();
        builder.Property(checkIn => checkIn.CapturedAt).HasColumnName("captured_at").HasColumnType("timestamp with time zone");
        builder.Property(checkIn => checkIn.RecordedAt).HasColumnName("recorded_at").HasColumnType("timestamp with time zone");
        builder.Property(checkIn => checkIn.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp with time zone");

        // Checkout looks up the latest accepted check-in for a rep on an order.
        builder.HasIndex(checkIn => new { checkIn.OrderId, checkIn.SalesRepId, checkIn.RecordedAt })
            .HasDatabaseName("ix_order_check_in_order_rep_recorded");
    }
}

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public const string OnePaymentPerOrderIndex = "uq_payment_order";

    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payment", table =>
        {
            table.HasCheckConstraint("ck_payment_amount_non_negative", "amount >= 0");
        });

        builder.HasKey(payment => payment.PaymentId).HasName("pk_payment");

        builder.Property(payment => payment.PaymentId).HasColumnName("payment_id").ValueGeneratedNever();
        builder.Property(payment => payment.OrderId).HasColumnName("order_id").IsRequired();
        builder.Property(payment => payment.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(payment => payment.Amount).HasColumnName("amount").HasPrecision(18, 2).IsRequired();
        builder.Property(payment => payment.Method).HasColumnName("method").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(payment => payment.SalesRepId).HasColumnName("sales_rep_id").IsRequired();
        builder.Property(payment => payment.CheckInId).HasColumnName("check_in_id").IsRequired();
        builder.Property(payment => payment.Latitude).HasColumnName("latitude").IsRequired();
        builder.Property(payment => payment.Longitude).HasColumnName("longitude").IsRequired();
        builder.Property(payment => payment.DistanceMeters).HasColumnName("distance_meters").IsRequired();
        builder.Property(payment => payment.RecordedAt).HasColumnName("recorded_at").HasColumnType("timestamp with time zone");

        // One cash collection per order; a double-submit hits this, not the till.
        builder.HasIndex(payment => payment.OrderId).IsUnique().HasDatabaseName(OnePaymentPerOrderIndex);

        builder.HasOne<OrderCheckIn>()
            .WithMany()
            .HasForeignKey(payment => payment.CheckInId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_payment_order_check_in");
    }
}
