using Sellora.OrderService.Domain.Orders;

namespace Sellora.OrderService.Application.Dependencies;

/// <summary>Calls sellora-organization, forwarding the caller's bearer token.</summary>
public interface IOrganizationClient
{
    /// <summary>GET /api/rep-shop-relationships/verify</summary>
    Task<VerifyRepShopRelationshipResponse> VerifyRepShopAsync(
        Guid repId,
        Guid shopId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds the shop in GET /api/hierarchy (scoped to the caller).
    /// Null when the shop is not visible to the caller.
    /// </summary>
    Task<ShopPlacement?> FindShopAsync(
        Guid shopId,
        CancellationToken cancellationToken);
}

/// <summary>Calls sellora-catalog's internal, anonymous resolve endpoint.</summary>
public interface ICatalogClient
{
    /// <summary>POST /internal/catalog/products/resolve</summary>
    Task<ProductResolutionResponse> ResolveProductsAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken);
}

/// <summary>Calls sellora-inventory, forwarding the caller's bearer token.</summary>
public interface IInventoryClient
{
    /// <summary>
    /// POST /api/stock/fulfilment/resolve — picks the fulfilment owner for the
    /// agency and creates the reservation in one all-or-nothing call.
    /// </summary>
    Task<StockReservationAttempt> ResolveFulfilmentAsync(
        string orderReference,
        Guid agencyId,
        IReadOnlyCollection<BasketLine> lines,
        CancellationToken cancellationToken);

    /// <summary>POST /api/stock/reservations — reserve against a known owner.</summary>
    Task<StockReservationAttempt> ReserveAsync(
        string orderReference,
        Guid inventoryOwnerId,
        IReadOnlyCollection<BasketLine> lines,
        CancellationToken cancellationToken);

    /// <summary>POST /api/stock/reservations/{id}/release — the saga's compensation.</summary>
    Task<bool> ReleaseReservationAsync(
        Guid reservationId,
        CancellationToken cancellationToken);
}

public enum StockReservationStatus
{
    Reserved,
    InsufficientStock,
    Rejected
}

public sealed record StockReservationAttempt(
    StockReservationStatus Status,
    StockReservationResponse? Reservation,
    IReadOnlyCollection<ReservationShortage> Shortages,
    string? Message)
{
    public static StockReservationAttempt Reserved(StockReservationResponse reservation) =>
        new(StockReservationStatus.Reserved, reservation, Array.Empty<ReservationShortage>(), null);

    public static StockReservationAttempt Short(string? message, IReadOnlyCollection<ReservationShortage> shortages) =>
        new(StockReservationStatus.InsufficientStock, null, shortages, message);

    public static StockReservationAttempt Rejected(string? message) =>
        new(StockReservationStatus.Rejected, null, Array.Empty<ReservationShortage>(), message);
}
