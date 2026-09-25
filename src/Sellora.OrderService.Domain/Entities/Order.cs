using Sellora.OrderService.Domain.Orders;
using Sellora.OrderService.Domain.Tenancy;

namespace Sellora.OrderService.Domain.Entities;

/// <summary>
/// Order aggregate root. Lines and totals are fixed at creation: there are
/// no public setters and no methods that change lines, so a submitted
/// order's content can only be read, never edited. Status only moves through
/// the aggregate's own methods (checkout here; approval and cancellation in
/// the Order.Approval.cs and Order.Cancellation.cs parts, US-E4-5).
/// </summary>
public sealed partial class Order : ITenantScoped
{
    // Matches Catalog's resolve limit so US-E4-1b can price a basket in one call.
    public const int MaxLines = 100;

    public const int MaxProductNameLength = 200;

    /// <summary>US-E4-5: longest reason a decision can carry (column length).</summary>
    public const int MaxDecisionReasonLength = 500;

    private readonly List<OrderLine> _lines = new();
    private readonly List<OrderVerificationStep> _verificationSteps = new();
    private readonly List<OrderCheckIn> _checkIns = new();
    private readonly List<OrderDecision> _decisions = new();

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
    /// Starts from <see cref="FulfilmentType"/> at creation and only moves
    /// through the aggregate's methods; there is no setter and no route that
    /// writes it directly.
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

    /// <summary>Every GPS check-in attempt, accepted or not (US-E4-3).</summary>
    public IReadOnlyCollection<OrderCheckIn> CheckIns => _checkIns.AsReadOnly();

    /// <summary>The cash collection, once checkout succeeds.</summary>
    public Payment? Payment { get; private set; }

    /// <summary>
    /// US-E4-5: every approval, rejection and shop cancellation, oldest
    /// first — actor, role, time and reason for each.
    /// </summary>
    public IReadOnlyCollection<OrderDecision> Decisions => _decisions.AsReadOnly();

    /// <summary>
    /// US-E4-5: when the order became binding — the agency's approval for a
    /// scheduled delivery, the payment for a cash sale. The shop's
    /// cancellation window is measured from this stored value, never from a
    /// time the client sends.
    /// </summary>
    public DateTimeOffset? ConfirmedAt { get; private set; }

    /// <summary>
    /// US-E4-5: optimistic concurrency token (PostgreSQL <c>xmin</c>). Two
    /// requests deciding the same order at once — an approval racing a
    /// cancellation, or a payment racing a cancellation — cannot both win;
    /// the second save fails instead of silently overwriting the first.
    /// </summary>
    public uint Version { get; private set; }

    /// <summary>
    /// Where the sale was completed — the accepted check-in's coordinates.
    /// Stored on the order itself so US-E4-4's event publisher can read it
    /// without joining anything.
    /// </summary>
    public double? CheckoutLatitude { get; private set; }

    public double? CheckoutLongitude { get; private set; }

    public DateTimeOffset? CheckedOutAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    // ---- Contact snapshot (US-E4-4) ------------------------------------
    // Taken when the order is placed, like the price snapshot on each line:
    // the order's events carry them, so the Notification service can email
    // the shop and agency without calling Organization back. Nullable
    // because orders placed before US-E4-4 have none.

    public string? ShopName { get; private set; }

    public string? ShopOwnerName { get; private set; }

    public string? ShopOwnerEmail { get; private set; }

    public string? AgencyName { get; private set; }

    public string? AgencyEmail { get; private set; }

    public string? SalesRepName { get; private set; }

    public string? CancellationReason { get; private set; }

