using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Sellora.OrderService.Application.Dependencies;
using Sellora.OrderService.Application.Identity;
using Sellora.OrderService.Domain.Orders;
using Sellora.OrderService.Infrastructure.Dependencies;

namespace Sellora.OrderService.Tests;

/// <summary>
/// US-E4-1b-T1: each client parses the real JSON its dependency returns.
/// The payloads below are copied from the owning services' response shapes.
/// </summary>
public sealed class DependencyClientTests
{
    private static HttpClient Http(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://dependency.test/") };

    // Shape copied from sellora-organization's CallerScopeResponse.
    [Fact]
    public async Task Organization_scope_parses_the_real_response()
    {
        var rep = Guid.NewGuid();
        var agency = Guid.NewGuid();
        var province = Guid.NewGuid();
        var handler = StubHttpHandler.Json(HttpStatusCode.OK, $$"""
            {
              "subject": "6c83c690-72bb-46ef-a8fb-5902303924a8",
              "companyId": "{{Guid.NewGuid()}}",
              "role": "SalesRep",
              "staffProfileId": "{{rep}}",
              "displayName": "Ruwan Dias",
              "salesRepId": "{{rep}}",
              "agencyId": "{{agency}}",
              "territoryId": "{{Guid.NewGuid()}}",
              "shopId": null,
              "provinceIds": ["{{province}}"],
              "agencyIds": ["{{agency}}"],
              "territoryIds": [],
              "shopIds": []
            }
            """);

        var scope = await new OrganizationClient(Http(handler)).GetCallerScopeAsync(CancellationToken.None);

        Assert.Equal(rep, scope!.SalesRepId);
        Assert.Equal(agency, scope.AgencyId);
        Assert.Null(scope.ShopId);
        Assert.Equal(new[] { province }, scope.ProvinceIds);
        Assert.Equal("/api/me/scope", handler.Requests.Single().Request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Organization_scope_is_null_when_the_caller_has_no_profile()
    {
        var handler = StubHttpHandler.Json(HttpStatusCode.NotFound, """{ "title": "Profile not found" }""");

        Assert.Null(await new OrganizationClient(Http(handler)).GetCallerScopeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Organization_verify_parses_the_real_response()
    {
        var handler = StubHttpHandler.Json(HttpStatusCode.OK,
            """{ "isValid": false, "reason": "repNotAssignedToShopTerritory" }""");
        var client = new OrganizationClient(Http(handler));
        var rep = Guid.NewGuid();
        var shop = Guid.NewGuid();

        var result = await client.VerifyRepShopAsync(rep, shop, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("repNotAssignedToShopTerritory", result.Reason);
        Assert.Equal(
            $"http://dependency.test/api/rep-shop-relationships/verify?repId={rep}&shopId={shop}",
            handler.Requests.Single().Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task Organization_finds_a_shop_with_credit_limit_and_placement_in_the_hierarchy()
    {
        var shopId = Guid.NewGuid();
        var handler = StubHttpHandler.Json(HttpStatusCode.OK, $$"""
            {
              "companyId": "{{Guid.NewGuid()}}", "name": "Acme",
              "provinces": [{
                "provinceId": "11111111-1111-1111-1111-111111111111", "code": "WP", "name": "Western",
                "unassignedTerritories": [],
                "agencies": [{
                  "agencyId": "22222222-2222-2222-2222-222222222222", "name": "Colombo Agency",
                  "territories": [{
                    "territoryId": "33333333-3333-3333-3333-333333333333", "code": "T1", "name": "Dehiwala",
                    "shops": [{ "shopId": "{{shopId}}", "name": "Perera Stores", "ownerName": null,
                                "address": "12 Galle Rd", "latitude": 6.85, "longitude": 79.86,
                                "creditLimit": 250000.00 }]
                  }]
                }]
              }]
            }
            """);

        var shop = await new OrganizationClient(Http(handler)).FindShopAsync(shopId, CancellationToken.None);

        Assert.NotNull(shop);
        Assert.Equal(250_000m, shop!.CreditLimit);
        Assert.Equal(6.85m, shop.Latitude);
        Assert.Equal(79.86m, shop.Longitude);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), shop.AgencyId);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), shop.ProvinceId);
    }

    [Fact]
    public async Task Organization_hierarchy_404_means_shop_not_visible()
    {
        var handler = StubHttpHandler.Json(HttpStatusCode.NotFound, """{ "title": "Not found" }""");

        Assert.Null(await new OrganizationClient(Http(handler)).FindShopAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Catalog_sends_company_and_product_ids_and_parses_items()
    {
        var productId = Guid.NewGuid();
        var handler = StubHttpHandler.Json(HttpStatusCode.OK, $$"""
            { "items": [{ "productId": "{{productId}}", "name": "Soap", "sku": "S-1", "unitOfMeasure": "Piece",
                          "currentUnitPrice": 120.50, "status": "Active", "earliestExpiryDate": "2027-01-31",
                          "isAvailable": true, "unavailableReason": null }] }
            """);

        var result = await new CatalogClient(Http(handler))
            .ResolveProductsAsync(Guid.NewGuid(), new[] { productId }, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(120.50m, item.CurrentUnitPrice);
        Assert.Equal(new DateOnly(2027, 1, 31), item.EarliestExpiryDate);

        var sent = handler.Requests.Single();
        Assert.Equal("/internal/catalog/products/resolve", sent.Request.RequestUri!.AbsolutePath);
        Assert.Contains("\"companyId\"", sent.Body);
        Assert.Contains(productId.ToString(), sent.Body);
        Assert.Null(sent.Request.Headers.Authorization);
    }

    [Fact]
    public async Task Inventory_409_shortages_are_parsed()
    {
        var productId = Guid.NewGuid();
        var handler = StubHttpHandler.Json(HttpStatusCode.Conflict, $$"""
            { "message": "Insufficient stock.",
              "shortages": [{ "productId": "{{productId}}", "batchId": null, "requestedQuantity": 10, "availableQuantity": 4 }] }
            """);
        var client = new InventoryClient(Http(handler), NullLogger<InventoryClient>.Instance);

        var attempt = await client.ResolveFulfilmentAsync(
            "ORD-260918-ABCDEF", Guid.NewGuid(), new[] { new BasketLine(productId, 10) }, CancellationToken.None);

        Assert.Equal(StockReservationStatus.InsufficientStock, attempt.Status);
        var shortage = Assert.Single(attempt.Shortages);
        Assert.Equal(10, shortage.RequestedQuantity);
        Assert.Equal(4, shortage.AvailableQuantity);
        Assert.Equal("/api/stock/fulfilment/resolve", handler.Requests.Single().Request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Inventory_success_parses_the_reservation()
    {
        var reservationId = Guid.NewGuid();
        var handler = StubHttpHandler.Json(HttpStatusCode.OK, $$"""
            { "reservationId": "{{reservationId}}", "orderReference": "ORD-260918-ABCDEF",
              "inventoryOwnerId": "{{Guid.NewGuid()}}", "status": "Reserved",
              "expiresAt": "2026-09-18T10:30:00+00:00", "lines": [] }
            """);
        var client = new InventoryClient(Http(handler), NullLogger<InventoryClient>.Instance);

        var attempt = await client.ResolveFulfilmentAsync(
            "ORD-260918-ABCDEF", Guid.NewGuid(), new[] { new BasketLine(Guid.NewGuid(), 1) }, CancellationToken.None);

        Assert.Equal(StockReservationStatus.Reserved, attempt.Status);
        Assert.Equal(reservationId, attempt.Reservation!.ReservationId);
    }

    [Fact]
    public async Task Van_owner_is_found_by_rep_id_among_the_inventory_owners()
    {
        var repId = Guid.NewGuid();
        var vanOwnerId = Guid.NewGuid();
        var handler = StubHttpHandler.Json(HttpStatusCode.OK, $$"""
            [ { "inventoryOwnerId": "{{Guid.NewGuid()}}", "ownerType": "Agency",
                "externalOwnerId": "{{Guid.NewGuid()}}", "displayName": "Colombo Distribution Agency" },
              { "inventoryOwnerId": "{{vanOwnerId}}", "ownerType": "SalesRep",
                "externalOwnerId": "{{repId}}", "displayName": "Ruwan Dias" } ]
            """);
        var client = new InventoryClient(Http(handler), NullLogger<InventoryClient>.Instance);

        Assert.Equal(vanOwnerId, await client.FindVanOwnerAsync(repId, CancellationToken.None));
        Assert.Null(await client.FindVanOwnerAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.Equal("/api/inventory-owners", handler.Requests[0].Request.RequestUri!.AbsolutePath);
    }

    // The two 409 messages are copied verbatim from Inventory's
    // StockReservationService.ConfirmAsync. If Inventory rewords them this
    // test fails, instead of checkout silently misreading the outcome.
    [Theory]
    [InlineData(HttpStatusCode.OK, "{}", ReservationConfirmOutcome.Confirmed)]
    [InlineData(HttpStatusCode.Conflict, """{ "message": "The stock reservation has already been confirmed." }""", ReservationConfirmOutcome.AlreadyConfirmed)]
    [InlineData(HttpStatusCode.Conflict, """{ "message": "The stock reservation is no longer active." }""", ReservationConfirmOutcome.NoLongerActive)]
    [InlineData(HttpStatusCode.Conflict, """{ "message": "something else" }""", ReservationConfirmOutcome.Rejected)]
    [InlineData(HttpStatusCode.NotFound, """{ "message": "Stock reservation was not found." }""", ReservationConfirmOutcome.Rejected)]
    public async Task Confirm_distinguishes_every_outcome(HttpStatusCode status, string json, ReservationConfirmOutcome expected)
    {
        var handler = StubHttpHandler.Json(status, json);
        var client = new InventoryClient(Http(handler), NullLogger<InventoryClient>.Instance);
        var reservationId = Guid.NewGuid();

        Assert.Equal(expected, await client.ConfirmReservationAsync(reservationId, CancellationToken.None));
        Assert.Equal($"/api/stock/reservations/{reservationId}/confirm",
            handler.Requests.Single().Request.RequestUri!.AbsolutePath);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.Conflict, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task Release_treats_already_released_as_done(HttpStatusCode status, bool expected)
    {
        var handler = StubHttpHandler.Json(status, """{ "message": "x" }""");
        var client = new InventoryClient(Http(handler), NullLogger<InventoryClient>.Instance);

        Assert.Equal(expected, await client.ReleaseReservationAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Server_errors_become_a_named_unavailable_dependency()
    {
        var handler = StubHttpHandler.Json(HttpStatusCode.BadGateway, "{}");

        var exception = await Assert.ThrowsAsync<DependencyUnavailableException>(() =>
            new CatalogClient(Http(handler)).ResolveProductsAsync(Guid.NewGuid(), new[] { Guid.NewGuid() }, CancellationToken.None));

        Assert.Equal(Dependency.Catalog, exception.Dependency);
        Assert.Equal("Catalog service is currently unavailable, please retry shortly.", exception.Message);
    }

    [Fact]
    public async Task Bearer_token_is_forwarded()
    {
        var inner = StubHttpHandler.Json(HttpStatusCode.OK, """{ "isValid": true, "reason": null }""");
        var handler = new ForwardBearerTokenHandler(new TokenStub("abc.def.ghi")) { InnerHandler = inner };

        await new OrganizationClient(Http(handler)).VerifyRepShopAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        var auth = inner.Requests.Single().Request.Headers.Authorization!;
        Assert.Equal("Bearer", auth.Scheme);
        Assert.Equal("abc.def.ghi", auth.Parameter);
    }

    private sealed class TokenStub(string token) : IAccessTokenAccessor
    {
        public string? GetBearerToken() => token;
    }
}
