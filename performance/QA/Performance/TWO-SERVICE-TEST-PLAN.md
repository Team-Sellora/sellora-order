# Sprint 3 Order and Notification performance test plan

Files now live under `sellora-order/performance`. Run test commands from that directory. This document records the bounded plan; the report and result files record which tests actually completed.

Prepared 18 October 2026 from the local service controllers, request contracts, OrderCreationService, OrderEventOutbox and NotificationPlanner. Routes must be smoke-checked against the deployed build; API Manager may expose a different external context or a subset of routes.

## Objective and fixed scope

Retain the completed Order List evidence. Add a bounded create-and-retrieve scenario, a Notification List read-load test and an event-processing observation using those same created orders. No new maximum-capacity search is planned. Do not reuse the infinite-loop read plan for order creation.

Known Order direct base: https://sellora-order-staging-hjbqevb4eafbdydg.eastasia-01.azurewebsites.net

Known Order gateway base: https://13.61.228.129:8244/sellora-order/1.0.0

Notification direct and gateway bases are still required; do not infer their hostnames or API versions from Order.

## Endpoint selection

| Scenario | Method and service-relative route | Identity | Expected success | Checks |
| --- | --- | --- | --- | --- |
| Order List already completed | GET /api/orders?page=1&pageSize=20 | SalesRep used; controller also accepts CompanyAdmin, AreaManager, AgencyOperator, ShopOwner | 200 | items array and pagination; per-endpoint latency and errors |
| Create Order | POST /api/orders | SalesRep with valid company and placement claims | 201 | orderId, orderReference, reservationId; correct shop, lines, quantity and calculated totals |
| Retrieve created Order | GET /api/orders/{orderId} | Same SalesRep within scope | 200 | ID equals extracted ID; shop, products, quantities and total match POST response |
| Notification List | GET /api/notifications?page=1&pageSize=20 | CompanyAdmin in same company | 200 | items array and pagination; read latency and errors |
| Observe order notifications | GET /api/notifications?orderId={orderId}&page=1&pageSize=20 | CompanyAdmin in same company | 200 while waiting | filter response client-side for OrderPlaced and this order; correlate sourceEventId, status, recipients and dispatch timestamps |
| Inspect one notification if needed | GET /api/notifications/{notificationRequestId} | CompanyAdmin | 200 for known scoped record | expected source event, recipients, status and dispatch |

Notification also exposes /api/notification-requests as an alias for the same resource. Prefer /api/notifications consistently; do not count aliases as separate tested behaviours.

## Setup before writes

- Sales Rep and Company Admin test access tokens from the same company, supplied separately through environment variables. Use a Shop Owner test identity for supported cancellation if required.
- A designated test shop assigned to the Sales Rep; active test product IDs with known prices and enough stock for at most 11 successful orders. Choose the fulfilment type supported by the prepared fixture, not simply whichever avoids a validation error.
- Confirm credit headroom where applicable. If each order contains one unit of a single product, budget at least 11 units in the correct inventory ownership scope, plus any existing commitments.
- Route both shop-owner and agency email contacts to a controlled SMTP test sink or test inboxes. The repository has a local MailHog configuration, but its presence in Azure is not established. Do not alter shared company contacts or mail configuration silently.
- Confirm deployed Kafka and Notification dispatch are connected. The local default order topic is sellora.order.v1; deployed configuration may override it.
- Agree how these exact test orders and stock changes will be cleaned up. Save every successful order ID and reservation ID. A request that times out may still have committed; reconcile it before retrying.

## Stage 1 One baseline order

Use a new fixed-loop JMeter plan: 1 thread, 1 loop, finite connection/response timeouts. Submit one order and extract $.orderId, $.orderReference, $.reservationId and $.total. Run GET for that extracted ID only after a valid creation response. Use thread-local variables so concurrent clients never retrieve another client's order.

Example request shape from the current controller:

```json
{
  "shopId": "REPLACE_WITH_TEST_SHOP_GUID",
  "fulfilmentType": "ScheduledDelivery",
  "lines": [
    { "productId": "REPLACE_WITH_TEST_PRODUCT_GUID", "quantity": 1 }
  ]
}
```

ScheduledDelivery is an example, not a default fixture choice. The other supported value is ImmediateCashSale. Prices and totals are resolved on the server and are not POST inputs. Scheduled deliveries commit reserved stock while awaiting approval; cash-sale reservations wait for checkout. These are genuine business changes.

Switch to the admin token for Notification requests. Poll by orderId at a bounded interval (for example every 2 seconds for at most 60 seconds), looking specifically for OrderPlaced. Stop if creation, event intake or delivery is not working. A polling HTTP 200 with empty items means delivery has not been established.

## Stage 2 Bounded Order write workload

Only after the baseline works, run 5 threads with a 5-second ramp up and 2 loops per thread. Each iteration creates one order then retrieves it. Maximum planned writes: 10 orders; maximum normal reads: 10. Together with baseline, total planned successful writes are at most 11. Disable infinite loops and duration-driven iteration for this scenario. Do not automatically retry POST failures.

