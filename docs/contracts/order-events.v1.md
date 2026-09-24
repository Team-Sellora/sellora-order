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
| Scheduled delivery accepted | `OrderPlaced`, `OrderConfirmed` |
| Cash sale accepted (stock held in the van) | `OrderPlaced` |
| Cash sale checked out and paid | `OrderConfirmed`, `PaymentRecorded` |
| Cash sale's stock hold expired before checkout | `OrderCancelled` |
| Order rejected by verification, failed check-in, failed payment | *nothing* |

## Fields on every event

| Field | Type | Notes |
|---|---|---|
| `eventId` | uuid | Unique per event; also the `event-id` header. Deduplicate on it. |
| `eventType` | string | `OrderPlaced` · `OrderConfirmed` · `PaymentRecorded` · `OrderCancelled` |
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

**`OrderCancelled`** — `cancelledAt` (timestamp), `reason` (string).

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
| **Inventory** (existing) | `OrderConfirmed` → confirms the reservation; `OrderCancelled` → releases it. Both are no-ops if already applied, so the synchronous confirm Order also makes is harmless. |
| **Notification** (US-E5-1) | All four; the shop and agency emails come from `shop.ownerEmail` and `agency.email`. |
| **Delivery** (E6) | `OrderConfirmed` for scheduled deliveries. |
| **Audit** (E7) | All four, including the location evidence. |

## Changing this contract

Adding a field is backwards-compatible and keeps `1.0`. Removing or renaming a field, or changing its meaning, requires `2.0` published alongside `1.0` until every consumer has moved.
