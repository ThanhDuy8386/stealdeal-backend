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
Implemented.
```

Store service must consume:

```text
inventory.release_requested
```

Implemented files:

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

group release items by SurpriseBagId
validate quantity > 0
for each item:
    atomically increase QuantityRemaining by quantity

add ProcessedMessage
save changes in the same DB transaction
```

Important:

- Must be idempotent.
- Duplicate `inventory.release_requested` must not add stock twice.
- Use `ProcessedMessage` in the same DB save as the stock update.
- Repository support added:

```csharp
Task<bool> TryReleaseQuantityAsync(
    Guid surpriseBagId,
    Guid storeId,
    int quantity,
    CancellationToken cancellationToken = default);
```

Registered in `Program.cs`:

```csharp
builder.Services.AddScoped<
    IIntegrationEventHandler<InventoryReleaseRequestedEvent>,
    InventoryReleaseRequestedEventHandler>();

builder.Services.Configure<InventoryReleaseRequestedConsumerSettings>(
    builder.Configuration.GetSection("InventoryReleaseRequestedConsumer"));

builder.Services.AddHostedService<InventoryReleaseRequestedConsumer>();
```

Current trade-off:

- Payment publishes `StoreId` and item list from `Transaction.ReservedItemsJson`.
- Store does not yet use a reservation table.

### Step 13 - Add Expiration Worker

Status:

```text
Implemented.
```

Why:

- User can abandon VNPAY checkout.
- IPN may never arrive.
- Reserved stock should not stay locked forever.

Implemented file:

```text
Payment/StealDeal.Services.Payment.Infrastructure/BackgroundServices/PaymentExpirationProcessor.cs
```

Repository support added:

```csharp
Task<List<Transaction>> GetExpiredPendingBatchAsync(
    DateTime nowUtc,
    int batchSize,
    CancellationToken cancellationToken = default);
```

Flow:

```text
find Pending transactions where ExpiresAt <= UtcNow
mark transaction Expired
add payment.failed outbox
add inventory.release_requested outbox
save changes
```

Settings:

```text
PaymentExpiration:
  Enabled
  BatchSize
  PollingIntervalSeconds
```

Registered in `Program.cs`:

```csharp
builder.Services.Configure<PaymentExpirationSettings>(
    builder.Configuration.GetSection("PaymentExpiration"));

builder.Services.AddHostedService<PaymentExpirationProcessor>();
```

Implementation note:

- Reuses `PaymentOutboxMessageFactory`.
- ReasonCode is `PaymentExpired`.
- Reason is `Payment expired before gateway confirmation.`
- `payment.failed` lets Order update order status.
- `inventory.release_requested` lets Store release reserved stock.

### Step 14 - Late Success After Compensation

Status:

```text
Implemented at DB-record level.
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

if VNPAY IPN success arrives again for RefundPending/Refunded transaction:
    return RspCode 00
    do not update transaction
    do not publish payment.completed
```

Still not implemented:

- Actual VNPAY refund API call.
- Refund query/status update.
- Retry strategy for failed refund.
- Mapping VNPAY refund response to `Refund.Status`.
- Mapping final refund state back to `Transaction.Status = Refunded` or `RefundFailed`.

Potential future files:

```text
Payment/StealDeal.Services.Payment.Application/Gateways/IPaymentRefundGateway.cs
Payment/StealDeal.Services.Payment.Application/Services/RefundProcessingService.cs
Payment/StealDeal.Services.Payment.Infrastructure/Gateways/VnPayRefundGateway.cs
```

Future refund action note:

```text
Refund Pending currently means "money was captured after the saga had already failed/expired."
The next implementation should process these Refund rows asynchronously:

1. Find Refund Status = Pending.
2. Call VNPAY refund API.
3. Save GatewayRefundRef/GatewayResponseCode.
4. If refund succeeds:
   - Refund.Status = Processed
   - Refund.ProcessedAt = UtcNow
   - Transaction.Status = Refunded
5. If refund fails:
   - Refund.Status = Failed
   - Refund.FailureReason = gateway reason
   - Transaction.Status = RefundFailed
6. Add retry/backoff for transient gateway failures.
```

### Step 15 - Edge Cases Checklist

Status:

```text
Covered for current milestone through manual testing and build verification.
```

Covered by current implementation:

- Duplicate `inventory.reserved` with same `MessageId` returns early through `ProcessedMessage`.
- Duplicate `inventory.reserved` with different `MessageId` but same active order does not create another active transaction.
- Invalid VNPAY signature returns `RspCode = 97`.
- Missing transaction returns `RspCode = 01`.
- Amount mismatch returns `RspCode = 04`.
- Pending + successful IPN marks transaction `Success` and publishes `payment.completed`.
- Pending + failed IPN marks transaction `Failed`, publishes `payment.failed`, and publishes `inventory.release_requested`.
- Duplicate successful IPN for already-success transaction returns `RspCode = 02` and does not publish another `payment.completed`.
- Duplicate failed IPN for `Failed/Expired/RefundPending/Refunded` returns `RspCode = 00` and does not publish duplicate failure/release events.
- Success IPN after failed/expired transaction creates refund path instead of confirming order.
- Success IPN after `RefundPending/Refunded` returns `RspCode = 00` and does not confirm order.
- Pending transaction expiration marks transaction `Expired`, publishes `payment.failed`, and publishes `inventory.release_requested`.
- Store consumes `inventory.release_requested` and restores `QuantityRemaining` idempotently through `ProcessedMessage`.
- Return endpoint verifies/parses VNPAY params but does not update DB.

Still not implemented / future hardening:

- Actual VNPAY refund API.
- Refund retry worker.
- Automated integration tests for the full saga.
- Script/tool to generate signed VNPAY IPN payloads.
- Race-condition hardening between IPN success and expiration worker.
- Compare `PaidAtUtc` with `ExpiresAt` if the business later wants to accept a payment made before expiry but delivered after the expiration worker ran.
- CorrelationId/CausationId for full saga tracing.
- Dead-letter queue for malformed RabbitMQ messages.

### Step 16 - Local Testing

Status:

```text
Manual testing completed. Script/tool not implemented.
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
- expiration worker flow

### Step 17 - VNPAY Sandbox Through Public Tunnel

Status:

```text
Ngrok local-to-public mapping completed.
VNPAY ReturnUrl browser redirect tested successfully.
VNPAY IPN endpoint is ready, but real server-to-server IPN depends on VNPAY-side IPN URL configuration.
```

Use after local curl tests pass.

Requirements:

- Real VNPAY sandbox `TmnCode`.
- Real VNPAY sandbox `HashSecret`.
- Public HTTPS tunnel for Payment API.
- ngrok is the recommended tunnel for current sandbox testing because it is simpler to set up than Cloudflare Tunnel.

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
- Current code sends `ReturnUrl` to VNPAY through `vnp_ReturnUrl`.
- Current code does not send `IpNUrl` in the checkout URL. The IPN URL must be configured on VNPAY's merchant/sandbox side or sent to VNPAY support for mapping to the sandbox `TmnCode`.
- If VNPAY has not configured the IPN URL yet, the user can still complete checkout and be redirected to `/api/vnpay/return`, but the transaction remains `Pending` until `/api/vnpay/ipn` is called.

Ngrok setup used for local sandbox testing:

```powershell
cd C:\Users\ADMIN\Desktop\Capstone-BE\stealdeal-backend\src\Services\Payment\StealDeal.Services.Payment.API
dotnet run --launch-profile https
```

Expected Payment API local URLs:

```text
https://localhost:7080
http://localhost:5155
```

In a second terminal:

```powershell
ngrok config add-authtoken "<YOUR_NGROK_AUTHTOKEN>"
ngrok http https://localhost:7080
```

If HTTPS upstream causes local dev certificate issues, an alternative is:

```powershell
ngrok http http://localhost:5155
```

Ngrok will output a public URL like:

```text
https://abc-xyz.ngrok-free.app
```

Then update Payment API config and restart the Payment API:

```json
"VnPay": {
  "TmnCode": "VNPAY_SANDBOX_TMN_CODE",
  "HashSecret": "VNPAY_SANDBOX_HASH_SECRET",
  "PaymentUrl": "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
  "ReturnUrl": "https://abc-xyz.ngrok-free.app/api/vnpay/return",
  "IpNUrl": "https://abc-xyz.ngrok-free.app/api/vnpay/ipn",
  "Version": "2.1.0",
  "Command": "pay",
  "CurrCode": "VND",
  "Locale": "vn",
  "ExpireMinutes": 15
}
```

VNPAY-side IPN URL to configure or send to VNPAY support:

```text
https://abc-xyz.ngrok-free.app/api/vnpay/ipn
```

Ngrok inspection dashboard:

```text
http://127.0.0.1:4040
```

Use this dashboard to confirm whether VNPAY called:

```text
GET /api/vnpay/return
GET /api/vnpay/ipn
```

Important ngrok notes:

- Free ngrok domains can change when the tunnel restarts.
- Every time the ngrok domain changes, update `VnPay:ReturnUrl`, `VnPay:IpNUrl`, restart Payment API, and update/send the new IPN URL to VNPAY.
- Do not configure `localhost` as the VNPAY IPN URL. VNPAY servers cannot reach the developer machine's localhost.
- Keep Payment API running while ngrok is running. If Payment API stops, ngrok will show gateway/upstream errors.

Manual fallback if VNPAY IPN is not configured yet:

```text
1. Complete checkout through VNPAY sandbox.
2. Browser redirects to /api/vnpay/return with signed vnp_* params.
3. Copy the full query string.
4. Call /api/vnpay/ipn with the same query string in Postman.
5. This simulates the server-to-server IPN using VNPAY-signed params.
```

## End-to-End Payment Flow Summary

Current implemented happy path:

```text
1. Buyer places order.
2. Order service publishes order.created.
3. Store service consumes order.created.
4. Store validates stock and decreases QuantityRemaining.
5. Store publishes inventory.reserved.
6. Payment service consumes inventory.reserved.
7. Payment creates Pending transaction.
8. Payment creates VNPAY checkout URL.
9. Frontend polls GET /api/transactions/order/{orderId}.
10. Frontend redirects buyer to CheckoutUrl.
11. Buyer completes/cancels payment on VNPAY.
12. VNPAY redirects browser to /api/vnpay/return.
13. VNPAY server calls /api/vnpay/ipn if IPN URL is configured.
14. Payment verifies signature, vnp_TxnRef, and amount.
15. If success:
    - Transaction -> Success
    - publish payment.completed
