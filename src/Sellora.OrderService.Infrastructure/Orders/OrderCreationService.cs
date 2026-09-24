using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sellora.OrderService.Application.Dependencies;
using Sellora.OrderService.Application.Identity;
using Sellora.OrderService.Application.Orders;
using Sellora.OrderService.Domain.Entities;
using Sellora.OrderService.Domain.Orders;
using Sellora.OrderService.Domain.Tenancy;
using Sellora.OrderService.Infrastructure.Dependencies;
using Sellora.OrderService.Infrastructure.Persistence;

namespace Sellora.OrderService.Infrastructure.Orders;

/// <summary>
/// US-E4-1b order creation saga. Four checks run in a fixed order:
/// (1) rep ↔ shop, (2) authoritative prices, (3) credit limit, (4) stock
/// reservation. Nothing is written until all four pass. The only step with a
/// side effect to undo is the reservation, so any failure after it exists
/// releases it — a stranded reservation silently removes sellable stock.
/// </summary>
public sealed class OrderCreationService : IOrderCreationService
{
    private const int MaxReferenceAttempts = 3;

    private readonly OrderDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUserContext _caller;
    private readonly IOrganizationClient _organization;
    private readonly ICatalogClient _catalog;
    private readonly IInventoryClient _inventory;
    private readonly TimeProvider _clock;
    private readonly ILogger<OrderCreationService> _logger;

    public OrderCreationService(
        OrderDbContext db,
        ITenantContext tenant,
        ICurrentUserContext caller,
        IOrganizationClient organization,
        ICatalogClient catalog,
        IInventoryClient inventory,
        TimeProvider clock,
        ILogger<OrderCreationService> logger)
    {
        _db = db;
        _tenant = tenant;
        _caller = caller;
        _organization = organization;
        _catalog = catalog;
        _inventory = inventory;
        _clock = clock;
        _logger = logger;
    }

    public async Task<CreateOrderResult> CreateAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (_tenant.CompanyId is not { } companyId)
        {
            return CreateOrderResult.Failed(
                CreateOrderOutcome.TenantNotAvailable,
                "A valid company identifier was not found in the access token.");
        }

        if (_caller.SalesRepId is not { } salesRepId)
        {
            return CreateOrderResult.Failed(
                CreateOrderOutcome.CallerNotSalesRep,
                "The access token does not identify a sales rep.");
        }

        if (request.ShopId == Guid.Empty)
        {
            return CreateOrderResult.Failed(CreateOrderOutcome.InvalidRequest, "shopId is required.");
        }

        // Cheap local checks first, so a bad basket never costs a network call.
        try
        {
            Order.ValidateBasket(request.Lines);
        }
        catch (OrderRuleViolationException exception)
        {
            return CreateOrderResult.Failed(CreateOrderOutcome.InvalidRequest, exception.Message);
        }

        var saga = new SagaTrace();
        StockReservationResponse? reservation = null;

