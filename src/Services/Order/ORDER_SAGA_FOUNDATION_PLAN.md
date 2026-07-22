# Order Service - Saga Foundation Plan

Source context: `STEALDEAL_REVIEW_AND_PLAN.md`, lines 319-355.

## Goal

Implement the Order side of the order-to-stock reservation flow using saga choreography.

Order service is both:

- Producer: publishes `order.created` after an order is created.
- Consumer: consumes Store stock result events and updates order state.

Week 2 exit criteria:

- Creating an order publishes `order.created`.
- Store consumes `order.created` and reserves or rejects stock.
- Order reacts to Store stock result.
- Stock does not go negative in normal local testing.

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

- Shared integration event contracts.
- Outbox write during order creation.
- Background outbox publisher.
- RabbitMQ consumer for Store stock result events.
- Application handlers for stock reserved / stock reservation failed.
- Status transition rules aligned with saga states.
- Registration of outbox, processed-message, publisher, and consumer services in `Program.cs`.
- Operational conventions for exchange names, routing keys, retry policy, and dead-letter behavior.

## Event Contracts To Define

Recommended contract location for now:

- Prefer a small shared contracts project if Store and Order will both implement saga in the same sprint.
- If speed matters more, temporarily duplicate DTOs in both services but keep event names, property names, and routing keys identical.

Order should publish:

```text
order.created
```

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

Order should consume:

```text
store.stock.reserved
store.stock.reservation-failed
```

Suggested `store.stock.reserved` payload:

```json
{
  "messageId": "guid",
  "occurredAtUtc": "2026-07-22T00:00:00Z",
  "orderId": "guid",
  "storeId": "guid",
  "reservationId": "guid",
  "items": [
    {
      "bagId": "guid",
      "quantity": 1
    }
  ]
}
```

Suggested `store.stock.reservation-failed` payload:

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

## RabbitMQ Topology

Recommended simple topology:

- Exchange: `stealdeal.events`
- Exchange type: `topic`
- Order publish routing key: `order.created`
- Order consumer queue: `order.stock-results`
- Order consumer bindings:
  - `store.stock.reserved`
  - `store.stock.reservation-failed`

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
- On success:
  - set `Status = "Processed"` or `"Published"`;
  - set `ProcessedAt = DateTime.UtcNow`;
  - clear `Error`.
- On failure:
  - increment `RetryCount`;
  - store `Error`;
  - if `RetryCount >= Outbox:MaxRetryCount`, set `Status = "Failed"`.
- Respect `Outbox:PollingIntervalSeconds`.

Register dependencies in `Program.cs`:

- `IOutboxMessageRepository`
- RabbitMQ connection/channel abstraction
- outbox publisher hosted service

## Store Stock Result Consumer

Add an infrastructure hosted service, for example:

```text
StealDeal.Services.Order.Infrastructure/Messaging/StoreStockResultConsumerHostedService.cs
```

Responsibilities:

- Declare/bind queue `order.stock-results`.
- Consume:
  - `store.stock.reserved`
  - `store.stock.reservation-failed`
- Deserialize event payload.
- Check `ProcessedMessages` by `(messageId, consumerName)`.
- If already processed, ack message and stop.
- Call application handler.
- Save `ProcessedMessage`.
- Ack only after DB changes are saved.

Consumer name recommendation:

```text
Order.StoreStockResultConsumer
```

## Application Handlers

Add application-level handler/service methods rather than putting logic inside the RabbitMQ consumer.

Recommended interface:

```text
IOrderSagaService
```

Suggested methods:

```csharp
Task HandleStockReservedAsync(StoreStockReservedEvent integrationEvent);
Task HandleStockReservationFailedAsync(StoreStockReservationFailedEvent integrationEvent);
```

`HandleStockReservedAsync`:

- Load order by `OrderId`.
- If order does not exist, fail the message for retry or send to dead-letter.
- If order is already `StockReserved`, `PaymentPending`, `Confirmed`, `Completed`, or `Cancelled`, treat as idempotent based on transition rules.
- Change status from `Pending` to `StockReserved`.
- Then either:
  - set immediately to `PaymentPending` if payment flow is next; or
  - leave as `StockReserved` until payment foundation is implemented.

