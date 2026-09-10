# Payment Gateway Current Context

Use this file to quickly restore context before continuing Payment gateway implementation.

## Current Direction

Payment service participates in the existing choreography saga:

```text
Order -> order.created
Store -> inventory.reserved or inventory.reservation_failed
Payment -> payment.completed or payment.failed
Payment -> inventory.release_requested when payment fails/expires
Order <- payment.completed/payment.failed
Store <- inventory.release_requested
```

Important event name correction:

```text
Payment consumes inventory.reserved
```

The project does not use `reserved.success`.

## Accepted Trade-Off

For now, Payment stores inventory compensation data directly on `Transaction`:

```csharp
public Guid? StoreId { get; set; }
public string? ReservedItemsJson { get; set; }
```

This is accepted for simplicity.

Reason:

- Store currently decreases `QuantityRemaining` directly.
- Store does not yet have an `InventoryReservation` table or `ReservationId`.
- Payment needs enough data to publish `inventory.release_requested` if payment fails.

Trade-off:

- Payment is coupled to the `inventory.reserved` event shape.
- Payment stores a saga snapshot of reserved store/items.
- This is acceptable for the current milestone.

Cleaner future design:

```text
Store creates InventoryReservation
Store publishes reservationId
Payment stores reservationId only
Payment failed -> inventory.release_requested { reservationId }
Store releases reservation by reservationId
```

## Implemented So Far

### Basic Messaging Foundation

Payment now mirrors Order/Store messaging foundation:

- `OutboxMessage`
- `ProcessedMessage`
- `IOutboxMessageRepository`
- `IProcessedMessageRepository`
- `IIntegrationEventHandler`
- `IntegrationEventContext`
- `IMessagePublisher`
- `RabbitMqMessagePublisher`
- `OutboxMessageProcessor`
- `RabbitMqSettings`
- `OutboxSettings`

Outbox messages are stored first, then `OutboxMessageProcessor` publishes them to RabbitMQ.

Processed messages are used for consumer idempotency.

## Step 1 - Constants

Added constants under:

```text
Payment/StealDeal.Services.Payment.Domain/Constants
```

Files:

- `PaymentMethods.cs`
- `TransactionStatuses.cs`
- `RefundStatuses.cs`

Main values:

```text
PaymentMethods.VnPay = "VNPAY"

TransactionStatuses:
Pending
Success
Failed
Expired
RefundPending
Refunded
RefundFailed

RefundStatuses:
Pending
Processed
Failed
```

Added event name constants:

```text
Payment/StealDeal.Services.Payment.Application/DTOs/Events/PaymentEventTypes.cs
```

Values:

```text
inventory.reserved
payment.completed
payment.failed
inventory.release_requested
```

## Step 2 - Event DTOs

Added local event DTOs under:

```text
Payment/StealDeal.Services.Payment.Application/DTOs/Events
```

Files:

- `InventoryReservedEvent.cs`
- `PaymentCompletedEvent.cs`
- `PaymentFailedEvent.cs`
- `InventoryReleaseRequestedEvent.cs`

`InventoryReservedEvent` mirrors Store's published event:

```csharp
public Guid MessageId { get; set; }
public DateTime OccurredAtUtc { get; set; }
public Guid OrderId { get; set; }
public Guid UserId { get; set; }
public Guid StoreId { get; set; }
public decimal TotalAmount { get; set; }
public List<InventoryReservedItemDto> Items { get; set; } = new();
```

`PaymentCompletedEvent` is for Order:

```csharp
public Guid MessageId { get; set; }
public DateTime OccurredAtUtc { get; set; }
public Guid OrderId { get; set; }
public Guid PaymentId { get; set; }
public decimal Amount { get; set; }
public string PaymentMethod { get; set; } = null!;
public string? GatewayRef { get; set; }
```

`PaymentFailedEvent` is for Order:

```csharp
public Guid MessageId { get; set; }
public DateTime OccurredAtUtc { get; set; }
public Guid OrderId { get; set; }
public Guid PaymentId { get; set; }
public string ReasonCode { get; set; } = null!;
public string Reason { get; set; } = null!;
```

`InventoryReleaseRequestedEvent` is for Store compensation:

```csharp
public Guid MessageId { get; set; }
public DateTime OccurredAtUtc { get; set; }
public Guid OrderId { get; set; }
public Guid StoreId { get; set; }
public string ReasonCode { get; set; } = null!;
public string Reason { get; set; } = null!;
public List<InventoryReleaseRequestedItemDto> Items { get; set; } = new();
```

## Model Updates

`Transaction` now includes gateway fields:

```csharp
public Guid? StoreId { get; set; }
public string? ReservedItemsJson { get; set; }
public string? CheckoutUrl { get; set; }
public string? GatewayTransactionNo { get; set; }
public string? GatewayResponseCode { get; set; }
public string? GatewayTransactionStatus { get; set; }
public DateTime? ExpiresAt { get; set; }
```

`Refund` now includes gateway fields:

```csharp
public string? GatewayRefundRef { get; set; }
public string? GatewayResponseCode { get; set; }
public string? FailureReason { get; set; }
public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
```

`ApplicationDbContext` was updated with:

- `OutboxMessages`
- `ProcessedMessages`
- gateway field mapping
- index on `GatewayRef`
- index on `GatewayRefundRef`
- index on `(MessageId, ConsumerName)` for processed messages

Migration may be needed after these model changes.

## CRUD/Mapping Updates

`TransactionResponse` now returns gateway/checkout fields:

```csharp
StoreId
GatewayRef
CheckoutUrl
GatewayTransactionNo
GatewayResponseCode
GatewayTransactionStatus
ExpiresAt
```

`RefundResponse` now returns:

```csharp
GatewayRefundRef
GatewayResponseCode
FailureReason
UpdatedAt
```

`TransactionMapping` and `RefundMapping` were updated to map these fields.

`TransactionService.UpdateTransactionStatusAsync` and `RefundService.UpdateRefundStatusAsync` were updated so optional gateway fields are only changed when present in the request.

## Step 3 - VNPAY Settings

Added:

```text
Payment/StealDeal.Services.Payment.Infrastructure/Configuration/VnPaySettings.cs
```

Fields:

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

`Program.cs` binds:

```csharp
builder.Services.Configure<VnPaySettings>(builder.Configuration.GetSection("VnPay"));
```

`appsettings.json` includes sandbox placeholders:

```json
"VnPay": {
  "TmnCode": "YOUR_VNPAY_TMN_CODE",
  "HashSecret": "YOUR_VNPAY_HASH_SECRET",
  "PaymentUrl": "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
  "ReturnUrl": "https://localhost:7080/api/vnpay/return",
  "IpNUrl": "https://localhost:7080/api/vnpay/ipn",
  "Version": "2.1.0",
  "Command": "pay",
  "CurrCode": "VND",
  "Locale": "vn",
  "ExpireMinutes": 15
}
```

For local curl testing, `TmnCode` and `HashSecret` can be local fake values if the test tool signs with the same `HashSecret`.

For real VNPAY sandbox testing, use VNPAY-provided `TmnCode` and `HashSecret`, and expose IPN through a public HTTPS tunnel like ngrok.

## Step 4 - Gateway Abstraction

Gateway abstractions live in:

```text
Payment/StealDeal.Services.Payment.Application/Gateways
```

Files:

- `IPaymentGateway.cs`
- `IPaymentGatewayFactory.cs`

Gateway DTOs live in:

```text
Payment/StealDeal.Services.Payment.Application/DTOs/Gateways
```

Files:

- `CreatePaymentRequest.cs`
- `CreatePaymentResult.cs`
- `PaymentCallbackResult.cs`
- `VnPayIpnHandleResult.cs`

Purpose:

- Application code depends on generic gateway contracts.
- Infrastructure handles VNPAY-specific parameters and checksum logic.
- Future gateways can implement `IPaymentGateway` without changing saga logic heavily.

## Step 5 - VNPAY Gateway Adapter

Implemented:

```text
Payment/StealDeal.Services.Payment.Infrastructure/Gateways/VnPayGateway.cs
Payment/StealDeal.Services.Payment.Infrastructure/Gateways/PaymentGatewayFactory.cs
```

`CreatePaymentAsync`:

- validates request and settings
- converts UTC time to Vietnam time
- formats dates as `yyyyMMddHHmmss`
- maps amount to VNPAY amount by multiplying by 100
- uses `TransactionId.ToString("N")` as `vnp_TxnRef`
- builds sorted query string
- signs with HMAC-SHA512 using `HashSecret`
- returns `GatewayRef`, `CheckoutUrl`, `ExpiresAtUtc`

`VerifyIpnAsync`:

- reads VNPAY query parameters
- removes `vnp_SecureHash` and `vnp_SecureHashType`
- rebuilds signed data
- verifies HMAC-SHA512
- parses:
  - `vnp_TxnRef`
  - `vnp_Amount`
  - `vnp_TransactionNo`
  - `vnp_ResponseCode`
  - `vnp_TransactionStatus`
  - `vnp_PayDate`
- treats payment as success only when:

```text
signature valid
vnp_ResponseCode == "00"
vnp_TransactionStatus == "00"
```

Registered in `Program.cs`:

```csharp
builder.Services.AddSingleton<IPaymentGateway, VnPayGateway>();
builder.Services.AddSingleton<IPaymentGatewayFactory, PaymentGatewayFactory>();
```

## Step 6 - inventory.reserved Consumer

Added:

```text
Payment/StealDeal.Services.Payment.Infrastructure/Configuration/InventoryReservedConsumerSettings.cs
Payment/StealDeal.Services.Payment.Infrastructure/BackgroundServices/InventoryReservedConsumer.cs
```

Config:

```json
"InventoryReservedConsumer": {
  "ExchangeName": "stealdeal.events",
  "ExchangeType": "topic",
  "QueueName": "payment.inventory-reserved",
  "BindingKey": "inventory.reserved",
  "PrefetchCount": 10
}
```

Consumer responsibilities:

- connect RabbitMQ
- declare exchange
- declare queue
- bind queue to `inventory.reserved`
- deserialize payload to `InventoryReservedEvent`
- create `IntegrationEventContext`
- dispatch to `IIntegrationEventHandler<InventoryReservedEvent>`
- ack after handler success
- nack without requeue on failure

Registered in `Program.cs`:

```csharp
builder.Services.Configure<InventoryReservedConsumerSettings>(
    builder.Configuration.GetSection("InventoryReservedConsumer"));

builder.Services.AddHostedService<InventoryReservedConsumer>();
```

## Step 7 - inventory.reserved Handler

Added:

```text
Payment/StealDeal.Services.Payment.Application/EventHandlers/InventoryReservedEventHandler.cs
```

Handler flow:

```text
if ProcessedMessage exists:
    return

find transaction by OrderId
if existing Pending/Success:
    add ProcessedMessage
    save
    return

create Transaction Pending
copy StoreId from event
serialize reserved event items to ReservedItemsJson
call VNPAY gateway CreatePaymentAsync
save GatewayRef, CheckoutUrl, ExpiresAt
add Transaction
add ProcessedMessage
save
```

Important:

- This step does not publish `payment.completed`.
- This step does not publish `payment.failed`.
- `inventory.reserved` means stock was reserved and checkout can be prepared, not payment completed.

Registered in `Program.cs`:

```csharp
builder.Services.AddScoped<
    IIntegrationEventHandler<InventoryReservedEvent>,
    InventoryReservedEventHandler>();
```

## Step 8 - Checkout URL API

No new endpoint was needed.

Existing endpoint:

```text
GET /api/transactions/order/{orderId}
```

