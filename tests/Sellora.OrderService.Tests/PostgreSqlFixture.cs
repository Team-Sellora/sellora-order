using Microsoft.EntityFrameworkCore;
using Sellora.OrderService.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Sellora.OrderService.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSQL integration tests";
}

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("order_tests")
        .WithUsername("sellora")
        .WithPassword("sellora_test_pw")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // MigrateAsync (not EnsureCreated) so the real migration is exercised.
        await using var db = CreateContext(null);
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public OrderDbContext CreateContext(Guid? companyId) => new(
        new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .EnableSensitiveDataLogging()
            .Options,
        new TenantStub(companyId));
}
