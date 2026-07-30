# StealDeal Frontend API Reference

> Source snapshot: 2026-07-30
>
> Purpose: frontend mock data, UI field planning, and TypeScript API types
>
> Source of truth: the controllers, DTOs, mappings, and models under `src/Services`

This file documents what the backend exposes now. It does not make unfinished
backend behavior part of the future contract. Missing and proposed contracts
are kept in [STEALDEAL_BACKEND_API_GAPS.md](STEALDEAL_BACKEND_API_GAPS.md).

## 1. Shared Contract

| Type | TypeScript | JSON example |
|---|---|---|
| UUID / C# `Guid` | `string` | `"6bfe535e-e205-4031-88a8-36d8993863f7"` |
| C# `DateTime` | ISO-8601 `string` | `"2026-07-30T08:30:00Z"` |
| C# `decimal` | `number` | `70000` |
| Nullable value | `T \| null` | `null` |

```ts
export type UUID = string;
export type ISODateTime = string;
export type Money = number;
export type Role = "Customer" | "Seller" | "Admin";
export type NoContent = void;

// Several controllers currently return anonymous { message } objects.
export interface MessageResponse {
  message: string;
}

export interface ProblemDetails {
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  errors?: Record<string, string[]>;
}
```

- JSON property names are `camelCase`.
- Collections are raw arrays unless `PagedResult<T>` is shown.
- There is no shared `{ data, success, message }` response envelope.
- Send protected requests with `Authorization: Bearer <accessToken>`.
- Login and refresh store the refresh token in the HttpOnly
  `refresh_token` cookie. Browser requests to session endpoints should use
  `credentials: "include"`.
- `204` and some `200` operations have no response body.

Access labels below describe the intended frontend contract. `[bypass]` marks a
temporary implementation bypass; see the gap report before integrating it.

### Services

| Service | HTTPS | HTTP |
|---|---:|---:|
| Identity | `https://localhost:7282` | `http://localhost:5158` |
| Store | `https://localhost:7036` | `http://localhost:5169` |
| Order | `https://localhost:7092` | `http://localhost:5165` |
| Payment | `https://localhost:7080` | `http://localhost:5155` |
| Notification | `https://localhost:7112` | `http://localhost:5053` |

## 2. Identity

### Endpoints

| Method | Path | Access | Request | Success |
|---|---|---|---|---|
| `POST` | `/api/auth/register` | Public | `RegisterRequest` | `200 RegistrationResponse` |
| `POST` | `/api/auth/login` | Public | `LoginRequest` | `200 AccessTokenResponse` + refresh cookie |
| `POST` | `/api/auth/refresh` | Refresh cookie | none | `200 AccessTokenResponse` + rotated cookie |
| `POST` | `/api/auth/verify-email` | Public | `VerifyEmailOtpRequest` | `200 MessageResponse` |
| `POST` | `/api/auth/resend-otp` | Public | `ResendOtpRequest` | `200 MessageResponse` |
| `GET` | `/api/auth/me` | Bearer | none | `200 CurrentUserResponse` |
| `POST` | `/api/auth/logout` | Refresh cookie optional | none | `200 MessageResponse` |
| `GET` | `/api/account/profile` | Bearer | none | `200 UserDetailResponse` |
| `PUT` | `/api/account/profile` | Bearer | `UpdateMyProfileRequest` | `200 UserDetailResponse` |
| `PUT` | `/api/account/password` | Bearer | `ChangePasswordRequest` | `204 NoContent` |
| `POST` | `/api/user` | Admin | `AdminCreateUserRequest` | `200 UserDetailResponse` |
| `GET` | `/api/user` | Admin | `GetUsersQueryRequest` query | `200 PagedResult<UserResponse>` |
| `GET` | `/api/user/{id}` | Admin | none | `200 UserDetailResponse` |
| `PUT` | `/api/user/{id}` | Admin | `AdminUpdateUserRequest` | `200 NoContent` |
| `DELETE` | `/api/user/{id}` | Admin | none | `204 NoContent` |

> **Deprecated - do not use from the frontend:** `RoleController` exposes six
> Admin endpoints under `/api/role`, but StealDeal uses the three fixed roles
> `Customer`, `Seller`, and `Admin`. Dynamic role CRUD is not part of the
> product contract.

### Requests