Controller:

```text
Payment/StealDeal.Services.Payment.API/Controllers/TransactionController.cs
```

Response already includes:

```text
CheckoutUrl
Status
ExpiresAt
PaymentMethod
GatewayRef
```

Frontend expected behavior:

```text
POST order
go to checkout loading page
poll GET /api/transactions/order/{orderId}
if transaction exists and CheckoutUrl is present -> redirect user to VNPAY
if order status becomes inventory failed -> show out of stock
```

## Step 9 - VNPAY IPN and Return Endpoints

Added:

```text
Payment/StealDeal.Services.Payment.API/Controllers/VnPayController.cs
```

Endpoints:

```text
GET /api/vnpay/ipn
GET /api/vnpay/return
```

Both are anonymous because VNPAY/browser callbacks do not use JWT.

`/api/vnpay/ipn`:

- reads query params
- calls `IPaymentCallbackService.HandleVnPayIpnAsync`
- returns JSON with exact VNPAY casing:

```json
{
  "RspCode": "00",
  "Message": "Confirm success"
}
```

Possible response codes currently used:

```text
00: Confirm success
01: Order not found
02: Order already confirmed
04: Invalid amount
97: Invalid signature
```

`/api/vnpay/return`:

- reads query params
- verifies through VNPAY gateway
- returns parsed callback result for local/manual testing
- does not update transaction or publish saga events

Return URL is for browser UX only.

IPN is the source of truth.

## Remaining Work From Original Plan

Use this section as the live checklist for future sessions.

### Step 10 - Handle VNPAY IPN

Status:

```text
Implemented, but should be reviewed before moving on.
```

Implemented files:

```text
Payment/StealDeal.Services.Payment.Application/Services/Interfaces/IPaymentCallbackService.cs
Payment/StealDeal.Services.Payment.Application/Services/PaymentCallbackService.cs
Payment/StealDeal.Services.Payment.Application/DTOs/Gateways/VnPayIpnHandleResult.cs
```

`PaymentCallbackService` currently:

- verifies IPN
- finds transaction by `GatewayRef`
- checks amount
- updates transaction success/failure
- writes outbox `payment.completed`
- writes outbox `payment.failed`
- writes outbox `inventory.release_requested`
- creates `Refund Pending` for late success after failed/expired

Supporting repository method added:

```csharp
Task<Transaction?> GetByGatewayRefAsync(string gatewayRef);
```

Current IPN handler flow:

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

if gateway status success:
    if transaction already Failed/Expired:
        mark RefundPending
        create Refund Pending
    else:
        mark Success
        add payment.completed outbox

if gateway status failed:
    if transaction already Failed/Expired/RefundPending/Refunded:
        return success response to stop VNPAY retry
    else:
        mark Failed
        add payment.failed outbox
        add inventory.release_requested outbox

save changes
return RspCode 00
```

Review points before continuing:

- Confirm whether returning `RspCode = 02` for duplicate successful IPN is desired.
- Confirm late success behavior: current decision is `RefundPending`, not order confirmation.
- Confirm whether failed IPN after `RefundPending` should be ignored as currently implemented.
- Consider moving event factory methods out of this service in Step 11.

### Step 11 - Add Outbox Event Builders

Status:

```text
Not implemented as separate factory.
```

Current state:

- Outbox event creation is inside `PaymentCallbackService`.
- It works, but the service is carrying both business decisions and event construction.

Recommended next improvement:

```text
Payment/StealDeal.Services.Payment.Application/Events/PaymentOutboxMessageFactory.cs
```

Factory methods:

```csharp
OutboxMessage CreatePaymentCompleted(Transaction transaction);
OutboxMessage CreatePaymentFailed(Transaction transaction, string reasonCode, string reason);
OutboxMessage CreateInventoryReleaseRequested(
    Transaction transaction,
    IEnumerable<InventoryReleaseRequestedItemDto> items,
    string reasonCode,
    string reason);
