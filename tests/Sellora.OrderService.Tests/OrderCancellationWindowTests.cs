using Sellora.OrderService.Domain.Orders;

namespace Sellora.OrderService.Tests;

/// <summary>
/// US-E4-5-T1 / DoD 2: the cancellation window on the aggregate — measured
/// from the stored confirmation time, boundary tested on both sides.
/// </summary>
public sealed class OrderCancellationWindowTests
{
    private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);
    private static readonly DateTimeOffset ConfirmedAt = new(2026, 9, 25, 4, 30, 0, TimeSpan.Zero);
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Placement _place = Placement.New();

    private Sellora.OrderService.Domain.Entities.Order Confirmed() =>
        DecisionTestOrders.Approved(_companyId, _place, ConfirmedAt);

    // Acceptance scenario 1.
    [Fact]
    public void Twenty_minutes_after_confirmation_the_shop_can_cancel()
    {
        var order = Confirmed();
        var now = ConfirmedAt.AddMinutes(20);

        var decision = order.CancelByShop(_place.ShopId, "shop-1", "ShopOwner", null, now, OneHour);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal("shop-1", order.CancelledBy);
        Assert.Equal(now, order.CancelledAt);
        Assert.Equal(OrderDecisionKind.CancelledByShop, decision.Kind);
        Assert.Equal(Sellora.OrderService.Domain.Entities.Order.DefaultShopCancellationReason, decision.Reason);
        Assert.Equal(OrderStatus.Confirmed, decision.StatusBefore);
    }

    // Acceptance scenario 2.
    [Fact]
    public void Ninety_minutes_after_confirmation_the_window_has_closed_and_says_by_how_much()
    {
        var order = Confirmed();

        var error = Assert.Throws<OrderDecisionRuleException>(() =>
            order.CancelByShop(_place.ShopId, "shop-1", "ShopOwner", null, ConfirmedAt.AddMinutes(90), OneHour));

        Assert.Equal(OrderDecisionFailure.CancellationWindowClosed, error.Failure);
        Assert.Contains("closed 30 minutes ago", error.Message);
        Assert.Contains("confirmed 1 hour 30 minutes ago", error.Message);
        Assert.Equal(ConfirmedAt, error.ConfirmedAt);
        Assert.Equal(ConfirmedAt + OneHour, error.WindowClosedAt);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Single(order.Decisions); // only the approval
    }

    [Theory]
    [InlineData(59 * 60 * 1000 + 59_999, true)]  // 59:59.999 — just inside
    [InlineData(60 * 60 * 1000, false)]          // 60:00.000 — closes exactly here
    [InlineData(60 * 60 * 1000 + 1, false)]      // 60:00.001 — just outside
    public void The_boundary_is_open_before_the_hour_and_closed_from_it(int millisecondsAfter, bool open)
    {
        var window = Confirmed().GetCancellationWindow(ConfirmedAt.AddMilliseconds(millisecondsAfter), OneHour);

        Assert.Equal(open, window.CanCancel);
        Assert.Equal(ConfirmedAt + OneHour, window.ClosesAt);
    }

    [Fact]
    public void Remaining_time_counts_down_from_the_stored_confirmation()
    {
        var window = Confirmed().GetCancellationWindow(ConfirmedAt.AddMinutes(20), OneHour);

        Assert.True(window.CanCancel);
        Assert.Equal(TimeSpan.FromMinutes(40), window.Remaining);
        Assert.Null(window.ClosedAgo);
    }

    // US-E4-5-D1: the same instant written with different offsets gives the
    // same answer, so the server's time zone setting cannot matter.
    [Fact]
    public void The_offset_of_the_clock_makes_no_difference()
    {
        var order = Confirmed();
        var colombo = TimeSpan.FromHours(5.5);
        var utcNow = ConfirmedAt.AddMinutes(59);
        var colomboNow = utcNow.ToOffset(colombo);

        var fromUtc = order.GetCancellationWindow(utcNow, OneHour);
        var fromColombo = order.GetCancellationWindow(colomboNow, OneHour);

        Assert.True(fromUtc.CanCancel);
        Assert.Equal(fromUtc.CanCancel, fromColombo.CanCancel);
        Assert.Equal(fromUtc.Remaining, fromColombo.Remaining);
        Assert.Equal(fromUtc.ClosesAt, fromColombo.ClosesAt);
    }

    [Fact]
    public void The_window_length_comes_from_configuration()
    {
        var window = Confirmed().GetCancellationWindow(ConfirmedAt.AddMinutes(20), TimeSpan.FromMinutes(15));

        Assert.False(window.CanCancel);
        Assert.Equal(TimeSpan.FromMinutes(5), window.ClosedAgo);
    }

    [Fact]
    public void Another_shop_cannot_cancel()
    {
        var order = Confirmed();

        var error = Assert.Throws<OrderDecisionRuleException>(() =>
            order.CancelByShop(Guid.NewGuid(), "shop-2", "ShopOwner", null, ConfirmedAt.AddMinutes(5), OneHour));

        Assert.Equal(OrderDecisionFailure.NotVisible, error.Failure);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void An_order_awaiting_approval_can_be_cancelled_before_its_window_starts()
    {
        var order = DecisionTestOrders.Pending(_companyId, _place, ConfirmedAt);
        var muchLater = ConfirmedAt.AddDays(3);

        var window = order.GetCancellationWindow(muchLater, OneHour);
        order.CancelByShop(_place.ShopId, "shop-1", "ShopOwner", "Wrong pack size", muchLater, OneHour);

        Assert.True(window.CanCancel);
        Assert.Null(window.ClosesAt);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal("Wrong pack size", order.CancellationReason);
    }

    [Fact]
    public void A_paid_cash_sale_cannot_be_cancelled_by_the_shop()
    {
        var order = DecisionTestOrders.New(_companyId, _place, ConfirmedAt, OrderFulfilmentType.ImmediateCashSale);
        var shop = GeoTestPoints.Shop;
        order.RecordCheckIn(order.SalesRepId, shop, 5, shop, ConfirmedAt, ConfirmedAt, CheckInPolicy.Default);
        order.CompleteCashCheckout(order.SalesRepId, order.Total, PaymentMethod.Cash, ConfirmedAt);

        var error = Assert.Throws<OrderDecisionRuleException>(() =>
            order.CancelByShop(_place.ShopId, "shop-1", "ShopOwner", null, ConfirmedAt.AddMinutes(1), OneHour));

        Assert.Equal(OrderDecisionFailure.NotCancellable, error.Failure);
        Assert.Contains("cash sale", error.Message);
        Assert.Equal(ConfirmedAt, order.ConfirmedAt);
    }

    [Fact]
    public void An_order_cannot_be_cancelled_twice()
    {
        var order = Confirmed();
        order.CancelByShop(_place.ShopId, "shop-1", "ShopOwner", null, ConfirmedAt.AddMinutes(1), OneHour);

        var error = Assert.Throws<OrderDecisionRuleException>(() =>
            order.CancelByShop(_place.ShopId, "shop-1", "ShopOwner", null, ConfirmedAt.AddMinutes(2), OneHour));

        Assert.Equal(OrderDecisionFailure.NotCancellable, error.Failure);
    }

    [Theory]
    [InlineData(45, "45 seconds")]
    [InlineData(60, "1 minute")]
    [InlineData(6 * 60, "6 minutes")]
    [InlineData(90 * 60, "1 hour 30 minutes")]
    [InlineData(6 * 3600, "6 hours")]
    [InlineData(26 * 3600, "1 day 2 hours")]
    public void Durations_read_naturally(int seconds, string expected)
    {
        Assert.Equal(expected, DurationText.Describe(TimeSpan.FromSeconds(seconds)));
    }
}
