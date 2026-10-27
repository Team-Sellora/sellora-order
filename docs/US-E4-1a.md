# US-E4-1a — Order aggregate and creation

## What this PR delivers
| Subtask | Where |
|---|---|
| T1 Bootstrap | 4-layer solution + tests; `RolePolicies.cs`, `ITenantContext`, `ITenantScoped`, `HttpTenantContext` copied from existing services (namespace only changed) |
| T2 Order + OrderLine snapshots | `Domain/Entities/Order.cs`, `OrderLine.cs`; EF config `customer_order` / `order_line` in `order_db` |
| T3 `POST /api/orders` | `OrdersController.Create` — `RequireSalesRep`, server-side totals, 400 names the exact bad line |
| T4 Scoped reads | `GET /api/orders`, `GET /api/orders/{id}` — tenant query filter, then role scope (`OrderScope.cs`) |
| T5 Immutability by omission | No PUT/PATCH; `OrdersRouteImmutabilityTests` fails CI if one is added |

## ⚠️ PROVISIONAL — replaced in US-E4-1b
- `lines[].unitPrice` and `lines[].productName` come from the request body **temporarily**. US-E4-1b replaces them with Catalog's `POST /internal/catalog/products/resolve`.
- `agencyId`, `territoryId`, `provinceId` also come from the body temporarily; 1b derives them server-side.
- This is **not** the final behaviour. Do not build frontend logic that depends on sending prices.

## Rules enforced
- Company comes only from the token `companyId`; sales rep only from token `salesRepId`. Neither is a body field.
- Totals: `lineTotal = round(qty × unitPrice, 2)`, `subtotal = Σ lineTotal`, `total = subtotal`. The body has no total fields, so client totals are discarded.
- 400 cases: empty basket, >100 lines (Catalog resolve limit), qty ≤ 0, duplicate productId, missing name, negative price, >2 decimals. DB check constraints + unique `(order_id, product_id)` back these up.
- Order reference: `ORD-YYMMDD-XXXXXX` (no 0/O/1/I/L/U/V), unique per company, retried on collision.
- Out-of-scope and non-existent orders both return 404 (no ID probing).

## Scope claims (known gap — needs DevOps)
Scoping reads `salesRepId`, `agencyId`, `shopId`, `provinceId` (multi-value) claims — the same convention Inventory already uses.
`sellora-infra/configure-apps.sh` currently only puts `companyId` and `roles` in tokens, so on staging:
- `POST /api/orders` returns 403 "Sales rep identity missing"
- non-admin `GET /api/orders` returns an empty list (fails closed)

Fix: add these claims to WSO2 (claim dialect + app requestedClaims + accessTokenAttributes), or resolve them from Organization by `sub` in a later story.
