# StealDeal Backend API Gaps and Proposed Contracts

> Reviewed: 2026-07-30
>
> Status: planning only - nothing in this file is implemented unless explicitly stated

This report keeps backend problems and future contract proposals separate from
the current [frontend API reference](STEALDEAL_API_REFERENCE.md). Frontend
agents may use high-confidence proposed fields for mock UI, but must not call
the proposed routes until the backend implements them.

## 1. Current Problems

### Critical before real integration

| Area | Current problem | Required direction |
|---|---|---|
| Store authorization | Most Store `[Authorize]` attributes are commented out. Some actions still parse claims and fail poorly for anonymous callers. | Restore role/ownership authorization and return proper `401`/`403`. |
| Fixed user identity | Store creation, bag creation, and order creation use the fixed user ID `6BFE535E-E205-4031-88A8-36D8993863F7`. | Always derive the acting user from the validated access token. |
| Order trust boundary | The client supplies store/bag names, unit prices, delivery fee, discount, quantities, and IDs. | Resolve and validate authoritative catalog data server-side. |
| Payment trust boundary | The client supplies the transaction amount. | Load the amount and owner from a trusted order or event. |
| Notification creation | `POST /api/notifications` is public and accepts any `userId`. | Make it internal/event-driven or Admin-only for explicit testing. |
| Status mutation | Bag, order, dispute, transaction, and refund statuses accept unrestricted strings. | Define fixed values and allowed transitions. |

### Important contract and ownership gaps

| Area | Gap |
|---|---|
| Seller access | Store/order/refund code does not consistently prove that the Seller owns the target store or order. |
| Registration | Public registration always creates a `Customer`; no seller onboarding flow has been chosen. |
| Role API | Dynamic Role CRUD exists even though roles are fixed. Deprecate and later remove the controller. |
| User list DTO | `UserResponse.UserTrustScore` is a domain model property. The mapper leaves it `null`, while `UserTrustScoreResponse` already exists. |
| User update | Admin email updates are not normalized or checked for uniqueness. Null/empty phone values do not clear the phone. |
| Store response | Seller-entered `bankAccount` and `licenseUrl` are never returned; `avatarUrl` is returned but cannot be written. |
| Review response | Stored `storeId`, `bagId`, and `isReported` are omitted; there is no reported-review moderation list. |
| Bag presentation | Surprise bags have no image or media field. |
| Collection APIs | Most lists have no paging, filtering, or stable sort contract. |
| Browser integration | Only Identity configures CORS. Refresh-cookie behavior can also fail when frontend/API schemes and origins do not align. |
| Error contract | Business errors use `ProblemDetails`, but authentication and automatic model validation can produce different or empty bodies. |

### Existing data with no usable frontend API

| Model/data | What exists | What is missing |
|---|---|---|
| `UserAddress` | Model, database mapping, and nested `UserAddressResponse` | Create, update, delete, and set-default endpoints |
| `TrustScoreEvent` | Model and database mapping | Response DTO and self/admin history endpoint |
| Store verification data | `bankAccount`, `licenseUrl`, `updatedAt` on the model | Seller-private/admin response |
| Review moderation data | `isReported` on the model | Admin response and moderation query/action |
| Surprise-bag media | Nothing | Model field, request fields, and response fields |

Internal models such as refresh tokens, OTP records, outbox messages, and
processed-message records intentionally need no public controllers or DTOs.

## 2. High-Confidence Proposed Contracts

These proposals reuse existing models and current request/response styles. They
are the minimum additions needed for likely frontend screens.

### Address management

Proposed routes:

| Method | Path | Access | Request | Response |
|---|---|---|---|---|
| `GET` | `/api/account/addresses` | Bearer | none | `UserAddressResponse[]` |
| `POST` | `/api/account/addresses` | Bearer | `CreateUserAddressRequest` | `201 UserAddressResponse` |
| `PUT` | `/api/account/addresses/{id}` | Owner | `UpdateUserAddressRequest` | `200 UserAddressResponse` |
| `DELETE` | `/api/account/addresses/{id}` | Owner | none | `204` |
| `PATCH` | `/api/account/addresses/{id}/default` | Owner | none | `204` |

```ts
export interface CreateUserAddressRequest {
  label: string;
  address: string;
  district: string;
  city: string;
  isDefault: boolean;
}

export type UpdateUserAddressRequest = CreateUserAddressRequest;
```

`userId` must come from the token, never from the request. Setting a default
address must clear the previous default in the same database operation.

### Trust-score history

Proposed routes:

| Method | Path | Access | Response |
|---|---|---|---|
| `GET` | `/api/account/trust-score/events` | Bearer | `TrustScoreEventResponse[]` |
| `GET` | `/api/user/{id}/trust-score/events` | Admin | `TrustScoreEventResponse[]` |

```ts
export interface TrustScoreEventResponse {
  id: UUID;
  eventType: string;
  scoreDelta: number;
  scoreAfter: number;
  referenceId: string | null;
  referenceType: string | null;
  note: string | null;
  createdAt: ISODateTime;
}
```

The self-service response should omit `userId`; the Admin variant may add it if
the UI consumes events from multiple users in one result.

### Seller-private store details

Keep `StoreProfileResponse` public. Add a separate private response for
`GET /api/stores/me` rather than exposing verification/bank data to everyone.

