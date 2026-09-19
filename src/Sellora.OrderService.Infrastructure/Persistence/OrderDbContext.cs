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
    }
}
