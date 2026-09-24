using Microsoft.EntityFrameworkCore;
using Sellora.OrderService.Domain.Entities;
using Sellora.OrderService.Domain.Tenancy;

namespace Sellora.OrderService.Infrastructure.Persistence;

public class OrderDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public OrderDbContext(
        DbContextOptions<OrderDbContext> options,
        ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    public DbSet<OrderCheckIn> OrderCheckIns => Set<OrderCheckIn>();

    public DbSet<Payment> Payments => Set<Payment>();

    /// <summary>
    /// Events committed with the order change they describe (US-E4-4).
    /// No tenant filter: the relay reads every tenant's rows, and nothing
    /// else queries this table.
    /// </summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(OrderDbContext).Assembly);

        // Company boundary: no tenant in the token means no rows.
        modelBuilder.Entity<Order>()
            .HasQueryFilter(order =>
                _tenantContext.CompanyId != null &&
                order.CompanyId == _tenantContext.CompanyId);

        modelBuilder.Entity<OrderCheckIn>()
            .HasQueryFilter(checkIn =>
                _tenantContext.CompanyId != null &&
                checkIn.CompanyId == _tenantContext.CompanyId);

        modelBuilder.Entity<Payment>()
            .HasQueryFilter(payment =>
                _tenantContext.CompanyId != null &&
                payment.CompanyId == _tenantContext.CompanyId);
    }
}