```ts
export interface RegisterRequest {
  email: string;
  password: string; // application requires at least 8 characters
  firstName: string;
  lastName: string;
  phone?: string | null;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface VerifyEmailOtpRequest {
  email: string;
  otp: string;
}

export interface ResendOtpRequest {
  email: string;
}

export interface UpdateMyProfileRequest {
  fullName: string;
  phone?: string | null;
  avatarUrl?: string | null;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string; // at least 8 characters and different from current
}

export interface AdminCreateUserRequest {
  email: string;
  password: string;
  fullName: string;
  phone?: string | null;
  roles: Role[]; // at least one
}

export interface AdminUpdateUserRequest {
  fullName?: string | null;
  email?: string | null;
  phone?: string | null;
  isActive?: boolean | null;
  roles?: Role[] | null;
}

export interface GetUsersQueryRequest {
  searchTerm?: string;
  role?: Role;
  accountStatus?: "active" | "inactive";
  page?: number;     // default 1
  pageSize?: number; // default 10
}
```

Registration always creates a `Customer`. It returns a verification message,
not an access token. Login returns the first token pair.

### Responses

```ts
export interface RegistrationResponse {
  message: string;
  requiresEmailVerification: boolean;
}

export interface AccessTokenResponse {
  accessToken: string;
  accessTokenExpiresAt: ISODateTime;
}

// Anonymous response in AuthController; no named C# DTO yet.
export interface CurrentUserResponse {
  userId: string | null;
  email: string | null;
  name: string | null;
  roles: string[];
}

export interface UserAddressResponse {
  id: UUID;
  label: string;
  address: string;
  district: string;
  city: string;
  isDefault: boolean;
}

export interface UserTrustScoreResponse {
  id: UUID;
  score: number;
  totalOrders: number;
  successfulPickups: number;
  noShowCount: number;
  disputeCount: number;
  lastCalculatedAt: ISODateTime | null;
}

export interface UserDetailResponse {
  id: UUID;
  email: string | null;
  phone: string | null;
  fullName: string | null;
  avatarUrl: string | null;
  isEmailVerified: boolean;
  isActive: boolean;
  createdAt: ISODateTime;
  userAddresses: UserAddressResponse[];
  userTrustScore: UserTrustScoreResponse | null;
  roles: string[];
}

export interface UserResponse {
  id: UUID;
  email: string;
  phone: string | null;
  fullName: string;
  avatarUrl: string | null;
  isEmailVerified: boolean;
  isActive: boolean;
  createdAt: ISODateTime;
  userTrustScore: null; // current list mapper never assigns this property
  roles: string[];
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}
```

`UserAddressResponse` is currently nested read-only data. There are no address
create/update/delete endpoints.

## 3. Store

### Endpoints

| Method | Path | Access | Request | Success |
|---|---|---|---|---|
| `GET` | `/api/categories` | Public | none | `200 CategoryResponse[]` |
| `GET` | `/api/categories/{slug}` | Public | none | `200 CategoryResponse` |
| `POST` | `/api/categories` | Admin `[bypass]` | `CreateCategoryRequest` | `201 CategoryResponse` |
| `PUT` | `/api/categories/{id}` | Admin `[bypass]` | `UpdateCategoryRequest` | `200 CategoryResponse` |
| `DELETE` | `/api/categories/{id}` | Admin `[bypass]` | none | `204 NoContent` |
| `GET` | `/api/stores` | Public | none | `200 StoreProfileResponse[]` |
| `GET` | `/api/stores/{id}` | Public | none | `200 StoreProfileResponse` |
| `GET` | `/api/stores/me` | Seller `[bypass]` | none | `200 StoreProfileResponse` |
| `POST` | `/api/stores` | Seller `[bypass]` | `CreateStoreRequest` | `201 StoreProfileResponse` |
| `PUT` | `/api/stores/{id}` | Owning Seller `[bypass]` | `UpdateStoreRequest` | `200 StoreProfileResponse` |
| `PATCH` | `/api/stores/{id}/verify` | Admin `[bypass]` | none | `204 NoContent` |
| `PATCH` | `/api/stores/{id}/toggle-active` | Admin `[bypass]` | none | `204 NoContent` |
| `GET` | `/api/bags` | Public | none | `200 SurpriseBagResponse[]` |
| `GET` | `/api/bags/{id}` | Public | none | `200 SurpriseBagResponse` |
| `GET` | `/api/bags/store/{storeId}` | Public | none | `200 SurpriseBagResponse[]` |
| `POST` | `/api/bags` | Seller `[bypass]` | `CreateBagRequest` | `201 SurpriseBagResponse` |
| `PUT` | `/api/bags/{id}` | Owning Seller `[bypass]` | `UpdateBagRequest` | `200 SurpriseBagResponse` |
| `DELETE` | `/api/bags/{id}` | Owning Seller `[bypass]` | none | `204 NoContent` |
| `PATCH` | `/api/bags/{id}/status` | Owning Seller `[bypass]` | `UpdateBagStatusRequest` | `204 NoContent` |
| `GET` | `/api/reviews/store/{storeId}` | Public | none | `200 StoreReviewResponse[]` |
| `GET` | `/api/reviews/bag/{bagId}` | Public | none | `200 StoreReviewResponse[]` |
| `POST` | `/api/reviews` | Customer `[bypass]` | `CreateReviewRequest` | `201 StoreReviewResponse` |
| `PATCH` | `/api/reviews/{id}/reply` | Owning Seller `[bypass]` | `ReplyReviewRequest` | `204 NoContent` |
| `PATCH` | `/api/reviews/{id}/report` | Bearer `[bypass]` | none | `204 NoContent` |

