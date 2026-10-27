# US-E4-6 — Rep returns unsold van stock

## Endpoints
| Endpoint | Who | Body | Success | Main failures |
|---|---|---|---|---|
| `POST /api/van-returns` | `RequireSalesRep` | `{ lines: [{ productId, quantity }] }` | **201** `VanReturnResponse`, status `Declared` | **422** more than the van holds (`shortages[]` with `requestedQuantity`, `heldQuantity`) · 422 rep has no van stock · 400 bad lines · 503 Inventory/Catalog down |
| `PUT /api/van-returns/{id}/acceptance` | `RequireAgencyOperator`, the rep's own agency only | `{ lines: [{ productId, countedQuantity }], note? }` | 200 `VanReturnResponse`, status `Accepted` | 400 count above declared / product missing · 404 another agency's return · **409** already accepted with different counts · 422 van no longer holds the counted stock |
| `GET /api/van-returns?status=Declared` | order readers | | paged list | rep: own · operator: own agency · admin: all · others: none |
| `GET /api/van-returns/{id}` | order readers | | `VanReturnResponse` | 404 if not visible |

**Route:** the story's `/api/van-returns`, not the backlog subtask's `/api/orders/van-returns`. A van return is not an order and never touches one, and every route under `api/orders` is guarded as immutable by `OrdersRouteImmutabilityTests` (a PUT there would fail CI).

## Where it lives (the backlog's design question)
In **sellora-order**, as its own aggregate (`VanReturn`), not on the Order aggregate. The two-step flow (declare → count → accept) needs state, role checks and an event written atomically with the acceptance; Order already has the outbox, the Kafka producer and the caller-scope plumbing. Inventory owns the stock rules: it moves stock only when it consumes `VanStockReturned`.

The backlog's T3 idea (call `POST /api/stock/adjustments` twice) was not used: two separate HTTP calls can half-succeed (van debited, agency not credited), an Agency Operator cannot adjust a rep's van stock, and adjustments lose the batch. One event handled in one Inventory transaction cannot half-succeed.

## Rules
- **Declared ≤ what the van can hand over now** — Inventory's `POST /api/stock/availability` for the rep's van owner, summed across batches, *minus stock held for an unfinished cash sale*. Otherwise 422 naming the product and how many units the van holds; **no return is created** (scenario 2).
- **Counted is 0..declared.** Lower is normal (missing, damaged). Higher is refused: undeclared goods were never checked against the van and belong on a new return.
- **Every line is counted** before acceptance.
- **Both figures and the variance** (`declared − counted`) are stored per line and in total, and returned by the API; DB check constraints keep `variance = declared − counted` and `0 ≤ counted ≤ declared`.
- **Checked again at acceptance:** if the rep sold some of the stock after declaring, acceptance is refused (422) instead of publishing an event Inventory would have to refuse.
- **Only the rep's own agency** (from Organization's `/api/me/scope` when the rep declared) can accept; any other agency gets 404.
- Repeating an acceptance with the same counts is a 200 no-op; different counts are a 409 (the stock has already moved).
- `xmin` concurrency: two operators accepting at once cannot both publish.

## What moves, and when
Accept → `VanStockReturned` is written in the same `SaveChanges` → relay publishes it → Inventory, in one transaction:
1. checks the van owner really is this rep's (`SalesRep` owner, `externalOwnerId = salesRepId`) and finds the agency's owner;
2. takes each line's `acceptedQuantity` from the van, oldest batch first, never below held stock;
3. adds it to the agency's stock **in the same batch** (creating the row if needed);
4. writes a `Transferred` movement on each side (`referenceType = VanReturn`, `referenceId = VR-…`).

Only the counted quantity moves (scenario 3: 12 declared, 10 counted → 10 move). The 2-unit variance stays on the van's ledger — the gap the agency has to explain. Replaying the event changes nothing.

## Migration `AddVanReturns`
New tables `van_return`, `van_return_line`. No change to existing tables.

## Not in scope
Writing off a variance (the units left on the van ledger) — a stock adjustment by the agency, already possible.