```ts
export interface SellerStoreResponse extends StoreProfileResponse {
  bankAccountLast4: string | null;
  licenseUrl: string | null;
  updatedAt: ISODateTime | null;
}

export interface CreateStoreRequestV2 extends CreateStoreRequest {
  avatarUrl?: string | null;
}

export type UpdateStoreRequestV2 = CreateStoreRequestV2;
```

Do not return the full bank account from public store endpoints. A seller edit
form can show a masked value and accept a replacement account separately.

### Surprise-bag media

The smallest useful contract is a URL list, not a separate media subsystem.

```ts
export interface CreateBagRequestV2 extends CreateBagRequest {
  imageUrls: string[];
}

export interface UpdateBagRequestV2 extends UpdateBagRequest {
  imageUrls: string[];
}

export interface SurpriseBagResponseV2 extends SurpriseBagResponse {
  imageUrls: string[];
}
```

Use an empty array when no image exists. Keep ordering meaningful: index `0` is
the card/cover image.

### Review identity and moderation

Public review responses need enough identity for bag/store UI. Report state is
Admin-only.

```ts
export interface StoreReviewResponseV2 extends StoreReviewResponse {
  storeId: UUID;
  bagId: UUID;
}

export interface AdminStoreReviewResponse extends StoreReviewResponseV2 {
  isReported: boolean;
}
```

Proposed Admin route:

```text
GET /api/reviews/reported -> AdminStoreReviewResponse[]
```

Do not add `isReported` to public reviews unless the product has a reason to
show moderation state to customers.

### Server-authoritative order creation

Replace client-owned snapshots and money with identifiers and quantities.

```ts
export interface CreateOrderItemRequestV2 {
  bagId: UUID;
  quantity: number;
}

export interface CreateOrderRequestV2 {
  deliveryType: string;
  deliveryAddress: string | null;
  items: CreateOrderItemRequestV2[];
}
```

The backend must load bag/store names and prices, verify that all items belong
to one store, validate stock, calculate totals, and create snapshots itself.
`deliveryAddress` may be `null` for pickup after the final delivery contract is
fixed. Do not add a voucher code until a voucher model/rule exists.

### Payment checkout

Replace direct client-created ledger entries with an order-based checkout.

Proposed route:

```text
POST /api/payments/vnpay/checkout
```

```ts
export interface CreateCheckoutRequest {
  orderId: UUID;
}

export interface CheckoutResponse {
  transactionId: UUID;
  status: "Pending";
  checkoutUrl: string;
}
```

The backend must obtain amount and ownership from the order. The frontend
redirects to `checkoutUrl` and later reads authoritative transaction status; a
browser return URL must not be treated as proof of payment.

### Named inline responses

The current JSON shapes can stay unchanged while gaining named DTOs:

```ts
export interface MessageResponse {
  message: string;
}

export interface CurrentUserResponse {
  userId: UUID;
  email: string;
  name: string;
  roles: Role[];
}

export interface UnreadCountResponse {
  unreadCount: number;
}
```

`UnreadCountResponse` would change the current raw integer response and should
only be adopted when frontend integration begins. `MessageResponse` and
`CurrentUserResponse` can replace anonymous C# objects without changing JSON.

## 3. Proposed Closed Values

The current database and DTOs use free-form strings. These unions are a target
for backend validation and frontend mock data, not current guarantees.

```ts
export type BagStatus =
  | "Draft"
  | "Available"
  | "SoldOut"
  | "Expired"
  | "Inactive";

export type DeliveryType = "Pickup" | "Delivery";

export type OrderStatus =
  | "Pending"
  | "PaymentPending"
  | "Confirmed"
  | "Cancelled"
  | "Completed"
  | "Disputed";

export type DisputeStatus = "Pending" | "Resolved" | "Rejected";

export type TransactionStatus =
  | "Pending"
  | "Success"
  | "Failed"
  | "Expired";

export type RefundStatus = "Pending" | "Processed" | "Failed";
```

For the first real gateway, support only the chosen provider value rather than
publishing a speculative multi-provider union.

## 4. Product Decisions Still Required

Do not define backend DTOs for these until the team chooses the behavior:

| Decision | Why it blocks a stable contract |
|---|---|
| Seller onboarding | Registration currently creates Customers only; decide direct Seller signup versus application/approval. |
| Cart persistence | A cart could remain frontend-local or become an account resource. |
| Favorites | No model or confirmed persistence requirement exists. |
| Vouchers | Order stores only a discount amount; there is no voucher identity, eligibility, or lifecycle model. |
| Delivery | No delivery provider, fee rule, address snapshot policy, or tracking model exists. |
| Seller payouts | Bank account storage exists, but earnings, commission, ledger, payout, and KYC rules do not. |
| Review moderation outcome | Reporting exists, but approve/hide/delete decisions and audit history do not. |
| List pagination | Choose query names, default size, maximum size, filters, and sort values before publishing a shared contract. |

## 5. Recommended Backend Order

1. Restore authentication/ownership and remove fixed user IDs.
2. Close the order/payment trust boundary before connecting real frontend data.
3. Add bag media and seller-private store details because they directly block
   core marketplace UI.
4. Add address CRUD and complete review identity/moderation fields.
5. Replace free-form statuses with validated transitions.
6. Implement real checkout only after server-authoritative orders work.
7. Decide seller onboarding, vouchers, delivery, and payouts separately; they
   do not need speculative scaffolding now.