`[bypass]` Store authorization attributes are currently commented out. Some actions
still read JWT claims and can fail without a token; store, bag, and order
creation also contain fixed development user IDs.

### Requests

```ts
export interface CreateCategoryRequest {
  name: string;
  slug: string;
  iconUrl?: string | null;
}

export interface UpdateCategoryRequest extends CreateCategoryRequest {
  isActive: boolean;
}

export interface CreateStoreRequest {
  name: string;
  description?: string | null;
  address?: string | null;
  latitude: number;
  longitude: number;
  phone?: string | null;
  bankAccount?: string | null;
  licenseUrl?: string | null;
}

export type UpdateStoreRequest = CreateStoreRequest;

export interface CreateBagRequest {
  name: string;
  description?: string | null;
  originalPrice: Money;
  salePrice: Money;
  quantityTotal: number;
  status: string;
  pickupStartTime: ISODateTime;
  pickupEndTime: ISODateTime;
  expiryDate: ISODateTime;
  categoryIds?: UUID[];
}

export interface UpdateBagRequest {
  name: string;
  description?: string | null;
  originalPrice: Money;
  salePrice: Money;
  quantityTotal: number;
  pickupStartTime: ISODateTime;
  pickupEndTime: ISODateTime;
  expiryDate: ISODateTime;
  categoryIds?: UUID[];
}

export interface UpdateBagStatusRequest {
  status: string;
}

export interface CreateReviewRequest {
  orderId: UUID;
  bagId: UUID;
  ratingScore: number; // 1..5
  comment?: string | null;
}

export interface ReplyReviewRequest {
  storeReply: string;
}
```

### Responses

```ts
export interface CategoryResponse {
  id: UUID;
  name: string;
  slug: string;
  iconUrl: string | null;
  isActive: boolean;
}

export interface StoreProfileResponse {
  id: UUID;
  ownerId: UUID;
  name: string;
  description: string | null;
  address: string | null;
  latitude: number;
  longitude: number;
  avatarUrl: string | null;
  phone: string | null;
  ratingScore: number;
  isVerify: boolean;
  isActive: boolean;
  createdAt: ISODateTime;
}

export interface SurpriseBagResponse {
  id: UUID;
  storeId: UUID;
  storeName: string;
  name: string;
  description: string | null;
  originalPrice: Money;
  salePrice: Money;
  quantityTotal: number;
  quantityRemaining: number;
  pickupStartTime: ISODateTime;
  pickupEndTime: ISODateTime;
  expiryDate: ISODateTime;
  status: string;
  categories: CategoryResponse[];
  createdAt: ISODateTime;
}

export interface StoreReviewResponse {
  id: UUID;
  orderId: UUID;
  buyerId: UUID;
  ratingScore: number;
  comment: string | null;
  storeReply: string | null;
  createdAt: ISODateTime;
}
```

Important current omissions:

- A store accepts `bankAccount` and `licenseUrl`, but `StoreProfileResponse`
  never returns them.
- `avatarUrl` is returned but cannot be set by either store request.
- A surprise bag has no image/media field in its model or DTO.
- A review response omits its stored `storeId`, `bagId`, and `isReported`.

## 4. Order

### Endpoints

| Method | Path | Access | Request | Success |
|---|---|---|---|---|
| `POST` | `/api/orders` | Customer `[bypass]` | `CreateOrderRequest` | `201 OrderResponse` |
| `GET` | `/api/orders/{id}` | Owner, Seller, or Admin | none | `200 OrderResponse` |
| `GET` | `/api/orders/my-orders` | Bearer | none | `200 OrderResponse[]` |
| `GET` | `/api/orders/store/{storeId}` | Seller or Admin | none | `200 OrderResponse[]` |
| `PATCH` | `/api/orders/{id}/status` | Bearer | `UpdateOrderStatusRequest` | `200 OrderResponse` |
| `POST` | `/api/pickup-disputes` | Bearer | `CreateDisputeRequest` | `201 PickupDisputeResponse` |
| `GET` | `/api/pickup-disputes/{id}` | Related user or Admin | none | `200 PickupDisputeResponse` |
| `GET` | `/api/pickup-disputes` | Admin | none | `200 PickupDisputeResponse[]` |
| `PATCH` | `/api/pickup-disputes/{id}/status` | Admin | `UpdateDisputeStatusRequest` | `200 PickupDisputeResponse` |

`[bypass]` Order creation currently has authorization commented out and assigns a
fixed development user ID.

### Requests

```ts
export interface CreateOrderItemRequest {
  bagId: UUID;
  bagNameSnapshot: string;
  unitPriceSnapshot: Money;
  quantity: number;
}

export interface CreateOrderRequest {
  storeId: UUID;
  storeNameSnapshot: string;
  deliveryFee: Money;
  voucherDiscount: Money;
  deliveryType: string;
  deliveryAddress: string;
  items: CreateOrderItemRequest[]; // at least one
}

export interface UpdateOrderStatusRequest {
  status: string;
}

export interface CreateDisputeRequest {
  orderId: UUID;
  disputeType: string;
  description: string;
  evidenceUrls?: string[];
}

export interface UpdateDisputeStatusRequest {
  status: string;
}
```

The server calculates each item subtotal and the final total, but currently
trusts the client-provided names, prices, fees, discounts, and IDs.

### Responses

```ts
export interface OrderItemResponse {
  id: UUID;
  bagId: UUID;
  bagNameSnapshot: string;
  unitPriceSnapshot: Money;
  quantity: number;
  subtotal: Money;
}

export interface OrderResponse {
  id: UUID;
  userId: UUID;
  storeId: UUID;
  storeNameSnapshot: string;
  deliveryFee: Money;
  voucherDiscount: Money;
  totalAmount: Money;
  deliveryType: string;
  deliveryAddress: string;
  pickupCode: string | null;
  status: string;
  pickupDeadline: ISODateTime | null;
  createdAt: ISODateTime;
  updatedAt: ISODateTime;
  items: OrderItemResponse[];
}

export interface PickupDisputeResponse {
  id: UUID;
  orderId: UUID;
  reporterId: UUID;
  disputeType: string;
  evidenceUrls: string[];
  description: string;
  status: string;
  createdAt: ISODateTime;
}
```

## 5. Payment

### Endpoints

| Method | Path | Access | Request | Success |
|---|---|---|---|---|
| `POST` | `/api/transactions` | Bearer | `CreateTransactionRequest` | `201 TransactionResponse` |
| `GET` | `/api/transactions/{id}` | Owner or Admin | none | `200 TransactionResponse` |
| `GET` | `/api/transactions/order/{orderId}` | Owner or Admin | none | `200 TransactionResponse` |
| `GET` | `/api/transactions/my-transactions` | Bearer | none | `200 TransactionResponse[]` |
| `PATCH` | `/api/transactions/{id}/status` | Admin | `UpdateTransactionStatusRequest` | `200 TransactionResponse` |
| `POST` | `/api/refunds` | Seller or Admin | `CreateRefundRequest` | `201 RefundResponse` |
| `GET` | `/api/refunds/{id}` | Transaction owner or Admin | none | `200 RefundResponse` |
| `GET` | `/api/refunds/transaction/{transactionId}` | Transaction owner or Admin | none | `200 RefundResponse[]` |
| `GET` | `/api/refunds` | Admin | none | `200 RefundResponse[]` |
| `PATCH` | `/api/refunds/{id}/status` | Admin | `UpdateRefundStatusRequest` | `200 RefundResponse` |

