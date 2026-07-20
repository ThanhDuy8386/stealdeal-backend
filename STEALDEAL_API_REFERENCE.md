# StealDeal Backend API Reference

> Code snapshot reviewed: 2026-07-20  
> Audience: frontend developers and future backend maintainers  
> Source of truth: controllers, DTOs, application services, mappings, repositories, middleware, and startup code under `src/Services`  
> Coverage: all 64 controller actions currently present

## 1. What This Project Is

StealDeal is a capstone ecommerce backend for discounted "surprise bags," similar to Too Good To Go. A seller creates a store and lists time-limited bags; a customer buys a bag for pickup or delivery; admins manage users, catalog data, store verification, disputes, transaction states, and refunds.

The backend uses five independently runnable ASP.NET Core microservices:

| Service | Owns | Local HTTPS base URL | Local HTTP URL |
|---|---|---:|---:|
| Identity | users, roles, JWT/refresh tokens, email OTP, trust score | `https://localhost:7282` | `http://localhost:5158` |
| Store | categories, stores, surprise bags, reviews | `https://localhost:7036` | `http://localhost:5169` |
| Order | orders, order items, pickup disputes | `https://localhost:7092` | `http://localhost:5165` |
| Payment | transactions and refunds | `https://localhost:7080` | `http://localhost:5155` |
| Notification | in-app notifications; Identity OTP event consumer | `https://localhost:7112` | `http://localhost:5053` |

There is currently no API gateway, so the frontend must use a base URL per service (or provide its own development proxy). Every service follows a four-layer Clean Architecture shape:

```text
API -> Application -> Domain
  \        ^
   -> Infrastructure -> Domain
```

- `API`: controllers, auth setup, middleware, dependency injection.
- `Application`: request/response DTOs, mappings, use-case services, business exceptions.
- `Domain`: entities and repository/unit-of-work interfaces.
- `Infrastructure`: EF Core SQL Server repositories/DbContext, JWT/password implementation, RabbitMQ workers.

Each service has its own database configuration: `IdentityDb`, `StoreDb`, `OrderDb`, `PaymentDb`, and `NotificationDb`.

## 2. Frontend Integration Conventions

### 2.1 JSON and HTTP

- Request and response JSON uses `camelCase` property names.
- GUID values are JSON strings such as `"18372b33-e8bf-47d6-8fc5-3171d1e35774"`.
- `DateTime` values are ISO-8601 JSON strings. Server-generated timestamps use UTC.
- `decimal` values are JSON numbers, not formatted currency strings.
- Nullable fields are marked with `?` in the schema tables below.
- List endpoints return a raw JSON array unless a different response is documented.
- The backend does not use a shared `{ data, message, success }` response envelope.
- `204 No Content` and some `200 OK` operations intentionally return no body.

### 2.2 Access Token

Send the Identity access token to protected endpoints:

```http
Authorization: Bearer <accessToken>
```

JWTs contain the user ID, email, name, and one or more role claims. Roles currently used by code are:

- `Customer` (product documents may call this role Buyer)
- `Seller`
- `Admin`

Role checks use these exact role names. Registration accepts only `Customer` or `Seller`.

### 2.3 Refresh-Token Cookie

Register, login, and refresh return only an access token in the JSON body. The raw refresh token is stored in an HttpOnly cookie:

| Cookie option | Value |
|---|---|
| Name | `refresh_token` |
| Path | `/api/auth` |
| HttpOnly | `true` |
| Secure | `true` |
| SameSite | `Lax` |

Browser calls to register, login, refresh, and logout should include credentials:

```ts
await fetch(`${identityBaseUrl}/api/auth/refresh`, {
  method: "POST",
  credentials: "include",
});
```

Because the cookie is `Secure`, use the HTTPS Identity URL in normal browser development.

There is also a likely browser-session mismatch in the current development settings: CORS allows an `http://localhost:3000` frontend, while the cookie is `Secure` and `SameSite=Lax` and the preferred Identity API is HTTPS. Modern schemeful same-site rules can prevent that cookie from being sent on cross-scheme `fetch` POST requests. A same-origin frontend proxy, an HTTPS frontend plus matching CORS origin, or an intentional development cookie-policy change may be required for refresh/logout to work reliably.

### 2.4 Current CORS Limitation

Only Identity registers a CORS policy, and it permits only `http://localhost:3000` with credentials. Store, Order, Payment, and Notification do not currently register CORS. Direct cross-origin browser calls to those four services will therefore be blocked unless the frontend uses a same-origin proxy or the backend adds CORS/gateway support.

