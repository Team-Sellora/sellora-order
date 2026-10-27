using Sellora.OrderService.Domain.Orders;

namespace Sellora.OrderService.Tests;

/// <summary>US-E4-5-T3: the approval rules on the aggregate, no database.</summary>
public sealed class OrderApprovalTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Placement _place = Placement.New();

    [Fact]
    public void Approval_confirms_the_order_and_records_who_and_when()
    {
        var order = DecisionTestOrders.Pending(_companyId, _place, Now.AddMinutes(-10));

        var decision = order.Approve(_place.AgencyId, "op-1", "AgencyOperator", Now)!;

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(Now, order.ConfirmedAt);
        Assert.Equal(OrderDecisionKind.Approved, decision.Kind);
        Assert.Equal("op-1", decision.ActorUserId);
        Assert.Equal("AgencyOperator", decision.ActorRole);
        Assert.Equal(Now, decision.DecidedAt);
        Assert.Equal(OrderStatus.PendingApproval, decision.StatusBefore);
        Assert.Equal(OrderStatus.Confirmed, decision.StatusAfter);
    }

    // Acceptance scenario 3.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejection_without_a_reason_is_refused_and_the_order_stays_pending(string? reason)
    {
        var order = DecisionTestOrders.Pending(_companyId, _place, Now);

        var error = Assert.Throws<OrderDecisionRuleException>(() =>
            order.Reject(_place.AgencyId, "op-1", "AgencyOperator", reason, Now));

        Assert.Equal(OrderDecisionFailure.ReasonRequired, error.Failure);
        Assert.Equal(OrderStatus.PendingApproval, order.Status);
        Assert.Empty(order.Decisions);
    }

    [Fact]
    public void Rejection_cancels_with_the_reason_actor_and_time()
    {
        var order = DecisionTestOrders.Pending(_companyId, _place, Now);

        var decision = order.Reject(_place.AgencyId, "op-1", "AgencyOperator", "  Unpaid invoice  ", Now)!;

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal("Unpaid invoice", order.CancellationReason);
        Assert.Equal("op-1", order.CancelledBy);
        Assert.Equal(Now, order.CancelledAt);
        Assert.Null(order.ConfirmedAt);
        Assert.Equal("Unpaid invoice", decision.Reason);
    }

    [Fact]
    public void A_reason_longer_than_the_column_is_refused()
    {
        var order = DecisionTestOrders.Pending(_companyId, _place, Now);

        var error = Assert.Throws<OrderDecisionRuleException>(() =>
            order.Reject(_place.AgencyId, "op-1", "AgencyOperator", new string('x', 501), Now));

        Assert.Equal(OrderDecisionFailure.InvalidRequest, error.Failure);
    }

    [Fact]
    public void Another_agency_cannot_decide()
    {
        var order = DecisionTestOrders.Pending(_companyId, _place, Now);

        var error = Assert.Throws<OrderDecisionRuleException>(() =>
            order.Approve(Guid.NewGuid(), "op-2", "AgencyOperator", Now));

        Assert.Equal(OrderDecisionFailure.NotVisible, error.Failure);
        Assert.Equal(OrderStatus.PendingApproval, order.Status);
    }

    [Fact]
    public void A_cash_sale_is_never_approved()
    {
        var order = DecisionTestOrders.New(_companyId, _place, Now, OrderFulfilmentType.ImmediateCashSale);

        var error = Assert.Throws<OrderDecisionRuleException>(() =>
            order.Approve(_place.AgencyId, "op-1", "AgencyOperator", Now));

        Assert.Equal(OrderDecisionFailure.NotAwaitingApproval, error.Failure);
    }

    [Fact]
    public void A_cancelled_order_cannot_then_be_approved()
    {
        var order = DecisionTestOrders.Pending(_companyId, _place, Now);
        order.CancelByShop(_place.ShopId, "shop-1", "ShopOwner", null, Now, TimeSpan.FromHours(1));

        var error = Assert.Throws<OrderDecisionRuleException>(() =>
            order.Approve(_place.AgencyId, "op-1", "AgencyOperator", Now.AddMinutes(1)));

        Assert.Equal(OrderDecisionFailure.NotAwaitingApproval, error.Failure);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void An_approved_order_the_shop_then_cancelled_cannot_be_approved_again()
    {
        var order = DecisionTestOrders.Pending(_companyId, _place, Now);
        order.Approve(_place.AgencyId, "op-1", "AgencyOperator", Now);
        order.CancelByShop(_place.ShopId, "shop-1", "ShopOwner", null, Now.AddMinutes(10), TimeSpan.FromHours(1));

        // Not an idempotent no-op: the approval no longer stands.
        var error = Assert.Throws<OrderDecisionRuleException>(() =>
            order.Approve(_place.AgencyId, "op-1", "AgencyOperator", Now.AddMinutes(11)));

        Assert.Equal(OrderDecisionFailure.NotAwaitingApproval, error.Failure);
    }

    [Fact]
    public void A_rejected_order_cannot_then_be_approved()
    {
        var order = DecisionTestOrders.Pending(_companyId, _place, Now);
        order.Reject(_place.AgencyId, "op-1", "AgencyOperator", "No credit", Now);

        Assert.Throws<OrderDecisionRuleException>(() =>
            order.Approve(_place.AgencyId, "op-1", "AgencyOperator", Now.AddMinutes(1)));
    }

    [Fact]
    public void Repeating_the_same_decision_changes_nothing()
    {
        var order = DecisionTestOrders.Pending(_companyId, _place, Now);
        order.Approve(_place.AgencyId, "op-1", "AgencyOperator", Now);

        var repeat = order.Approve(_place.AgencyId, "op-1", "AgencyOperator", Now.AddMinutes(5));

        Assert.Null(repeat);
        Assert.Single(order.Decisions);
        Assert.Equal(Now, order.ConfirmedAt);
    }
}
