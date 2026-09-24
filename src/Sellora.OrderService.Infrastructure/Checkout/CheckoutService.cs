using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Sellora.OrderService.Application.Checkout;
using Sellora.OrderService.Application.Dependencies;
using Sellora.OrderService.Application.Identity;
using Sellora.OrderService.Domain.Entities;
using Sellora.OrderService.Domain.Orders;
using Sellora.OrderService.Domain.Tenancy;
using Sellora.OrderService.Infrastructure.Dependencies;
using Sellora.OrderService.Infrastructure.Persistence;
using Sellora.OrderService.Infrastructure.Persistence.Configurations;

namespace Sellora.OrderService.Infrastructure.Checkout;

/// <summary>
/// US-E4-3: the GPS gate and cash payment. The check-in is verified and
/// stored server-side; payment trusts nothing the client says about it.
///
/// Payment ordering, and why: every rule is checked first without side
/// effects; then Inventory confirms the stock (held becomes sold); then the
/// payment and confirmed order are saved. Confirming first matches reality —
/// the goods leave the van at this moment — and Inventory's "already
/// confirmed" answer is treated as success, so if the save fails the rep can
/// simply retry and nothing is sold twice.
/// </summary>
public sealed class CheckoutService : ICheckoutService
{
    private readonly OrderDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUserContext _caller;
    private readonly IOrganizationClient _organization;
    private readonly IInventoryClient _inventory;
    private readonly TimeProvider _clock;
    private readonly CheckInPolicy _policy;
    private readonly ILogger<CheckoutService> _logger;

    public CheckoutService(
        OrderDbContext db,
        ITenantContext tenant,
        ICurrentUserContext caller,
        IOrganizationClient organization,
        IInventoryClient inventory,
        TimeProvider clock,
        IOptions<CheckInOptions> options,
        ILogger<CheckoutService> logger)
    {
        _db = db;
        _tenant = tenant;
        _caller = caller;
        _organization = organization;
        _inventory = inventory;
        _clock = clock;
        _policy = options.Value.ToPolicy();
        _logger = logger;
    }