### 2.5 Errors

Business exceptions normally return RFC-style `ProblemDetails`:

```json
{
  "title": "Bad Request",
  "status": 400,
  "detail": "Order must have at least one item.",
  "instance": "/api/orders"
}
```

| Status | Meaning |
|---:|---|
| `400` | bad input, invalid business state, invalid route/model binding |
| `401` | missing/invalid/expired token or invalid login/refresh token |
| `403` | authenticated but not allowed by role/ownership rule |
| `404` | entity not found |
| `409` | duplicate/conflicting entity or state |
| `500` | unhandled server error |

ASP.NET authentication/authorization failures may return an empty `401`/`403`, and automatic `[ApiController]` model validation may return `ValidationProblemDetails` with an `errors` object. The Identity exception middleware uses Vietnamese titles for some errors, although `detail` still carries the useful application message. Identity refresh has one special response when the cookie is absent:

```json
{ "message": "Refresh token is missing." }
```

## 3. Endpoint Overview

Auth labels in this document describe what the code enforces today:

- `Public`: no bearer token is enforced.
- `Bearer`: any authenticated user.
- `Role: X`: authenticated user with one of the named roles.
- `Claim only`: `[Authorize]` is currently commented out, but the action parses a user ID claim. An anonymous call is likely to become a `500`, not a clean `401`.
- `Public (intended ...)`: comments/product intent say the endpoint should be protected, but code currently exposes it.

## 4. Identity API

Base URL: `https://localhost:7282`

### 4.1 Authentication Endpoints

| Method | Path | Auth | Request | Success | Behavior and important errors |
|---|---|---|---|---|---|
| `POST` | `/api/auth/register` | Public | `RegisterRequest` | `200 AccessTokenResponse`; sets refresh cookie | Creates user, role, trust score (100), 10-minute OTP and outbox event. `400` invalid fields/role; `409` email exists. |
| `POST` | `/api/auth/login` | Public | `LoginRequest` | `200 AccessTokenResponse`; sets refresh cookie | Inactive/deleted users and bad credentials all return `401 Invalid credentials.` |
| `POST` | `/api/auth/refresh` | Refresh cookie | none | `200 AccessTokenResponse`; rotates cookie | Old refresh token is revoked. Missing/invalid/expired cookie returns `401`. |
| `POST` | `/api/auth/verify-email` | Public | `VerifyEmailOtpRequest` | `200 { message }` | Marks email verified. Invalid/expired OTP returns `400`. |
| `POST` | `/api/auth/resend-otp` | Public | `ResendOtpRequest` | `200 { message }` | Revokes active OTP and creates a new 10-minute OTP/outbox event. `400` user missing; `409` already verified. |
| `GET` | `/api/auth/me` | Bearer | none | `200 CurrentUserClaims` | Returns claims from the access token; does not query the database. |
| `POST` | `/api/auth/logout` | Public + optional refresh cookie | none | `200 { message }`; deletes cookie | Revokes a valid cookie if present. Operation is idempotent. |

Register example:

```json
{
  "email": "buyer@example.com",
  "password": "password123",
  "firstName": "An",
  "lastName": "Nguyen",
  "phone": "0900000000",
  "role": "Customer"
}
```

Token response:

```json
{
  "accessToken": "<jwt>",
  "accessTokenExpiresAt": "2026-07-20T03:30:00Z"
}
```

`GET /api/auth/me` response:

```json
{
  "userId": "18372b33-e8bf-47d6-8fc5-3171d1e35774",
  "email": "buyer@example.com",
  "name": "An Nguyen",
  "roles": ["Customer"]
}
```

### 4.2 User-Management Endpoints

All endpoints in this controller require a bearer token with the `Admin` role.

| Method | Path | Auth | Request | Success | Notes |
|---|---|---|---|---|---|
| `POST` | `/api/user` | Role: Admin | `AdminCreateUserRequest` | `201 UserDetailResponse` | Creates an active, verified account with a hashed password, selected roles, and initial trust score. Does not issue tokens or create an OTP. |
| `GET` | `/api/user` | Role: Admin | query parameters | `200 PagedResult<UserResponse>` | Deleted users excluded; newest first. |
| `GET` | `/api/user/{id}` | Role: Admin | none | `200 UserDetailResponse` | `404` if missing. |
| `PUT` | `/api/user/{id}` | Role: Admin | `AdminUpdateUserRequest` | `200`, empty body | Updates the user's profile, account status, and roles. |
| `DELETE` | `/api/user/{id}` | Role: Admin | none | `204` | Soft-deletes/deactivates the user. |

