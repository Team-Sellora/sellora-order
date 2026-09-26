# Order events — schema version 1.0

Published by **sellora-order** through a transactional outbox.

| | |
|---|---|
| Topic | `sellora.order.v1` (`Kafka:OrderTopic`) |
| Key | the **order reference** (e.g. `ORD-260924-K7P2QM`) — every event of one order lands on one partition, in order |
| Value | UTF-8 JSON, camelCase, flat (no envelope) |
| Headers | `event-id`, `event-type`, `schema-version`, `company-id`, `correlation-id` (all UTF-8 strings) |
| Delivery | at-least-once — **deduplicate on `eventId`** |

## Guarantees

- **No order without its event.** Each event is written in the same database transaction as the order change it describes. If the change rolls back, no event exists; if it commits, the event will be published, even if Kafka is down at the time.
- **Per-order ordering.** Events of one order are published in the order they were written. The relay never publishes an event while an earlier event of the same order is still unpublished — including across retries and multiple relay instances.
- **No callbacks needed.** Every event carries the shop, agency and rep names and emails, the lines and totals, so a consumer can act on the event alone.

## When each event is published

| Transition | Events, in order |
|---|---|
| Scheduled delivery accepted (now awaits agency approval, US-E4-5) | `OrderPlaced` |
| Scheduled delivery approved by the agency | `OrderApproved`, `OrderConfirmed` |
| Scheduled delivery rejected by the agency | `OrderCancelled` (`source: AgencyRejection`) |
| Order cancelled by the shop owner inside the window | `OrderCancelled` (`source: ShopCancellation`) |
| Cash sale accepted (stock held in the van) | `OrderPlaced` |
| Cash sale checked out and paid | `OrderConfirmed`, `PaymentRecorded` |
| Cash sale's stock hold expired before checkout | `OrderCancelled` (`source: StockHoldExpired`) |
| Order rejected by verification, failed check-in, failed payment, rejection without a reason, cancellation outside the window | *nothing* |
| Van return accepted by the agency (US-E4-6) | `VanStockReturned` — see below; keyed by the **return reference** (`VR-…`), not an order |

## Fields on every event

