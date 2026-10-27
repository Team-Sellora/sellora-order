using Sellora.OrderService.Domain.Entities;
using Sellora.OrderService.Domain.Orders;

namespace Sellora.OrderService.Tests;

public sealed class OrderTests
{
    private static Order CreateWith(params NewOrderLine[] lines) => Order.Create(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), OrderFulfilmentType.ScheduledDelivery,
        "ORD-260918-ABCDEF", DateTimeOffset.UtcNow, lines);

    private static NewOrderLine Line(int quantity = 1, decimal price = 10m, Guid? productId = null, string name = "Soap") =>
        new(productId ?? Guid.NewGuid(), name, quantity, price);

    [Fact]
    public void Totals_are_computed_from_lines()
    {
        var order = CreateWith(Line(3, 120.50m), Line(2, 1150.00m));

        Assert.Equal(361.50m, order.Lines.Single(l => l.Quantity == 3).LineTotal);
        Assert.Equal(2661.50m, order.Subtotal);
        Assert.Equal(order.Subtotal, order.Total);
        Assert.Equal(OrderStatus.PendingApproval, order.Status);
    }

    [Fact]
    public void Snapshots_keep_the_values_given_at_creation()
    {
        var productId = Guid.NewGuid();
        var order = CreateWith(Line(1, 99.99m, productId, "  Anchor Milk 400g  "));
        var line = order.Lines.Single();

        Assert.Equal(productId, line.ProductId);
        Assert.Equal("Anchor Milk 400g", line.ProductNameSnapshot);
        Assert.Equal(99.99m, line.UnitPriceSnapshot);
        Assert.Equal(order.OrderId, line.OrderId);
    }

    [Fact]
    public void Empty_basket_is_rejected()
    {
        var ex = Assert.Throws<OrderRuleViolationException>(() => CreateWith());
        Assert.Contains("at least one line", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void Non_positive_quantity_is_rejected_and_named(int quantity)
    {
        var line = Line(quantity);
        var ex = Assert.Throws<OrderRuleViolationException>(() => CreateWith(line));

        Assert.Contains("quantity", ex.Message);
        Assert.Contains(line.ProductId.ToString(), ex.Message);
    }

    [Fact]
    public void Duplicate_product_is_rejected_and_named()
    {
        var productId = Guid.NewGuid();
        var ex = Assert.Throws<OrderRuleViolationException>(() =>
            CreateWith(Line(productId: productId), Line(productId: productId)));

        Assert.Contains("more than once", ex.Message);
        Assert.Contains(productId.ToString(), ex.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10.555)]
    public void Invalid_unit_price_is_rejected(double price)
    {
        Assert.Throws<OrderRuleViolationException>(() => CreateWith(Line(price: (decimal)price)));
    }

    [Fact]
    public void Too_many_lines_are_rejected()
    {
        var lines = Enumerable.Range(0, Order.MaxLines + 1).Select(_ => Line()).ToArray();
        Assert.Throws<OrderRuleViolationException>(() => CreateWith(lines));
    }

    [Fact]
    public void Missing_shop_is_rejected()
    {
        Assert.Throws<OrderRuleViolationException>(() => Order.Create(
            Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), OrderFulfilmentType.ScheduledDelivery, "ORD-260918-ABCDEF",
            DateTimeOffset.UtcNow, new[] { Line() }));
    }

    [Fact]
    public void Basket_is_validated_without_prices()
    {
        var productId = Guid.NewGuid();

        Order.ValidateBasket(new[] { new BasketLine(productId, 2) });
        Assert.Throws<OrderRuleViolationException>(() => Order.ValidateBasket(Array.Empty<BasketLine>()));
        Assert.Throws<OrderRuleViolationException>(() =>
            Order.ValidateBasket(new[] { new BasketLine(productId, 1), new BasketLine(productId, 1) }));
        Assert.Throws<OrderRuleViolationException>(() => Order.ValidateBasket(new[] { new BasketLine(productId, 0) }));
    }

    [Fact]
    public void Verification_records_all_four_steps_and_the_reservation_once()
    {
        var order = CreateWith(Line());
        var reservationId = Guid.NewGuid();
        var steps = Enum.GetValues<VerificationStep>()
            .Select(step => new VerificationStepRecord(step, true, "ok"))
            .ToArray();

        order.CompleteVerification(reservationId, Guid.NewGuid(), steps, DateTimeOffset.UtcNow);

        Assert.Equal(reservationId, order.ReservationId);
        Assert.Equal(4, order.VerificationSteps.Count);
        Assert.Throws<InvalidOperationException>(() =>
            order.CompleteVerification(Guid.NewGuid(), Guid.NewGuid(), steps, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Verification_refuses_a_failed_or_missing_step()
    {
        var order = CreateWith(Line());
        var steps = Enum.GetValues<VerificationStep>()
            .Select(step => new VerificationStepRecord(step, step != VerificationStep.CreditLimit, "x"))
            .ToArray();

        Assert.Throws<OrderRuleViolationException>(() =>
            order.CompleteVerification(Guid.NewGuid(), Guid.NewGuid(), steps, DateTimeOffset.UtcNow));
        Assert.Throws<OrderRuleViolationException>(() =>
            order.CompleteVerification(Guid.NewGuid(), Guid.NewGuid(), steps.Take(3).ToArray(), DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(OrderFulfilmentType.ImmediateCashSale, OrderStatus.AwaitingCheckout)]
    [InlineData(OrderFulfilmentType.ScheduledDelivery, OrderStatus.PendingApproval)]
    public void Fulfilment_type_decides_the_starting_status(
        OrderFulfilmentType fulfilmentType, OrderStatus expected)
    {
        var order = Order.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), fulfilmentType, "ORD-260918-ABCDEF", DateTimeOffset.UtcNow,
            new[] { Line() });

        Assert.Equal(fulfilmentType, order.FulfilmentType);
        Assert.Equal(expected, order.Status);
    }

    [Fact]
    public void Fulfilment_type_must_be_a_known_value()
    {
        Assert.Throws<OrderRuleViolationException>(() => Order.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), (OrderFulfilmentType)99, "ORD-260918-ABCDEF", DateTimeOffset.UtcNow,
            new[] { Line() }));
    }

    [Fact]
    public void Fulfilment_type_and_status_have_no_public_setter()
    {
        // US-E4-2-Q3: nothing outside the aggregate can change the routing.
        Assert.False(typeof(Order).GetProperty(nameof(Order.FulfilmentType))!.SetMethod!.IsPublic);
        Assert.False(typeof(Order).GetProperty(nameof(Order.Status))!.SetMethod!.IsPublic);
    }
}