`GET /api/user` query parameters:

| Parameter | Type | Default | Behavior |
|---|---|---:|---|
| `searchTerm` | string? | none | Case-insensitive contains match on email or full name. |
| `role` | string? | none | Exact database role match. |
| `accountStatus` | string? | none | `active` or `inactive`; any other value means no status filter. |
| `page` | integer? | `1` | No positive-range validation currently. |
| `pageSize` | integer? | `10` | No range/max validation currently. |

Example:

```http
GET /api/user?searchTerm=nguyen&role=Seller&accountStatus=active&page=1&pageSize=20
```

### 4.3 Self-Service Account Endpoints

These endpoints always derive the target user ID from the bearer token. They do not accept another user's ID.

| Method | Path | Auth | Request | Success | Notes |
|---|---|---|---|---|---|
| `GET` | `/api/account/profile` | Bearer | none | `200 UserDetailResponse` | Returns the current active user's profile. |
| `PUT` | `/api/account/profile` | Bearer | `UpdateMyProfileRequest` | `200 UserDetailResponse` | Replaces full name, phone, and avatar URL. Does not allow email, role, status, or trust-score changes. |
| `PUT` | `/api/account/password` | Bearer | `ChangePasswordRequest` | `204` | Verifies the current password, changes it, revokes all active refresh tokens, and deletes the refresh cookie. |

Profile-update example:

```json
{
  "fullName": "An Nguyen",
  "phone": "0900000000",
  "avatarUrl": "https://cdn.example.com/users/an.jpg"
}
```

Send `null` for `phone` or `avatarUrl` to clear that field. After a successful password change, the frontend should clear its cached access token and return to login. Existing access tokens remain valid until their normal expiry because access-token revocation is not implemented.

## 5. Store API

Base URL: `https://localhost:7036`

### 5.1 Categories

| Method | Path | Auth | Request | Success | Notes |
|---|---|---|---|---|---|
| `GET` | `/api/categories` | Public | none | `200 CategoryResponse[]` | Includes active and inactive categories. |
| `GET` | `/api/categories/{slug}` | Public | none | `200 CategoryResponse` | Lookup is exact; create/update normalize slug to lowercase. |
| `POST` | `/api/categories` | Public (intended Admin) | `CreateCategoryRequest` | `201 CategoryResponse` | Slug must be unique; new category is active. |
| `PUT` | `/api/categories/{id}` | Public (intended Admin) | `UpdateCategoryRequest` | `200 CategoryResponse` | Does not re-check slug uniqueness in the service. |
| `DELETE` | `/api/categories/{id}` | Public (intended Admin) | none | `204` | Soft delete: sets `isActive = false`. |

### 5.2 Store Profiles

| Method | Path | Auth | Request | Success | Notes |
|---|---|---|---|---|---|
| `GET` | `/api/stores` | Public | none | `200 StoreProfileResponse[]` | Includes verified/unverified and active/inactive stores. |
| `GET` | `/api/stores/{id}` | Public | none | `200 StoreProfileResponse` | `404` if missing. |
| `GET` | `/api/stores/me` | Claim only (intended Seller) | none | `200 StoreProfileResponse` | Store is resolved from JWT owner ID. |
| `POST` | `/api/stores` | Claim only (intended Seller) | `CreateStoreRequest` | `201 StoreProfileResponse` | One store per owner; defaults unverified and active. |
| `PUT` | `/api/stores/{id}` | Claim only (intended Seller) | `UpdateStoreRequest` | `200 StoreProfileResponse` | Service verifies owner ID. |
| `PATCH` | `/api/stores/{id}/verify` | Public (intended Admin) | none | `204` | Sets `isVerify = true`; no unverify endpoint. |
| `PATCH` | `/api/stores/{id}/toggle-active` | Public (intended Admin) | none | `204` | Flips the current active state. |

Create-store example:

```json
{
  "name": "Green Bakery",
  "description": "Daily bakery surprise bags",
  "address": "12 Example Street",
  "latitude": 10.7769,
  "longitude": 106.7009,
  "phone": "0900000000",
  "bankAccount": "0123456789",
  "licenseUrl": "https://cdn.example.com/license.jpg"
}
```

