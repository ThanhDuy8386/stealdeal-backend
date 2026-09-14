# Payment Gateway Current Context

Short handoff note for the current VNPAY payment flow.

## Current Status

Payment flow is implemented end-to-end for the current milestone:

```text
Order -> order.created
Store -> inventory.reserved
Payment -> create VNPAY checkout transaction
VNPAY -> /api/vnpay/return for browser redirect
VNPAY -> /api/vnpay/ipn for server-to-server payment result
Payment -> payment.completed or payment.failed
Payment -> inventory.release_requested when payment fails/expires
Order <- payment.completed/payment.failed
Store <- inventory.release_requested
```

Important:

- `/api/vnpay/ipn` is the source of truth for updating payment state.
- `/api/vnpay/return` only verifies/parses VNPAY query params for browser/testing. It does not update DB.
- Refund record creation exists for late-success cases, but actual VNPAY refund API call is not implemented yet.

## Main Files

Gateway/config:

```text
Payment/StealDeal.Services.Payment.Infrastructure/Configuration/VnPaySettings.cs
Payment/StealDeal.Services.Payment.Infrastructure/Gateways/VnPayGateway.cs
Payment/StealDeal.Services.Payment.Infrastructure/Gateways/PaymentGatewayFactory.cs
Payment/StealDeal.Services.Payment.Application/Gateways/IPaymentGateway.cs
Payment/StealDeal.Services.Payment.Application/Gateways/IPaymentGatewayFactory.cs
```

Checkout creation from `inventory.reserved`:

```text
Payment/StealDeal.Services.Payment.Application/EventHandlers/InventoryReservedEventHandler.cs
Payment/StealDeal.Services.Payment.Infrastructure/BackgroundServices/InventoryReservedConsumer.cs
```

VNPAY return/IPN endpoints:

```text
Payment/StealDeal.Services.Payment.API/Controllers/VnPayController.cs
Payment/StealDeal.Services.Payment.Application/Services/Interfaces/IPaymentCallbackService.cs
Payment/StealDeal.Services.Payment.Application/Services/PaymentCallbackService.cs
```

Outbox event creation:

```text
Payment/StealDeal.Services.Payment.Application/Events/PaymentOutboxMessageFactory.cs
```

Expiration:

```text
Payment/StealDeal.Services.Payment.Infrastructure/BackgroundServices/PaymentExpirationProcessor.cs
Payment/StealDeal.Services.Payment.Infrastructure/Configuration/PaymentExpirationSettings.cs
```

Store compensation:

```text
Store/StealDeal.Services.Store.Application/DTOs/Events/InventoryReleaseRequestedEvent.cs
Store/StealDeal.Services.Store.Application/EventHandlers/InventoryReleaseRequestedEventHandler.cs
Store/StealDeal.Services.Store.Infrastructure/BackgroundServices/InventoryReleaseRequestedConsumer.cs
```

Domain/constants:

```text
Payment/StealDeal.Services.Payment.Domain/Constants/PaymentMethods.cs
Payment/StealDeal.Services.Payment.Domain/Constants/TransactionStatuses.cs
Payment/StealDeal.Services.Payment.Domain/Constants/RefundStatuses.cs
Payment/StealDeal.Services.Payment.Domain/Models/Transaction.cs
Payment/StealDeal.Services.Payment.Domain/Models/Refund.cs
```

## VNPAY Config

Payment API config shape:

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

Notes:

- `ReturnUrl` is sent to VNPAY in the checkout URL through `vnp_ReturnUrl`.
- `IpNUrl` is not sent in the checkout URL. Configure it on VNPAY merchant/sandbox side or send it to VNPAY support for mapping to the sandbox `TmnCode`.
- VNPAY cannot call `localhost` for IPN.

## Run Payment API With HTTPS

Payment API must run with the `https` launch profile when testing VNPAY/ngrok:

```powershell
cd C:\Users\ADMIN\Desktop\Capstone-BE\stealdeal-backend\src\Services\Payment\StealDeal.Services.Payment.API
dotnet run --launch-profile https
```

Expected local URLs:

```text
https://localhost:7080
http://localhost:5155
```

## Expose Localhost With Ngrok

Initialize ngrok with your account token if needed:

```powershell
ngrok config add-authtoken "<YOUR_NGROK_AUTHTOKEN>"
```

Open tunnel to the Payment API HTTPS endpoint:

```powershell
ngrok http --url=<YOUR_DOMAIN>.ngrok-free.app https://localhost:7080
```

Then set:

```text
ReturnUrl = https://<YOUR_DOMAIN>.ngrok-free.app/api/vnpay/return
IpNUrl    = https://<YOUR_DOMAIN>.ngrok-free.app/api/vnpay/ipn
```

Restart Payment API after changing config.

Use ngrok inspection dashboard to verify calls:

```text
http://127.0.0.1:4040
```

Expected request paths:

```text
GET /api/vnpay/return
GET /api/vnpay/ipn
```

Production note:

- Ngrok is only for local sandbox testing.
- After deploy, use the real public API domain instead of ngrok, for example:

```text
https://api.your-domain.com/api/vnpay/return
https://api.your-domain.com/api/vnpay/ipn
```

## End-To-End Payment Flow

Happy path:

```text
1. Buyer places order.
2. Order publishes order.created.
3. Store consumes order.created and reserves stock.
4. Store publishes inventory.reserved.
5. Payment consumes inventory.reserved.
6. Payment creates Transaction Status = Pending.
7. Payment creates VNPAY checkout URL and saves GatewayRef/CheckoutUrl/ExpiresAt.
8. Frontend polls GET /api/transactions/order/{orderId}.
9. Frontend redirects buyer to CheckoutUrl.
10. Buyer pays on VNPAY.
11. VNPAY redirects browser to /api/vnpay/return.
12. VNPAY calls /api/vnpay/ipn.
13. Payment verifies signature, vnp_TxnRef, and amount.
14. Payment marks Transaction = Success.
15. Payment publishes payment.completed.
16. Order consumes payment.completed and confirms order.
```

Payment failed path:

```text
1. VNPAY calls /api/vnpay/ipn with valid signature and non-success status.
2. Payment marks Transaction = Failed.
3. Payment publishes payment.failed.
4. Payment publishes inventory.release_requested if StoreId and ReservedItemsJson exist.
5. Order consumes payment.failed.
6. Store consumes inventory.release_requested and restores QuantityRemaining.
```

Expiration path:

```text
1. Transaction stays Pending.
2. ExpiresAt <= UtcNow.
3. PaymentExpirationProcessor marks Transaction = Expired.
4. Payment publishes payment.failed.
5. Payment publishes inventory.release_requested.
6. Order fails/cancels order.
7. Store releases reserved stock.
```

Late success / refund path:

```text
1. Transaction is already Failed or Expired.
2. A valid VNPAY success IPN arrives later.
3. Payment does not confirm order.
4. Payment marks Transaction = RefundPending.
5. Payment creates Refund Status = Pending.
6. No payment.completed event is published.
```

Refund note:

- Current implementation only creates a `Refund` row with `Status = Pending`.
- Actual VNPAY refund API call is not implemented yet.
- Future work should process pending refunds, call VNPAY refund API, store gateway refund result, and update:

```text
Refund.Status = Processed / Failed
Transaction.Status = Refunded / RefundFailed
```

## Cases Currently Handled

IPN validation:

```text
Invalid signature -> RspCode 97, no DB update
Missing/wrong vnp_TxnRef -> RspCode 01, no DB update
Amount mismatch -> RspCode 04, no DB update
Already Success -> RspCode 02, no duplicate payment.completed
```

Payment states:

```text
Pending + success IPN -> Success + payment.completed
Pending + failed IPN -> Failed + payment.failed + inventory.release_requested
Failed/Expired + success IPN -> RefundPending + Refund Pending row
RefundPending/Refunded + success IPN -> RspCode 00, no-op
Failed/Expired/RefundPending/Refunded + failed IPN -> RspCode 00, no-op
Pending + ExpiresAt passed -> Expired + payment.failed + inventory.release_requested
```

Store compensation:

```text
inventory.release_requested is idempotent through ProcessedMessage.
Duplicate inventory.release_requested must not increase stock twice.
```

## Manual Testing Notes

Success through real VNPAY sandbox:

```text
Use VNPAY test card.
Expect /api/vnpay/return and /api/vnpay/ipn to appear in ngrok dashboard.
Expect Transaction = Success.
Expect payment.completed outbox/event.
```

Failure through real VNPAY sandbox:

```text
Some VNPAY UI validation failures do not produce IPN.
If VNPAY blocks the input before a transaction result exists, there may be no callback.
Use cancel flow if available, or signed manual IPN for failure branch.
```

Manual signed failed IPN:

```text
Use existing Transaction.GatewayRef as vnp_TxnRef.
Use Transaction.Amount * 100 as vnp_Amount.
Set vnp_ResponseCode != 00 or vnp_TransactionStatus != 00.
Sign params with VnPay:HashSecret.
Call /api/vnpay/ipn.
```

Manual refund branch test:

```text
1. Make transaction Failed or Expired.
2. Send signed success IPN for the same vnp_TxnRef.
3. Expect Transaction = RefundPending.
4. Expect Refund Status = Pending.
5. Expect no payment.completed event.
```

Return endpoint:

```text
/api/vnpay/return verifies and parses VNPAY params only.
It does not update transaction state.
```

## Production IPN Direction

For production:

```text
1. Deploy Payment API behind a stable HTTPS public domain.
2. Configure production ReturnUrl and IPN URL with VNPAY.
3. Store TmnCode/HashSecret in secure configuration/secrets, not committed appsettings.
4. Keep /api/vnpay/ipn anonymous but rely on VNPAY HMAC signature verification.
5. Add structured logs for GatewayRef, TransactionId, RspCode, ResponseCode, TransactionStatus.
6. Add alerts for invalid signature, amount mismatch, RefundPending, and outbox publish failures.
7. Add concurrency hardening between IPN success and expiration worker.
8. Add automated integration tests with signed VNPAY callbacks.
```

## Build Verification

Payment:

```text
dotnet build .\Payment\StealDeal.Services.Payment.slnx
```

Store:

```text
dotnet build .\Store\StealDeal.Services.Store.API\StealDeal.Services.Store.API.csproj
```

Known warning:

```text
Microsoft.OpenApi NU1903 vulnerability warning
```

This warning existed before the payment gateway implementation work and is not related to the saga/payment changes.
