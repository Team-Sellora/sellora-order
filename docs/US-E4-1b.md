# US-E4-1b — Synchronous verification and saga rollback

## The saga (in `OrderCreationService`)
| # | Step | Call | Fails with |
|---|---|---|---|
| 1 | RepShopRelationship | Organization `GET /api/rep-shop-relationships/verify` (caller's token) | 422, human reason for `shopNotFound` / `shopInactive` / `repNotAssignedToShopTerritory` |
| 2 | PriceResolution | Catalog `POST /internal/catalog/products/resolve` (`X-Internal-Api-Key`, no user token) | 422 + `unresolvedProducts[]` |
| 3 | CreditLimit | Organization `GET /api/hierarchy` → shop's `creditLimit`, territory, agency, province | 422 + `credit { creditLimit, outstandingBalance, orderTotal, exceededBy }` |
| 4 | StockReservation | Inventory `POST /api/stock/fulfilment/resolve` (caller's token) | 422 + `shortages[] { productName, requestedQuantity, availableQuantity, shortBy }` |

Nothing is written until all four pass. The order then stores its `reservationId` and the four step outcomes (`order_verification_step`).
Compensation: any failure after a reservation exists (e.g. the save fails) calls `POST /api/stock/reservations/{id}/release`.

`POST /api/stock/reservations` is also implemented in `InventoryClient`, but the saga uses `fulfilment/resolve`, which picks the fulfilment owner **and** creates the reservation in one all-or-nothing call.

## Resilience
Each client: circuit breaker (outer) → per-call timeout (inner), configured under `Dependencies` in appsettings.
Timeout / open breaker / network error / 5xx → **503** `"<Service> service is currently unavailable, please retry shortly."` with `dependency` and `Retry-After: 30`.

## Configuration
| Setting (Order) | Must match |
|---|---|
| `Dependencies__Catalog__InternalApiKey` | Catalog's `InternalApi__ApiKey` (same secret on both App Services; never committed) |
| `Dependencies__{Organization,Catalog,Inventory}__BaseUrl` | Each service's App Service URL — called directly, not through the API gateway |

Timeouts are 2s per call; a normal order makes 4 sequential calls, so the worst case is ~8s.

## Contract changes
- Body is now `{ shopId, lines: [{ productId, quantity }] }`. `unitPrice`, `productName`, `agencyId`, `territoryId`, `provinceId` are gone; if a client still sends them they are ignored.
- `OrderResponse` gains `reservationId` and `verificationSteps[]`.

## Known limits
- **Outstanding balance** = sum of this shop's `Submitted` orders. There is no payment recording until US-E4-3, so every accepted order counts as unpaid.
- **Timeout on step 4**: if Inventory times out, Order never learns the reservation ID, so it cannot release it. Inventory's own reservation expiry (`expiresAt`) is the backstop.
- **Token claims (needs DevOps)**: step 1 needs the `salesRepId` claim, and Inventory's `fulfilment/resolve` requires the caller's `agencyId` claim to match the shop's agency. WSO2 does not emit either yet.
- A rejected attempt is not stored as an order (by design: "no order record"). Its full step trace is returned in the 422 body and logged as a warning.
