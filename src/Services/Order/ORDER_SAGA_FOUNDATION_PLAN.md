# Order Service - Saga Foundation Plan

Source context: `STEALDEAL_REVIEW_AND_PLAN.md`, lines 319-355.

## Goal

Implement the Order side of the order saga using choreography.

Target service flow:

```text
Order -> Store -> Payment -> Notification (if needed)
```

Order service is both:

- Producer: publishes `order.created` after an order is created.
- Producer: publishes `order.confirmed` after consuming `payment.completed`.
- Producer: publishes `order.cancelled` after consuming `inventory.reservation_failed`, `payment.failed`, or a future user-cancel request.
- Consumer: consumes Store/Payment result events and updates order state.

Week 2 exit criteria:

- Creating an order persists the order and publishes `order.created` through the outbox.
- Store consumes `order.created` and either continues the flow toward Payment or publishes `inventory.reservation_failed`.
- Payment publishes `payment.completed` or `payment.failed`.
- Order reacts to `inventory.reservation_failed`, `payment.completed`, and `payment.failed`.
- Failed message handling is logged clearly so errors are visible during local testing.

## Current Order Service State

Already present:

- CRUD-style order APIs in `StealDeal.Services.Order.API`.
- `OrderProfile` and `OrderItem` domain models.
- `OutboxMessage` domain model.
- `ProcessedMessage` domain model for consumer idempotency.
- `OutboxMessageRepository` with pending batch query.
- `ProcessedMessageRepository` with unique `(MessageId, ConsumerName)` handling.
- RabbitMQ config in `appsettings.json`.
- `RabbitMQ.Client` referenced by `StealDeal.Services.Order.Infrastructure`.

Missing foundation pieces:

- Local integration event DTOs in each service that participates in the saga.
- Outbox write during order creation and during saga state changes that produce new events.
- Background outbox publisher.
- RabbitMQ consumer for inventory/payment result events.
- Application handlers for order cancellation and confirmation.
- Status transition rules aligned with saga states.
- Registration of outbox, processed-message, publisher, and consumer services in `Program.cs`.
- Operational conventions for exchange names, routing keys, retry/logging behavior.

## Event DTO Policy

Do not add a shared contracts project for this milestone.

Each service may define its own local DTO classes even if that duplicates code across services. The important rule is that event names, property names, and routing keys must stay identical across services.

Minimum common event fields:

- `messageId`
- `occurredAtUtc`
- `orderId`

Optional but useful fields:

- `correlationId`
- `causationId`
- `eventType`
- `version`

## Events

### Order Service Produces

```text
order.created
order.confirmed
order.cancelled
```

`order.created` is published when an order is created.

Suggested payload:

```json
{
  "messageId": "guid",
  "occurredAtUtc": "2026-07-22T00:00:00Z",
  "orderId": "guid",
  "buyerId": "guid",
  "storeId": "guid",
  "items": [
    {
      "bagId": "guid",
      "quantity": 1,
      "unitPriceSnapshot": 50000
    }
  ],
  "totalAmount": 50000,
  "deliveryType": "Pickup",
  "pickupDeadline": "2026-07-22T12:00:00Z"
}
```

`order.confirmed` is published after Order consumes `payment.completed` and updates the order to `Confirmed`.

Suggested payload:

```json
{
  "messageId": "guid",
  "occurredAtUtc": "2026-07-22T00:00:00Z",
  "orderId": "guid",
  "paymentId": "guid",
  "buyerId": "guid",
  "storeId": "guid",
  "totalAmount": 50000
}
```

`order.cancelled` is published after Order consumes `inventory.reservation_failed`, consumes `payment.failed`, or handles a future user-cancel request.

Suggested payload:

```json
{
  "messageId": "guid",
  "occurredAtUtc": "2026-07-22T00:00:00Z",
  "orderId": "guid",
  "buyerId": "guid",
  "storeId": "guid",
  "reasonCode": "InventoryReservationFailed",
  "reason": "QuantityRemaining is not enough."
}
```

