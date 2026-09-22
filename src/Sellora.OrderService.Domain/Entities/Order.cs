using Sellora.OrderService.Domain.Orders;
using Sellora.OrderService.Domain.Tenancy;

namespace Sellora.OrderService.Domain.Entities;

/// <summary>
/// Order aggregate root. Lines and totals are fixed at creation: there are
/// no public setters and no methods that change lines, so a submitted
/// order's content can only be read, never edited.
/// </summary>
public sealed class Order : ITenantScoped
{
    // Matches Catalog's resolve limit so US-E4-1b can price a basket in one call.
    public const int MaxLines = 100;

    public const int MaxProductNameLength = 200;

    private readonly List<OrderLine> _lines = new();
    private readonly List<OrderVerificationStep> _verificationSteps = new();

    private Order()
    {
    }

    public Guid OrderId { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid ShopId { get; private set; }

    public Guid SalesRepId { get; private set; }

    public Guid AgencyId { get; private set; }

    public Guid TerritoryId { get; private set; }

    // Needed so an Area Manager can be scoped to their provinces (T4).
    public Guid ProvinceId { get; private set; }

    public OrderFulfilmentType FulfilmentType { get; private set; }

    /// <summary>
    /// Decided by <see cref="FulfilmentType"/> at creation; there is no
    /// setter and no route that changes either value afterwards.
    /// </summary>
    public OrderStatus Status { get; private set; }

    public DateTimeOffset OrderDate { get; private set; }

    public string OrderReference { get; private set; } = string.Empty;

    public decimal Subtotal { get; private set; }

    public decimal Total { get; private set; }

    /// <summary>Inventory reservation holding this order's stock (US-E4-1b).</summary>
    public Guid ReservationId { get; private set; }

    public Guid InventoryOwnerId { get; private set; }

    public IReadOnlyCollection<OrderLine> Lines => _lines.AsReadOnly();

    public IReadOnlyCollection<OrderVerificationStep> VerificationSteps =>
        _verificationSteps.AsReadOnly();

    public static Order Create(
        Guid companyId,
        Guid shopId,
        Guid salesRepId,
        Guid agencyId,
        Guid territoryId,
        Guid provinceId,
        OrderFulfilmentType fulfilmentType,
        string orderReference,
        DateTimeOffset orderDate,
        IReadOnlyCollection<NewOrderLine>? lines)
    {
        RequireId(companyId, "companyId");
        RequireId(shopId, "shopId");
        RequireId(salesRepId, "salesRepId");
        RequireId(agencyId, "agencyId");
        RequireId(territoryId, "territoryId");
        RequireId(provinceId, "provinceId");

        if (!Enum.IsDefined(fulfilmentType))
        {
            throw new OrderRuleViolationException(
                "fulfilmentType must be ImmediateCashSale or ScheduledDelivery.");
        }

        if (string.IsNullOrWhiteSpace(orderReference))
        {
            throw new OrderRuleViolationException(
                "An order reference is required.");
        }

        ValidateLines(lines);

        var order = new Order
        {
            OrderId = Guid.NewGuid(),
            CompanyId = companyId,
            ShopId = shopId,
            SalesRepId = salesRepId,
            AgencyId = agencyId,
            TerritoryId = territoryId,
            ProvinceId = provinceId,
            FulfilmentType = fulfilmentType,
            // A cash sale still owes a payment at the counter; a scheduled
            // delivery is a real order the moment it is verified.
            Status = fulfilmentType == OrderFulfilmentType.ImmediateCashSale
                ? OrderStatus.AwaitingCheckout
                : OrderStatus.Confirmed,
            OrderDate = orderDate,
            OrderReference = orderReference
        };

        foreach (var line in lines!)
        {
            order._lines.Add(new OrderLine(
                order.OrderId,
                line.ProductId,
                line.ProductName.Trim(),
                line.Quantity,
                line.UnitPrice));
        }

        // Totals are always derived from the lines, never supplied.
        order.Subtotal = order._lines.Sum(line => line.LineTotal);
        order.Total = order.Subtotal;

        return order;
    }

    /// <summary>
    /// Records a completed verification chain and the reservation it produced.
    /// Called once, before the order is first saved.
    /// </summary>
    public void CompleteVerification(
        Guid reservationId,
        Guid inventoryOwnerId,
        IReadOnlyCollection<VerificationStepRecord> steps,
        DateTimeOffset recordedAt)
    {
        if (ReservationId != Guid.Empty)
        {
            throw new InvalidOperationException("Verification has already been recorded for this order.");
        }

        RequireId(reservationId, "reservationId");
        RequireId(inventoryOwnerId, "inventoryOwnerId");

        var expected = Enum.GetValues<VerificationStep>();
        if (steps.Count != expected.Length ||
            !steps.Select(step => step.Step).SequenceEqual(expected) ||
            steps.Any(step => !step.Passed))
        {
            throw new OrderRuleViolationException(
                "An order can only be accepted after all four verification steps pass, in order.");
        }

        ReservationId = reservationId;
        InventoryOwnerId = inventoryOwnerId;

        foreach (var step in steps)
        {
            _verificationSteps.Add(new OrderVerificationStep(
                OrderId, step.Step, step.Passed, step.Detail, recordedAt));
        }
    }

    /// <summary>
    /// Checks the rep's basket before any dependency is called: non-empty,
    /// within the line cap, positive quantities, no duplicate products.
    /// </summary>
    public static void ValidateBasket(IReadOnlyCollection<BasketLine>? lines)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new OrderRuleViolationException(
                "An order must contain at least one line.");
        }