### 5.3 Surprise Bags

| Method | Path | Auth | Request | Success | Notes |
|---|---|---|---|---|---|
| `GET` | `/api/bags` | Public | none | `200 SurpriseBagResponse[]` | No filtering/paging; includes all statuses/expired bags. |
| `GET` | `/api/bags/{id}` | Public | none | `200 SurpriseBagResponse` | Includes store name and categories. |
| `GET` | `/api/bags/store/{storeId}` | Public | none | `200 SurpriseBagResponse[]` | Current repository does not load Store/Categories here; `storeName` may be empty and `categories` empty. |
| `POST` | `/api/bags` | Claim only (intended Seller) | `CreateBagRequest` | `201 SurpriseBagResponse` | Seller's store must exist, be verified, and be active. Remaining quantity starts equal to total. |
| `PUT` | `/api/bags/{id}` | Claim only (intended Seller) | `UpdateBagRequest` | `200 SurpriseBagResponse` | Owner enforced. Omitting/empty `categoryIds` keeps existing categories. Updating total does not recalculate remaining quantity. |
| `DELETE` | `/api/bags/{id}` | Public (intended Seller) | none | `204` | Hard delete. No JWT or owner check is currently performed. |
| `PATCH` | `/api/bags/{id}/status` | Claim only (intended Seller) | `UpdateBagStatusRequest` | `204` | Owner enforced; status is an unrestricted string. |

Create-bag example:

```json
{
  "name": "Evening Bakery Bag",
  "description": "A mix of unsold baked goods",
  "originalPrice": 200000,
  "salePrice": 70000,
  "quantityTotal": 10,
  "status": "Available",
  "pickupStartTime": "2026-07-20T10:00:00Z",
  "pickupEndTime": "2026-07-20T12:00:00Z",
  "expiryDate": "2026-07-20T13:00:00Z",
  "categoryIds": ["a220a7ea-84e9-4449-8c6e-6e27a4913b87"]
}
```

There are currently no service validations for positive prices/quantity, sale price versus original price, pickup order, expiry, duplicate category IDs, or allowed bag-status values.

### 5.4 Store Reviews

| Method | Path | Auth | Request | Success | Notes |
|---|---|---|---|---|---|
| `GET` | `/api/reviews/store/{storeId}` | Public | none | `200 StoreReviewResponse[]` | No pagination or defined sort. |
| `GET` | `/api/reviews/bag/{bagId}` | Public | none | `200 StoreReviewResponse[]` | No pagination or defined sort. |
| `POST` | `/api/reviews` | Claim only (intended authenticated buyer) | `CreateReviewRequest` | `201 StoreReviewResponse` | Rating must be 1-5; one review per order; bag must exist. Order ownership/completion is not verified cross-service. |
| `PATCH` | `/api/reviews/{id}/reply` | Claim only (intended Seller) | `ReplyReviewRequest` | `204` | Seller ownership of reviewed store is enforced. |
| `PATCH` | `/api/reviews/{id}/report` | Claim only (intended authenticated user) | none | `204` | Only one global report flag exists; second report returns `409`. |

## 6. Order API

Base URL: `https://localhost:7092`

### 6.1 Orders

| Method | Path | Auth | Request | Success | Notes |
|---|---|---|---|---|---|
| `POST` | `/api/orders` | Bearer | `CreateOrderRequest` | `201 OrderResponse` | At least one item required. Server computes item subtotals/total, initial status, pickup code/deadline. |
| `GET` | `/api/orders/{id}` | Bearer | none | `200 OrderResponse` | Admin, owning buyer, or any Seller can view. Seller/store ownership is not checked. |
| `GET` | `/api/orders/my-orders` | Bearer | none | `200 OrderResponse[]` | Current user's orders; no pagination or guaranteed sorting. |
| `GET` | `/api/orders/store/{storeId}` | Role: Seller or Admin | none | `200 OrderResponse[]` | Store ownership is not checked. |
| `PATCH` | `/api/orders/{id}/status` | Bearer | `UpdateOrderStatusRequest` | `200 OrderResponse` | Buyer can cancel own pending order. Any Seller/Admin can cancel pending or set any other free-form status. |

Create-order example:

```json
{
  "storeId": "b16389bb-d082-440d-ab94-d8b5139f7a67",
  "storeNameSnapshot": "Green Bakery",
  "deliveryFee": 0,
  "voucherDiscount": 10000,
  "deliveryType": "Pickup",
  "deliveryAddress": "12 Example Street",
  "items": [
    {
      "bagId": "d84d9812-0cb4-49e7-bb7c-79b30967129e",
      "bagNameSnapshot": "Evening Bakery Bag",
      "unitPriceSnapshot": 70000,
      "quantity": 2
    }
  ]
}
```

The Order service currently trusts all store/bag names, prices, IDs, delivery fees, voucher discounts, and quantities supplied by the frontend. It does not call Store, reserve stock, validate that all bags belong to the given store, or publish `order.created`. The server calculates:

```text
item.subtotal = unitPriceSnapshot * quantity
totalAmount = max(sum(item.subtotal) + deliveryFee - voucherDiscount, 0)
```

If `deliveryType` equals `Pickup` ignoring case, the server generates an 8-character pickup code and a pickup deadline 24 hours from creation.

### 6.2 Pickup Disputes

| Method | Path | Auth | Request | Success | Notes |
|---|---|---|---|---|---|
| `POST` | `/api/pickup-disputes` | Bearer | `CreateDisputeRequest` | `201 PickupDisputeResponse` | Order must exist. Current code does not actually reject unrelated reporters. Initial status is `Pending`. |
| `GET` | `/api/pickup-disputes/{id}` | Bearer | none | `200 PickupDisputeResponse` | Allowed for Admin, reporter, or order buyer. |
| `GET` | `/api/pickup-disputes` | Role: Admin | none | `200 PickupDisputeResponse[]` | No pagination or guaranteed sorting. |
| `PATCH` | `/api/pickup-disputes/{id}/status` | Role: Admin | `UpdateDisputeStatusRequest` | `200 PickupDisputeResponse` | Status is an unrestricted string. |

## 7. Payment API

Base URL: `https://localhost:7080`

### 7.1 Transactions

| Method | Path | Auth | Request | Success | Notes |
|---|---|---|---|---|---|
| `POST` | `/api/transactions` | Bearer | `CreateTransactionRequest` | `201 TransactionResponse` | Initial status `Pending`. Conflicts if first transaction found for order is `Pending` or `Success`. Order/amount are not cross-service validated. |
| `GET` | `/api/transactions/{id}` | Bearer | none | `200 TransactionResponse` | Owner or Admin. |
| `GET` | `/api/transactions/order/{orderId}` | Bearer | none | `200 TransactionResponse` | Owner or Admin; returns one transaction. |
| `GET` | `/api/transactions/my-transactions` | Bearer | none | `200 TransactionResponse[]` | Newest first; no pagination. |
| `PATCH` | `/api/transactions/{id}/status` | Role: Admin | `UpdateTransactionStatusRequest` | `200 TransactionResponse` | Status unrestricted. Setting `Success` sets `paidAt`; later status changes do not clear it. |

Create-transaction example:

```json
{
  "orderId": "410d13ba-d6e5-4bbf-a6f9-e1f93a70f00e",
  "amount": 130000,
  "paymentMethod": "Mock"
}
```

### 7.2 Refunds

| Method | Path | Auth | Request | Success | Notes |
|---|---|---|---|---|---|
| `POST` | `/api/refunds` | Role: Seller or Admin | `CreateRefundRequest` | `201 RefundResponse` | Transaction must be `Success`; pending + processed refunds cannot exceed transaction amount. Seller ownership is not checked. |
| `GET` | `/api/refunds/{id}` | Bearer | none | `200 RefundResponse` | Original transaction owner or Admin. |
| `GET` | `/api/refunds/transaction/{transactionId}` | Bearer | none | `200 RefundResponse[]` | Original transaction owner or Admin; newest first. |
| `GET` | `/api/refunds` | Role: Admin | none | `200 RefundResponse[]` | Newest first; no pagination. |
| `PATCH` | `/api/refunds/{id}/status` | Role: Admin | `UpdateRefundStatusRequest` | `200 RefundResponse` | Status unrestricted. Setting `Processed` sets `processedAt`; later changes do not clear it. |

There is no real payment gateway, callback/IPN verification, order status synchronization, or RabbitMQ event publication in Payment yet.

## 8. Notification API

Base URL: `https://localhost:7112`