### Order Service Consumes

```text
inventory.reservation_failed
payment.completed
payment.failed
```

Suggested `inventory.reservation_failed` payload:

```json
{
  "messageId": "guid",
  "occurredAtUtc": "2026-07-22T00:00:00Z",
  "orderId": "guid",
  "storeId": "guid",
  "reasonCode": "InsufficientStock",
  "reason": "QuantityRemaining is not enough."
}
```

Suggested `payment.completed` payload:

```json
{
  "messageId": "guid",
  "occurredAtUtc": "2026-07-22T00:00:00Z",
  "orderId": "guid",
  "paymentId": "guid",
  "amount": 50000,
  "paymentMethod": "Wallet"
}
```

Suggested `payment.failed` payload:

```json
{
  "messageId": "guid",
  "occurredAtUtc": "2026-07-22T00:00:00Z",
  "orderId": "guid",
  "paymentId": "guid",
  "reasonCode": "PaymentDeclined",
  "reason": "Payment provider declined the transaction."
}
```

## RabbitMQ Topology

Recommended simple topology:

- Exchange: `stealdeal.events`
- Exchange type: `topic`
- Order publish routing keys:
  - `order.created`
  - `order.confirmed`
  - `order.cancelled`
- Order consumer queue: `order.saga-events`
- Order consumer bindings:
  - `inventory.reservation_failed`
  - `payment.completed`
  - `payment.failed`

Outbox rows should store:

- `EventType`
- `Payload`
- `ExchangeName = "stealdeal.events"`
- `ExchangeType = "topic"`
- `RoutingKey`
- `Status = "Pending"`
- `RetryCount`
- `ProcessedAt`
- `Error`

## Failure Handling Policy

No DLQ is required for this milestone.

If a consumed message fails during deserialize, idempotency check, handler execution, or database save:

- Log the error with routing key, message id if available, consumer name, and exception details.
- Do not publish a dead-letter message.
- Keep the behavior simple and visible for local debugging.

Recommended consumer behavior for now:

- Ack duplicate messages after confirming they were already processed.
- Ack malformed messages after logging, because reprocessing the same invalid payload will not fix it.
- For handler/database failures, log clearly. If the current consumer implementation supports retry through `BasicNack(requeue: true)`, keep retries limited and visible; otherwise ack after logging to avoid an infinite local retry loop.

Outbox publishing can still use bounded retry:

- On publish success:
  - set `Status = "Processed"` or `"Published"`;
  - set `ProcessedAt = DateTime.UtcNow`;
  - clear `Error`.
- On publish failure:
  - increment `RetryCount`;
  - store `Error`;
  - if `RetryCount >= Outbox:MaxRetryCount`, set `Status = "Failed"`.

## Order Creation Flow

Current flow:

1. `OrderService.CreateOrderAsync` validates request.
2. Maps request to `OrderProfile`.
3. Saves order.
4. Returns response.

Target flow:

1. Validate request.
2. Create `OrderProfile` with initial status `Pending`.
3. Create an `order.created` integration event.
4. Insert `OrderProfile` and `OutboxMessage` in the same database transaction / same `SaveChangesAsync`.
5. Return response.
6. Background outbox publisher eventually publishes the message to RabbitMQ.

Important rule:

- Do not publish directly from `CreateOrderAsync`.
- Persist order and outbox message atomically first.
- Publish from a background outbox worker so a process crash does not lose the event.

## Outbox Publisher

Add an infrastructure hosted service, for example:

```text
StealDeal.Services.Order.Infrastructure/Messaging/OutboxPublisherHostedService.cs
```

Responsibilities:

- Poll `OutboxMessages` using `Outbox:BatchSize`.
- Publish pending messages to RabbitMQ.
- Update outbox status according to the failure handling policy.
- Respect `Outbox:PollingIntervalSeconds`.

Register dependencies in `Program.cs`:

- `IOutboxMessageRepository`
- RabbitMQ connection/channel abstraction
- outbox publisher hosted service

