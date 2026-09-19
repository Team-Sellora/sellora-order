using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sellora.OrderService.Application.Dependencies;
using Sellora.OrderService.Domain.Orders;

namespace Sellora.OrderService.Infrastructure.Dependencies;

/// <summary>
/// sellora-inventory. Reservation endpoints require RequireStockReservation,
/// so the caller's bearer token is forwarded.
/// </summary>
public sealed class InventoryClient : IInventoryClient
{
    private readonly HttpClient _http;
    private readonly ILogger<InventoryClient> _logger;

    public InventoryClient(HttpClient http, ILogger<InventoryClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public Task<StockReservationAttempt> ResolveFulfilmentAsync(
        string orderReference,
        Guid agencyId,
        IReadOnlyCollection<BasketLine> lines,
        CancellationToken cancellationToken) =>
        PostReservationAsync(
            "api/stock/fulfilment/resolve",
            // Matches Inventory's ResolveFulfilmentRequestBody.
            new { OrderReference = orderReference, AgencyId = agencyId, Lines = ToBody(lines) },
            cancellationToken);

    public Task<StockReservationAttempt> ReserveAsync(
        string orderReference,
        Guid inventoryOwnerId,
        IReadOnlyCollection<BasketLine> lines,
        CancellationToken cancellationToken) =>
        PostReservationAsync(
            "api/stock/reservations",
            // Matches Inventory's ReserveStockRequestBody.
            new { OrderReference = orderReference, InventoryOwnerId = inventoryOwnerId, Lines = ToBody(lines) },
            cancellationToken);

    public async Task<bool> ReleaseReservationAsync(
        Guid reservationId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"api/stock/reservations/{reservationId}/release");

        using var response = await DependencyHttp.SendAsync(
            _http, request, Dependency.Inventory, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        // 409 = already released or confirmed: nothing is stranded.
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            _logger.LogInformation(
                "Reservation {ReservationId} was already released or confirmed: {Detail}",
                reservationId, await DependencyHttp.ReadErrorAsync(response, cancellationToken));
            return true;
        }

        _logger.LogError(
            "Inventory refused to release reservation {ReservationId} (HTTP {Status}): {Detail}",
            reservationId, (int)response.StatusCode, await DependencyHttp.ReadErrorAsync(response, cancellationToken));
        return false;
    }

    private async Task<StockReservationAttempt> PostReservationAsync(
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: DependencyHttp.Json)
        };

        using var response = await DependencyHttp.SendAsync(
            _http, request, Dependency.Inventory, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return StockReservationAttempt.Reserved(
                await DependencyHttp.ReadAsync<StockReservationResponse>(
                    response, Dependency.Inventory, cancellationToken));
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            // Inventory returns 409 { message, shortages } for insufficient stock.
            var conflict = await response.Content.ReadFromJsonAsync<ConflictBody>(
                DependencyHttp.Json, cancellationToken);

            if (conflict?.Shortages is { Count: > 0 } shortages)
            {
                return StockReservationAttempt.Short(conflict.Message, shortages);
            }

            return StockReservationAttempt.Rejected(conflict?.Message ?? "Inventory reported a conflict.");
        }

        var detail = await DependencyHttp.ReadErrorAsync(response, cancellationToken);
        return StockReservationAttempt.Rejected(
            $"Inventory refused the reservation (HTTP {(int)response.StatusCode})" +
            (string.IsNullOrWhiteSpace(detail) ? "." : $": {detail}"));
    }

    private static object[] ToBody(IReadOnlyCollection<BasketLine> lines) =>
        lines.Select(line => (object)new
        {
            line.ProductId,
            BatchId = (Guid?)null,
            line.Quantity
        }).ToArray();

    private sealed record ConflictBody(string? Message, IReadOnlyCollection<ReservationShortage>? Shortages);
}
