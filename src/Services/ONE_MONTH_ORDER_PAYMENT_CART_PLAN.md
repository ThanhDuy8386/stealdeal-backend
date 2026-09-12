# StealDeal - 1 Month Order/Payment And Redis Cart Plan

> Created: 2026-09-03
> Planning window: 2026-09-03 to 2026-10-02
> Scope: finish Order payment workflow and implement Redis-backed ephemeral Cart.

## 1. Review Snapshot

I reviewed the current `.md` files in `Identity`, `Notification`, `Order`,
`Payment`, and root `Services`. `Store` currently has no `.md` file, so I read
important Store code related to inventory reservation.

Build snapshot:

| Service | Build result | Notes |
|---|---:|---|
| Identity | Pass, 0 warnings | Admin/SuperAdmin split exists. User and admin auth are separate. |
| Store | Pass, 0 warnings | Consumes `order.created`, reserves stock, publishes inventory result. |
| Order | Pass, 0 warnings | Publishes `order.created`, consumes inventory failure/payment result. |
| Payment | Pass, 2 warnings | VNPAY gateway pieces exist, but saga consumer/callback flow is incomplete. |
| Notification | Pass, 2 warnings | OTP email via Brevo exists. |

Important warning:

- Payment and Notification still show `NU1903` for `Microsoft.OpenApi 2.4.1`.
  This should not block the main workflow, but should be cleaned during hardening.

Current Order/Store workflow:

```text
Order API
  -> OrderService.CreateOrderAsync saves Pending order
  -> Order writes order.created outbox message
  -> Order OutboxMessageProcessor publishes to RabbitMQ
  -> Store CreatedOrderConsumer consumes order.created
  -> Store CreateOrderEventHandler reserves SurpriseBag.QuantityRemaining
  -> Store publishes inventory.reserved or inventory.reservation_failed
  -> Order OrderStatusConsumer consumes inventory.reservation_failed/payment.* events
```

Current Payment status:

- `PaymentDb` config and migration exist.
- `Transaction`, `Refund`, `OutboxMessage`, and `ProcessedMessage` exist.
- Transaction response already includes `CheckoutUrl`, `GatewayRef`,
  gateway response fields, and `ExpiresAt`.
- Payment constants exist for methods/statuses.
- VNPAY gateway abstraction and `VnPayGateway` exist.
- Payment outbox publisher exists.
- Missing key pieces:
  - consumer for `inventory.reserved`;
  - handler that creates a pending transaction from trusted saga data;
  - VNPAY IPN/return controller;
  - callback application service that updates transaction and writes outbox;
  - `payment.completed`, `payment.failed`, `inventory.release_requested`
    outbox factory/use case;
  - expiration worker for abandoned pending transactions;
  - Store compensation consumer for `inventory.release_requested`;
  - stricter transaction transition/idempotency rules.

Current Cart status:

- No `Cart` service/module was found in current source.
- No Redis usage was found in application code.
- Redis cart should therefore be treated as a new feature, starting from service
  boundary and API contract decisions.

## 2. Target End State After 1 Month

The target is a demo-ready flow:

```text
Buyer adds bags to Redis cart
  -> Buyer checks out cart
  -> Order is created as Pending
  -> Store reserves stock atomically
  -> Payment creates Pending VNPAY transaction and checkout URL
  -> Frontend redirects buyer to VNPAY sandbox
  -> VNPAY IPN updates transaction
  -> Payment publishes payment.completed/payment.failed
  -> Order confirms or cancels
  -> Store releases reserved stock when payment fails/expires
```

Non-goals for this month:

- Multiple payment gateways.
- Real seller payout.
- Full BuildingBlocks/shared contracts refactor.
- Full API Gateway if it slows down the two requested flows.
- Real production-grade distributed saga orchestration.

## 3. Architecture Decisions For This Month

### Payment Flow

- Payment must not trust buyer-supplied amount from `POST /api/transactions`.
- Payment should create a transaction from trusted `inventory.reserved` event
  data, because Store has already accepted the order and stock reservation.
- Existing public transaction creation can be kept only as a dev/admin endpoint,
  or deprecated once saga payment creation works.
- VNPAY Return URL is for user display only.
- VNPAY IPN is the source of truth for transaction status.
- Transaction update and payment outbox messages must be committed together.

