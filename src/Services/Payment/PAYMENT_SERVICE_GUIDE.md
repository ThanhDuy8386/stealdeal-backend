# StealDeal Payment Service Research Guide

> Updated: 2026-07-24  
> Decision: integrate VNPAY Sandbox first; model seller payouts internally for the capstone.

## 1. Purpose and Recommended Direction

This guide explains:

- What the Payment service already implements.
- What must change before it can handle a real gateway.
- How VNPAY checkout, Return URL, and IPN work.
- How a marketplace can calculate commission and seller earnings.
- How to test the complete flow without real cards or money.

Recommended capstone scope:

```text
VNPAY Sandbox collects fake buyer payments for StealDeal
  -> Payment verifies and records the result
  -> Order is confirmed or cancelled through events
  -> Seller earnings and payouts are simulated internally
```

Do not start with multiple gateways, automatic bank payouts, or provider-managed
split settlement. Complete one reliable VNPAY payment flow first.

## 2. Current Payment Service

Payment follows the same Clean Architecture shape as the other services:

```text
Payment.API
  Controllers, JWT, middleware, dependency injection

Payment.Application
  DTOs, mappings, transaction/refund workflows

Payment.Domain
  Transaction and Refund entities, repository interfaces

Payment.Infrastructure
  EF Core DbContext, repositories, unit of work
```

Useful entry points:

- [`Program.cs`](StealDeal.Services.Payment.API/Program.cs)
- [`TransactionController.cs`](StealDeal.Services.Payment.API/Controllers/TransactionController.cs)
- [`RefundController.cs`](StealDeal.Services.Payment.API/Controllers/RefundController.cs)
- [`TransactionService.cs`](StealDeal.Services.Payment.Application/Services/TransactionService.cs)
- [`RefundService.cs`](StealDeal.Services.Payment.Application/Services/RefundService.cs)
- [`ApplicationDbContext.cs`](StealDeal.Services.Payment.Infrastructure/Persistence/ApplicationDbContext.cs)

The old documentation about Payment using the wrong connection string is stale.
The checked-out code correctly reads `PaymentDb`.

### 2.1 Current transaction flow

```text
Authenticated user
  -> POST /api/transactions
  -> JWT supplies UserId
  -> request supplies OrderId, Amount, PaymentMethod
  -> TransactionService creates Pending transaction
  -> Admin manually PATCHes status to Success or Failed
```

Available endpoints:

| Method | Endpoint | Access |
|---|---|---|
| `POST` | `/api/transactions` | Authenticated |
| `GET` | `/api/transactions/{id}` | Owner or Admin |
| `GET` | `/api/transactions/order/{orderId}` | Owner or Admin |
| `GET` | `/api/transactions/my-transactions` | Authenticated owner |
| `PATCH` | `/api/transactions/{id}/status` | Admin |

This is a manual payment ledger, not a gateway integration.

### 2.2 Current refund flow

```text
Seller/Admin creates refund
  -> transaction must be Success
  -> Pending + Processed refunds cannot exceed payment
  -> refund starts Pending
  -> Admin manually marks it Processed
```

The service supports partial-refund records, but no money is returned because it
does not call a provider refund API.

### 2.3 What is already useful

- Payment owns its transaction and refund records.
- Transaction reads have owner/Admin checks.
- Refunds belong to their original transaction.
- Application logic is separated from controllers.
- EF Core repositories and unit of work are wired.
- JWT and exception middleware match the other services.
- The solution builds successfully.

## 3. Gaps to Fix Before Gateway Integration

### 3.1 Never trust the frontend amount

The current public request accepts `OrderId` and `Amount`. A buyer could send:

```json
{
  "orderId": "an-expensive-real-order",
  "amount": 1000,
  "paymentMethod": "VNPAY"
}
```

Payment must obtain authoritative facts from:

- A trusted `payment.requested` event from Order; or
- An authenticated internal Order API.

The public checkout request should contain only an order identifier:

```json
{
  "orderId": "..."
}
```

Payment must verify the buyer, order state, amount, and payment deadline using
server-owned data.

### 3.2 Replace arbitrary statuses with transitions

Current transaction and refund statuses are unrestricted strings. Use a small
state machine:

```text
Transaction:
Pending -> Success
Pending -> Failed
Pending -> Expired

Refund:
Pending -> Processed
Pending -> Failed
```

Reject every other transition. A successful payment must never return to
`Pending`.

### 3.3 Validate money and input

Reject:

- Empty IDs and strings.
- Zero or negative transactions.
- Zero or negative refunds.
- Fractional VND amounts.
- Unknown payment methods.
- Refund totals above the original payment.

If `decimal` remains the storage type, enforce:

```text
amount > 0
amount == decimal.Truncate(amount)
```