| Method | Path | Auth | Request | Success | Notes |
|---|---|---|---|---|---|
| `GET` | `/api/notifications` | Bearer | none | `200 NotificationResponse[]` | Current user's notifications, newest first. |
| `GET` | `/api/notifications/unread-count` | Bearer | none | `200` raw integer | Example response: `3`. |
| `PATCH` | `/api/notifications/{id}/read` | Bearer | none | `200 NotificationResponse` | Owner only; idempotent when already read. |
| `PATCH` | `/api/notifications/read-all` | Bearer | none | `204` | Marks all current-user notifications read. |
| `POST` | `/api/notifications` | Public (intended Admin/system test) | `CreateNotificationRequest` | `201 NotificationResponse` | Caller can currently create a notification for any user ID. |
| `DELETE` | `/api/notifications/{id}` | Bearer | none | `204` | Owner only; hard delete. |

Notification currently consumes `identity.user.email-verification.requested` RabbitMQ events and saves the OTP in an in-app notification. It does not send a real email. No order/payment notification consumers exist yet.

## 9. Request Schemas

Fields marked `required` are non-nullable in the DTO or explicitly required by application code. The code has limited validation beyond the rules stated below.

### 9.1 Identity Requests

| Schema | Fields |
|---|---|
| `RegisterRequest` | `email: string` required; `password: string` required, min 8; `firstName: string` required; `lastName: string` required; `phone: string?`; `role: "Customer" | "Seller"` required |
| `LoginRequest` | `email: string` required; `password: string` required |
| `VerifyEmailOtpRequest` | `email: string` required; `otp: string` required |
| `ResendOtpRequest` | `email: string` required |
| `AdminCreateUserRequest` | `email: string` required; `password: string` required, min 8; `fullName: string` required; `phone: string?`; `roles: ("Customer" | "Seller" | "Admin")[]` with at least one role |
| `AdminUpdateUserRequest` | `fullName: string?`; `email: string?`; `phone: string?`; `isActive: boolean?`; `roles: ("Customer" | "Seller" | "Admin")[]?` |
| `UpdateMyProfileRequest` | `fullName: string` required; `phone: string?`; `avatarUrl: string?` |
| `ChangePasswordRequest` | `currentPassword: string` required; `newPassword: string` required, min 8 and different from current password |

Admin role values are accepted case-insensitively and normalized to their canonical casing. User-update email is not normalized and is not checked for uniqueness by the application service.

### 9.2 Store Requests

| Schema | Fields |
|---|---|
| `CreateCategoryRequest` | `name: string` required; `slug: string` required; `iconUrl: string?` |
| `UpdateCategoryRequest` | `name: string` required; `slug: string` required; `iconUrl: string?`; `isActive: boolean` |
| `CreateStoreRequest` | `name: string` required; `description: string?`; `address: string?`; `latitude: number`; `longitude: number`; `phone: string?`; `bankAccount: string?`; `licenseUrl: string?` |
| `UpdateStoreRequest` | same fields as `CreateStoreRequest` |
| `CreateBagRequest` | `name: string` required; `description: string?`; `originalPrice: number`; `salePrice: number`; `quantityTotal: integer`; `status: string` required; `pickupStartTime: datetime`; `pickupEndTime: datetime`; `expiryDate: datetime`; `categoryIds: uuid[]` |
| `UpdateBagRequest` | `name: string` required; `description: string?`; `originalPrice: number`; `salePrice: number`; `quantityTotal: integer`; `pickupStartTime: datetime`; `pickupEndTime: datetime`; `expiryDate: datetime`; `categoryIds: uuid[]` |
| `UpdateBagStatusRequest` | `status: string` required |
| `CreateReviewRequest` | `orderId: uuid`; `bagId: uuid`; `ratingScore: integer` from 1 through 5; `comment: string?` |
| `ReplyReviewRequest` | `storeReply: string` required |

### 9.3 Order Requests

| Schema | Fields |
|---|---|
| `CreateOrderRequest` | `storeId: uuid`; `storeNameSnapshot: string` required; `deliveryFee: number`; `voucherDiscount: number`; `deliveryType: string` required; `deliveryAddress: string` required; `items: CreateOrderItemRequest[]` with at least one item |
| `CreateOrderItemRequest` | `bagId: uuid`; `bagNameSnapshot: string` required; `unitPriceSnapshot: number`; `quantity: integer` |
| `UpdateOrderStatusRequest` | `status: string` required |
| `CreateDisputeRequest` | `orderId: uuid`; `disputeType: string` required; `description: string` required; `evidenceUrls: string[]` |
| `UpdateDisputeStatusRequest` | `status: string` required |