This is currently a manually updated payment ledger, not a real gateway
checkout.

### Requests

```ts
export interface CreateTransactionRequest {
  orderId: UUID;
  amount: Money;
  paymentMethod: string;
}

export interface UpdateTransactionStatusRequest {
  status: string;
  failureReason?: string | null;
  gatewayRef?: string | null;
}

export interface CreateRefundRequest {
  transactionId: UUID;
  amount: Money;
  reason: string;
}

export interface UpdateRefundStatusRequest {
  status: string;
}
```

### Responses

```ts
export interface RefundResponse {
  id: UUID;
  transactionId: UUID;
  orderId: UUID;
  amount: Money;
  reason: string;
  status: string;
  createdAt: ISODateTime;
  processedAt: ISODateTime | null;
}

export interface TransactionResponse {
  id: UUID;
  orderId: UUID;
  userId: UUID;
  amount: Money;
  paymentMethod: string;
  gatewayRef: string | null;
  status: string;
  failureReason: string | null;
  paidAt: ISODateTime | null;
  createdAt: ISODateTime;
  updatedAt: ISODateTime;
  refunds: RefundResponse[];
}
```

## 6. Notification

### Endpoints

| Method | Path | Access | Request | Success |
|---|---|---|---|---|
| `GET` | `/api/notifications` | Bearer | none | `200 NotificationResponse[]` |
| `GET` | `/api/notifications/unread-count` | Bearer | none | `200 number` |
| `PATCH` | `/api/notifications/{id}/read` | Owner | none | `200 NotificationResponse` |
| `PATCH` | `/api/notifications/read-all` | Bearer | none | `204 NoContent` |
| `POST` | `/api/notifications` | Internal/test `[bypass]` | `CreateNotificationRequest` | `201 NotificationResponse` |
| `DELETE` | `/api/notifications/{id}` | Owner | none | `204 NoContent` |

`[bypass]` Notification creation is currently public in code. Frontend applications
must not depend on it; production notifications should come from trusted
backend processes.

### Contracts

```ts
export interface CreateNotificationRequest {
  userId: UUID;
  title: string;
  body: string;
  type: string;
  actionUrl?: string | null;
  referenceId?: UUID | null;
  referenceType?: string | null;
}

export interface NotificationResponse {
  id: UUID;
  userId: UUID;
  title: string;
  body: string;
  type: string;
  actionUrl: string | null;
  referenceId: UUID | null;
  referenceType: string | null;
  isRead: boolean;
  createdAt: ISODateTime;
}
```

The unread-count response is a raw JSON integer such as `3`, not an object.

## 7. Values for Mock Data

Only roles are currently closed values. Most other values are stored as
unrestricted strings, so frontend types must remain `string` until the backend
contract is fixed.

| Field | Values currently created or handled by code |
|---|---|
| `role` | `Customer`, `Seller`, `Admin` |
| initial order status | `Pending` |
| saga order statuses | `InventoryReservationFailed`, `PaymentFailed`, `Confirmed` |
| manual cancellation | `Cancelled` |
| pickup delivery type | `Pickup` |
| initial dispute status | `Pending` |
| initial transaction status | `Pending` |
| handled transaction success | `Success` |
| initial refund status | `Pending` |
| handled refund completion | `Processed` |

For dummy UI, use those spellings but keep a single frontend constants file so
they can be replaced when the backend adopts final unions.

## 8. Frontend Field Boundaries

- Safe to model now: every field in the TypeScript contracts above.
- Do not invent sensitive public fields such as password hashes, full bank
  accounts, OTP hashes, refresh tokens, outbox messages, or processed messages.
- Bag images, seller-private store details, address editing, review moderation,
  trust-score history, safe checkout, and final status unions are not current
  API fields. The proposed shapes are in the separate gap report.
- Cart, favorites, vouchers, seller payouts, and seller onboarding do not yet
  have an agreed backend contract.