        if (lines.Count > MaxLines)
        {
            throw new OrderRuleViolationException(
                $"An order cannot contain more than {MaxLines} lines.");
        }

        var seen = new HashSet<Guid>();
        var position = 0;

        foreach (var line in lines)
        {
            position++;

            if (line.ProductId == Guid.Empty)
            {
                throw new OrderRuleViolationException(
                    $"Line {position}: productId is required.");
            }

            if (!seen.Add(line.ProductId))
            {
                throw new OrderRuleViolationException(
                    $"Line {position}: product {line.ProductId} appears more than once. " +
                    "Combine the quantities into one line.");
            }

            if (line.Quantity <= 0)
            {
                throw new OrderRuleViolationException(
                    $"Line {position}: quantity for product {line.ProductId} must be greater than zero.");
            }
        }
    }

    private static void ValidateLines(IReadOnlyCollection<NewOrderLine>? lines)
    {
        ValidateBasket(lines?.Select(line => new BasketLine(line.ProductId, line.Quantity)).ToList());

        var position = 0;

        foreach (var line in lines!)
        {
            position++;

            if (string.IsNullOrWhiteSpace(line.ProductName))
            {
                throw new OrderRuleViolationException(
                    $"Line {position}: productName for product {line.ProductId} is required.");
            }

            if (line.ProductName.Trim().Length > MaxProductNameLength)
            {
                throw new OrderRuleViolationException(
                    $"Line {position}: productName for product {line.ProductId} cannot exceed {MaxProductNameLength} characters.");
            }

            if (line.UnitPrice < 0)
            {
                throw new OrderRuleViolationException(
                    $"Line {position}: unitPrice for product {line.ProductId} cannot be negative.");
            }

            if (decimal.Round(line.UnitPrice, 2) != line.UnitPrice)
            {
                throw new OrderRuleViolationException(
                    $"Line {position}: unitPrice for product {line.ProductId} cannot have more than 2 decimal places.");
            }
        }
    }

    private static void RequireId(Guid value, string name)
    {
        if (value == Guid.Empty)
        {
            throw new OrderRuleViolationException($"{name} is required.");
        }
    }
}