### 9.4 Payment and Notification Requests

| Schema | Fields |
|---|---|
| `CreateTransactionRequest` | `orderId: uuid`; `amount: number`; `paymentMethod: string` required |
| `UpdateTransactionStatusRequest` | `status: string` required; `failureReason: string?`; `gatewayRef: string?` |
| `CreateRefundRequest` | `transactionId: uuid`; `amount: number`; `reason: string` required |
| `UpdateRefundStatusRequest` | `status: string` required |
| `CreateNotificationRequest` | `userId: uuid`; `title: string` required; `body: string` required; `type: string` required; `actionUrl: string?`; `referenceId: uuid?`; `referenceType: string?` |

## 10. Response Schemas

### 10.1 Identity Responses

| Schema | Fields |
|---|---|
| `AccessTokenResponse` | `accessToken: string`; `accessTokenExpiresAt: datetime` |
| `CurrentUserClaims` | `userId: string?`; `email: string?`; `name: string?`; `roles: string[]` |
| `PagedResult<T>` | `items: T[]`; `page: integer`; `pageSize: integer`; `totalCount: integer`; `totalPages: integer` |
| `UserResponse` | `id: uuid`; `email: string`; `phone: string?`; `fullName: string`; `avatarUrl: string?`; `isEmailVerified: boolean`; `isActive: boolean`; `createdAt: datetime`; `userTrustScore: object?`; `roles: string[]` |
| `UserDetailResponse` | all basic user fields above plus `userAddresses: UserAddressResponse[]`, `userTrustScore: UserTrustScoreResponse?`, `roles: string[]` |
| `UserAddressResponse` | `id: uuid`; `label: string`; `address: string`; `district: string`; `city: string`; `isDefault: boolean` |
| `UserTrustScoreResponse` | `id: uuid`; `score: integer`; `totalOrders: integer`; `successfulPickups: integer`; `noShowCount: integer`; `disputeCount: integer`; `lastCalculatedAt: datetime?` |

`UserResponse.userTrustScore` is currently always left unset by `UserMapping` for list results and therefore normally serializes as `null`. Use the detail endpoint for the mapped trust-score DTO.

### 10.2 Store Responses

| Schema | Fields |
|---|---|
| `CategoryResponse` | `id: uuid`; `name: string`; `slug: string`; `iconUrl: string?`; `isActive: boolean` |
| `StoreProfileResponse` | `id: uuid`; `ownerId: uuid`; `name: string`; `description: string?`; `address: string?`; `latitude: number`; `longitude: number`; `avatarUrl: string?`; `phone: string?`; `ratingScore: number`; `isVerify: boolean`; `isActive: boolean`; `createdAt: datetime` |
| `SurpriseBagResponse` | `id: uuid`; `storeId: uuid`; `storeName: string`; `name: string`; `description: string?`; `originalPrice: number`; `salePrice: number`; `quantityTotal: integer`; `quantityRemaining: integer`; `pickupStartTime: datetime`; `pickupEndTime: datetime`; `expiryDate: datetime`; `status: string`; `categories: CategoryResponse[]`; `createdAt: datetime` |
| `StoreReviewResponse` | `id: uuid`; `orderId: uuid`; `buyerId: uuid`; `ratingScore: integer`; `comment: string?`; `storeReply: string?`; `createdAt: datetime` |

Store responses intentionally omit some stored data. For example, `StoreProfileResponse` omits `bankAccount`, `licenseUrl`, and `updatedAt`; `StoreReviewResponse` omits `storeId`, `bagId`, and `isReported`.

### 10.3 Order Responses

| Schema | Fields |
|---|---|
| `OrderItemResponse` | `id: uuid`; `bagId: uuid`; `bagNameSnapshot: string`; `unitPriceSnapshot: number`; `quantity: integer`; `subtotal: number` |
| `OrderResponse` | `id: uuid`; `userId: uuid`; `storeId: uuid`; `storeNameSnapshot: string`; `deliveryFee: number`; `voucherDiscount: number`; `totalAmount: number`; `deliveryType: string`; `deliveryAddress: string`; `pickupCode: string?`; `status: string`; `pickupDeadline: datetime?`; `createdAt: datetime`; `updatedAt: datetime`; `items: OrderItemResponse[]` |
| `PickupDisputeResponse` | `id: uuid`; `orderId: uuid`; `reporterId: uuid`; `disputeType: string`; `evidenceUrls: string[]`; `description: string`; `status: string`; `createdAt: datetime` |