| Field | Type | Notes |
|---|---|---|
| `eventId` | uuid | Unique per event; also the `event-id` header. Deduplicate on it. |
| `eventType` | string | `OrderPlaced` · `OrderConfirmed` · `PaymentRecorded` · `OrderCancelled` · `OrderApproved` |
| `schemaVersion` | string | `"1.0"` |
| `companyId` | uuid | Tenant |
| `entityId` | uuid | The order ID (name matches Inventory's envelope) |
| `orderId` | uuid | Same as `entityId` |
| `orderReference` | string | Also the Kafka key |
| `reservationId` | uuid | Inventory's stock reservation |
| `occurredAt` | timestamp | When the transition happened |
| `correlationId` | string | From the originating HTTP request (`X-Correlation-ID`) |
| `fulfilmentType` | string | `ImmediateCashSale` · `ScheduledDelivery` (the story's "DeliveryType") |
| `status` | string | Order status after the transition |
| `orderDate` | timestamp | |
| `shop` | object | `shopId`, `name`, `ownerName`, `ownerEmail` |
| `agency` | object | `agencyId`, `name`, `email` |
| `territoryId`, `provinceId` | uuid | |
| `salesRep` | object | `salesRepId`, `name` |
| `lines` | array | `productId`, `productName`, `quantity`, `unitPrice`, `lineTotal` — prices as snapshotted when placed |
| `subtotal`, `total` | decimal | Server-computed |
| `currency` | string | `"LKR"` |
| `checkoutLocation` | object \| null | `latitude`, `longitude`, `distanceMeters`, `accuracyMeters`, `checkedInAt`. Null until a cash sale is checked out; always null for scheduled deliveries (no GPS in that flow). |

Names and emails are snapshots taken when the order was placed. They may be `null` for orders placed before this version, or if Organization has no email on file.

## Event-specific fields

**`OrderPlaced`** — none.

**`OrderConfirmed`** — `confirmedAt` (timestamp).

**`PaymentRecorded`**
- `payment`: `paymentId`, `amount`, `method` (`"Cash"`), `recordedAt`, `checkInId`
- `checkInLocation`: `latitude`, `longitude`, `distanceMeters`, `accuracyMeters`, `checkedInAt` — the accepted check-in that permitted the payment

**`OrderCancelled`**
- `cancelledAt` (timestamp), `reason` (string)
- `source` (string, US-E4-5): `ShopCancellation` · `AgencyRejection` · `StockHoldExpired`
- `cancelledBy` (object \| null, US-E4-5): `userId` (identity-provider `sub`), `role`. Null when the system cancelled.

**`OrderApproved`** (US-E4-5) — `approvedAt` (timestamp), `approvedBy` (`userId`, `role`). Always followed by `OrderConfirmed` in the same transaction.

Both US-E4-5 additions are additive, so the schema stays `1.0`.

## `VanStockReturned` (US-E4-6)

Not an order event, so it does not carry the order fields above. Same topic, same headers, same outbox and delivery guarantees; the Kafka key is the return reference.

| Field | Type | Notes |
|---|---|---|
| `eventId`, `eventType` (`VanStockReturned`), `schemaVersion` (`1.0`), `companyId`, `correlationId`, `occurredAt` | | as above |
| `entityId`, `vanReturnId` | uuid | the van return |
| `returnReference` | string | e.g. `VR-260925-K7MQ4R`; also the Kafka key |
| `salesRepId`, `salesRepName` | uuid, string | the rep who declared it |
| `agencyId` | uuid | the rep's agency — the owner credited |
| `vanInventoryOwnerId` | uuid | Inventory owner of the rep's van — the owner debited |
| `declaredAt`, `acceptedAt` | timestamp | |
| `acceptedBy` | object | `userId`, `role` |
| `acceptanceNote` | string \| null | |
| `lines` | array | `productId`, `productName`, `declaredQuantity`, `acceptedQuantity`, `variance` (declared − accepted) |
| `totalDeclared`, `totalAccepted`, `totalVariance` | int | |

**Only `acceptedQuantity` moves.** Inventory takes it from the van (oldest batch first, never touching stock held for a cash sale) and adds it to the agency's stock in the same batches. A line with `acceptedQuantity: 0` moves nothing. `variance` is the shrinkage signal and changes no stock.

## Example — `PaymentRecorded`

```json
{
  "eventId": "5b7a1c0e-9f7d-4c6b-8a2e-2f1d3c4b5a69",
  "eventType": "PaymentRecorded",
  "schemaVersion": "1.0",
  "companyId": "30000000-0000-0000-0000-000000000001",
  "entityId": "8c1e3d52-6a1f-4b7e-9d2c-0f4e5a6b7c8d",
  "orderId": "8c1e3d52-6a1f-4b7e-9d2c-0f4e5a6b7c8d",
  "orderReference": "ORD-260924-K7P2QM",
  "reservationId": "e2b9f0a4-3c5d-4e6f-8a7b-9c0d1e2f3a4b",
  "occurredAt": "2026-09-24T05:12:41+00:00",
  "correlationId": "4f1c2b9e-7d3a-4e8f-b6c5-a1d2e3f4a5b6",
  "fulfilmentType": "ImmediateCashSale",
  "status": "Confirmed",
  "orderDate": "2026-09-24T05:03:10+00:00",
  "shop": { "shopId": "…", "name": "Lake View Mini Mart", "ownerName": "Chaminda Perera", "ownerEmail": "chaminda@lakeview.lk" },
  "agency": { "agencyId": "…", "name": "Colombo Distribution Agency", "email": "orders@colombo-agency.lk" },
  "territoryId": "…",
  "provinceId": "…",
  "salesRep": { "salesRepId": "…", "name": "Ruwan Dias" },
  "lines": [
    { "productId": "…", "productName": "Ceylon Black Tea 500g", "quantity": 2, "unitPrice": 1250.00, "lineTotal": 2500.00 }
  ],
  "subtotal": 2500.00,
  "total": 2500.00,
  "currency": "LKR",
  "checkoutLocation": { "latitude": 6.9165, "longitude": 79.8487, "distanceMeters": 42.18, "accuracyMeters": 8, "checkedInAt": "2026-09-24T05:12:41+00:00" },
  "payment": { "paymentId": "…", "amount": 2500.00, "method": "Cash", "recordedAt": "2026-09-24T05:12:41+00:00", "checkInId": "…" },
  "checkInLocation": { "latitude": 6.9165, "longitude": 79.8487, "distanceMeters": 42.18, "accuracyMeters": 8, "checkedInAt": "2026-09-24T05:11:58+00:00" }
}
```

## Consumers

| Consumer | Uses |
|---|---|
| **Inventory** (existing) | `OrderConfirmed` → confirms the reservation; `OrderCancelled` → releases a held reservation, or returns the stock of a confirmed one (US-E4-5 — a scheduled delivery's stock is committed at placement). Both are no-ops if already applied. Ignores `OrderApproved`. |
| **Notification** (US-E5-1) | All of them; the shop and agency emails come from `shop.ownerEmail` and `agency.email`. `source` says who to tell about a cancellation. |
| **Delivery** (E6) | `OrderConfirmed` for scheduled deliveries. |
| **Audit** (E7) | All four, including the location evidence. |
| **Inventory** (US-E4-6) | `VanStockReturned` → transfers `acceptedQuantity` from `vanInventoryOwnerId` to the agency's owner. |

## Changing this contract

Adding a field is backwards-compatible and keeps `1.0`. Removing or renaming a field, or changing its meaning, requires `2.0` published alongside `1.0` until every consumer has moved.