### 3.4 Add database protection

The current duplicate check uses an unordered `FirstOrDefaultAsync`, and the
database has no corresponding unique constraint. Concurrent requests can create
duplicate pending payments.

For the first version, choose one explicit rule:

- One transaction per order; or
- Multiple attempts, with only one active attempt.

The repository must explicitly return the newest/current attempt rather than an
arbitrary row.

Refund balance checks also need transaction/concurrency protection so two
simultaneous requests cannot over-refund a payment.

### 3.5 Fix refund authorization

Any Seller can currently create a refund if they know a successful transaction
ID. Refund creation must verify that the seller owns the order's store.

### 3.6 Add operational foundations

Payment still needs:

- EF Core migration.
- Gateway configuration and secret storage.
- Structured payment logs.
- Callback idempotency.
- Outbox messages and publisher.
- Reconciliation for old `Pending` transactions.
- Focused automated tests.

## 4. Payment Terminology

These are separate concepts:

| Term | Meaning |
|---|---|
| Customer payment | Buyer pays for an order |
| Gateway settlement | Gateway transfers collected funds to its merchant |
| Platform commission | Amount StealDeal earns |
| Seller payable | Amount StealDeal owes a seller |
| Payout | StealDeal transfers money to a seller |
| Refund | Money returns to the buyer |

A refund is not a seller payout.

## 5. VNPAY Payment Flow

```text
Buyer               Payment Service              VNPAY
  │                         │                       │
  │ checkout(orderId)       │                       │
  ├────────────────────────>│                       │
  │                         │ verify trusted order  │
  │                         │ save Pending payment  │
  │                         │ build signed URL      │
  │ checkoutUrl             │                       │
  │<────────────────────────┤                       │
  │                                                 │
  │ redirect to checkoutUrl                         │
  ├────────────────────────────────────────────────>│
  │                                                 │
  │                  fake sandbox payment           │
  │                                                 │
  │ browser Return URL                              │
  │<────────────────────────────────────────────────┤
  │                                                 │
  │                         │ signed IPN             │
  │                         │<───────────────────────┤
  │                         │ verify and update DB   │
  │                         │ write outbox event     │
  │                         │ acknowledge            │
  │                         ├───────────────────────>│
```

### 5.1 Return URL

VNPAY redirects the buyer's browser to the Return URL after checkout.

Use it to:

- Verify returned query integrity.
- Display `Processing`, `Success`, or `Failed`.
- Direct the frontend to query the Payment API.

Do not use the Return URL as the authoritative payment update. The buyer may
close the browser, lose connectivity, or tamper with browser parameters.

### 5.2 IPN

IPN means **Instant Payment Notification**. It is VNPAY's server-to-server
callback to the Payment API.

```text
VNPAY server
  -> GET https://public-payment-api/api/payments/vnpay/ipn?...signed fields...
  -> Payment verifies the signature and stored transaction
  -> Payment updates the transaction exactly once
  -> Payment acknowledges VNPAY
```

The IPN handler must:

1. Read the returned `vnp_*` fields.
2. Remove `vnp_SecureHash` and `vnp_SecureHashType`.
3. Rebuild the canonical sorted query string.
4. Calculate HMAC-SHA512 with `vnp_HashSecret`.
5. Compare signatures in constant time.
6. Find the local transaction using `vnp_TxnRef`.
7. Verify merchant code and amount.
8. Verify the transaction is still `Pending`.
9. Require response and transaction status `00` for success.
10. Store the VNPAY transaction reference.
11. Update the transaction and outbox atomically.
12. Return the acknowledgement expected by VNPAY.

Typical acknowledgement results:

```text
00  Confirm success
01  Order not found
02  Order already confirmed
04  Invalid amount
97  Invalid signature
99  Internal/unknown error
```

Duplicate IPNs are normal. Processing the same valid IPN twice must not publish
two events or apply payment twice.

### 5.3 Signed checkout URL

The Payment service builds a VNPAY URL containing fields such as:

```text
vnp_Version
vnp_Command
vnp_TmnCode
vnp_Amount
vnp_CurrCode
vnp_TxnRef
vnp_OrderInfo
vnp_OrderType
vnp_ReturnUrl
vnp_IpAddr
vnp_CreateDate
vnp_ExpireDate
vnp_SecureHash
```

Important rules:

- Current API version is `2.1.0`.
- Sort parameters before signing.
- Sign using the sandbox `vnp_HashSecret`.
- Multiply the whole VND amount by 100 for `vnp_Amount`.
- Use a unique merchant reference for `vnp_TxnRef`.
- Use VNPAY's hosted page; never collect card details or OTPs in StealDeal.

## 6. Marketplace Money Model

