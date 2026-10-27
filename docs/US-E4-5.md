# US-E4-5 — Order approval and shop cancellation window

## Endpoints
| Endpoint | Who | Body | Success | Main failures |
|---|---|---|---|---|
| `PUT /api/orders/{id}/approval` | `RequireAgencyOperator`, own agency only | `{ decision: "Approve" \| "Reject", reason? }` | 200 `OrderResponse` | **400** reject without a reason (`errors.reason`) · 404 another agency's order · **409** not awaiting approval (e.g. already cancelled) |
| `POST /api/orders/{id}/cancellation` | `RequireShopOwner`, own shop only | `{ reason? }` — body optional, **no time field** | 200 `OrderResponse` | **409** window closed, with `closedAgo`, `closedMinutesAgo`, `elapsedSinceConfirmationMinutes`, `confirmedAt`, `windowClosedAt`, `windowMinutes` · 409 cash sale / already cancelled · 404 another shop's order |

Another agency's or shop's order returns **404**, same as a missing one, so order IDs cannot be probed.

`PUT` is used because the story specifies it and the request *sets* the approval decision: repeating the same decision returns 200 and changes nothing. It never touches lines or totals. `OrdersRouteImmutabilityTests` allow-lists this one route by exact template; any other PUT, and every PATCH, still fails CI.

## Status flow
```
ScheduledDelivery:  PendingApproval ──approve──▶ Confirmed ──shop cancels (≤ window)──▶ Cancelled
                           │ reject (reason)            
                           │ shop cancels (any time)    
                           ▼                            
                       Cancelled                        
ImmediateCashSale:  AwaitingCheckout ──payment──▶ Confirmed (not cancellable: paid and handed over)
                           │ shop cancels
                           ▼
                       Cancelled
```
A scheduled delivery used to be `Confirmed` at placement; it now waits for its agency (the enum already reserved `PendingApproval` for this).

## The cancellation window
- **Measured from `confirmed_at`**, a stored column: set when the agency approves a scheduled delivery, and when payment is recorded for a cash sale. The request carries no time at all — a client clock is exactly what someone would adjust.
- **Length** from configuration: `Cancellation:WindowMinutes`, default **60** (open issue #4), clamped to 1–1440.
- **Boundary:** open while `now < confirmedAt + window`; **closed from the exact hour**. Tested at 59:59.999, 60:00.000 and 60:00.001.
- **Before confirmation** (`PendingApproval`, `AwaitingCheckout`) the window has not started, so the shop can cancel any time — the order is not binding yet.
- **Allow-list, not deny-list.** Only those three statuses are ever cancellable. Any status added later (E6 delivery statuses) is refused until someone decides otherwise — "has not entered delivery" holds by default.
- **Time zones (US-E4-5-D1):** `confirmed_at` is `timestamp with time zone`, the clock is `TimeProvider.GetUtcNow()`, and `DateTimeOffset` compares instants, so the server's local time zone cannot change the answer. `OrderCancellationWindowTests.The_offset_of_the_clock_makes_no_difference` proves it with the same instant in UTC and +05:30.
- **Order view:** `GET /api/orders/{id}` returns `cancellation { canCancel, confirmedAt, closesAt, windowMinutes, remainingSeconds, closedSecondsAgo, reason, checkedAt }`, computed by the server. The web counts `remainingSeconds` down from when it received the response, so the device clock only drives the display; the endpoint re-checks regardless.

## Every decision is recorded
New `order_decision` table, append-only: `kind` (`Approved` · `Rejected` · `CancelledByShop`), `actor_user_id` (the token's `sub`), `actor_role`, `reason`, `status_before`, `status_after`, `decided_at`. A check constraint makes a rejection without a reason impossible even outside the API. The order also gets `cancelled_by`. A shop's reason is optional; if blank, "Cancelled by the shop owner." is recorded so every decision has one.

## Stock
- A scheduled delivery's stock is **committed at placement** (Inventory confirm), as before. Inventory's hold expires in 15 minutes — far sooner than an agency decides — and the agency must not sell the same stock twice meanwhile.
- **Rejection and cancellation release it through `OrderCancelled`**, written in the same transaction as the decision. Inventory's consumer releases a held reservation, or returns the stock of a confirmed one (sellora-inventory change in this story).
- Order does **not** call Inventory over HTTP here: Order forwards the caller's token, and Inventory's reservation endpoints (correctly) do not accept a Shop Owner. Opening them would let one shop release another's reservation. The event is also the only path that cannot be lost between two writes.

## Events (additive, schema stays 1.0 — see contracts/order-events.v1.md)
| Transition | Events |
|---|---|
| Scheduled delivery placed | `OrderPlaced` (was `OrderPlaced`, `OrderConfirmed`) |
| Approved | `OrderApproved`, `OrderConfirmed` |
| Rejected | `OrderCancelled` — `source: AgencyRejection`, `reason`, `cancelledBy` |
| Shop cancelled | `OrderCancelled` — `source: ShopCancellation`, `cancelledBy` |

## Concurrency
`customer_order` uses PostgreSQL `xmin` as an optimistic concurrency token. An approval racing a cancellation, or a cancellation racing a cash payment, cannot both win: the loser gets **409 "changed by someone else, reload"** instead of overwriting. If a shop cancels while the rep's payment is between Inventory's confirm and the save, the payment is refused and `OrderCancelled` returns the stock.

## Migration `AddOrderApprovalAndCancellation`
Adds `order_decision`, `customer_order.confirmed_at`, `customer_order.cancelled_by`, widens `cancellation_reason` to 500, and back-fills `confirmed_at` for orders confirmed before this change (scheduled delivery → `order_date`, cash sale → `checked_out_at`). The `xmin` row version adds no column.
