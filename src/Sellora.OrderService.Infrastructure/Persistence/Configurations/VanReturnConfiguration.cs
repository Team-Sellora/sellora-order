using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sellora.OrderService.Domain.Entities;

namespace Sellora.OrderService.Infrastructure.Persistence.Configurations;

/// <summary>US-E4-6: van returns and their lines.</summary>
public sealed class VanReturnConfiguration : IEntityTypeConfiguration<VanReturn>
{
    public void Configure(EntityTypeBuilder<VanReturn> builder)
    {
        builder.ToTable("van_return", table =>
            table.HasCheckConstraint("ck_van_return_status", "status IN ('Declared', 'Accepted')"));

        builder.HasKey(vanReturn => vanReturn.VanReturnId).HasName("pk_van_return");

        builder.Property(vanReturn => vanReturn.VanReturnId).HasColumnName("van_return_id").ValueGeneratedNever();
        builder.Property(vanReturn => vanReturn.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(vanReturn => vanReturn.ReturnReference).HasColumnName("return_reference").HasMaxLength(30).IsRequired();
        builder.Property(vanReturn => vanReturn.SalesRepId).HasColumnName("sales_rep_id").IsRequired();
        builder.Property(vanReturn => vanReturn.SalesRepName).HasColumnName("sales_rep_name").HasMaxLength(200);
        builder.Property(vanReturn => vanReturn.AgencyId).HasColumnName("agency_id").IsRequired();
        builder.Property(vanReturn => vanReturn.VanInventoryOwnerId).HasColumnName("van_inventory_owner_id").IsRequired();
        builder.Property(vanReturn => vanReturn.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(vanReturn => vanReturn.DeclaredAt).HasColumnName("declared_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(vanReturn => vanReturn.DeclaredBy).HasColumnName("declared_by").HasMaxLength(200).IsRequired();
        builder.Property(vanReturn => vanReturn.AcceptedAt).HasColumnName("accepted_at").HasColumnType("timestamp with time zone");
        builder.Property(vanReturn => vanReturn.AcceptedBy).HasColumnName("accepted_by").HasMaxLength(200);
        builder.Property(vanReturn => vanReturn.AcceptanceNote).HasColumnName("acceptance_note").HasMaxLength(VanReturn.MaxNoteLength);

        // xmin: two operators accepting the same return cannot both win.
        builder.Property(vanReturn => vanReturn.Version).IsRowVersion();

        builder.Ignore(vanReturn => vanReturn.TotalDeclared);
        builder.Ignore(vanReturn => vanReturn.TotalCounted);
        builder.Ignore(vanReturn => vanReturn.TotalVariance);

        builder.HasIndex(vanReturn => new { vanReturn.CompanyId, vanReturn.ReturnReference })
            .IsUnique()
            .HasDatabaseName("uq_van_return_company_reference");

        // The agency's pending-acceptance list.
        builder.HasIndex(vanReturn => new { vanReturn.CompanyId, vanReturn.AgencyId, vanReturn.Status })
            .HasDatabaseName("ix_van_return_agency_status");

        builder.HasIndex(vanReturn => new { vanReturn.CompanyId, vanReturn.SalesRepId })
            .HasDatabaseName("ix_van_return_sales_rep");

        builder.HasMany(vanReturn => vanReturn.Lines)
            .WithOne()
            .HasForeignKey(line => line.VanReturnId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_van_return_line_van_return");

        builder.Navigation(vanReturn => vanReturn.Lines)
            .HasField("_lines")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class VanReturnLineConfiguration : IEntityTypeConfiguration<VanReturnLine>
{
    public void Configure(EntityTypeBuilder<VanReturnLine> builder)
    {
        builder.ToTable("van_return_line", table =>
        {
            table.HasCheckConstraint("ck_van_return_line_declared", "declared_quantity > 0");

            // Counted is never negative nor above declared; variance is
            // always declared minus counted.
            table.HasCheckConstraint(
                "ck_van_return_line_counted",
                "counted_quantity IS NULL OR (counted_quantity >= 0 AND counted_quantity <= declared_quantity)");
            table.HasCheckConstraint(
                "ck_van_return_line_variance",
                "(counted_quantity IS NULL AND variance IS NULL) OR variance = declared_quantity - counted_quantity");
        });

        builder.HasKey(line => line.VanReturnLineId).HasName("pk_van_return_line");

        builder.Property(line => line.VanReturnLineId).HasColumnName("van_return_line_id").ValueGeneratedNever();
        builder.Property(line => line.VanReturnId).HasColumnName("van_return_id").IsRequired();
        builder.Property(line => line.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(line => line.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(line => line.ProductNameSnapshot).HasColumnName("product_name_snapshot").HasMaxLength(200);
        builder.Property(line => line.DeclaredQuantity).HasColumnName("declared_quantity").IsRequired();
        builder.Property(line => line.CountedQuantity).HasColumnName("counted_quantity");
        builder.Property(line => line.Variance).HasColumnName("variance");

        builder.HasIndex(line => new { line.VanReturnId, line.ProductId })
            .IsUnique()
            .HasDatabaseName("uq_van_return_line_product");
    }
}