        try
        {
            // ---- Step 1: rep is assigned to the shop's territory ----------
            var relationship = await _organization.VerifyRepShopAsync(
                salesRepId, request.ShopId, cancellationToken);

            if (!relationship.IsValid)
            {
                return saga.Fail(
                    VerificationStep.RepShopRelationship,
                    DescribeRelationshipFailure(relationship.Reason));
            }

            saga.Pass(VerificationStep.RepShopRelationship, "Rep is assigned to the shop's territory.");

            // ---- Step 2: authoritative prices (one Catalog call) ----------
            var resolution = await _catalog.ResolveProductsAsync(
                companyId,
                request.Lines.Select(line => line.ProductId).ToList(),
                cancellationToken);

            var priced = PriceLines(request.Lines, resolution, out var unresolved);

            if (unresolved.Count > 0)
            {
                return saga.Fail(
                    VerificationStep.PriceResolution,
                    unresolved.Count == 1
                        ? $"Product {unresolved[0].ProductId} could not be priced: {unresolved[0].Reason}"
                        : $"{unresolved.Count} products could not be priced.",
                    unresolvedProducts: unresolved);
            }

            saga.Pass(
                VerificationStep.PriceResolution,
                $"{priced.Count} line(s) priced from the catalogue; any client price was ignored.");

            // ---- Step 3: credit limit -------------------------------------
            var shop = await _organization.FindShopAsync(request.ShopId, cancellationToken);

            if (shop is null)
            {
                return saga.Fail(
                    VerificationStep.CreditLimit,
                    "The shop's credit details are not visible to this rep, or the shop's territory has no agency.");
            }

            var orderDate = _clock.GetUtcNow();
            var reference = await NewUniqueReferenceAsync(orderDate, cancellationToken);

            var order = Order.Create(
                companyId,
                shop.ShopId,
                salesRepId,
                shop.AgencyId,
                shop.TerritoryId,
                shop.ProvinceId,
                request.FulfilmentType,
                reference,
                orderDate,
                priced);

            var outstanding = await OutstandingBalanceAsync(shop.ShopId, cancellationToken);
            var exposure = outstanding + order.Total;

            if (exposure > shop.CreditLimit)
            {
                var credit = new CreditCheckDetail(
                    shop.CreditLimit, outstanding, order.Total, exposure - shop.CreditLimit);

                return saga.Fail(
                    VerificationStep.CreditLimit,
                    $"Order total {order.Total:N2} plus outstanding balance {outstanding:N2} " +
                    $"exceeds {shop.Name}'s credit limit of {shop.CreditLimit:N2} by {credit.ExceededBy:N2}.",
                    credit: credit);
            }

            saga.Pass(
                VerificationStep.CreditLimit,
                $"{exposure:N2} of {shop.CreditLimit:N2} credit used after this order.");

            // ---- Step 4: reserve stock, from the right source ------------
            // A cash sale hands goods over there and then, so it can only be
            // served from the rep's own van. Inventory's fulfilment resolver
            // only ever picks agency or company stock, so the two paths use
            // different endpoints rather than the same one with a check after.
            StockReservationAttempt attempt;

            if (request.FulfilmentType == OrderFulfilmentType.ImmediateCashSale)
            {
                var vanOwnerId = await _inventory.FindVanOwnerAsync(salesRepId, cancellationToken);

                if (vanOwnerId is null)
                {
                    return saga.Fail(
                        VerificationStep.StockReservation,
                        "An immediate cash sale hands the goods over from your van, but you have " +
                        "no van stock recorded. Choose scheduled delivery instead.");
                }

                attempt = await _inventory.ReserveAsync(
                    reference, vanOwnerId.Value, request.Lines, cancellationToken);
            }
            else
            {
                attempt = await _inventory.ResolveFulfilmentAsync(
                    reference, shop.AgencyId, request.Lines, cancellationToken);
            }

            switch (attempt.Status)
            {
                case StockReservationStatus.InsufficientStock:
                    var shortages = attempt.Shortages
                        .Select(shortage => new StockShortage(
                            shortage.ProductId,
                            order.Lines.FirstOrDefault(line => line.ProductId == shortage.ProductId)?.ProductNameSnapshot,
                            shortage.RequestedQuantity,
                            shortage.AvailableQuantity,
                            shortage.RequestedQuantity - shortage.AvailableQuantity))
                        .ToList();

                    var where = request.FulfilmentType == OrderFulfilmentType.ImmediateCashSale
                        ? " Your van is short — scheduled delivery can source this from the agency."
                        : string.Empty;

                    return saga.Fail(
                        VerificationStep.StockReservation,
                        string.Join(" ", shortages.Select(shortage =>
                            $"{shortage.ProductName ?? shortage.ProductId.ToString()}: requested {shortage.RequestedQuantity}, " +
                            $"only {shortage.AvailableQuantity} available (short by {shortage.ShortBy}).")) + where,
                        shortages: shortages);

                case StockReservationStatus.Rejected:
                    return saga.Fail(
                        VerificationStep.StockReservation,
                        attempt.Message ?? "Inventory could not reserve the stock.");
            }

            reservation = attempt.Reservation!;
            saga.Pass(
                VerificationStep.StockReservation,
                $"Reservation {reservation.ReservationId} holds " +
                (request.FulfilmentType == OrderFulfilmentType.ImmediateCashSale ? "van" : "agency") +
                $" stock until {reservation.ExpiresAt:u}.");

            // ---- Accept: persist the order and its recorded outcomes ------
            order.CompleteVerification(
                reservation.ReservationId,
                reservation.InventoryOwnerId,
                saga.Records,
                _clock.GetUtcNow());

            _db.Orders.Add(order);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Order {OrderReference} ({OrderId}) accepted for shop {ShopId} by rep {SalesRepId} as {FulfilmentType} in status {Status}; reservation {ReservationId}",
                order.OrderReference, order.OrderId, order.ShopId, salesRepId,
                order.FulfilmentType, order.Status, reservation.ReservationId);

