# US-E4-2 — Paying and non-paying routing

## The branch
`fulfilmentType` is required on `POST /api/orders`, is set once at creation and has no route or setter that changes it afterwards.

| `fulfilmentType` | Stock comes from | Reservation | Status after creation |
|---|---|---|---|
| `ImmediateCashSale` | the rep's **own van** — `POST /api/stock/reservations` against the rep's inventory owner | stays **held** until the GPS checkout takes payment (US-E4-3) | `AwaitingCheckout` |
| `ScheduledDelivery` | agency, then company — `POST /api/stock/fulfilment/resolve` | **confirmed** immediately (held becomes sold) | `Confirmed` |

Why two endpoints instead of one call plus a check: Inventory's fulfilment resolver only ever picks agency or company stock (`FulfilmentOwnerLookup` has no sales-rep branch). A cash sale must come out of the van the rep is standing next to, so Order looks up the rep's van owner in `GET /api/inventory-owners` and reserves against it directly.

A cash sale is rejected at the stock step when:
- the rep has no van stock recorded → "…you have no van stock recorded. Choose scheduled delivery instead."
- the van is short → the usual shortage list plus "Your van is short — scheduled delivery can source this from the agency."

## Statuses
`OrderStatus` (Domain/Orders/OrderStatus.cs) is the single list: `AwaitingCheckout`, `Confirmed`, `Cancelled`, `PendingApproval` (US-E4-5). `OrderStatuses.Outstanding` is what the credit check counts, so a cancelled order stops using up credit.

`Submitted` from US-E4-1a is gone. Add this line to the new migration so existing rows are not left with a status that no longer exists:

```csharp
migrationBuilder.Sql("UPDATE customer_order SET status = 'Confirmed' WHERE status = 'Submitted';");
```

## Known limit
If the order saves but Inventory then refuses to confirm the reservation, the order stays `Confirmed` while its stock is still *held* rather than sold. Nothing is oversold, and the error is logged for an operator to reconcile. Confirming before saving would be worse: a confirmed reservation cannot be released, so a failed save would sell stock with no order behind it.

## Not in this story
`OrderPlaced` carrying the fulfilment type is US-E4-4 (outbox and Kafka); opening a delivery job is US-E6-1.
