namespace Sellora.OrderService.Application.Dependencies;

// Wire contracts copied field-for-field from the owning services.
// Do not rename: System.Text.Json binds these by name (case-insensitive).

/// <summary>sellora-organization: Application/SalesRepAssignments/VerifyRepShopRelationshipContract.cs</summary>
public sealed record VerifyRepShopRelationshipResponse(bool IsValid, string? Reason);

/// <summary>sellora-catalog: Application/Products/ProductResolutionResponse.cs</summary>
public sealed record ProductResolutionResponse(IReadOnlyCollection<ProductForOrderResponse> Items);

/// <summary>sellora-catalog: Application/Products/ProductForOrderResponse.cs</summary>
public sealed record ProductForOrderResponse(
    Guid ProductId,
    string? Name,
    string? Sku,
    string? UnitOfMeasure,
    decimal? CurrentUnitPrice,
    string Status,
    DateOnly? EarliestExpiryDate,
    bool IsAvailable,
    string? UnavailableReason);

/// <summary>sellora-inventory: Application/Stock/StockReservationResponse.cs</summary>
public sealed record StockReservationResponse(
    Guid ReservationId,
    string OrderReference,
    Guid InventoryOwnerId,
    string Status,
    DateTimeOffset ExpiresAt,
    IReadOnlyCollection<ReservationLineResponse> Lines);

/// <summary>sellora-inventory: Application/Stock/ReservationLineResponse.cs</summary>
public sealed record ReservationLineResponse(
    Guid StockItemId,
    Guid ProductId,
    Guid? BatchId,
    int Quantity);

/// <summary>sellora-inventory: Application/Stock/ReservationShortage.cs</summary>
public sealed record ReservationShortage(
    Guid ProductId,
    Guid? BatchId,
    int RequestedQuantity,
    int AvailableQuantity);

/// <summary>
/// The shop details Order needs from Organization's GET /api/hierarchy:
/// its credit limit, where it sits in the hierarchy, and its registered
/// coordinates for the GPS check-in gate (US-E4-3).
/// </summary>
public sealed record ShopPlacement(
    Guid ShopId,
    string Name,
    decimal CreditLimit,
    Guid TerritoryId,
    Guid AgencyId,
    Guid ProvinceId,
    decimal Latitude,
    decimal Longitude,
    string? OwnerName = null,
    string? OwnerEmail = null,
    string? AgencyName = null,
    string? AgencyEmail = null);

/// <summary>sellora-inventory: Application/Stock/InventoryOwnerResponse.cs</summary>
public sealed record InventoryOwnerResponse(
    Guid InventoryOwnerId,
    string OwnerType,
    Guid ExternalOwnerId,
    string DisplayName);