### 10.4 Payment and Notification Responses

| Schema | Fields |
|---|---|
| `TransactionResponse` | `id: uuid`; `orderId: uuid`; `userId: uuid`; `amount: number`; `paymentMethod: string`; `gatewayRef: string?`; `status: string`; `failureReason: string?`; `paidAt: datetime?`; `createdAt: datetime`; `updatedAt: datetime`; `refunds: RefundResponse[]` |
| `RefundResponse` | `id: uuid`; `transactionId: uuid`; `orderId: uuid`; `amount: number`; `reason: string`; `status: string`; `createdAt: datetime`; `processedAt: datetime?` |
| `NotificationResponse` | `id: uuid`; `userId: uuid`; `title: string`; `body: string`; `type: string`; `actionUrl: string?`; `referenceId: uuid?`; `referenceType: string?`; `isRead: boolean`; `createdAt: datetime` |

## 11. Current Workflow Versus Planned Workflow

### 11.1 Implemented Cross-Service Flow

```text
Register or resend OTP in Identity
  -> Identity transaction saves EmailVerification + OutboxMessage
  -> Identity background worker publishes RabbitMQ event
  -> Notification background consumer receives event
  -> Notification database stores an in-app OTP notification
```

RabbitMQ details:

- Exchange: `stealdeal.events` (`topic`, durable)
- Routing key: `identity.user.email-verification.requested`
- Queue: `notification.email-verification`
- Binding: `identity.user.email-verification.#`

### 11.2 Not Implemented Yet

The following parts described in the review/roadmap are plans, not current API behavior:

- order-created event and saga orchestration;
- Store stock reservation/release;
- Store/Order/Payment event consumers and producers;
- real payment gateway;
- automatic order changes after payment success/failure;
- refund compensation;
- order/payment notifications;
- real OTP email delivery;
- consumer idempotency and dead-letter queues;
- cross-service store, order, bag, price, and ownership verification;
- API gateway and shared response wrapper.

For the current frontend, creation is a manual sequence:

```text
Browse Store API
  -> POST Order API with frontend-provided snapshots/prices
  -> POST Payment transaction with frontend-provided order ID/amount
  -> Admin PATCHes transaction/order statuses for the mock flow
```

## 12. OpenAPI Status

There is no Swagger UI. In Development, Store, Order, Payment, and Notification call `MapOpenApi()` and should expose generated JSON at:

```text
https://localhost:<service-port>/openapi/v1.json
```

Identity does not register or map OpenAPI. This Markdown reference is therefore the only single document covering all five services and also records application-level rules that generated schemas would miss.

## 13. Frontend Mapping Checklist and Known Risks

Before treating the API as production-safe, account for these current-code facts:

1. Configure five base URLs; there is no gateway.
2. Always use `credentials: "include"` for Identity session endpoints.
3. Resolve the current HTTP-frontend/HTTPS-API `SameSite=Lax` refresh-cookie mismatch, preferably with a same-origin proxy or aligned HTTPS origins.
4. Use role string `Customer`, not `Buyer`, in registration and role checks.
5. Add a frontend proxy or backend CORS for Store, Order, Payment, and Notification.
6. Do not assume list endpoints are paginated or filtered; only `GET /api/user` is paginated.
7. Treat statuses and payment methods as backend-unrestricted strings for now; centralize temporary frontend constants so they are easy to change.
8. Do not trust UI authorization alone. Several current admin/seller Store endpoints and Notification's test-create endpoint are actually public.
9. Expect claim-only Store endpoints to fail poorly when no JWT is sent because `[Authorize]` is commented out.
10. Do not assume creating an order reduces bag quantity or that payment updates the order.
11. Do not assume the backend recalculates price from Store data; Order and Payment trust frontend amounts.
12. `GET /api/bags/store/{storeId}` may return incomplete nested store/category display data; prefer `GET /api/bags/{id}` when full bag detail is needed.
13. The July 19 context documents say Payment still reads the wrong connection string, but current `Payment.API/Program.cs` and `appsettings.json` both use `PaymentDb`; that documented issue is already fixed in the checked-out code.

## 14. Maintenance Rule

When controllers, DTOs, response mappings, auth attributes, local ports, or application-service rules change, update this file in the same change. The controller route is authoritative for URL/method/auth, the DTO is authoritative for wire shape, and the application service/mapping is authoritative for business behavior and defaults.