    public async Task<CheckInResult> CheckInAsync(
        Guid orderId,
        CheckInRequest request,
        CancellationToken cancellationToken)
    {
        if (Caller() is { } failure)
        {
            return CheckInResult.Failed(failure.Outcome, failure.Message);
        }

        var salesRepId = _caller.SalesRepId!.Value;
        var order = await LoadOwnOrderAsync(orderId, salesRepId, cancellationToken);

        if (order is null)
        {
            return CheckInResult.Failed(CheckoutOutcome.OrderNotFound, $"No order {orderId} is visible to you.");
        }

        try
        {
            // Validate the device position before any network call.
            var reported = new GeoPoint(request.Latitude, request.Longitude);

            var shop = await _organization.FindShopAsync(order.ShopId, cancellationToken);

            if (shop is null)
            {
                return CheckInResult.Failed(
                    CheckoutOutcome.ShopLocationUnavailable,
                    "The shop's registered location is not visible to you, so the check-in cannot be verified.");
            }

            var shopPoint = new GeoPoint((double)shop.Latitude, (double)shop.Longitude);
            var now = _clock.GetUtcNow();

            var checkIn = order.RecordCheckIn(
                salesRepId, reported, request.AccuracyMeters, shopPoint, request.CapturedAt, now, _policy);

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Check-in {CheckInId} for order {OrderReference} by rep {SalesRepId}: {Distance} m of {Radius} m, accepted={Accepted}",
                checkIn.OrderCheckInId, order.OrderReference, salesRepId,
                checkIn.DistanceMeters, checkIn.RadiusMeters, checkIn.Accepted);

            return CheckInResult.From(ToResponse(checkIn));
        }
        catch (CheckoutRuleViolationException exception)
        {
            return CheckInResult.Failed(Map(exception.Failure), exception.Message);
        }
        catch (DependencyUnavailableException exception)
        {
            return CheckInResult.Failed(CheckoutOutcome.DependencyUnavailable, exception.Message);
        }
    }

    public async Task<PaymentResult> RecordPaymentAsync(
        Guid orderId,
        PaymentRequest request,
        CancellationToken cancellationToken)
    {
        if (Caller() is { } failure)
        {
            return PaymentResult.Failed(failure.Outcome, failure.Message);
        }

        var salesRepId = _caller.SalesRepId!.Value;
        var order = await LoadOwnOrderAsync(orderId, salesRepId, cancellationToken);

        if (order is null)
        {
            return PaymentResult.Failed(CheckoutOutcome.OrderNotFound, $"No order {orderId} is visible to you.");
        }

        var now = _clock.GetUtcNow();

        // 1. Every rule, no side effects.
        try
        {
            order.EnsureCanCompleteCashCheckout(salesRepId, request.Amount, request.Method, now);
        }
        catch (CheckoutRuleViolationException exception)
        {
            return PaymentResult.Failed(
                Map(exception.Failure),
                exception.Message,
                exception.Failure == CheckoutFailure.AmountMismatch ? order.Total : null);
        }

        // 2. Held stock becomes sold.
        ReservationConfirmOutcome confirmation;
        try
        {
            confirmation = await _inventory.ConfirmReservationAsync(order.ReservationId, cancellationToken);
        }
        catch (DependencyUnavailableException exception)
        {
            return new PaymentResult(CheckoutOutcome.DependencyUnavailable, null, exception.Message, Dependency: exception.Dependency.ToString());
        }

        switch (confirmation)
        {
            case ReservationConfirmOutcome.NoLongerActive:
                order.CancelBecauseReservationExpired(now);
                await _db.SaveChangesAsync(CancellationToken.None);

                _logger.LogWarning(
                    "Order {OrderReference} cancelled at checkout: reservation {ReservationId} had expired",
                    order.OrderReference, order.ReservationId);

                return PaymentResult.Failed(
                    CheckoutOutcome.ReservationExpired,
                    "The stock held for this order expired before checkout, so the order has been cancelled. Place the order again.");

            case ReservationConfirmOutcome.Rejected:
                return new PaymentResult(
                    CheckoutOutcome.DependencyUnavailable,
                    null,
                    "Inventory could not confirm the stock for this order. No payment was recorded; try again shortly.",
                    Dependency: Dependency.Inventory.ToString());
        }

        // 3. Record the payment and confirm the order.
        var payment = order.CompleteCashCheckout(salesRepId, request.Amount, request.Method, now);

        try
        {
            // Not the request token: the stock is already sold, so this save
            // should not be abandoned because the caller disconnected.
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException exception) when (IsDuplicatePayment(exception))
        {
            return PaymentResult.Failed(CheckoutOutcome.NotAwaitingCheckout, "A payment has already been recorded for this order.");
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Stock for order {OrderReference} is confirmed but the payment could not be saved; a retry will complete it",
                order.OrderReference);
            throw;
        }

        _logger.LogInformation(
            "Order {OrderReference} checked out: {Amount} {Method} by rep {SalesRepId} at {Latitude},{Longitude}",
            order.OrderReference, payment.Amount, payment.Method, salesRepId, payment.Latitude, payment.Longitude);

        return PaymentResult.Succeeded(ToResponse(payment));
    }

    private (CheckoutOutcome Outcome, string Message)? Caller()
    {
        if (_tenant.CompanyId is null)
        {
            return (CheckoutOutcome.TenantNotAvailable, "A valid company identifier was not found in the access token.");
        }

        if (_caller.SalesRepId is null)
        {
            return (CheckoutOutcome.CallerNotSalesRep, "The access token does not identify a sales rep.");
        }

        return null;
    }

    /// <summary>
    /// Another rep's order looks exactly like a missing one, so order IDs
    /// cannot be probed.
    /// </summary>
    private Task<Order?> LoadOwnOrderAsync(Guid orderId, Guid salesRepId, CancellationToken cancellationToken) =>
        _db.Orders
            .Include(order => order.CheckIns)
            .Include(order => order.Payment)
            .SingleOrDefaultAsync(
                order => order.OrderId == orderId && order.SalesRepId == salesRepId,
                cancellationToken);

    private static CheckoutOutcome Map(CheckoutFailure failure) => failure switch
    {
        CheckoutFailure.InvalidCoordinates or
        CheckoutFailure.CapturedInFuture or
        CheckoutFailure.CaptureTooOld or
        CheckoutFailure.UnsupportedPaymentMethod => CheckoutOutcome.InvalidRequest,
        CheckoutFailure.NotAwaitingCheckout => CheckoutOutcome.NotAwaitingCheckout,
        CheckoutFailure.WrongSalesRep => CheckoutOutcome.OrderNotFound,
        CheckoutFailure.CheckInRequired => CheckoutOutcome.CheckInRequired,
        CheckoutFailure.CheckInExpired => CheckoutOutcome.CheckInExpired,
        CheckoutFailure.AmountMismatch => CheckoutOutcome.AmountMismatch,
        _ => CheckoutOutcome.InvalidRequest
    };

    private static bool IsDuplicatePayment(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: PaymentConfiguration.OnePaymentPerOrderIndex
        };

    internal static CheckInResponse ToResponse(OrderCheckIn checkIn) => new(
        checkIn.OrderCheckInId,
        checkIn.OrderId,
        checkIn.Accepted,
        checkIn.DistanceMeters,
        checkIn.RadiusMeters,
        checkIn.Latitude,
        checkIn.Longitude,
        checkIn.RecordedAt,
        checkIn.Accepted ? checkIn.ExpiresAt : null);

    internal static PaymentResponse ToResponse(Payment payment) => new(
        payment.PaymentId,
        payment.OrderId,
        payment.Amount,
        payment.Method.ToString(),
        payment.SalesRepId,
        payment.CheckInId,
        payment.Latitude,
        payment.Longitude,
        payment.DistanceMeters,
        payment.RecordedAt);
}