## Order Saga Consumer

Add an infrastructure hosted service, for example:

```text
StealDeal.Services.Order.Infrastructure/Messaging/OrderSagaConsumerHostedService.cs
```

Responsibilities:

- Declare/bind queue `order.saga-events`.
- Consume:
  - `inventory.reservation_failed`
  - `payment.completed`
  - `payment.failed`
- Deserialize event payload into local DTO classes.
- Check `ProcessedMessages` by `(messageId, consumerName)`.
- If already processed, ack message and stop.
- Call application handler.
- Save `ProcessedMessage`.
- Ack only after DB changes are saved.
- On any failure, log the problem clearly; do not use DLQ for this milestone.

Consumer name recommendation:

```text
Order.SagaConsumer
```

## Application Handlers

Add application-level handler/service methods rather than putting logic inside the RabbitMQ consumer.

Recommended interface:

```text
IOrderSagaService
```

Suggested methods:

```csharp
Task HandleInventoryReservationFailedAsync(InventoryReservationFailedEvent integrationEvent);
Task HandlePaymentCompletedAsync(PaymentCompletedEvent integrationEvent);
Task HandlePaymentFailedAsync(PaymentFailedEvent integrationEvent);
```

`HandleInventoryReservationFailedAsync`:

- Load order by `OrderId`.
- If order does not exist, log the error and treat the message as handled for this milestone.
- If current status is `Pending`, set status to `Cancelled`.
- Create an `order.cancelled` outbox message with reason code `InventoryReservationFailed`.
- If order is already `Cancelled`, treat as idempotent.
- If order is already `Confirmed`, log a warning because inventory failure arrived too late.

`HandlePaymentCompletedAsync`:

- Load order by `OrderId`.
- If order does not exist, log the error and treat the message as handled for this milestone.
- If current status is `Pending` or `PaymentPending`, set status to `Confirmed`.
- Create an `order.confirmed` outbox message.
- If order is already `Confirmed`, treat as idempotent.
- If order is already `Cancelled`, log a warning and do not confirm the order.

`HandlePaymentFailedAsync`:

- Load order by `OrderId`.
- If order does not exist, log the error and treat the message as handled for this milestone.
- If current status is `Pending` or `PaymentPending`, set status to `Cancelled`.
- Create an `order.cancelled` outbox message with reason code `PaymentFailed`.
- If order is already `Cancelled`, treat as idempotent.
- If order is already `Confirmed`, log a warning because payment failure arrived too late.

## Status Flow

Target statuses from review plan:

```text
Pending
PaymentPending
Confirmed
Cancelled
Completed
Disputed
```

Recommended meaning:

- `Pending`: order created, waiting for Store/Payment saga outcome.
- `PaymentPending`: Store has accepted/reserved inventory and payment is waiting, if Order chooses to track this intermediate state later.
- `Confirmed`: payment succeeded and order is confirmed.
- `Cancelled`: inventory reservation failed, payment failed, buyer cancelled, seller/admin cancelled, or future user cancel event was accepted.
- `Completed`: pickup/delivery completed.
- `Disputed`: order has an active pickup dispute.

Recommended immediate change:

- Replace free-form status strings with constants or enum-like static class.
- Update manual status API so it cannot bypass saga-owned transitions accidentally.

Example ownership:

- Buyer/Seller/Admin API may request cancellation where allowed.
- Order creation owns initial status `Pending`.
- Inventory failure consumer owns `Pending -> Cancelled`.
- Payment consumer owns `Pending/PaymentPending -> Confirmed` and `Pending/PaymentPending -> Cancelled`.
- Future user cancel flow owns allowed cancellation transitions and publishes `order.cancelled`.
- Pickup/dispute flow owns `Confirmed -> Completed` or `Confirmed -> Disputed`.

## Idempotency Strategy

Order consumer must be idempotent because RabbitMQ can redeliver.

Minimum strategy:

- Every integration event must contain `messageId`.
- Before handling, check `ProcessedMessages.ExistsAsync(messageId, consumerName)`.
- After successful handling, insert `ProcessedMessage`.
- Unique index already exists on `(MessageId, ConsumerName)`.
- If duplicate insert race happens, treat it as already processed and ack.

Also make handlers status-aware:

- Receiving `inventory.reservation_failed` twice should not corrupt state or publish repeated cancellation side effects.
- Receiving `payment.completed` twice should not corrupt state or publish repeated confirmation side effects.
- Receiving `payment.failed` twice should not corrupt state or publish repeated cancellation side effects.
- Receiving a late conflicting event for a cancelled/confirmed order should be logged.

## Files To Add Or Update

Domain:

- Add order status constants or enum-like type.
- Consider adding `CancellationReason`, `CancelledAt`, `ConfirmedAt`, or `PaymentId` later.

Application:

- Add local integration event DTOs.
- Add `IOrderSagaService`.
- Add `OrderSagaService`.
- Update `OrderService.CreateOrderAsync` to create `order.created` outbox message atomically with order.
- Update saga handlers to create `order.confirmed` and `order.cancelled` outbox messages atomically with order status updates.
- Tighten `UpdateOrderStatusAsync` transition rules.

Infrastructure:

- Add RabbitMQ connection/publisher abstraction.
- Add outbox publisher hosted service.
- Add Order saga consumer hosted service.
- Register `IOutboxMessageRepository` and `IProcessedMessageRepository`.
- Add options classes for `RabbitMq` and `Outbox`.

API:

- Register messaging services in `Program.cs`.
- Keep Swagger/Postman setup for API testing.
- Ensure Development config has RabbitMQ and Outbox values.

Database:

- Existing migration already includes `OutboxMessages` and `ProcessedMessages`.
- Add migration only if new columns are added to `OrderProfile` or outbox schema.

## Implementation Order

1. Finalize event names and payloads with Store and Payment services.
2. Add local integration event DTOs inside Order service.
3. Add status constants and update order creation initial status.
4. Register outbox and processed-message repositories in DI.
5. Update `CreateOrderAsync` to write `order.created` outbox message.
6. Implement RabbitMQ publisher abstraction.
7. Implement outbox publisher hosted service.
8. Implement `IOrderSagaService` handlers.
9. Implement Order saga consumer hosted service for `inventory.reservation_failed`, `payment.completed`, and `payment.failed`.
10. Add local test path:
    - create order;
    - verify outbox row;
    - verify RabbitMQ publishes `order.created`;
    - simulate `inventory.reservation_failed`;
    - verify order moves to `Cancelled` and `order.cancelled` is queued;
    - simulate `payment.completed`;
    - verify order moves to `Confirmed` and `order.confirmed` is queued;
    - simulate `payment.failed`;
    - verify order moves to `Cancelled` and `order.cancelled` is queued.

## Local Testing Checklist

- RabbitMQ is running locally.
- Order API starts without failing if RabbitMQ is unavailable, or fails fast intentionally with clear logs.
- Creating an order creates one `order.created` outbox row.
- Outbox worker publishes `order.created`.
- Published payload has stable `messageId`.
- Consumer creates one `ProcessedMessage` per consumed message.
- Duplicate consumed events do not apply duplicate state changes.
- `inventory.reservation_failed` cancels a pending order.
- `payment.completed` confirms a pending/payment-pending order.
- `payment.failed` cancels a pending/payment-pending order.
- Consumer failures are logged clearly.
- No DLQ setup is required for this milestone.
- Postman can still test CRUD endpoints through Swagger-imported collection.

## Open Questions

1. Should Order track `PaymentPending`, or keep the status as `Pending` until `payment.completed` / `payment.failed` arrives?
2. What exact event envelope should all services use: `messageId`, `correlationId`, `causationId`, `occurredAtUtc`, `eventType`, `version`?
3. Should `order.created` include price snapshots from the request as-is, or should Order validate snapshots against Store/Product data before publishing?
4. What should happen if Payment returns `payment.completed` after the buyer already cancelled the order?
