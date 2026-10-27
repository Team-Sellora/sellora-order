using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sellora.OrderService.Domain.Tenancy;

namespace Sellora.OrderService.Infrastructure.Persistence;

/// <summary>
/// Lets `dotnet ef` build the context without starting the API
/// (the API needs JWT settings that design time does not have).
/// </summary>
public sealed class OrderDbContextFactory : IDesignTimeDbContextFactory<OrderDbContext>
{
    public OrderDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5436;Database=order_db;Username=sellora;Password=sellora_dev_pw";

        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new OrderDbContext(options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid? CompanyId => null;
    }
}