### Cart Flow

Recommended option: add a new service folder:

```text
Services/Cart
```

Reason:

- Cart has a clear separate responsibility.
- It is ephemeral and Redis-backed, not owned by Order DB or Store DB.
- Order should receive a final `CreateOrderRequest`, not manage temporary cart
  state.

Recommended Redis model:

```text
cart:{userId}
  metadata: userId, storeId, updatedAtUtc, expiresAtUtc
  items:
    bagId
    storeId
    bagNameSnapshot
    unitPriceSnapshot
    quantity
    pickupStart
    pickupEnd
    expiryDate
    addedAtUtc
```

Implementation detail:

- Use `StackExchange.Redis` directly instead of only `IDistributedCache`,
  because cart item update/removal benefits from hash operations and atomic
  Lua/transaction support.
- TTL should be short. Suggested default: 24 hours, capped by bag expiry or
  pickup end when possible.
- Cart may cache bag snapshot on first add. Quantity +/- operations should not
  query Store every time. Checkout must revalidate through the existing
  Order/Store reservation saga.
- Enforce one store per cart for now, because `OrderProfile` has one `StoreId`.

## 4. Week 1 - 2026-09-03 To 2026-09-09

Goal: complete the Payment saga foundation from `inventory.reserved` to pending
VNPAY checkout.

Tasks:

- Add Payment consumer settings:
  - `InventoryReservedConsumerSettings`
  - queue: `payment.inventory-reserved`
  - binding key: `inventory.reserved`
- Add Payment `InventoryReservedConsumer` background service following the
  current Order/Store consumer style.
- Add `InventoryReservedEventHandler`.
- Handler behavior:
  - check `ProcessedMessage` by `MessageId + ConsumerName`;
  - reject duplicate handling safely;
  - if transaction for `OrderId` already exists in `Pending` or `Success`, mark
    message processed and return;
  - create `Transaction` with `Pending`, `PaymentMethod = VNPAY`, trusted amount
    from event;
  - call `IPaymentGateway.CreatePaymentAsync`;
  - save `GatewayRef`, `CheckoutUrl`, `ExpiresAt`;
  - save `ProcessedMessage` in the same commit.
- Add transaction lookup helpers:
  - get by `GatewayRef`;
  - get newest/current by `OrderId` deterministically.
- Decide one transaction rule:
  - recommended for this capstone: one active transaction per order.
- Add/adjust database constraints where needed.
- Add minimal manual test path:
  - create order;
  - Store reserves stock;
  - Payment creates pending transaction;
  - `GET /api/transactions/order/{orderId}` returns `CheckoutUrl`.

Exit criteria:

- Payment consumes `inventory.reserved`.
- One pending transaction is created per order.
- Checkout URL can be fetched by frontend.
- Duplicate `inventory.reserved` does not create duplicate transactions.
- `dotnet build` passes for Payment, Store, and Order.

## 5. Week 2 - 2026-09-10 To 2026-09-16

Goal: finish VNPAY IPN/return and publish payment result events.

Tasks:

- Add `VnPayController`:
  - `GET /api/payments/vnpay/ipn`
  - `GET /api/payments/vnpay/return`
- Add application service:
  - `IPaymentCallbackService`
  - `PaymentCallbackService`
- Add callback response DTO for VNPAY acknowledgement:
  - `RspCode`
  - `Message`
- Add `PaymentOutboxMessageFactory`.
- On valid successful IPN:
  - verify signature through `IPaymentGateway.VerifyIpnAsync`;
  - find transaction by `GatewayRef`;
  - verify amount;
  - if `Pending`, update to `Success`;
  - set `PaidAt`, gateway transaction number/status/response code;
  - add `payment.completed` outbox message;
  - commit transaction and outbox together.
- On valid failed IPN:
  - update `Pending` transaction to `Failed`;
  - set gateway response fields and failure reason;
  - add `payment.failed` outbox;
  - add `inventory.release_requested` outbox;
  - commit together.
- Add idempotency/status rules:
  - duplicate success IPN returns already confirmed acknowledgement;
  - duplicate failure IPN does not add duplicate outbox rows;
  - invalid signature does not mutate database;
  - amount mismatch does not mutate database.