```

Purpose:

- Keep `PaymentCallbackService` focused on state transition rules.
- Keep event payload/routing-key construction in one place.
- Reuse the same factory later from expiration worker.

All outbox rows should use:

```text
ExchangeName = "stealdeal.events"
ExchangeType = "topic"
Status = "Pending"
RoutingKey = event name
EventType = event name
```

### Step 12 - Store Compensation Consumer

Status:

```text
Not implemented.
```

Store service must consume:

```text
inventory.release_requested
```

Suggested files:

```text
Store/StealDeal.Services.Store.Application/DTOs/Events/InventoryReleaseRequestedEvent.cs
Store/StealDeal.Services.Store.Application/EventHandlers/InventoryReleaseRequestedEventHandler.cs
Store/StealDeal.Services.Store.Infrastructure/Configuration/InventoryReleaseRequestedConsumerSettings.cs
Store/StealDeal.Services.Store.Infrastructure/BackgroundServices/InventoryReleaseRequestedConsumer.cs
```

Handler flow:

```text
if ProcessedMessage exists:
    return

for each item:
    increase QuantityRemaining

add ProcessedMessage
save changes
```

Important:

- Must be idempotent.
- Duplicate `inventory.release_requested` must not add stock twice.
- Use `ProcessedMessage` in the same DB save as the stock update.

Current trade-off:

- Payment publishes `StoreId` and item list from `Transaction.ReservedItemsJson`.
- Store does not yet use a reservation table.

### Step 13 - Add Expiration Worker

Status:

```text
Not implemented.
```

Why:

- User can abandon VNPAY checkout.
- IPN may never arrive.
- Reserved stock should not stay locked forever.

Suggested file:

```text
Payment/StealDeal.Services.Payment.Infrastructure/BackgroundServices/PaymentExpirationProcessor.cs
```

Required repository support:

```csharp
Task<List<Transaction>> GetExpiredPendingBatchAsync(DateTime nowUtc, int batchSize);
```

Flow:

```text
find Pending transactions where ExpiresAt <= UtcNow
mark transaction Expired
add payment.failed outbox
add inventory.release_requested outbox
save changes
```

Recommended settings:

```text
PaymentExpiration:
  BatchSize
  PollingIntervalSeconds
```

Implementation note:

- If Step 11 factory exists, reuse it here.
- ReasonCode can be `PaymentExpired`.
- Reason can be `Payment expired before gateway confirmation.`

### Step 14 - Late Success After Compensation

Status:

```text
Partially implemented in Step 10.
```

Current business decision:

```text
Do not confirm the order.
Create refund.
Mark transaction RefundPending.
```

Current implemented behavior:

```text
if VNPAY IPN success arrives for Failed/Expired transaction:
    transaction.Status = RefundPending
    create Refund Pending
    do not publish payment.completed
