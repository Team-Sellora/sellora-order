using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sellora.OrderService.Application.Identity;
using Sellora.OrderService.Application.Orders;
using Sellora.OrderService.Domain.Orders;
using Sellora.OrderService.Infrastructure.Orders;

namespace Sellora.OrderService.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class OrderPersistenceTests
{
    private readonly PostgreSqlFixture _fixture;

    public OrderPersistenceTests(PostgreSqlFixture fixture) => _fixture = fixture;

    private sealed record Placement(Guid ShopId, Guid AgencyId, Guid TerritoryId, Guid ProvinceId);

    private static Placement NewPlacement() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    private async Task<OrderResponse> CreateAsync(Guid companyId, Guid repId, Placement p)
    {
        await using var db = _fixture.CreateContext(companyId);
        var service = new OrderCreationService(
            db,
            new TenantStub(companyId),
            new CallerStub { Role = SelloraRoles.SalesRep, SalesRepId = repId },
            TimeProvider.System,
            NullLogger<OrderCreationService>.Instance);

        var result = await service.CreateAsync(
            new CreateOrderRequest(p.ShopId, p.AgencyId, p.TerritoryId, p.ProvinceId, new[]
            {
                new NewOrderLine(Guid.NewGuid(), "Sunlight Soap 100g", 3, 120.50m),
                new NewOrderLine(Guid.NewGuid(), "Anchor Milk 400g", 2, 1150.00m)
            }),
            CancellationToken.None);

        Assert.Equal(CreateOrderOutcome.Created, result.Outcome);
        return result.Order!;
    }

    private async Task<IReadOnlyList<Guid>> ListIdsAsync(Guid companyId, CallerStub caller)
    {
        await using var db = _fixture.CreateContext(companyId);
        var page = await new OrderReadService(db, caller)
            .ListAsync(new OrderListQuery(1, 200), CancellationToken.None);
        return page.Items.Select(item => item.OrderId).ToList();
    }

    [Fact]
    public async Task Created_order_is_persisted_with_server_totals_and_readable_reference()
    {
        var companyId = Guid.NewGuid();
        var repId = Guid.NewGuid();

        var created = await CreateAsync(companyId, repId, NewPlacement());

        Assert.Equal(2661.50m, created.Total);
        Assert.Equal(repId, created.SalesRepId);
        Assert.Matches("^ORD-\\d{6}-[A-Z2-9]{6}$", created.OrderReference);

        await using var db = _fixture.CreateContext(companyId);
        var stored = await db.Orders.Include(o => o.Lines).SingleAsync(o => o.OrderId == created.OrderId);

        Assert.Equal(2661.50m, stored.Total);
        Assert.Equal(2, stored.Lines.Count);
        Assert.Equal(companyId, stored.CompanyId);
    }

    [Fact]
    public async Task Missing_sales_rep_claim_is_refused_without_writing()
    {
        var companyId = Guid.NewGuid();
        await using var db = _fixture.CreateContext(companyId);
        var service = new OrderCreationService(
            db, new TenantStub(companyId),
            new CallerStub { Role = SelloraRoles.SalesRep, SalesRepId = null },
            TimeProvider.System, NullLogger<OrderCreationService>.Instance);

        var p = NewPlacement();
        var result = await service.CreateAsync(
            new CreateOrderRequest(p.ShopId, p.AgencyId, p.TerritoryId, p.ProvinceId,
                new[] { new NewOrderLine(Guid.NewGuid(), "Soap", 1, 10m) }),
            CancellationToken.None);

        Assert.Equal(CreateOrderOutcome.CallerNotSalesRep, result.Outcome);
        Assert.Equal(0, await db.Orders.CountAsync());
    }

    [Fact]
    public async Task Reads_are_scoped_by_role_and_tenant()
    {
        var companyId = Guid.NewGuid();
        var repA = Guid.NewGuid();
        var repB = Guid.NewGuid();
        var placeA = NewPlacement();
        var placeB = NewPlacement();

        var orderA = await CreateAsync(companyId, repA, placeA);
        var orderB = await CreateAsync(companyId, repB, placeB);
        var otherTenantOrder = await CreateAsync(Guid.NewGuid(), repA, placeA);

        Assert.Equal(new[] { orderA.OrderId },
            await ListIdsAsync(companyId, new CallerStub { Role = SelloraRoles.SalesRep, SalesRepId = repA }));

        Assert.Equal(new[] { orderB.OrderId },
            await ListIdsAsync(companyId, new CallerStub { Role = SelloraRoles.AgencyOperator, AgencyId = placeB.AgencyId }));

        Assert.Equal(new[] { orderA.OrderId },
            await ListIdsAsync(companyId, new CallerStub { Role = SelloraRoles.AreaManager, ProvinceIds = new[] { placeA.ProvinceId } }));

        Assert.Equal(new[] { orderB.OrderId },
            await ListIdsAsync(companyId, new CallerStub { Role = SelloraRoles.ShopOwner, ShopId = placeB.ShopId }));

        var adminView = await ListIdsAsync(companyId, new CallerStub { Role = SelloraRoles.CompanyAdmin });
        Assert.Equal(2, adminView.Count);
        Assert.DoesNotContain(otherTenantOrder.OrderId, adminView);

        // A role without its scope claim fails closed.
        Assert.Empty(await ListIdsAsync(companyId, new CallerStub { Role = SelloraRoles.AgencyOperator }));
    }

    [Fact]
    public async Task Detail_hides_out_of_scope_and_cross_tenant_orders()
    {
        var companyId = Guid.NewGuid();
        var repA = Guid.NewGuid();
        var order = await CreateAsync(companyId, repA, NewPlacement());

        async Task<OrderResponse?> GetAs(Guid tenant, CallerStub caller)
        {
            await using var db = _fixture.CreateContext(tenant);
            return await new OrderReadService(db, caller).GetAsync(order.OrderId, CancellationToken.None);
        }

        var own = await GetAs(companyId, new CallerStub { Role = SelloraRoles.SalesRep, SalesRepId = repA });
        Assert.NotNull(own);
        Assert.Equal(2, own!.Lines.Count);

        Assert.Null(await GetAs(companyId, new CallerStub { Role = SelloraRoles.SalesRep, SalesRepId = Guid.NewGuid() }));
        Assert.Null(await GetAs(Guid.NewGuid(), new CallerStub { Role = SelloraRoles.CompanyAdmin }));
    }
}
