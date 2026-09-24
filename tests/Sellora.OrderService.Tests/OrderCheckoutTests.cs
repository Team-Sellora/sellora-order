using Sellora.OrderService.Domain.Entities;
using Sellora.OrderService.Domain.Orders;

namespace Sellora.OrderService.Tests;

/// <summary>US-E4-3 rules on the aggregate: the GPS gate and cash checkout.</summary>
public sealed class OrderCheckoutTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);
    private static readonly CheckInPolicy Policy = CheckInPolicy.Default;
    private readonly Guid _repId = Guid.NewGuid();

    private Order CashOrder(OrderFulfilmentType type = OrderFulfilmentType.ImmediateCashSale)
    {
        var order = Order.Create(
            Guid.NewGuid(), Guid.NewGuid(), _repId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            type, "ORD-260924-ABCDEF", Now.AddMinutes(-2),
            new[] { new NewOrderLine(Guid.NewGuid(), "Soap", 5, 2_500m) });

        order.CompleteVerification(Guid.NewGuid(), Guid.NewGuid(), TestOrders.AllPassed, Now.AddMinutes(-2));
        return order;
    }

    private OrderCheckIn CheckInAt(Order order, double meters, DateTimeOffset? at = null, Guid? rep = null) =>
        order.RecordCheckIn(
            rep ?? _repId, GeoTestPoints.NorthOf(GeoTestPoints.Shop, meters), 8,
            GeoTestPoints.Shop, (at ?? Now).AddSeconds(-5), at ?? Now, Policy);

    [Fact]
    public void Check_in_within_radius_is_accepted_and_the_order_still_waits_for_payment()
    {
        var order = CashOrder();

        var checkIn = CheckInAt(order, 120);

        Assert.True(checkIn.Accepted);
        Assert.Equal(120, checkIn.DistanceMeters);
        Assert.Equal(Now + Policy.Validity, checkIn.ExpiresAt);
        Assert.Equal(OrderStatus.AwaitingCheckout, order.Status);
    }

    // US-E4-3-Q3. Documented rule: a check-in exactly on the radius is inside.
    [Theory]
    [InlineData(299, true)]
    [InlineData(300, true)]
    [InlineData(300.01, false)]
    [InlineData(301, false)]
    public void The_300_metre_boundary_is_exact(double meters, bool accepted)
    {
        var checkIn = CheckInAt(CashOrder(), meters);

        Assert.Equal(accepted, checkIn.Accepted);
    }

    [Fact]
    public void Check_in_outside_radius_is_recorded_as_rejected_without_failing_the_order()
    {
        var order = CashOrder();

        var checkIn = CheckInAt(order, 450);

        Assert.False(checkIn.Accepted);
        Assert.Equal(450, checkIn.DistanceMeters);
        Assert.Single(order.CheckIns);
        // US-E4-3-T4: a failed check-in is not a failed order.
        Assert.Equal(OrderStatus.AwaitingCheckout, order.Status);
    }

    [Fact]
    public void Another_reps_check_in_is_refused()
    {
        var exception = Assert.Throws<CheckoutRuleViolationException>(() =>
            CheckInAt(CashOrder(), 50, rep: Guid.NewGuid()));

        Assert.Equal(CheckoutFailure.WrongSalesRep, exception.Failure);
    }

    [Fact]
    public void Scheduled_delivery_orders_are_not_checked_out()
    {
        var exception = Assert.Throws<CheckoutRuleViolationException>(() =>
            CheckInAt(CashOrder(OrderFulfilmentType.ScheduledDelivery), 50));

        Assert.Equal(CheckoutFailure.NotAwaitingCheckout, exception.Failure);
    }

    [Fact]
    public void A_timestamp_significantly_in_the_future_is_refused()
    {
        var exception = Assert.Throws<CheckoutRuleViolationException>(() =>
            CashOrder().RecordCheckIn(_repId, GeoTestPoints.Shop, null, GeoTestPoints.Shop, Now.AddMinutes(5), Now, Policy));

        Assert.Equal(CheckoutFailure.CapturedInFuture, exception.Failure);
    }

    [Fact]
    public void Small_device_clock_drift_is_tolerated()
    {
        var checkIn = CashOrder().RecordCheckIn(_repId, GeoTestPoints.Shop, null, GeoTestPoints.Shop, Now.AddSeconds(30), Now, Policy);

        Assert.True(checkIn.Accepted);
    }

    [Fact]
    public void An_old_location_fix_cannot_be_replayed()
    {
        var exception = Assert.Throws<CheckoutRuleViolationException>(() =>
            CashOrder().RecordCheckIn(_repId, GeoTestPoints.Shop, null, GeoTestPoints.Shop, Now.AddMinutes(-10), Now, Policy));

        Assert.Equal(CheckoutFailure.CaptureTooOld, exception.Failure);
    }

    [Fact]
    public void Payment_without_a_check_in_is_refused()
    {
        var exception = Assert.Throws<CheckoutRuleViolationException>(() =>
            CashOrder().CompleteCashCheckout(_repId, 12_500m, PaymentMethod.Cash, Now));

        Assert.Equal(CheckoutFailure.CheckInRequired, exception.Failure);
    }

    [Fact]
    public void A_rejected_check_in_does_not_permit_payment()
    {
        var order = CashOrder();
        CheckInAt(order, 450);

        var exception = Assert.Throws<CheckoutRuleViolationException>(() =>
            order.CompleteCashCheckout(_repId, 12_500m, PaymentMethod.Cash, Now));

        Assert.Equal(CheckoutFailure.CheckInRequired, exception.Failure);
    }

    [Fact]
    public void An_expired_check_in_does_not_permit_payment()
    {
        var order = CashOrder();
        CheckInAt(order, 50);

        var exception = Assert.Throws<CheckoutRuleViolationException>(() =>
            order.CompleteCashCheckout(_repId, 12_500m, PaymentMethod.Cash, Now + Policy.Validity));

        Assert.Equal(CheckoutFailure.CheckInExpired, exception.Failure);
    }

    [Fact]
    public void Another_rep_cannot_pay_on_this_reps_check_in()
    {
        var order = CashOrder();
        CheckInAt(order, 50);

        var exception = Assert.Throws<CheckoutRuleViolationException>(() =>
            order.CompleteCashCheckout(Guid.NewGuid(), 12_500m, PaymentMethod.Cash, Now));

        Assert.Equal(CheckoutFailure.WrongSalesRep, exception.Failure);
    }

    // Acceptance scenario 4: an order totalling 12,500 paid with 12,000.
    [Theory]
    [InlineData(12_000)]
    [InlineData(13_000)]
    [InlineData(12_499.99)]
    public void Payment_must_equal_the_order_total(double amount)
    {
        var order = CashOrder();
        CheckInAt(order, 50);

        var exception = Assert.Throws<CheckoutRuleViolationException>(() =>
            order.CompleteCashCheckout(_repId, (decimal)amount, PaymentMethod.Cash, Now));

        Assert.Equal(CheckoutFailure.AmountMismatch, exception.Failure);
        Assert.Contains("12,500.00", exception.Message);
        Assert.Null(order.Payment);
        Assert.Equal(OrderStatus.AwaitingCheckout, order.Status);
    }

    [Fact]
    public void Successful_checkout_records_payment_with_the_check_in_location_and_confirms_the_order()
    {
        var order = CashOrder();
        var checkIn = CheckInAt(order, 120);

        var payment = order.CompleteCashCheckout(_repId, 12_500m, PaymentMethod.Cash, Now.AddMinutes(1));

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(12_500m, payment.Amount);
        Assert.Equal(PaymentMethod.Cash, payment.Method);
        Assert.Equal(_repId, payment.SalesRepId);
        Assert.Equal(checkIn.OrderCheckInId, payment.CheckInId);
        Assert.Equal(checkIn.Latitude, payment.Latitude);
        Assert.Equal(checkIn.Longitude, payment.Longitude);
        Assert.Equal(checkIn.Latitude, order.CheckoutLatitude);
        Assert.Equal(checkIn.Longitude, order.CheckoutLongitude);
        Assert.Equal(Now.AddMinutes(1), order.CheckedOutAt);
    }

    [Fact]
    public void A_confirmed_order_cannot_be_paid_twice()
    {
        var order = CashOrder();
        CheckInAt(order, 50);
        order.CompleteCashCheckout(_repId, 12_500m, PaymentMethod.Cash, Now);

        var exception = Assert.Throws<CheckoutRuleViolationException>(() =>
            order.CompleteCashCheckout(_repId, 12_500m, PaymentMethod.Cash, Now));

        Assert.Equal(CheckoutFailure.NotAwaitingCheckout, exception.Failure);
    }

    [Fact]
    public void An_expired_reservation_cancels_the_order()
    {
        var order = CashOrder();

        order.CancelBecauseReservationExpired(Now);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(Now, order.CancelledAt);
        Assert.NotNull(order.CancellationReason);
    }
}
