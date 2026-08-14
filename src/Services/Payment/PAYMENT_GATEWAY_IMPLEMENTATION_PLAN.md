# Payment Gateway Implementation Plan

## Goal

Implement Payment service as the next participant in the current order saga.

Current saga foundation:

```text
Order -> order.created
Store -> inventory.reserved or inventory.reservation_failed
Payment -> payment.completed or payment.failed
Store <- inventory.release_requested when payment fails/expires
Order <- payment.completed/payment.failed
```

Payment service responsibilities:

- Consume `inventory.reserved`.
- Create a local `Transaction` in `Pending` status.
- Build a VNPAY checkout URL.
- Let frontend redirect the user to VNPAY.
- Receive VNPAY IPN/callback.
- Verify gateway response.
- Update `Transaction`.
- Publish saga events through outbox.
- Request inventory release when payment fails or expires.

## Important Concepts

### Checkout URL

Payment service does not redirect the user directly from the RabbitMQ consumer.

The consumer runs in the background, so it cannot return an HTTP redirect. Instead:

```text
Payment consumer creates Transaction + CheckoutUrl
Frontend asks Payment API for transaction/checkout URL
Frontend redirects user to CheckoutUrl
```

### ReturnUrl vs IPN

`ReturnUrl` is where VNPAY redirects the user's browser after payment.

Use it only for user display, for example:

```text
Payment successful, waiting for confirmation...
Payment failed, please try again.
```

`IPN` is the server-to-server notification from VNPAY.

Use IPN as the source of truth for:

- Updating `Transaction.Status`.
- Publishing `payment.completed`.
- Publishing `payment.failed`.
- Publishing `inventory.release_requested`.

### Outbox

Payment must not publish saga events directly from the transaction update logic.

Write transaction changes and outbox rows in the same DB commit, then let `OutboxMessageProcessor` publish to RabbitMQ.

### Idempotency

VNPAY can send IPN more than once.

RabbitMQ can redeliver messages.

Every handler must be status-aware so repeated messages do not create duplicate side effects.

## Event Names

Payment consumes:

```text
inventory.reserved
```

Payment publishes:

```text
payment.completed
payment.failed
inventory.release_requested
```

## Target Transaction Statuses

Use string constants first, then convert to enum later if wanted.

```text
Pending
Success
Failed
Expired
RefundPending
Refunded
RefundFailed
```

Recommended meaning:

- `Pending`: checkout URL created, waiting for user/gateway result.
- `Success`: VNPAY confirms payment success.
- `Failed`: VNPAY confirms payment failure.
- `Expired`: user did not finish before `ExpiresAt`.
- `RefundPending`: money was captured but order/inventory was already cancelled/released.
- `Refunded`: refund processed successfully.
- `RefundFailed`: refund request failed.

## Target Refund Statuses

```text
Pending
Processed
Failed
```

Recommended meaning:

- `Pending`: refund needs to be sent to gateway or is waiting for result.
- `Processed`: gateway confirms refund success.
- `Failed`: gateway rejected refund or request failed after retries.

## Step 1 - Add Constants

Add domain/application constants so code does not compare raw strings everywhere.

Suggested files:

```text
Payment/StealDeal.Services.Payment.Domain/Constants/PaymentMethods.cs
Payment/StealDeal.Services.Payment.Domain/Constants/TransactionStatuses.cs
Payment/StealDeal.Services.Payment.Domain/Constants/RefundStatuses.cs
Payment/StealDeal.Services.Payment.Application/DTOs/Events/PaymentEventTypes.cs
```

Suggested values:

```csharp
public static class PaymentMethods
{
    public const string VnPay = "VNPAY";
}
```

```csharp
public static class TransactionStatuses
{
    public const string Pending = "Pending";
    public const string Success = "Success";
    public const string Failed = "Failed";
    public const string Expired = "Expired";
    public const string RefundPending = "RefundPending";
    public const string Refunded = "Refunded";
    public const string RefundFailed = "RefundFailed";
}
```

## Step 2 - Add Event DTOs

Create local DTOs in Payment service. Do not add a shared contracts project for this milestone.

Suggested folder:

```text
Payment/StealDeal.Services.Payment.Application/DTOs/Events
```

Add:

```text
InventoryReservedEvent.cs
PaymentCompletedEvent.cs
PaymentFailedEvent.cs
InventoryReleaseRequestedEvent.cs
```