    /// <summary>
    /// US-E4-5: who cancelled the order (the token's <c>sub</c>). Null when
    /// the system cancelled it, e.g. an expired stock hold.
    /// </summary>
    public string? CancelledBy { get; private set; }

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
            // delivery is taken on credit, so the agency approves it first
            // (US-E4-5) before it becomes binding.
            Status = fulfilmentType == OrderFulfilmentType.ImmediateCashSale
                ? OrderStatus.AwaitingCheckout
                : OrderStatus.PendingApproval,
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
    /// Snapshots who the order is for and who took it, as they were when it
    /// was placed. Values are trimmed and cut to the column length.
    /// </summary>
    public void RecordContacts(OrderContacts contacts)
    {
        ArgumentNullException.ThrowIfNull(contacts);

        ShopName = Clip(contacts.ShopName, 200);
        ShopOwnerName = Clip(contacts.ShopOwnerName, 200);
        ShopOwnerEmail = Clip(contacts.ShopOwnerEmail, 320);
        AgencyName = Clip(contacts.AgencyName, 200);
        AgencyEmail = Clip(contacts.AgencyEmail, 320);
        SalesRepName = Clip(contacts.SalesRepName, 200);
    }

    private static string? Clip(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    /// <summary>
    /// Records a GPS check-in attempt for this order and returns it. A
    /// check-in outside the radius is recorded as rejected but does not
    /// throw: a failed check-in is not a failed order, the rep may just need
    /// to walk closer (US-E4-3-T4). The reservation is untouched either way.
    /// </summary>
    public OrderCheckIn RecordCheckIn(
        Guid salesRepId,
        GeoPoint reported,
        double? accuracyMeters,
        GeoPoint shop,
        DateTimeOffset capturedAt,
        DateTimeOffset now,
        CheckInPolicy policy)
    {
        EnsureAwaitingCashCheckout();
        EnsureOwnRep(salesRepId);

        if (capturedAt > now + policy.MaxClockSkew)
        {
            throw new CheckoutRuleViolationException(
                CheckoutFailure.CapturedInFuture,
                $"The location timestamp {capturedAt:u} is in the future. Check the device clock and try again.");
        }

        if (capturedAt < now - policy.MaxCaptureAge)
        {
            throw new CheckoutRuleViolationException(
                CheckoutFailure.CaptureTooOld,
                $"The location fix from {capturedAt:u} is too old. Refresh your location and try again.");
        }

        if (accuracyMeters is { } accuracy && (accuracy < 0 || double.IsNaN(accuracy)))
        {
            throw new CheckoutRuleViolationException(
                CheckoutFailure.InvalidCoordinates,
                "accuracyMeters cannot be negative.");
        }

        var distance = GeoDistance.Meters(reported, shop);

        // Documented rule: the exact radius is inside (<=).
        var accepted = distance <= policy.RadiusMeters;

        var checkIn = new OrderCheckIn(
            OrderId,
            CompanyId,
            salesRepId,
            reported,
            accuracyMeters,
            shop,
            distance,
            policy.RadiusMeters,
            accepted,
            capturedAt,
            now,
            accepted ? now + policy.Validity : now);

        _checkIns.Add(checkIn);
        return checkIn;
    }

    /// <summary>
    /// Every rule checkout depends on, checked without changing anything, so
    /// the caller can verify before confirming stock in Inventory. Returns
    /// the check-in that permits the payment.
    /// </summary>
    public OrderCheckIn EnsureCanCompleteCashCheckout(
        Guid salesRepId,
        decimal amount,
        PaymentMethod method,
        DateTimeOffset now)
    {
        EnsureAwaitingCashCheckout();
        EnsureOwnRep(salesRepId);

        if (!Enum.IsDefined(method))
        {
            throw new CheckoutRuleViolationException(
                CheckoutFailure.UnsupportedPaymentMethod,
                "Only cash payments can be recorded.");
        }

        var latestAccepted = _checkIns
            .Where(checkIn => checkIn.Accepted && checkIn.SalesRepId == salesRepId)
            .OrderByDescending(checkIn => checkIn.RecordedAt)
            .FirstOrDefault();

        if (latestAccepted is null)
        {
            throw new CheckoutRuleViolationException(
                CheckoutFailure.CheckInRequired,
                "A successful GPS check-in at the shop is required before recording payment.");
        }

        if (!latestAccepted.IsValidFor(salesRepId, now))
        {
            throw new CheckoutRuleViolationException(
                CheckoutFailure.CheckInExpired,
                $"Your check-in expired at {latestAccepted.ExpiresAt:u}. Check in again at the shop.");
        }

        if (amount != Total)
        {
            throw new CheckoutRuleViolationException(
                CheckoutFailure.AmountMismatch,
                FormattableString.Invariant(
                    $"The payment amount {amount:N2} does not match the order total {Total:N2}."));
        }

        return latestAccepted;
    }

    /// <summary>
    /// Records the cash payment, stamps the checkout location and confirms
    /// the order. The caller must already have confirmed the stock
    /// reservation in Inventory.
    /// </summary>
    public Payment CompleteCashCheckout(
        Guid salesRepId,
        decimal amount,
        PaymentMethod method,
        DateTimeOffset now)
    {
        var checkIn = EnsureCanCompleteCashCheckout(salesRepId, amount, method, now);

        var payment = new Payment(OrderId, CompanyId, amount, method, salesRepId, checkIn, now);

        Payment = payment;
        CheckoutLatitude = checkIn.Latitude;
        CheckoutLongitude = checkIn.Longitude;
        CheckedOutAt = now;
        ConfirmedAt = now;
        Status = OrderStatus.Confirmed;

        return payment;
    }

    /// <summary>
    /// Inventory released the held stock (its sweeper expired the
    /// reservation) before the rep checked out. The sale cannot complete,
    /// so the order is cancelled rather than left waiting forever.
    /// </summary>
    public void CancelBecauseReservationExpired(DateTimeOffset now)
    {
        EnsureAwaitingCashCheckout();

        Status = OrderStatus.Cancelled;
        CancelledAt = now;
        CancellationReason = "The stock hold expired before checkout.";
    }

    /// <summary>
    /// Appends a decision and moves the order to <paramref name="statusAfter"/>.
    /// The only place a US-E4-5 decision changes <see cref="Status"/>.
    /// </summary>
    private OrderDecision RecordDecision(
        OrderDecisionKind kind,
        string actorUserId,
        string actorRole,
        string? reason,
        OrderStatus statusAfter,
        DateTimeOffset now)
    {
        var decision = new OrderDecision(
            OrderId,
            CompanyId,
            kind,
            RequireActor(actorUserId, "actorUserId"),
            RequireActor(actorRole, "actorRole"),
            reason,
            Status,
            statusAfter,
            now);

        _decisions.Add(decision);
        Status = statusAfter;
        return decision;
    }

    /// <summary>Trims a free-text reason; null when blank. Throws when too long.</summary>
    private static string? NormaliseReason(string? reason)
    {
        var trimmed = reason?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > MaxDecisionReasonLength)
        {
            throw new OrderDecisionRuleException(
                OrderDecisionFailure.InvalidRequest,
                $"reason cannot exceed {MaxDecisionReasonLength} characters.");
        }

        return trimmed;
    }

    private static string RequireActor(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new OrderDecisionRuleException(
                OrderDecisionFailure.InvalidRequest,
                $"{name} is required to record a decision.");
        }

        var trimmed = value.Trim();
        return trimmed.Length <= OrderDecision.MaxActorLength ? trimmed : trimmed[..OrderDecision.MaxActorLength];
    }

    private void EnsureAwaitingCashCheckout()
    {
        if (FulfilmentType != OrderFulfilmentType.ImmediateCashSale)
        {
            throw new CheckoutRuleViolationException(
                CheckoutFailure.NotAwaitingCheckout,
                "Only immediate cash sales are checked out at the shop; this order is a scheduled delivery.");
        }

        if (Status != OrderStatus.AwaitingCheckout)
        {
            throw new CheckoutRuleViolationException(
                CheckoutFailure.NotAwaitingCheckout,
                $"This order is {Status} and is no longer awaiting checkout.");
        }
    }

    private void EnsureOwnRep(Guid salesRepId)
    {
        if (salesRepId != SalesRepId)
        {
            throw new CheckoutRuleViolationException(
                CheckoutFailure.WrongSalesRep,
                "Only the sales rep who placed the order can check it out.");
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