- Update Order status handling:
  - map `payment.completed` to `Confirmed`;
  - map `payment.failed` to a final failed/cancelled status consistently.
  - recommended cleanup: use `Cancelled` with reason fields later, or keep
    `PaymentFailed` if frontend already expects it. Pick one and document it.

Exit criteria:

- VNPAY-style signed IPN can update a pending transaction.
- Payment writes `payment.completed` or `payment.failed`.
- Order consumes payment result and updates order.
- Replaying the same IPN is safe.
- Local fake IPN test cases are documented.

## 6. Week 3 - 2026-09-17 To 2026-09-23

Goal: implement compensation and Redis Cart MVP.

Payment/Store compensation tasks:

- Add Payment `PaymentExpirationProcessor`.
- Expiration behavior:
  - find `Pending` transactions where `ExpiresAt <= UtcNow`;
  - update status to `Expired`;
  - write `payment.failed`;
  - write `inventory.release_requested`.
- Add Store consumer for `inventory.release_requested`.
- Add Store handler:
  - check `ProcessedMessage`;
  - increase `QuantityRemaining` for each released item;
  - insert `ProcessedMessage`;
  - commit together.
- Add idempotency check so duplicate release does not increase stock twice.

Cart service MVP tasks:

- Create new `Cart` service using the existing Clean Architecture shape:
  - `StealDeal.Services.Cart.API`
  - `StealDeal.Services.Cart.Application`
  - `StealDeal.Services.Cart.Domain`
  - `StealDeal.Services.Cart.Infrastructure`
- Add Redis configuration:
  - `Redis:ConnectionString`
  - `Cart:DefaultTtlMinutes`
  - `Cart:MaxQuantityPerItem`
- Add infrastructure Redis adapter:
  - `ICartRepository`
  - `RedisCartRepository`
- Add cart application service:
  - `GetCartAsync`
  - `AddItemAsync`
  - `UpdateItemQuantityAsync`
  - `RemoveItemAsync`
  - `ClearCartAsync`
  - `BuildCheckoutRequestAsync`
- Add API endpoints:
  - `GET /api/cart`
  - `POST /api/cart/items`
  - `PATCH /api/cart/items/{bagId}`
  - `DELETE /api/cart/items/{bagId}`
  - `DELETE /api/cart`
  - optional `POST /api/cart/checkout`
- Store validation strategy:
  - on first add, call Store API or internal Store endpoint to validate bag and
    get snapshot;
  - on quantity changes, mutate Redis without repeated DB calls when the bag
    snapshot is still fresh;
  - on checkout, rely on Order/Store reservation as final validation.
- Enforce cart rules:
  - authenticated user only for MVP;
  - one store per cart;
  - positive quantity;
  - remove item when quantity becomes zero;
  - refresh TTL on successful mutation.

Exit criteria:

- Payment failure/expiration releases reserved stock.
- Redis cart supports add/update/remove/clear/get.
- Quantity changes do not hit Store DB repeatedly.
- Cart can produce a valid order checkout payload.

## 7. Week 4 - 2026-09-24 To 2026-10-02

Goal: finish end-to-end integration, hardening, and documentation.

End-to-end tasks:

- Connect Cart checkout to Order creation:
  - frontend may call `POST /api/orders` using payload from Cart; or
  - Cart API can call Order API if an internal HTTP pattern is accepted.
  - recommended for this month: frontend calls Order directly after reading
    checkout payload from Cart, keeping Cart simple.
- Add Notification events if time allows:
  - payment success;
  - payment failed;
  - order confirmed/cancelled.
- Clean status naming:
  - Order status should not drift between `InventoryReservationFailed`,
    `PaymentFailed`, and `Cancelled` without a clear frontend contract.
- Harden authorization:
  - buyer sees only own cart/orders/transactions;
  - admin can inspect payment/refund;
  - seller ownership checks remain a known follow-up unless quick to implement.
- Add focused tests/manual scripts:
  - duplicate `inventory.reserved`;
  - VNPAY success IPN;
  - VNPAY failed IPN;
  - payment expiration;
  - duplicate `inventory.release_requested`;
  - Redis cart add/update/remove;
  - cart checkout with stale stock.
- Fix OpenAPI package warning if compatible package update is available.
- Update docs:
  - `PAYMENT_SERVICE_GUIDE.md`
  - `ORDER_SAGA_FOUNDATION_PLAN.md`
  - create `Cart` README/plan after implementation starts.

