using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sellora.OrderService.Application.Identity;
using Sellora.OrderService.Application.Orders;
using Sellora.OrderService.Domain.Entities;
using Sellora.OrderService.Domain.Orders;
using Sellora.OrderService.Infrastructure.Orders;
using Sellora.OrderService.Infrastructure.Outbox;
using Sellora.OrderService.Infrastructure.Persistence;

namespace Sellora.OrderService.Tests;

/// <summary>US-E4-5-T1: the shop cancellation endpoint's service against a real database.</summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class OrderCancellationServiceTests
{
    private readonly PostgreSqlFixture _fixture;
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Placement _place = Placement.New();
    private readonly DateTimeOffset _confirmedAt = new(2026, 9, 25, 4, 0, 0, TimeSpan.Zero);
    private readonly FakeClock _clock;

    public OrderCancellationServiceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        _clock = new FakeClock(_confirmedAt);
    }

    private async Task<Order> SeedAsync(Order order)
    {
        await using var db = _fixture.CreateContext(_companyId);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private Task<Order> SeedConfirmedAsync() =>
        SeedAsync(DecisionTestOrders.Approved(_companyId, _place, _confirmedAt));

    private OrderCancellationService Service(OrderDbContext db, Guid? shopId = null) => new(
        db,
        new TenantStub(_companyId),
        new CallerStub { Subject = DecisionTestOrders.ShopSub, Role = SelloraRoles.ShopOwner, ShopId = shopId ?? _place.ShopId },
        _clock,
        Options.Create(new CancellationOptions()),
        new OrderEventOutbox(new EntityFrameworkOutboxWriter(db), new FixedCorrelation("cancel-test-correlation")),
        NullLogger<OrderCancellationService>.Instance);

    private async Task<OrderDecisionResult> CancelAsync(Guid orderId, string? reason = null, Guid? shopId = null)
    {
        await using var db = _fixture.CreateContext(_companyId);
        return await Service(db, shopId).CancelAsync(orderId, new CancelOrderRequest(reason), CancellationToken.None);
    }

    private async Task<Order> ReloadAsync(Guid orderId)
    {
        await using var db = _fixture.CreateContext(_companyId);
        return await db.Orders.Include(order => order.Decisions).SingleAsync(order => order.OrderId == orderId);
    }

    private async Task<List<string>> EventTypesAsync(Guid orderId)
    {
        await using var db = _fixture.CreateContext(_companyId);
        return await db.OutboxMessages
            .Where(message => message.AggregateId == orderId)
            .OrderBy(message => message.Sequence)
            .Select(message => message.EventType)
            .ToListAsync();
    }

    // Acceptance scenario 1.
    [Fact]
    public async Task Cancelling_twenty_minutes_after_confirmation_records_the_shop_owner_and_time()
    {
        var order = await SeedConfirmedAsync();
        _clock.Now = _confirmedAt.AddMinutes(20);

        var result = await CancelAsync(order.OrderId, "Ordered the wrong pack size");

        Assert.Equal(OrderDecisionOutcome.Succeeded, result.Outcome);
        Assert.Equal("Cancelled", result.Order!.Status);
        Assert.False(result.Order.Cancellation!.CanCancel);

        var stored = await ReloadAsync(order.OrderId);
        Assert.Equal(OrderStatus.Cancelled, stored.Status);
        Assert.Equal(DecisionTestOrders.ShopSub, stored.CancelledBy);
        Assert.Equal(_clock.Now, stored.CancelledAt);
        var cancellation = stored.Decisions.Single(decision => decision.Kind == OrderDecisionKind.CancelledByShop);
        Assert.Equal(SelloraRoles.ShopOwner, cancellation.ActorRole);
        Assert.Equal("Ordered the wrong pack size", cancellation.Reason);

        Assert.Equal(new[] { "OrderCancelled" }, await EventTypesAsync(order.OrderId));
    }

    // Acceptance scenario 2.
    [Fact]
    public async Task Ninety_minutes_after_confirmation_is_refused_with_the_elapsed_time_and_the_order_stays_confirmed()
    {
        var order = await SeedConfirmedAsync();
        _clock.Now = _confirmedAt.AddMinutes(90);

        var result = await CancelAsync(order.OrderId);

        Assert.Equal(OrderDecisionOutcome.CancellationWindowClosed, result.Outcome);
        Assert.Contains("closed 30 minutes ago", result.Message);
        Assert.Equal(TimeSpan.FromMinutes(90), result.WindowClosed!.ElapsedSinceConfirmation);
        Assert.Equal(TimeSpan.FromMinutes(30), result.WindowClosed.ClosedAgo);

        Assert.Equal(OrderStatus.Confirmed, (await ReloadAsync(order.OrderId)).Status);
        Assert.Empty(await EventTypesAsync(order.OrderId));
    }

    // US-E4-5-Q2 asks for exactly this: the stored timestamp decides.
    [Fact]
    public async Task The_window_is_read_from_the_stored_confirmation_time()
    {
        var order = await SeedConfirmedAsync();
        await using (var db = _fixture.CreateContext(_companyId))
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE customer_order SET confirmed_at = {_confirmedAt.AddHours(-2)} WHERE order_id = {order.OrderId}");
        }

        _clock.Now = _confirmedAt.AddMinutes(1);

        var result = await CancelAsync(order.OrderId);

        Assert.Equal(OrderDecisionOutcome.CancellationWindowClosed, result.Outcome);
        Assert.Contains("closed 1 hour 1 minute ago", result.Message);
    }

    [Fact]
    public async Task A_shop_owner_cannot_cancel_another_shops_order()
    {
        var order = await SeedConfirmedAsync();
        _clock.Now = _confirmedAt.AddMinutes(5);

        var result = await CancelAsync(order.OrderId, shopId: Guid.NewGuid());

        Assert.Equal(OrderDecisionOutcome.OrderNotFound, result.Outcome);
        Assert.Equal(OrderStatus.Confirmed, (await ReloadAsync(order.OrderId)).Status);
    }

    [Fact]
    public async Task A_pending_order_the_shop_cancelled_cannot_then_be_approved()
    {
        var order = await SeedAsync(DecisionTestOrders.Pending(_companyId, _place, _confirmedAt));
        await CancelAsync(order.OrderId);

        await using var db = _fixture.CreateContext(_companyId);
        var approvals = new OrderApprovalService(
            db, new TenantStub(_companyId),
            new CallerStub { Subject = DecisionTestOrders.AgencySub, Role = SelloraRoles.AgencyOperator, AgencyId = _place.AgencyId },
            _clock, new OrderEventOutbox(new EntityFrameworkOutboxWriter(db), new FixedCorrelation("c")),
            NullLogger<OrderApprovalService>.Instance);

        var result = await approvals.DecideAsync(
            order.OrderId, new ApprovalDecisionRequest(ApprovalDecision.Approve, null), CancellationToken.None);

        Assert.Equal(OrderDecisionOutcome.Conflict, result.Outcome);
        Assert.Equal(OrderStatus.Cancelled, (await ReloadAsync(order.OrderId)).Status);
    }

    // Two decisions racing on the same order: the second save must fail, not overwrite.
    [Fact]
    public async Task A_cancellation_that_loses_a_race_with_an_approval_is_a_conflict()
    {
        var order = await SeedAsync(DecisionTestOrders.Pending(_companyId, _place, _confirmedAt));

        await using var cancelDb = _fixture.CreateContext(_companyId);
        var stale = await cancelDb.Orders
            .Include(candidate => candidate.Lines)
            .Include(candidate => candidate.Decisions)
            .SingleAsync(candidate => candidate.OrderId == order.OrderId);

        await using (var approveDb = _fixture.CreateContext(_companyId))
        {
            var fresh = await approveDb.Orders.Include(candidate => candidate.Decisions)
                .SingleAsync(candidate => candidate.OrderId == order.OrderId);
            fresh.Approve(_place.AgencyId, DecisionTestOrders.AgencySub, SelloraRoles.AgencyOperator, _clock.Now);
            await approveDb.SaveChangesAsync();
        }

        stale.CancelByShop(_place.ShopId, DecisionTestOrders.ShopSub, SelloraRoles.ShopOwner, null, _clock.Now, TimeSpan.FromHours(1));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => cancelDb.SaveChangesAsync());
        Assert.Equal(OrderStatus.Confirmed, (await ReloadAsync(order.OrderId)).Status);
    }

    [Fact]
    public async Task The_order_view_shows_the_remaining_window_computed_by_the_server()
    {
        var order = await SeedConfirmedAsync();
        _clock.Now = _confirmedAt.AddMinutes(20);

        await using var db = _fixture.CreateContext(_companyId);
        var reads = new OrderReadService(
            db, new CallerStub { Role = SelloraRoles.ShopOwner, ShopId = _place.ShopId },
            _clock, Options.Create(new CancellationOptions()));

        var view = await reads.GetAsync(order.OrderId, CancellationToken.None);

        Assert.True(view!.Cancellation!.CanCancel);
        Assert.Equal(40L * 60, view.Cancellation.RemainingSeconds);
        Assert.Equal(_confirmedAt.AddHours(1), view.Cancellation.ClosesAt);
        Assert.Equal(60, view.Cancellation.WindowMinutes);
        Assert.Equal("Approved", view.Approval!.State);
    }
}
