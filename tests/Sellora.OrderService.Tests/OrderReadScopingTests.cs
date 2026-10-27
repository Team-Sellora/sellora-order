using Microsoft.Extensions.Options;
using Sellora.OrderService.Application.Identity;
using Sellora.OrderService.Application.Orders;
using Sellora.OrderService.Infrastructure.Orders;

namespace Sellora.OrderService.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class OrderReadScopingTests
{
    private readonly PostgreSqlFixture _fixture;

    public OrderReadScopingTests(PostgreSqlFixture fixture) => _fixture = fixture;

    private async Task<Guid> SeedAsync(Guid companyId, Guid repId, Placement placement)
    {
        await using var db = _fixture.CreateContext(companyId);
        return (await TestOrders.SeedAsync(db, companyId, repId, placement)).OrderId;
    }

    private static OrderReadService Reads(Sellora.OrderService.Infrastructure.Persistence.OrderDbContext db, CallerStub caller) =>
        new(db, caller, TimeProvider.System, Options.Create(new CancellationOptions()));

    private async Task<IReadOnlyList<Guid>> ListIdsAsync(Guid companyId, CallerStub caller)
    {
        await using var db = _fixture.CreateContext(companyId);
        var page = await Reads(db, caller)
            .ListAsync(new OrderListQuery(1, 200), CancellationToken.None);
        return page.Items.Select(item => item.OrderId).ToList();
    }

    [Fact]
    public async Task Reads_are_scoped_by_role_and_tenant()
    {
        var companyId = Guid.NewGuid();
        var repA = Guid.NewGuid();
        var repB = Guid.NewGuid();
        var placeA = Placement.New();
        var placeB = Placement.New();

        var orderA = await SeedAsync(companyId, repA, placeA);
        var orderB = await SeedAsync(companyId, repB, placeB);
        var otherTenantOrder = await SeedAsync(Guid.NewGuid(), repA, placeA);

        Assert.Equal(new[] { orderA },
            await ListIdsAsync(companyId, new CallerStub { Role = SelloraRoles.SalesRep, SalesRepId = repA }));

        Assert.Equal(new[] { orderB },
            await ListIdsAsync(companyId, new CallerStub { Role = SelloraRoles.AgencyOperator, AgencyId = placeB.AgencyId }));

        Assert.Equal(new[] { orderA },
            await ListIdsAsync(companyId, new CallerStub { Role = SelloraRoles.AreaManager, ProvinceIds = new[] { placeA.ProvinceId } }));

        Assert.Equal(new[] { orderB },
            await ListIdsAsync(companyId, new CallerStub { Role = SelloraRoles.ShopOwner, ShopId = placeB.ShopId }));

        var adminView = await ListIdsAsync(companyId, new CallerStub { Role = SelloraRoles.CompanyAdmin });
        Assert.Equal(2, adminView.Count);
        Assert.DoesNotContain(otherTenantOrder, adminView);

        Assert.Empty(await ListIdsAsync(companyId, new CallerStub { Role = SelloraRoles.AgencyOperator }));
    }

    [Fact]
    public async Task Detail_includes_lines_and_steps_and_hides_out_of_scope_orders()
    {
        var companyId = Guid.NewGuid();
        var repA = Guid.NewGuid();
        var orderId = await SeedAsync(companyId, repA, Placement.New());

        async Task<OrderResponse?> GetAs(Guid tenant, CallerStub caller)
        {
            await using var db = _fixture.CreateContext(tenant);
            return await Reads(db, caller).GetAsync(orderId, CancellationToken.None);
        }

        var own = await GetAs(companyId, new CallerStub { Role = SelloraRoles.SalesRep, SalesRepId = repA });
        Assert.NotNull(own);
        Assert.Single(own!.Lines);
        Assert.Equal(4, own.VerificationSteps.Count);
        Assert.NotEqual(Guid.Empty, own.ReservationId);

        Assert.Null(await GetAs(companyId, new CallerStub { Role = SelloraRoles.SalesRep, SalesRepId = Guid.NewGuid() }));
        Assert.Null(await GetAs(Guid.NewGuid(), new CallerStub { Role = SelloraRoles.CompanyAdmin }));
    }
}