`InventoryReservedEvent`:

```json
{
  "messageId": "guid",
  "occurredAtUtc": "2026-08-04T00:00:00Z",
  "orderId": "guid",
  "userId": "guid",
  "storeId": "guid",
  "totalAmount": 50000,
  "items": [
    {
      "surpriseBagId": "guid",
      "quantity": 1
    }
  ]
}
```

`PaymentCompletedEvent`:

```json
{
  "messageId": "guid",
  "occurredAtUtc": "2026-08-04T00:00:00Z",
  "orderId": "guid",
  "paymentId": "guid",
  "amount": 50000,
  "paymentMethod": "VNPAY",
  "gatewayRef": "txn-ref"
}
```

`PaymentFailedEvent`:

```json
{
  "messageId": "guid",
  "occurredAtUtc": "2026-08-04T00:00:00Z",
  "orderId": "guid",
  "paymentId": "guid",
  "reasonCode": "GatewayDeclined",
  "reason": "Payment provider declined the transaction."
}
```

`InventoryReleaseRequestedEvent`:

```json
{
  "messageId": "guid",
  "occurredAtUtc": "2026-08-04T00:00:00Z",
  "orderId": "guid",
  "storeId": "guid",
  "reasonCode": "PaymentFailed",
  "reason": "Payment failed, release reserved inventory.",
  "items": [
    {
      "surpriseBagId": "guid",
      "quantity": 1
    }
  ]
}
```

## Step 3 - Add VNPAY Settings

Add settings class:

```text
Payment/StealDeal.Services.Payment.Infrastructure/Configuration/VnPaySettings.cs
```

Suggested fields:

```csharp
public string TmnCode { get; set; } = null!;
public string HashSecret { get; set; } = null!;
public string PaymentUrl { get; set; } = null!;
public string ReturnUrl { get; set; } = null!;
public string IpNUrl { get; set; } = null!;
public string Version { get; set; } = "2.1.0";
public string Command { get; set; } = "pay";
public string CurrCode { get; set; } = "VND";
public string Locale { get; set; } = "vn";
public int ExpireMinutes { get; set; } = 15;
```

Add config in `appsettings.json`:

```json
"VnPay": {
  "TmnCode": "YOUR_TMN_CODE",
  "HashSecret": "YOUR_HASH_SECRET",
  "PaymentUrl": "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
  "ReturnUrl": "https://localhost:PORT/api/vnpay/return",
  "IpNUrl": "https://PUBLIC_URL/api/vnpay/ipn",
  "Version": "2.1.0",
  "Command": "pay",
  "CurrCode": "VND",
  "Locale": "vn",
  "ExpireMinutes": 15
}
```

For local gateway sandbox testing, `IpNUrl` needs a public HTTPS URL such as ngrok or Cloudflare Tunnel.

For local business logic testing, use curl/Postman directly against localhost.

## Step 4 - Add Payment Gateway Abstraction

Create application interfaces:

```text
Payment/StealDeal.Services.Payment.Application/Gateways/IPaymentGateway.cs
Payment/StealDeal.Services.Payment.Application/Gateways/IPaymentGatewayFactory.cs
```

Suggested shape:

```csharp
public interface IPaymentGateway
{
    string Method { get; }
    Task<CreatePaymentResult> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default);
    Task<PaymentCallbackResult> VerifyIpnAsync(IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default);
}
```

The gateway adapter should handle gateway-specific details only:

- Build VNPAY URL.
- Sort parameters.
- Sign request.
- Verify response signature.
- Map gateway codes into normalized result.

It should not update database or publish events.

## Step 5 - Implement VNPAY Adapter

Suggested file:

```text
Payment/StealDeal.Services.Payment.Infrastructure/Gateways/VnPayGateway.cs
```

Responsibilities:

- Build payment parameters.
- Convert amount to VNPAY format: `Amount * 100`.
- Generate `vnp_TxnRef`.
- Generate `vnp_SecureHash`.
- Return `CheckoutUrl`, `GatewayRef`, `ExpiresAt`.
- Verify IPN secure hash.
- Parse:
  - `vnp_TxnRef`
  - `vnp_Amount`
  - `vnp_ResponseCode`
  - `vnp_TransactionStatus`
  - `vnp_TransactionNo`
  - `vnp_PayDate`

Implementation notes:

- Use URL encoding consistently.
- Exclude `vnp_SecureHash` and `vnp_SecureHashType` before recalculating hash.
- Sort parameters alphabetically by key before signing.
- Do not trust success codes until signature and amount are verified.

## Step 6 - Consume inventory.reserved

Add consumer settings:

```text
Payment/StealDeal.Services.Payment.Infrastructure/Configuration/InventoryReservedConsumerSettings.cs
```

Suggested values:

```csharp
public string ExchangeName { get; set; } = "stealdeal.events";
public string ExchangeType { get; set; } = "topic";
public string QueueName { get; set; } = "payment.inventory-reserved";
public string BindingKey { get; set; } = "inventory.reserved";
public ushort PrefetchCount { get; set; } = 10;
```

Add hosted service:

```text
Payment/StealDeal.Services.Payment.Infrastructure/BackgroundServices/InventoryReservedConsumer.cs
```

Copy the consumer style from Store's `CreatedOrderConsumer`.

Consumer responsibilities:

- Declare exchange.
- Declare queue.
- Bind `inventory.reserved`.
- Deserialize payload.
- Create `IntegrationEventContext`.
- Dispatch to `IIntegrationEventHandler<InventoryReservedEvent>`.
- Ack only after handler succeeds.

## Step 7 - Handle inventory.reserved

Add handler:

```text
Payment/StealDeal.Services.Payment.Application/EventHandlers/InventoryReservedEventHandler.cs
```

Handler flow:

```text
if ProcessedMessage exists:
    return

if Transaction exists for OrderId and status is Pending/Success:
    add ProcessedMessage
    return

create Transaction:
    OrderId = event.OrderId
    UserId = event.UserId
    Amount = event.TotalAmount
    PaymentMethod = VNPAY
    Status = Pending

call VnPayGateway.CreatePaymentAsync

update Transaction:
    GatewayRef
    CheckoutUrl
    ExpiresAt

add ProcessedMessage
save changes
```

Important:

- Do not publish `payment.completed` here.
- Do not publish `payment.failed` here unless gateway init itself fails permanently.
- If gateway init fails temporarily, let consumer retry or log clearly.

## Step 8 - Expose Checkout API

Frontend needs a way to get the checkout URL after Store reserved inventory.

Suggested endpoint:

```text
GET /api/transactions/order/{orderId}
```

Existing endpoint already gets transaction by order ID.

Make sure response includes:

```text
CheckoutUrl
Status
ExpiresAt
PaymentMethod
```

If current `TransactionResponse` does not include these, update it.

Alternative endpoint:

```text
GET /api/payments/checkout/{orderId}
```

Return only checkout information.

## Step 9 - Add VNPAY IPN Endpoint

Suggested controller:

```text
Payment/StealDeal.Services.Payment.API/Controllers/VnPayController.cs
```

Suggested endpoints:

```text
GET /api/vnpay/ipn
GET /api/vnpay/return
```

IPN endpoint flow:

```text
read query params
call application service HandleVnPayIpnAsync
return JSON { RspCode, Message }
```

Return endpoint flow:

```text
read query params
verify signature if possible
return simple response or redirect frontend URL
do not publish saga events here
```

## Step 10 - Handle VNPAY IPN

Add service:

```text
Payment/StealDeal.Services.Payment.Application/Services/Interfaces/IPaymentCallbackService.cs
Payment/StealDeal.Services.Payment.Application/Services/PaymentCallbackService.cs
```

Handler flow:

```text
verify signature
if invalid:
    return RspCode 97

find Transaction by GatewayRef/vnp_TxnRef
if not found:
    return RspCode 01

verify amount
if mismatch:
    return RspCode 04

if transaction already Success:
    return RspCode 02

if transaction already Failed/Expired/RefundPending/Refunded:
    handle late success/failure based on gateway status

if gateway status success:
    mark transaction Success
    add payment.completed outbox
else:
    mark transaction Failed
    add payment.failed outbox
    add inventory.release_requested outbox

save changes
return RspCode 00
```

Suggested VNPAY response codes from Payment service:

```text
00: Confirm success
01: Order not found
02: Order already confirmed
04: Invalid amount
97: Invalid signature
99: Unknown error
```

## Step 11 - Add Outbox Event Builders

Keep event creation in one place.

Suggested file:

```text
Payment/StealDeal.Services.Payment.Application/Events/PaymentOutboxMessageFactory.cs
```

Factory methods:

```text
CreatePaymentCompleted(Transaction transaction)
CreatePaymentFailed(Transaction transaction, reasonCode, reason)
CreateInventoryReleaseRequested(Transaction transaction, items, reasonCode, reason)
```

All outbox rows should use:

```text
ExchangeName = "stealdeal.events"
ExchangeType = "topic"
Status = "Pending"
RoutingKey = event name
EventType = event name
```

## Step 12 - Store Compensation Consumer

Store service must consume:

```text
inventory.release_requested
```

Add a Store consumer and handler later.

Handler should:

```text
if ProcessedMessage exists:
    return

for each item:
    increase QuantityRemaining

add ProcessedMessage
save changes
```

This must be idempotent. If a duplicate release event arrives, stock must not be added twice.

Best option:

- Track processed release message with `ProcessedMessage`.
- Only update stock inside the same transaction that inserts processed message.

## Step 13 - Add Expiration Worker

Payment can fail to receive IPN if user abandons payment.

Add a hosted worker:

```text
Payment/StealDeal.Services.Payment.Infrastructure/BackgroundServices/PaymentExpirationProcessor.cs
```

Flow:

```text
find Pending transactions where ExpiresAt <= UtcNow
mark Expired
add payment.failed outbox
add inventory.release_requested outbox
save changes
```

This prevents reserved stock from being locked forever.

## Step 14 - Late Success After Compensation

Case:

```text
Payment expired
Payment published inventory.release_requested
Store released stock
Later VNPAY sends success IPN
```

Business decision:

```text
Do not confirm the order.
Create refund.
Mark transaction RefundPending.
Call VNPAY refund API.
```

Implementation approach:

```text
if IPN success and transaction status is Expired/Failed:
    transaction.Status = RefundPending
    create Refund Pending
    do not publish payment.completed
    do not reserve stock again
```

Refund API implementation can be a later step.

## Step 15 - Edge Cases Checklist

Handle these before considering gateway flow done:

- Duplicate `inventory.reserved` event does not create duplicate transaction.
- Duplicate successful IPN does not publish duplicate `payment.completed`.
- Duplicate failed IPN does not publish duplicate `payment.failed` or duplicate `inventory.release_requested`.
- Invalid VNPAY signature does not update database.
- Amount mismatch does not update transaction as success.
- Transaction not found returns proper IPN response.
- Pending transaction expires and releases inventory.
- Success IPN after expiration creates refund path, not order confirmation.
- Success IPN after order was cancelled creates refund path.
- RabbitMQ publish failure keeps outbox message retryable.
- Gateway init failure is logged clearly.

## Step 16 - Local Testing

### Test with fake IPN by curl

This is enough for local business logic testing.

Recommended:

- Create helper method/test tool that generates valid VNPAY-style secure hash.
- Send curl to localhost IPN endpoint.
- Verify transaction status.
- Verify outbox rows.

### Test with VNPAY sandbox

Use this when the local flow is stable.

Requirements:

- Public HTTPS URL for IPN, for example ngrok or Cloudflare Tunnel.
- `VnPay:IpNUrl` points to public tunnel URL.
- `VnPay:ReturnUrl` points to frontend or API return endpoint.

## Implementation Order

Recommended coding sequence:

1. Add constants.
2. Add event DTOs.
3. Add VNPAY settings.
4. Add gateway abstraction.
5. Implement VNPAY URL builder/signature verifier.
6. Add `InventoryReservedConsumerSettings`.
7. Add `InventoryReservedConsumer`.
8. Add `InventoryReservedEventHandler`.
9. Update `TransactionResponse` to include checkout fields.
10. Add VNPAY IPN/return controller.
11. Add callback service.
12. Add outbox event factory.
13. Add expiration worker.
14. Add Store compensation consumer.
15. Add migration for Payment DB.
16. Test with curl.
17. Test with VNPAY sandbox through ngrok/Cloudflare Tunnel.

## Definition of Done

- Payment creates one pending transaction after `inventory.reserved`.
- Transaction contains checkout URL.
- User can be redirected to VNPAY.
- IPN success updates transaction to `Success`.
- IPN success writes `payment.completed` outbox.
- IPN failure updates transaction to `Failed`.
- IPN failure writes `payment.failed` and `inventory.release_requested` outbox rows.
- Expired payment releases inventory.
- Duplicate messages/IPNs are safe.
- `dotnet build` passes.