            // A scheduled delivery is done being verified, so its stock is
            // sold now. A cash sale keeps the stock merely held until the rep
            // checks in and takes payment (US-E4-3).
            if (order.FulfilmentType == OrderFulfilmentType.ScheduledDelivery &&
                await _inventory.ConfirmReservationAsync(reservation.ReservationId, CancellationToken.None)
                    is not (ReservationConfirmOutcome.Confirmed or ReservationConfirmOutcome.AlreadyConfirmed))
            {
                // The order stands; the stock is still held rather than sold,
                // so nothing is oversold. Needs an operator to reconcile.
                _logger.LogError(
                    "Order {OrderReference} is Confirmed but reservation {ReservationId} could not be confirmed; stock is still held",
                    order.OrderReference, reservation.ReservationId);
            }

            return CreateOrderResult.Created(OrderResponse.From(order));
        }
        catch (DependencyUnavailableException exception)
        {
            await CompensateAsync(reservation, "a dependency became unavailable");

            return saga.Unavailable(exception.Dependency, exception.Message);
        }
        catch (OrderRuleViolationException exception) when (reservation is null)
        {
            // Catalogue data the order cannot hold (e.g. a price with >2 decimals).
            return saga.Fail(saga.CurrentStep, exception.Message);
        }
        catch (DependencyRejectedException exception)
        {
            await CompensateAsync(reservation, "a dependency rejected the request");

            return saga.Fail(saga.CurrentStep, exception.Message);
        }
        catch (Exception exception) when (reservation is not null)
        {
            // Any failure after the reservation exists — typically the save —
            // must not strand stock.
            _logger.LogError(exception, "Order save failed after reservation {ReservationId}", reservation.ReservationId);
            await CompensateAsync(reservation, "the order could not be saved");
            throw;
        }
        finally
        {
            if (saga.FailedRejection is { } rejection)
            {
                _logger.LogWarning(
                    "Order rejected at {FailedStep} for shop {ShopId} by rep {SalesRepId}: {Reason}",
                    rejection.FailedStep, request.ShopId, salesRepId, rejection.Reason);
            }
        }
    }

    private async Task CompensateAsync(StockReservationResponse? reservation, string why)
    {
        if (reservation is null)
        {
            return;
        }

        try
        {
            // Not the request's token: compensation must run even if the caller disconnected.
            var released = await _inventory.ReleaseReservationAsync(reservation.ReservationId, CancellationToken.None);

            if (released)
            {
                _logger.LogWarning(
                    "Released reservation {ReservationId} for {OrderReference} because {Why}",
                    reservation.ReservationId, reservation.OrderReference, why);
            }
            else
            {
                _logger.LogError(
                    "COMPENSATION FAILED: reservation {ReservationId} for {OrderReference} is still held (expires {ExpiresAt})",
                    reservation.ReservationId, reservation.OrderReference, reservation.ExpiresAt);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "COMPENSATION FAILED: reservation {ReservationId} for {OrderReference} is still held (expires {ExpiresAt})",
                reservation.ReservationId, reservation.OrderReference, reservation.ExpiresAt);
        }
    }

    private static List<NewOrderLine> PriceLines(
        IReadOnlyCollection<BasketLine> lines,
        ProductResolutionResponse resolution,
        out List<UnresolvedProduct> unresolved)
    {
        var byId = resolution.Items.ToDictionary(item => item.ProductId);
        var priced = new List<NewOrderLine>();
        unresolved = new List<UnresolvedProduct>();

        foreach (var line in lines)
        {
            if (!byId.TryGetValue(line.ProductId, out var product))
            {
                unresolved.Add(new UnresolvedProduct(line.ProductId, "Not found in the catalogue."));
                continue;
            }

            if (!product.IsAvailable || product.CurrentUnitPrice is not { } price || string.IsNullOrWhiteSpace(product.Name))
            {
                unresolved.Add(new UnresolvedProduct(
                    line.ProductId,
                    product.UnavailableReason ?? $"Product is {product.Status} and cannot be ordered."));
                continue;
            }

            // The catalogue's price, never the client's.
            priced.Add(new NewOrderLine(line.ProductId, product.Name!, line.Quantity, price));
        }

        return priced;
    }

    /// <summary>
    /// The shop's current credit exposure: the total of its orders that are
    /// not yet paid. Until payment recording (US-E4-3) exists, every order
    /// that is not cancelled counts as unpaid.
    /// </summary>
    private Task<decimal> OutstandingBalanceAsync(Guid shopId, CancellationToken cancellationToken) =>
        _db.Orders
            .Where(order => order.ShopId == shopId && OrderStatuses.Outstanding.Contains(order.Status))
            .SumAsync(order => order.Total, cancellationToken);

    private async Task<string> NewUniqueReferenceAsync(DateTimeOffset orderDate, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxReferenceAttempts; attempt++)
        {
            var reference = OrderReferenceGenerator.Generate(orderDate);

            // Checked before reserving stock, because Inventory reserves
            // against this reference. The unique index remains the backstop.
            if (!await _db.Orders.AnyAsync(order => order.OrderReference == reference, cancellationToken))
            {
                return reference;
            }
        }

        throw new InvalidOperationException("Could not generate a unique order reference.");
    }

    private static string DescribeRelationshipFailure(string? reason) => reason switch
    {
        "shopNotFound" => "The shop was not found in this company.",
        "shopInactive" => "The shop is inactive and cannot receive orders.",
        "repNotAssignedToShopTerritory" => "You are not currently assigned to this shop's territory.",
        null or "" => "Organization did not confirm the rep-shop relationship.",
        _ => $"Rep-shop relationship not valid ({reason})."
    };

    /// <summary>Tracks which steps ran and turns a failure into a full rejection.</summary>
    private sealed class SagaTrace
    {
        private readonly List<VerificationStepRecord> _records = new();

        public IReadOnlyCollection<VerificationStepRecord> Records => _records;

        public VerificationStep CurrentStep =>
            _records.Count == 0 ? VerificationStep.RepShopRelationship : _records[^1].Step + 1;

        public OrderRejection? FailedRejection { get; private set; }

        public void Pass(VerificationStep step, string detail) =>
            _records.Add(new VerificationStepRecord(step, true, detail));

        public CreateOrderResult Fail(
            VerificationStep step,
            string reason,
            IReadOnlyList<StockShortage>? shortages = null,
            CreditCheckDetail? credit = null,
            IReadOnlyList<UnresolvedProduct>? unresolvedProducts = null)
        {
            FailedRejection = new OrderRejection(
                step.ToString(), reason, Steps(step, reason), shortages, credit, unresolvedProducts);

            return CreateOrderResult.Rejected(CreateOrderOutcome.VerificationFailed, FailedRejection);
        }

        public CreateOrderResult Unavailable(Dependency dependency, string reason)
        {
            var step = CurrentStep;
            FailedRejection = new OrderRejection(
                step.ToString(), reason, Steps(step, reason), Dependency: dependency.ToString());

            return CreateOrderResult.Rejected(CreateOrderOutcome.DependencyUnavailable, FailedRejection);
        }

        private List<StepOutcome> Steps(VerificationStep failed, string reason) =>
            _records
                .Where(record => record.Step < failed)
                .Select(record => new StepOutcome(record.Step.ToString(), record.Passed, record.Detail))
                .Append(new StepOutcome(failed.ToString(), false, reason))
                .ToList();
    }
}