### 6.1 Direct-to-seller gateway accounts

Having a seller enter a bank-account number is not enough to connect that seller
to a gateway.

For direct settlement, each seller would generally need:

- Their own VNPAY merchant onboarding.
- Their own `vnp_TmnCode` and secret.
- Their own provider contract and settlement account.

StealDeal would then need to manage many merchant credentials, callback secrets,
refund contexts, and onboarding states.

The standard public VNPAY PAY flow identifies one merchant per request. It does
not document automatically splitting a payment into seller revenue and platform
commission. A provider-supported marketplace/sub-merchant agreement would need
to be negotiated separately.

### 6.2 Platform collection and seller payouts

The more practical capstone model is:

```text
Buyer pays 100,000 VND
  -> StealDeal records 100,000 received
  -> platform commission is 10,000
  -> seller payable is 90,000

Order completes
  -> seller's 90,000 becomes available

Seller requests payout
  -> StealDeal creates a Payout
  -> capstone Admin marks it Paid
```

Seller earnings should not become withdrawable immediately after payment:

```text
Payment Success
  -> seller earning Pending

Order Completed
  -> seller earning Available

Order Cancelled/Refunded/Disputed
  -> earning Reversed or Held
```

This avoids paying a seller before pickup while StealDeal remains responsible
for a buyer refund.

Suggested future concepts:

```text
SellerLedgerEntry
├── SellerId
├── StoreId
├── OrderId
├── Type: OrderEarning | Commission | Refund | Payout
├── Amount
└── Status: Pending | Available | Reversed

Payout
├── SellerId
├── Amount
├── Destination bank snapshot
├── Status: Pending | Processing | Paid | Failed
└── ProviderReference
```

For production, holding and paying seller funds requires commercial, accounting,
KYC, and legal review. The capstone should clearly label seller payouts as mock
settlement.

## 7. Why VNPAY Instead of payOS for This Project

| Concern | VNPAY | payOS |
|---|---|---|
| Integration code | More manual signing code | Official .NET SDK, faster |
| True sandbox | Yes | No separate sandbox |
| Real bank account required for tests | No | Yes |
| Real money during tests | No | Small real transfers |
| Failure test cards | Yes | Production behavior |
| Best fit here | Learning and safe capstone testing | Fast production-like VietQR |

payOS is likely faster to code, but its official documentation says testing uses
the production API, personal/organization verification, a linked bank account,
and small real-value transfers.

VNPAY is the better choice when the goal is to learn and test without real
money.

## 8. VNPAY Sandbox Setup

### 8.1 Register

Register a test merchant:

- [VNPAY test merchant registration](https://sandbox.vnpayment.vn/devreg/)

VNPAY sends sandbox connection information by email:

```text
vnp_TmnCode
vnp_HashSecret
```

These are test credentials. Production requires a separate contract and
integration process.

### 8.2 Configure

Required settings:

```text
VnPay:TmnCode
VnPay:HashSecret
VnPay:PaymentUrl
VnPay:ReturnUrl
VnPay:IpnUrl
```

Sandbox payment URL:

```text
https://sandbox.vnpayment.vn/paymentv2/vpcpay.html
```

Keep the hash secret out of committed `appsettings.json`. Use environment
variables or .NET user secrets.

### 8.3 Make the local API reachable

A browser can return to localhost, but VNPAY's server cannot call:

```text
http://localhost:5155/api/payments/vnpay/ipn
```

Use a public HTTPS development tunnel:

```text
https://public-test-url/api/payments/vnpay/ipn
  -> tunnel
  -> http://localhost:5155/api/payments/vnpay/ipn
```

The public URL can be used for both Return and IPN during development. If the
tunnel URL changes, update the registered sandbox callback configuration.

### 8.4 Minimal API surface

```text
POST /api/payments/vnpay/checkout
GET  /api/payments/vnpay/return
GET  /api/payments/vnpay/ipn
GET  /api/transactions/{id}
```

Suggested checkout response:

```json
{
  "transactionId": "e6c49fc2-1f7f-4748-8660-a49b1df71d0c",
  "status": "Pending",
  "checkoutUrl": "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?..."
}
```

## 9. Testing Strategy

### 9.1 Unit tests

Use fixed parameters and a fixed test secret to verify:

- Canonical parameter ordering.
- Checkout HMAC generation.
- IPN signature validation.
- A changed amount invalidates the signature.

No VNPAY account or network is required.

### 9.2 Local IPN tests

Seed a test transaction:

```text
Reference = TEST123
Amount = 50,000
Status = Pending
```

Send a correctly signed IPN and verify:

```text
First delivery
  -> transaction becomes Success
  -> PaidAt and gateway reference are saved
  -> one outbox message is created

Second identical delivery
  -> transaction remains Success
  -> no duplicate outbox message
  -> duplicate is acknowledged
```

Also test:

- Invalid signature.
- Unknown reference.
- Wrong amount.
- Failed response code.
- Already completed transaction.

### 9.3 Manual sandbox tests

VNPAY supplies fake cards; do not use a real bank account.

Success case:

```text
Bank:        NCB
Card number: 9704198526191432198
Cardholder:  NGUYEN VAN A
Issue date:  07/15
OTP:         123456
```

VNPAY also publishes cards for insufficient funds, inactive, locked, and
expired-card cases:

- [VNPAY Sandbox test cards](https://sandbox.vnpayment.vn/apis/vnpay-demo/)

Manual test matrix:

| Scenario | Expected result |
|---|---|
| Success test card | Transaction becomes `Success` |
| Insufficient funds | Transaction becomes/remains failed according to callback |
| Locked card | Failure is recorded |
| Buyer closes browser | IPN still updates transaction |
| Duplicate IPN | No duplicate event |
| Tampered amount | Signature or amount validation rejects it |
| Missed callback | Query/reconciliation resolves final status |

## 10. Implementation Plan

### Milestone 1: Safe ledger

1. Add Payment migration.
2. Add fixed statuses and transition rules.
3. Validate positive whole-VND amounts.
4. Stop trusting buyer-supplied amounts.
5. Define the payment-attempt rule and database constraint.
6. Fix seller ownership checks for refunds.
7. Add one focused business-logic test.

### Milestone 2: Isolated VNPAY checkout

```text
Trusted test order
  -> create Pending transaction
  -> generate signed checkout URL
  -> pay with fake VNPAY card
  -> verify IPN
  -> update transaction exactly once
```

This milestone deliberately excludes RabbitMQ and seller payouts.

### Milestone 3: Order integration

```text
Store reserves stock
  -> Order becomes PaymentPending
  -> Order publishes payment.requested
  -> Payment creates transaction from trusted event data
```

### Milestone 4: Payment events

```text
Verified IPN
  -> transaction + outbox commit together
  -> payment.completed or payment.failed
  -> Order confirms or cancels
  -> Store releases stock on failure
  -> Notification informs buyer
```

Reuse the Identity service's existing outbox/publisher pattern rather than
creating another event framework.

### Milestone 5: Mock seller accounting

```text
Order completed
  -> commission and seller earning recorded
  -> seller earning becomes Available
  -> seller requests mock payout
  -> Admin marks payout Paid
```

### Milestone 6: Later production research

Only after the capstone flow works, evaluate:

- VNPAY production merchant onboarding.
- Provider-supported sub-merchants or split settlement.
- Real refund API.
- payOS or another payout API.
- Seller KYC, payout approval, reconciliation, and accounting.

## 11. Important Failure Rules

| Situation | Required behavior |
|---|---|
| Return URL arrives before IPN | Display `Processing`; query Payment status |
| Buyer closes browser | IPN still completes the payment |
| IPN is duplicated | Acknowledge without applying twice |
| Signature is invalid | Reject; do not change DB |
| Returned amount differs | Reject and alert |
| Gateway request times out | Query provider; do not assume failure |
| DB succeeds but RabbitMQ fails | Outbox retries later |
| Payment succeeds after cancellation | Record payment and initiate compensation/refund |
| Refund callback duplicates | Handle idempotently |

## 12. First Completion Target

The first meaningful end-to-end target is:

```text
A trusted 50,000 VND test order
  -> creates one Pending transaction
  -> redirects to VNPAY Sandbox
  -> fake test card completes checkout
  -> signed IPN is verified
  -> transaction becomes Success exactly once
  -> browser reads the authoritative status from Payment DB
```

Complete this before building multiple gateways, real seller payouts, or a
generic provider factory.

## 13. Official References

VNPAY:

- [Integration overview and sandbox credentials](https://sandbox.vnpayment.vn/apis/docs/gioi-thieu/)
- [PAY and IPN integration](https://sandbox.vnpayment.vn/apis/docs/thanh-toan-pay/pay.html)
- [Return URL versus IPN FAQ](https://sandbox.vnpayment.vn/apis/docs/faqs/)
- [Test merchant registration](https://sandbox.vnpayment.vn/devreg/)
- [Sandbox test cards](https://sandbox.vnpayment.vn/apis/vnpay-demo/)
- [C# samples and specifications](https://sandbox.vnpayment.vn/apis/downloads/)

payOS comparison:

- [Test environment policy](https://payos.vn/docs/moi-truong-test/)
- [Payment and payout APIs](https://payos.vn/docs/api/)
- [.NET SDK](https://payos.vn/docs/sdks/back-end/net/)
