# US-E4-3 — GPS check-in gate and payment recording

## Endpoints (both `RequireSalesRep`, own orders only)
| Endpoint | Body | Success | Main failures |
|---|---|---|---|
| `POST /api/orders/{id}/checkin` | `{ latitude, longitude, capturedAt, accuracyMeters? }` | 200 `CheckInResponse` | **403** outside radius, with `distanceMeters` and `radiusMeters` · 400 bad coordinates / timestamp · 409 not awaiting checkout · 422 shop location not visible |
| `POST /api/orders/{id}/payment` | `{ amount, method: "Cash" }` | 201 `PaymentResponse` | **403** no check-in / expired check-in · **422** amount ≠ total (`expectedAmount`, `submittedAmount`) · 409 already paid / stock hold expired · 503 Inventory down |

Routes follow the Jira subtasks (T1, T2). The story description's `/api/checkins` + `/checkout` names were not used, so there is one naming scheme.

Another rep's order returns **404**, same as a missing one, so order IDs cannot be probed.

## Rules
- **Distance:** Haversine, Earth radius 6,371,008.8 m, rounded to the centimetre. **Accepted when distance ≤ radius** — a check-in exactly on the radius is inside. Tested at 299 m, 300 m, 300.01 m and 301 m with points built exactly, not estimated.
- **Configuration** (`CheckIn` section, change without redeploy): `RadiusMeters` 300 (open issue #3) · `ValidityMinutes` 10 · `MaxClockSkewSeconds` 120 · `MaxCaptureAgeSeconds` 300.
- **Timestamps:** a `capturedAt` more than 2 min in the future is refused; one older than 5 min is refused too, so an old GPS fix cannot be replayed.
- **Every attempt is stored** in `order_check_in`, accepted or not, with the reported point, device accuracy, the shop point used, distance and radius. Rejected attempts are evidence of where a rep actually was.
- **A rejected check-in changes nothing else** (T4): the order stays `AwaitingCheckout`, the reservation stays held.
- **Payment** needs the rep's latest **accepted, unexpired** check-in, verified server-side from the stored row — the client never claims it checked in. Cash only. Amount must equal the server-computed total exactly.

## What a successful payment does, in order
1. Checks every rule above — no side effects yet.
2. Calls Inventory `POST /api/stock/reservations/{id}/confirm` (held → sold).
3. Saves a `payment` row (amount, method, rep, the check-in's coordinates and distance, timestamp), stamps `checkout_latitude` / `checkout_longitude` / `checked_out_at` on the order (T3 — US-E4-4's publisher reads these directly), and moves the order to `Confirmed`.

**Why confirm before saving:** the goods leave the van at this moment, so stock should be sold. If the save fails, a retry is safe: Inventory answers "already confirmed", which is treated as success, and the save completes. A unique index on `payment.order_id` stops a double submit from recording two payments.

## Reservation expiry (T4)
Inventory holds a reservation for **15 minutes** (`StockReservation:TtlMinutes`) and its `ReservationExpirySweeper` releases it after that, so an abandoned cash sale never strands stock.

If the rep checks out after that, Inventory says the reservation is "no longer active". The order is then **cancelled** (`cancellation_reason` recorded), no payment is saved, and the rep is told to place the order again — instead of a paid order with no stock behind it.

**Team decision needed:** 15 minutes from order creation is the whole window for a cash sale. That fits a rep who orders at the counter; it will cancel orders placed in the car before walking in. Either keep it and document it, or raise Inventory's TTL.

## Coordinates
Shop coordinates come from Organization's `GET /api/hierarchy` (already called by US-E4-1b), which now also carries each shop's latitude/longitude. So no new outbound call path is added; US-E4-3-D1 is covered by US-E4-1b-D1.

## Known dependency on Inventory wording
Inventory's confirm endpoint returns 409 with a message but no code. `InventoryClient` tells "already confirmed" from "no longer active" by those exact messages; `DependencyClientTests` pins both, so a wording change in Inventory fails CI here instead of silently misreading the outcome. Asking Inventory to add a machine-readable code would remove this coupling.
