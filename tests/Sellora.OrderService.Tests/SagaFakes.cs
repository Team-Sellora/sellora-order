using Sellora.OrderService.Application.Dependencies;
using Sellora.OrderService.Domain.Orders;

namespace Sellora.OrderService.Tests;

internal sealed class FakeOrganization : IOrganizationClient
{
    public VerifyRepShopRelationshipResponse Verification { get; set; } = new(true, null);
    public ShopPlacement? Shop { get; set; }
    public Dependency? Unavailable { get; set; }

    public Task<VerifyRepShopRelationshipResponse> VerifyRepShopAsync(Guid repId, Guid shopId, CancellationToken cancellationToken)
    {
        if (Unavailable is { } dependency) throw new DependencyUnavailableException(dependency);
        return Task.FromResult(Verification);
    }

    public Task<ShopPlacement?> FindShopAsync(Guid shopId, CancellationToken cancellationToken) =>
        Task.FromResult(Shop);

    public Sellora.OrderService.Application.Identity.CallerScope? Scope { get; set; }

    public int ScopeCalls { get; private set; }

    public Task<Sellora.OrderService.Application.Identity.CallerScope?> GetCallerScopeAsync(CancellationToken cancellationToken)
    {
        ScopeCalls++;
        if (Unavailable is { } dependency) throw new DependencyUnavailableException(dependency);
        return Task.FromResult(Scope);
    }
}

internal sealed class FakeCatalog : ICatalogClient
{
    public Dictionary<Guid, ProductForOrderResponse> Products { get; } = new();
    public bool Unavailable { get; set; }
    public int Calls { get; private set; }

    public ProductForOrderResponse Add(string name, decimal price, bool available = true)
    {
        var product = new ProductForOrderResponse(
            Guid.NewGuid(), name, $"SKU-{name.Length}", "Piece", available ? price : null,
            available ? "Active" : "Inactive", null, available, available ? null : "Product is inactive.");
        Products[product.ProductId] = product;
        return product;
    }

    public Task<ProductResolutionResponse> ResolveProductsAsync(Guid companyId, IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken)
    {
        Calls++;
        if (Unavailable) throw new DependencyUnavailableException(Dependency.Catalog);

        // Like Catalog: unknown IDs are simply absent from Items.
        return Task.FromResult(new ProductResolutionResponse(
            productIds.Where(Products.ContainsKey).Select(id => Products[id]).ToList()));
    }
}

internal sealed class FakeInventory : IInventoryClient
{
    public StockReservationStatus Mode { get; set; } = StockReservationStatus.Reserved;
    public IReadOnlyCollection<ReservationShortage> Shortages { get; set; } = Array.Empty<ReservationShortage>();
    public bool Unavailable { get; set; }
    public Func<string, Task>? OnReserved { get; set; }
    public Guid? VanOwnerId { get; set; }
    public List<Guid> Confirmed { get; } = new();
    public ReservationConfirmOutcome ConfirmOutcome { get; set; } = ReservationConfirmOutcome.Confirmed;
    public Guid? LastReservedOwnerId { get; private set; }
    public int ReserveCalls { get; private set; }
    public List<Guid> Released { get; } = new();
    public Guid? LastReservationId { get; private set; }

    public async Task<StockReservationAttempt> ResolveFulfilmentAsync(
        string orderReference, Guid agencyId, IReadOnlyCollection<BasketLine> lines, CancellationToken cancellationToken)
    {
        ReserveCalls++;
        if (Unavailable) throw new DependencyUnavailableException(Dependency.Inventory);

        switch (Mode)
        {
            case StockReservationStatus.InsufficientStock:
                return StockReservationAttempt.Short("Insufficient stock.", Shortages);
            case StockReservationStatus.Rejected:
                return StockReservationAttempt.Rejected("Inventory refused the reservation (HTTP 403).");
        }

        LastReservationId = Guid.NewGuid();
        var reservation = new StockReservationResponse(
            LastReservationId.Value, orderReference, Guid.NewGuid(), "Reserved",
            DateTimeOffset.UtcNow.AddMinutes(30),
            lines.Select(line => new ReservationLineResponse(Guid.NewGuid(), line.ProductId, null, line.Quantity)).ToList());

        if (OnReserved is not null) await OnReserved(orderReference);

        return StockReservationAttempt.Reserved(reservation);
    }

    public Task<StockReservationAttempt> ReserveAsync(
        string orderReference, Guid inventoryOwnerId, IReadOnlyCollection<BasketLine> lines, CancellationToken cancellationToken)
    {
        LastReservedOwnerId = inventoryOwnerId;
        return ResolveFulfilmentAsync(orderReference, Guid.Empty, lines, cancellationToken);
    }

    public Task<Guid?> FindVanOwnerAsync(Guid salesRepId, CancellationToken cancellationToken)
    {
        if (Unavailable) throw new DependencyUnavailableException(Dependency.Inventory);
        return Task.FromResult(VanOwnerId);
    }

    public Task<ReservationConfirmOutcome> ConfirmReservationAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        if (Unavailable) throw new DependencyUnavailableException(Dependency.Inventory);
        Confirmed.Add(reservationId);
        return Task.FromResult(ConfirmOutcome);
    }

    public Task<bool> ReleaseReservationAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        Released.Add(reservationId);
        return Task.FromResult(true);
    }
}