Report POST and GET samples separately, with response-time percentiles, throughput, request/assertion failures and business rejections. Also report full create-and-retrieve transaction time separately if using a Transaction Controller. The existing generic Total-statistics checker should not be used to blend POST, GET and notification polling into one endpoint SLA.

The provisional HTTP targets are p95 <= 2000 ms and errors < 1%; distinguish business-data rejections from timeouts and server failures. With only 10 POSTs, percentiles are descriptive and capacity is not established. One failed request is already a 10% observed write error rate.

## Stage 3 Notification read load

Using CompanyAdmin, test GET /api/notifications?page=1&pageSize=20 with 5 clients, a 5-second ramp up, 60-second duration and a 1000 ms pause per client. Run once. Apply status and JSON structure assertions and report p95, throughput and errors. This is notification read-API performance, not dispatch throughput.

## Stage 4 Notification processing under the same write burst

Reuse the 10 orders from Stage 2; do not create another batch merely for Notification. Start observing their notification records as they are created or rely on stored event/dispatch timestamps after completion. Save all correlations by run, orderId, sourceEventId and notificationRequestId.

The local OrderCreationService enqueues OrderPlaced in the same database transaction as the accepted order. Its outbox later publishes to Kafka. NotificationPlanner has an order-placed.v1 template and addresses this event to the shop and agency. Confirm this end to end with the baseline before assuming the deployed build matches.

For 10 successfully created orders, expect 10 distinct OrderPlaced notification requests with two expected recipients each, provided the deployed contracts and test contacts match. This is 20 expected recipient dispatches, not 20 source events. Filter out other event types and unrelated test runs. Do not count retry attempts as extra successful notifications.

Measure:

- Intake lag: receivedAt minus occurredAt.
- Event-to-completion time: dispatch.completedAt minus occurredAt, only for fully Sent requests with both expected recipients Sent.
- Recipient send gap and withinTolerance, using the service's reported tolerance, not a fabricated delivery SLA.
- Missing requests, duplicates, attempts/retries, failed recipients and remaining batch backlog at the observation cutoff.
- Completion throughput for this batch over an explicitly stated first-event to last-completion window. Record the actual event generation rate too; a tiny batch does not establish maximum consumer capacity.

UTC timestamps must be interpreted consistently, with possible clock skew recorded. completedAt or SMTP acceptance is not proof of real inbox delivery; the controlled mail sink is additional evidence. If observing only by polling, detection time has polling-resolution uncertainty. A 60-second observation cutoff is a provisional measurement bound, not an agreed business SLA. Extend or report incomplete work honestly if normal configured retries run longer.

## Cleanup and endpoints excluded from routine load

POST /api/orders/{orderId}/cancellation exists but requires ShopOwner for the same shop and must satisfy order state/window rules. It emits OrderCancelled and stock restoration may be asynchronous. Do not assume the Sales Rep can clean up through this route. Verify restored stock/reservation state and reconcile partial or timed-out operations; do not delete Order rows directly.

Do not load-loop approval, payment, cancellation, notification resend or settings updates in this bounded exercise. Available routes include PUT /api/orders/{orderId}/approval (AgencyOperator), POST /api/orders/{orderId}/checkin and /payment (SalesRep), POST /api/notifications/{notificationRequestId}/resend (CompanyAdmin), and PUT /api/notifications/settings (CompanyAdmin). They have side effects and require their own prepared workflows. There is no public HTTP create-notification endpoint in these controllers; normal intake is through Kafka.

GET /api/notifications/health is an admin status-count endpoint, not a substitute for correlated per-event completion evidence.

## Deliverables and stopping point

Keep the existing capacity report unchanged until new tests actually execute. Add separate JMeter plans/results for create-and-retrieve and Notification List. Add a CSV correlating the batch's order IDs, notification IDs, source event IDs, expected recipients, timestamps and outcomes. Add before/after stock evidence and cleanup status. Update the report and archive only with observed results.

Stop after one baseline, one ten-order burst and one sixty-second Notification read run. Diagnose failed prerequisites rather than expanding the workload or inventing a maximum-capacity claim.

## Code references checked

- sellora-order/src/Sellora.OrderService.Api/Controllers/OrdersController.cs
- sellora-order/src/Sellora.OrderService.Api/Contracts/CreateOrderRequestBody.cs
- sellora-order/src/Sellora.OrderService.Api/Controllers/OrderCancellationController.cs
- sellora-order/src/Sellora.OrderService.Infrastructure/Orders/OrderCreationService.cs
- sellora-order/src/Sellora.OrderService.Infrastructure/Outbox/OrderEventOutbox.cs
- sellora-notification/src/Sellora.NotificationService.Api/Controllers/NotificationRequestsController.cs
- sellora-notification/src/Sellora.NotificationService.Api/Authorization/RolePolicies.cs
- sellora-notification/src/Sellora.NotificationService.Application/Notifications/NotificationPlanner.cs
- sellora-notification/src/Sellora.NotificationService.Application/Notifications/NotificationContracts.cs
- sellora-notification/src/Sellora.NotificationService.Infrastructure/Kafka/OrderEventConsumerOptions.cs