16. If fail:
    - Transaction -> Failed
    - publish payment.failed
    - publish inventory.release_requested
17. Order consumes payment.completed/payment.failed and updates order status.
18. Store consumes inventory.release_requested and restores stock when needed.
```

Expiration path:

```text
1. Transaction remains Pending.
2. ExpiresAt <= UtcNow.
3. PaymentExpirationProcessor marks transaction Expired.
4. Payment publishes payment.failed.
5. Payment publishes inventory.release_requested.
6. Order fails/cancels the order.
7. Store releases reserved stock.
```

Late success path after compensation:

```text
1. Transaction already Failed/Expired.
2. IPN success arrives later.
3. Payment does not confirm order.
4. Payment marks transaction RefundPending.
5. Payment creates Refund Pending row.
6. Future refund worker/API will process the actual VNPAY refund.
```

Production IPN direction:

```text
1. Replace ngrok with a stable HTTPS production domain.
2. Use a real VNPAY production TmnCode and HashSecret from secure configuration/secrets, not appsettings committed to source.
3. Configure the production IPN URL with VNPAY:
   https://api.your-domain.com/api/vnpay/ipn
4. Keep /api/vnpay/ipn anonymous, but require valid VNPAY signature and amount matching.
5. Add structured logging for every IPN result:
   GatewayRef, TransactionId, RspCode, ResponseCode, TransactionStatus.
6. Add monitoring/alerts for invalid signature, amount mismatch, refund pending, and outbox publish failures.
7. Add concurrency hardening for race between IPN and expiration worker.
8. Add automated integration tests using signed callback payloads.
```

Testing notes:

```text
Payment success:
  vnp_ResponseCode = 00
  vnp_TransactionStatus = 00
  valid vnp_SecureHash
  amount matches transaction.Amount * 100

Payment fail:
  valid vnp_SecureHash
  amount matches
  vnp_ResponseCode != 00 or vnp_TransactionStatus != 00

Invalid signature:
  wrong vnp_SecureHash
  expect RspCode 97 and no DB update

Amount mismatch:
  valid signature but vnp_Amount != transaction.Amount * 100
  expect RspCode 04 and no DB update

Wrong/missing transaction:
  valid signature but vnp_TxnRef does not match any transaction.GatewayRef
  expect RspCode 01 and no DB update

Expired transaction:
  Status = Pending
  ExpiresAt <= UtcNow
  wait for PaymentExpirationProcessor polling interval
  expect Expired + payment.failed + inventory.release_requested

Late success:
  first make transaction Failed/Expired
  then send valid success IPN
  expect RefundPending + Refund Pending row, no payment.completed
```

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

Last known Payment build command:

```text
dotnet build .\Payment\StealDeal.Services.Payment.slnx
```

Last result:

```text
Build succeeded
```

Last known Store build command after Step 12:

```text
dotnet build .\Store\StealDeal.Services.Store.API\StealDeal.Services.Store.API.csproj
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

1. Finish VNPAY sandbox IPN configuration on VNPAY side.
2. Test real VNPAY server-to-server IPN through ngrok and confirm it appears in `http://127.0.0.1:4040`.
3. Add local helper script/tool for generating signed VNPAY IPN requests.
4. Add automated tests for IPN success/fail/invalid/duplicate/late-success/expiration cases.
5. Add refund processing implementation for `Refund Status = Pending`.
6. Add concurrency hardening between IPN handler and expiration worker.
7. Add CorrelationId/CausationId to outbox/processed messages for easier saga tracing.
8. Prepare production IPN configuration with stable HTTPS domain and secret management.