`HandleStockReservationFailedAsync`:

- Load order by `OrderId`.
- If current status is `Pending`, set status to `Cancelled`.
- Store cancellation/failure reason if a field is added later.
- If order is already cancelled, treat as idempotent.

## Status Flow

Target statuses from review plan:

```text
Pending
StockReserved
PaymentPending
Confirmed
Cancelled
Completed
Disputed
```

Recommended meaning:

- `Pending`: order created, waiting for Store stock reservation.
- `StockReserved`: Store has reserved stock for this order.
- `PaymentPending`: stock is reserved and buyer/payment flow is waiting.
- `Confirmed`: payment succeeded and order is confirmed.
- `Cancelled`: stock failed, buyer cancelled, seller/admin cancelled, or payment failed.
- `Completed`: pickup/delivery completed.
- `Disputed`: order has an active pickup dispute.

Recommended immediate change:

- Replace free-form status strings with constants or enum-like static class.
- Update manual status API so it cannot bypass saga-owned transitions accidentally.

Example ownership:

- Buyer/Seller/Admin API may request cancellation where allowed.
- Store result consumer owns `Pending -> StockReserved` and `Pending -> Cancelled`.
- Payment consumer later owns `PaymentPending -> Confirmed` and payment failure cancellation.
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

- Receiving `store.stock.reserved` twice should not corrupt state.
- Receiving `store.stock.reservation-failed` twice should not corrupt state.
- Receiving stock result for a cancelled/completed order should be explicitly handled.

## Files To Add Or Update

Domain:

- Add order status constants or enum-like type.
- Consider adding `CancellationReason`, `StockReservationId`, or `ReservedAt` later.

Application:

- Add integration event DTOs if not using shared contracts project.
- Add `IOrderSagaService`.
- Add `OrderSagaService`.
- Update `OrderService.CreateOrderAsync` to create outbox message atomically with order.
- Tighten `UpdateOrderStatusAsync` transition rules.

Infrastructure:

- Add RabbitMQ connection/publisher abstraction.
- Add outbox publisher hosted service.
- Add Store stock result consumer hosted service.
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

1. Finalize event names and payloads with Store service.
2. Add integration event DTOs/contracts.
3. Add status constants and update order creation initial status.
4. Register outbox and processed-message repositories in DI.
5. Update `CreateOrderAsync` to write `order.created` outbox message.
6. Implement RabbitMQ publisher abstraction.
7. Implement outbox publisher hosted service.
8. Implement `IOrderSagaService` handlers.
9. Implement Store stock result consumer hosted service.
10. Add local test path:
    - create order;
    - verify outbox row;
    - verify RabbitMQ message published;
    - simulate Store stock reserved;
    - verify order moves to `StockReserved` or `PaymentPending`;
    - simulate Store stock reservation failed;
    - verify order moves to `Cancelled`.

## Local Testing Checklist

- RabbitMQ is running locally.
- Order API starts without failing if RabbitMQ is unavailable, or fails fast intentionally with clear logs.
- Creating an order creates one outbox row.
- Outbox worker publishes `order.created`.
- Published payload has stable `messageId`.
- Consumer creates one `ProcessedMessage` per consumed message.
- Duplicate Store result events do not apply duplicate state changes.
- Stock reservation failure cancels pending order.
- Stock reservation success advances pending order.
- Postman can still test CRUD endpoints through Swagger-imported collection.

## Open Questions

1. Should Order set status to `StockReserved` first, or immediately move to `PaymentPending` after receiving `store.stock.reserved`?
2. Will there be a shared contracts project for integration events, or should Order and Store duplicate contracts for now?
3. What exact event envelope should all services use: `messageId`, `correlationId`, `causationId`, `occurredAtUtc`, `eventType`, `version`?
4. Should `order.created` include price snapshots from the request as-is, or should Order validate snapshots against Store/Product data before publishing?
5. What should happen if Store returns `stock.reserved` after the buyer already cancelled the order?
6. Do we need dead-letter queues in Week 2, or is retry + `Failed` outbox/consumer logging enough for the capstone milestone?