Exit criteria:

- Happy path demo works:
  - add to cart;
  - create order;
  - reserve inventory;
  - create payment checkout;
  - fake/sandbox IPN success;
  - order becomes confirmed.
- Failure path demo works:
  - payment failed or expired;
  - order becomes failed/cancelled;
  - Store releases reserved stock.
- Redis cart behavior is stable and documented.
- All services build.

## 8. Suggested Task Order

1. Payment consumes `inventory.reserved`.
2. Payment creates trusted pending transaction and checkout URL.
3. VNPAY IPN updates transaction.
4. Payment publishes `payment.completed` / `payment.failed`.
5. Store consumes `inventory.release_requested`.
6. Payment expiration releases inventory.
7. Add Redis Cart service skeleton.
8. Add Redis cart APIs.
9. Connect cart checkout payload to Order create.
10. Run full local/manual demo flow and update docs.

## 9. Main Files Expected To Change

Payment:

- `Payment/StealDeal.Services.Payment.API/Program.cs`
- `Payment/StealDeal.Services.Payment.API/Controllers/VnPayController.cs`
- `Payment/StealDeal.Services.Payment.Application/Services/PaymentCallbackService.cs`
- `Payment/StealDeal.Services.Payment.Application/EventHandlers/InventoryReservedEventHandler.cs`
- `Payment/StealDeal.Services.Payment.Application/Events/PaymentOutboxMessageFactory.cs`
- `Payment/StealDeal.Services.Payment.Infrastructure/BackgroundServices/InventoryReservedConsumer.cs`
- `Payment/StealDeal.Services.Payment.Infrastructure/BackgroundServices/PaymentExpirationProcessor.cs`
- `Payment/StealDeal.Services.Payment.Infrastructure/Repositories/TransactionRepository.cs`
- `Payment/StealDeal.Services.Payment.Infrastructure/Persistence/ApplicationDbContext.cs`

Store:

- `Store/StealDeal.Services.Store.Infrastructure/BackgroundServices/*Release*Consumer.cs`
- `Store/StealDeal.Services.Store.Application/EventHandlers/*Release*Handler.cs`
- `Store/StealDeal.Services.Store.Infrastructure/Repositories/SurpriseBagRepository.cs`
- `Store/StealDeal.Services.Store.API/Program.cs`

Order:

- `Order/StealDeal.Services.Order.Application/EventHandlers/OrderStatusEventHandler.cs`
- `Order/StealDeal.Services.Order.Infrastructure/BackgroundServices/OrderStatusConsumer.cs`
- Optional status constants in Order Domain/Application.

Cart:

- New `Cart` folder under `Services`.
- New Redis configuration and API endpoints.

## 10. Risks And Mitigations

| Risk | Level | Mitigation |
|---|---|---|
| Payment keeps trusting frontend amount | High | Move real checkout creation to `inventory.reserved` handler. |
| Duplicate IPN/event creates duplicate side effects | High | Use status-aware handlers and `ProcessedMessage`. |
| Stock released twice | High | Store release handler must insert `ProcessedMessage` in same transaction as stock increment. |
| Cart stale price/quantity | Medium | Cache snapshot for UX, but final checkout uses Order/Store saga validation. |
| VNPAY sandbox callback needs public URL | Medium | Use ngrok/Cloudflare Tunnel for sandbox; use fake signed IPN for local tests. |
| Scope becomes too large | High | Finish one VNPAY gateway and Redis authenticated cart first. |

## 11. Open Questions

These do not block the first implementation steps, but should be decided before
Week 3:

1. Should cart support guest users, or authenticated users only for capstone MVP?
2. Should an order/cart allow items from multiple stores, or enforce one store
   per cart as the current Order model suggests?
3. Should Order final failure status be `Cancelled`, `PaymentFailed`, or
   `InventoryReservationFailed`? I recommend `Cancelled` plus reason fields
   later, but current code may keep explicit failed statuses for speed.
4. Will the frontend call Order directly after reading cart checkout payload, or
   should Cart call Order internally?
5. Do you already have VNPAY sandbox credentials and a public tunnel URL, or
   should local fake signed IPN be the first demo target?

