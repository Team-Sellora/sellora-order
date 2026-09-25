using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sellora.OrderService.Application.Identity;
using Sellora.OrderService.Application.Orders;
using Sellora.OrderService.Domain.Entities;
using Sellora.OrderService.Domain.Orders;
using Sellora.OrderService.Infrastructure.Orders;
using Sellora.OrderService.Infrastructure.Outbox;

namespace Sellora.OrderService.Tests;

/// <summary>US-E4-5-T3: agency approval against a real database.</summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class OrderApprovalServiceTests
{
    private const string Correlation = "approval-test-correlation";

    private readonly PostgreSqlFixture _fixture;
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Placement _place = Placement.New();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 9, 25, 5, 0, 0, TimeSpan.Zero));

    public OrderApprovalServiceTests(PostgreSqlFixture fixture) => _fixture = fixture;

    private async Task<Order> SeedPendingAsync()
    {
        await using var db = _fixture.CreateContext(_companyId);
        var order = DecisionTestOrders.Pending(_companyId, _place, _clock.Now.AddMinutes(-30));
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private async Task<OrderDecisionResult> DecideAsync(
        Guid orderId, ApprovalDecision decision, string? reason = null, Guid? agencyId = null)
    {
        await using var db = _fixture.CreateContext(_companyId);
        var service = new OrderApprovalService(
            db,
            new TenantStub(_companyId),
            new CallerStub
            {
                Subject = DecisionTestOrders.AgencySub,
                Role = SelloraRoles.AgencyOperator,
                AgencyId = agencyId ?? _place.AgencyId
            },
            _clock,
            new OrderEventOutbox(new EntityFrameworkOutboxWriter(db), new FixedCorrelation(Correlation)),
            NullLogger<OrderApprovalService>.Instance);

        return await service.DecideAsync(orderId, new ApprovalDecisionRequest(decision, reason), CancellationToken.None);
    }

    private async Task<Order> ReloadAsync(Guid orderId)
    {
        await using var db = _fixture.CreateContext(_companyId);
        return await db.Orders.Include(order => order.Decisions).SingleAsync(order => order.OrderId == orderId);
    }

    private async Task<List<OutboxMessage>> EventsAsync(Guid orderId)
    {
        await using var db = _fixture.CreateContext(_companyId);
        return await db.OutboxMessages
            .Where(message => message.AggregateId == orderId)
            .OrderBy(message => message.Sequence)
            .ToListAsync();
    }

    [Fact]
    public async Task Approval_confirms_the_order_and_publishes_approved_then_confirmed()
    {
        var order = await SeedPendingAsync();

        var result = await DecideAsync(order.OrderId, ApprovalDecision.Approve);

        Assert.Equal(OrderDecisionOutcome.Succeeded, result.Outcome);
        Assert.True(result.Changed);
        Assert.Equal("Confirmed", result.Order!.Status);

        var stored = await ReloadAsync(order.OrderId);
        Assert.Equal(OrderStatus.Confirmed, stored.Status);
        Assert.Equal(_clock.Now, stored.ConfirmedAt);
        var decision = Assert.Single(stored.Decisions);
        Assert.Equal(DecisionTestOrders.AgencySub, decision.ActorUserId);
        Assert.Equal(SelloraRoles.AgencyOperator, decision.ActorRole);
        Assert.Equal(_clock.Now, decision.DecidedAt);

        var events = await EventsAsync(order.OrderId);
        Assert.Equal(new[] { "OrderApproved", "OrderConfirmed" }, events.Select(e => e.EventType));
        Assert.All(events, e => Assert.Equal(order.OrderReference, e.MessageKey));

        var approved = JsonDocument.Parse(events[0].Payload).RootElement;
        Assert.Equal(DecisionTestOrders.AgencySub, approved.GetProperty("approvedBy").GetProperty("userId").GetString());
        Assert.Equal("AgencyOperator", approved.GetProperty("approvedBy").GetProperty("role").GetString());
    }

    // Acceptance scenario 3, through the service and database.
    [Fact]
    public async Task Rejection_without_a_reason_is_a_validation_error_and_changes_nothing()
    {
        var order = await SeedPendingAsync();

        var result = await DecideAsync(order.OrderId, ApprovalDecision.Reject, "  ");

        Assert.Equal(OrderDecisionOutcome.ReasonRequired, result.Outcome);
        var stored = await ReloadAsync(order.OrderId);
        Assert.Equal(OrderStatus.PendingApproval, stored.Status);
        Assert.Empty(stored.Decisions);
        Assert.Empty(await EventsAsync(order.OrderId));
    }

    [Fact]
    public async Task Rejection_cancels_records_the_reason_and_publishes_order_cancelled()
    {
        var order = await SeedPendingAsync();

        var result = await DecideAsync(order.OrderId, ApprovalDecision.Reject, "Shop has an unpaid invoice");

        Assert.Equal(OrderDecisionOutcome.Succeeded, result.Outcome);
        var stored = await ReloadAsync(order.OrderId);
        Assert.Equal(OrderStatus.Cancelled, stored.Status);
        Assert.Equal("Shop has an unpaid invoice", stored.CancellationReason);
        Assert.Equal("Shop has an unpaid invoice", Assert.Single(stored.Decisions).Reason);

        var cancelled = Assert.Single(await EventsAsync(order.OrderId));
        Assert.Equal("OrderCancelled", cancelled.EventType);
        var payload = JsonDocument.Parse(cancelled.Payload).RootElement;
        Assert.Equal("AgencyRejection", payload.GetProperty("source").GetString());
        Assert.Equal(order.ReservationId, payload.GetProperty("reservationId").GetGuid());
        Assert.Equal("Shop has an unpaid invoice", payload.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Another_agencys_order_looks_missing()
    {
        var order = await SeedPendingAsync();

        var result = await DecideAsync(order.OrderId, ApprovalDecision.Approve, agencyId: Guid.NewGuid());

        Assert.Equal(OrderDecisionOutcome.OrderNotFound, result.Outcome);
        Assert.Equal(OrderStatus.PendingApproval, (await ReloadAsync(order.OrderId)).Status);
    }

    [Fact]
    public async Task Repeating_an_approval_succeeds_without_new_events()
    {
        var order = await SeedPendingAsync();
        await DecideAsync(order.OrderId, ApprovalDecision.Approve);

        var repeat = await DecideAsync(order.OrderId, ApprovalDecision.Approve);

        Assert.Equal(OrderDecisionOutcome.Succeeded, repeat.Outcome);
        Assert.False(repeat.Changed);
        Assert.Equal(2, (await EventsAsync(order.OrderId)).Count);
    }

    [Fact]
    public async Task An_approved_order_cannot_then_be_rejected()
    {
        var order = await SeedPendingAsync();
        await DecideAsync(order.OrderId, ApprovalDecision.Approve);

        var result = await DecideAsync(order.OrderId, ApprovalDecision.Reject, "Changed my mind");

        Assert.Equal(OrderDecisionOutcome.Conflict, result.Outcome);
        Assert.Equal(OrderStatus.Confirmed, (await ReloadAsync(order.OrderId)).Status);
    }

    [Fact]
    public async Task A_caller_without_an_agency_is_refused()
    {
        var order = await SeedPendingAsync();
        await using var db = _fixture.CreateContext(_companyId);
        var service = new OrderApprovalService(
            db, new TenantStub(_companyId), new CallerStub { Role = SelloraRoles.AgencyOperator },
            _clock, new OrderEventOutbox(new EntityFrameworkOutboxWriter(db), new FixedCorrelation(Correlation)),
            NullLogger<OrderApprovalService>.Instance);

        var result = await service.DecideAsync(
            order.OrderId, new ApprovalDecisionRequest(ApprovalDecision.Approve, null), CancellationToken.None);

        Assert.Equal(OrderDecisionOutcome.CallerNotPermitted, result.Outcome);
    }
}