```

Still not implemented:

- Actual VNPAY refund API call.
- Refund query/status update.
- Retry strategy for failed refund.
- Mapping VNPAY refund response to `Refund.Status`.

Potential future files:

```text
Payment/StealDeal.Services.Payment.Application/Gateways/IPaymentRefundGateway.cs
Payment/StealDeal.Services.Payment.Application/Services/RefundProcessingService.cs
Payment/StealDeal.Services.Payment.Infrastructure/Gateways/VnPayRefundGateway.cs
```

### Step 15 - Edge Cases Checklist

Status:

```text
Partially covered, not fully tested.
```

Covered by current implementation:

- Duplicate `inventory.reserved` with same `MessageId` returns early through `ProcessedMessage`.
- Duplicate `inventory.reserved` with different `MessageId` but same active order does not create another active transaction.
- Invalid VNPAY signature returns `RspCode = 97`.
- Missing transaction returns `RspCode = 01`.
- Amount mismatch returns `RspCode = 04`.
- Duplicate successful IPN for already-success transaction returns `RspCode = 02`.
- Success IPN after failed/expired transaction creates refund path instead of confirming order.

Still needs test/confirmation:

- Duplicate failed IPN does not publish duplicate `payment.failed`.
- Duplicate failed IPN does not publish duplicate `inventory.release_requested`.
- Outbox publisher retries failed RabbitMQ publish.
- Store compensation is idempotent after Step 12.
- Pending transaction expiration works after Step 13.
- Return endpoint does not update transaction.
- VNPAY curl helper signs params the same way `VerifyIpnAsync` expects.

### Step 16 - Local Testing

Status:

```text
Not implemented as scripts/tools.
```

Recommended local curl test approach:

1. Start Payment API locally.
2. Create or seed a `Transaction` with:
   - `Status = Pending`
   - `GatewayRef = some vnp_TxnRef`
   - matching `Amount`
   - `StoreId`
   - `ReservedItemsJson`
3. Generate VNPAY-style query params.
4. Sign params using the same `HashSecret`.
5. Curl:

```text
GET https://localhost:7080/api/vnpay/ipn?...&vnp_SecureHash=...
```

Recommended helper:

```text
Payment/tools/generate-vnpay-ipn.ps1
```

or a small test-only endpoint/script.

Scenarios to test:

- valid success IPN
- valid failed IPN
- invalid signature
- amount mismatch
- duplicate success IPN
- late success after expired/failed

### Step 17 - VNPAY Sandbox Through Public Tunnel

Status:

```text
Not tested.
```

Use after local curl tests pass.

Requirements:

- Real VNPAY sandbox `TmnCode`.
- Real VNPAY sandbox `HashSecret`.
- Public HTTPS tunnel for Payment API, for example ngrok or Cloudflare Tunnel.

Config shape:

```json
"VnPay": {
  "TmnCode": "VNPAY_SANDBOX_TMN_CODE",
  "HashSecret": "VNPAY_SANDBOX_HASH_SECRET",
  "PaymentUrl": "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
  "ReturnUrl": "https://your-public-url/api/vnpay/return",
  "IpNUrl": "https://your-public-url/api/vnpay/ipn",
  "Version": "2.1.0",
  "Command": "pay",
  "CurrCode": "VND",
  "Locale": "vn",
  "ExpireMinutes": 15
}
```

Note:

- VNPAY cannot call `localhost` for IPN.
- `ReturnUrl` is browser-facing.
- `IpNUrl` is server-to-server and should be treated as source of truth.

### Database Migration

Status:

```text
Needed before full runtime testing.
```

Because model and DbContext changed, Payment DB needs a new migration for:

- `Transaction.StoreId`
- `Transaction.ReservedItemsJson`
- gateway fields
- refund gateway fields
- `OutboxMessages`
- `ProcessedMessages`

Suggested command from service root:

```text
dotnet ef migrations add AddPaymentGatewaySagaFields --project .\Payment\StealDeal.Services.Payment.Infrastructure --startup-project .\Payment\StealDeal.Services.Payment.API
```

Then:

```text
dotnet ef database update --project .\Payment\StealDeal.Services.Payment.Infrastructure --startup-project .\Payment\StealDeal.Services.Payment.API
```

## Current Verification

Last known build command:

```text
dotnet build .\Payment\StealDeal.Services.Payment.slnx
```

Last result:

```text
Build succeeded
```

Known warning:

```text
Microsoft.OpenApi NU1903 vulnerability warning
```

This warning existed before the payment gateway implementation work and is not related to the saga/payment changes.

## Next Likely Work

Recommended next steps:

1. Review Step 10 implementation and confirm business rules.
2. Create/update EF migration for new `Transaction`, `Refund`, `OutboxMessage`, and `ProcessedMessage` fields/tables.
3. Implement or refine `PaymentOutboxMessageFactory` if you want event creation moved out of `PaymentCallbackService`.
4. Implement Step 12 in Store: consume `inventory.release_requested`.
5. Add local curl/IPN signature helper for testing.
6. Add expiration worker for pending transactions.
