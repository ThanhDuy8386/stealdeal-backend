# Redis Cart Service Step-By-Step Implementation Plan

> Updated: 2026-09-16  
> Context: Order saga is already completed end-to-end. Current frontend cart uses `useState`, so cart is lost on reload and unavailable across devices.  
> Goal: add a Redis-backed Cart service so authenticated users can persist cart state across reloads, tabs, and devices before checkout.

## 1. Target Decision

Create a separate Cart service:

```text
Services/Cart
  StealDeal.Services.Cart.API
  StealDeal.Services.Cart.Application
  StealDeal.Services.Cart.Domain
  StealDeal.Services.Cart.Infrastructure
```

Cart data is stored in Redis, not in a separate SQL database.

The frontend should stop treating `useState` as the source of truth for cart.
After Redis Cart exists, frontend state is only a UI cache.

## 2. Clear Answer For The New Flow

### Current Flow

```text
Frontend cart useState
  -> user clicks submit/place order
  -> frontend sends userId + list products + quantities to Order API
  -> Order saga starts
```

Problem:

```text
Reload page -> cart is gone
Login from another device -> cart is gone
Frontend is carrying too much order input data
```

### New Flow With Cart Service

```text
Frontend add/remove/update cart
  -> calls Cart API
  -> Cart service stores cart in Redis by userId

Frontend checkout/place order
  -> chooses one store cart
  -> calls checkout endpoint for that cart
  -> backend reads that store cart from Redis
  -> backend creates one Order from that Redis cart snapshot
  -> existing Order saga continues unchanged
```

Recommended request ownership:

```text
Add item:
  Frontend -> Cart API

Remove item:
  Frontend -> Cart API

Update quantity:
  Frontend -> Cart API

Get cart:
  Frontend -> Cart API

Place order:
  Frontend -> Order API checkout-from-cart endpoint with storeId/cart selector
  Order API -> reads selected store cart from Cart service or shared Cart application contract
```

The order request should no longer contain the full cart detail from frontend.
It should contain only checkout-level fields, such as note, pickup type, address
if needed, and idempotency key.

Example:

```http
POST /api/orders/checkout-from-cart
Authorization: Bearer <token>
Idempotency-Key: <client-generated-guid>
```

```json
{
  "storeId": "store-id",
  "note": "Please prepare before 6 PM"
}
```

## 3. Important Boundary: What Cart Owns And Does Not Own

Cart service owns temporary buyer intent:

```text
User wants to buy bag A, quantity 2
User wants to buy bag B, quantity 1
Cart should survive reload/device switch
Cart should expire if abandoned
User can have multiple active carts at the same time
Bags from the same store belong to the same cart
Each store cart becomes a separate order at checkout
```

Cart service does not own:

```text
Final order history
Final inventory truth
Payment status
Reservation/release logic
Audit/history after checkout
```

Store service remains the source of truth for:

```text
Bag exists
Bag is active
Bag price
Bag storeId
Bag pickup window
Bag quantity remaining
```

Order service remains the source of truth for:

```text
Created order
Order items
Order status
Order saga state
```

Payment service remains the source of truth for:

```text
VNPAY transaction
Payment completed/failed
Payment expiration
```

Redis Cart should disappear from the critical flow after a Pending order has
been created successfully.

## 4. Should Cart Call Store Service?

Yes, but only where it brings value.

Because bag information belongs to Store service, Cart service should not invent
bag details and should not fully trust bag details sent from frontend.

Recommended rule:

```text
Frontend sends bagId + quantity.
Cart service asks Store service for bag snapshot when needed.
Cart service stores that snapshot in Redis for display and later checkout.
```

### When Cart Should Call Store

Call Store when adding a new bag:

```text
POST /api/cart/items
  -> Cart calls Store: get bag cart snapshot
  -> Cart validates bag exists/active/basic purchasable state
  -> Cart saves cart in Redis
```

Call Store during checkout pre-check:

```text
POST /api/orders/checkout-from-cart
  -> backend reads Redis cart
  -> backend optionally asks Store if bags still exist/active
  -> backend creates Pending order
  -> Store saga still performs final inventory reservation
```

### When Cart Does Not Need To Call Store

Do not call Store for simple remove:

```text
DELETE /api/cart/items/{bagId}
  -> remove from Redis only
```

Usually do not call Store for quantity change of an existing cart item:

```text
PATCH /api/cart/items/{bagId}
  -> update quantity in Redis
```

Optional: call Store on quantity increase if you want early UX feedback, but do
not treat that as a real reservation.

## 5. Can Client Send Bag Details To Cart Service?

Client can send bag details only as optional UI hints, but Cart service should
not trust them.

Acceptable for optimistic UI:

```json
{
  "bagId": "bag-1",
  "quantity": 2,
  "clientSnapshot": {
    "bagName": "Bakery Surprise Bag",
    "unitPrice": 59000,
    "imageUrl": "..."
  }
}
```

Backend rule:

```text
Use clientSnapshot only if Store call fails and only for temporary display? Avoid for v1.
Use Store response as the stored snapshot.
Never use client-sent price to create final order/payment.
```

Recommended v1 request:

```json
{
  "bagId": "bag-1",
  "quantity": 2
}
```

## 6. What "Validate" Means Here

There are two different validations.

### Cart/Checkout Pre-Validation

This is for user experience and obvious errors:

```text
Bag does not exist
Bag is inactive/deleted
Bag pickup window is already invalid
Cart mixes multiple stores but system only supports one store per order
Quantity is <= 0
```

This does not reserve stock.

### Store Reservation In Saga

This is the final inventory truth:

```text
Order publishes order.created
Store consumes order.created
Store checks QuantityRemaining
Store reserves inventory
Store publishes inventory.reserved or inventory.reservation_failed
```

Keep this as-is. Redis Cart should not replace the saga reservation.

## 7. When Redis Actually Helps

Redis helps at the exact point where `useState` is weak:

```text
Reload page -> cart still exists
Open another browser/device -> cart still exists after login
Close tab and come back later -> cart still exists until TTL expires
Frontend becomes lighter -> cart source of truth is backend
Cart writes are fast -> add/remove/update does not need SQL order draft rows
TTL cleanup -> abandoned carts disappear automatically
Checkout lock -> prevent double-click creating duplicate orders
```

Redis is not used to make inventory consistent. Redis is used to persist
temporary cart state.

Consistency is achieved by this rule:

```text
Redis Cart can be stale.
Store reservation decides final stock truth.
Payment saga decides final payment truth.
```

## 8. Redis Concepts To Understand Before Coding

### 8.1 Key

A Redis key is like a named slot.

Recommended cart key:

```text
cart:user:{userId}:store:{storeId}
```

Example:

```text
cart:user:7b9e7f0e-0b6d-4f8a-95a5-8327d80c12f1:store:2c4d9f91-8d8e-4f2e-ae7f-80c9e5e9a001
```

Store cart index key:

```text
cart:user:{userId}:stores
```

This is a Redis Set containing storeIds that currently have cart keys for the
user. It lets `GET /api/cart` list carts without scanning Redis.

### 8.2 Value

For v1, store each cart as one Redis Hash.

```text
key:  cart:user:{userId}:store:{storeId}
type: hash
```

Recommended fields:

```text
storeId -> store-id
currency -> VND
version -> 3
createdAtUtc -> 2026-09-16T02:30:00.0000000Z
updatedAtUtc -> 2026-09-16T02:45:00.0000000Z
expiresAtUtc -> 2026-09-17T02:45:00.0000000Z
item:{bagId}:quantity -> 2
item:{bagId}:snapshot -> JSON display/order snapshot for that bag
```

Why Hash instead of one JSON string:

```text
Quantity changes can use atomic Redis commands such as HINCRBY.
Remove item can use HDEL.
The service avoids overwriting unrelated item updates with stale full-cart JSON.
GET /api/cart can reconstruct all user carts from the store index and HGETALL.
```

### 8.3 TTL

TTL is automatic expiration.

Recommended:

```text
Cart TTL: 24 hours
Refresh TTL whenever cart changes
Delete cart after successful order creation
```

### 8.4 Atomicity

Single Redis commands are atomic. A read-modify-write flow is not automatically
atomic, so do not update cart items by reading the full cart and writing it back.

Use item-level Redis operations:

```text
Add/increase item:
  HINCRBY cart:user:{userId}:store:{storeId} item:{bagId}:quantity {delta}
  HSET cart:user:{userId}:store:{storeId} item:{bagId}:snapshot {...}
  SADD cart:user:{userId}:stores {storeId}
  EXPIRE cart:user:{userId}:store:{storeId} {ttlSeconds}

Set quantity:
  HSET cart:user:{userId}:store:{storeId} item:{bagId}:quantity {quantity}
  EXPIRE cart:user:{userId}:store:{storeId} {ttlSeconds}

Remove item:
  HDEL cart:user:{userId}:store:{storeId} item:{bagId}:quantity item:{bagId}:snapshot
  EXPIRE cart:user:{userId}:store:{storeId} {ttlSeconds}
```

For multi-command mutations, use a Lua script so item update, TTL refresh, and
store index maintenance happen atomically.

For checkout, add a short lock to avoid duplicate order creation:

```text
checkout-lock:user:{userId}:store:{storeId}
TTL: 5 seconds
```

### 8.5 Persistence

Redis cart is temporary. It does not need SQL-style durability.

If Redis restarts, cart may be lost depending on Redis persistence config. That
is acceptable for ephemeral cart in many systems. If you want better survival,
enable Redis AOF later.

### 8.6 Eviction

If Redis memory is full, Redis can evict keys depending on policy.

For local capstone development, this is usually not a blocker. For production,
configure memory limit and eviction policy intentionally.

## 9. Redis Cart Data Shape

Use one hash cart per authenticated user.

```text
SMEMBERS cart:user:7b9e7f0e-0b6d-4f8a-95a5-8327d80c12f1:stores

HGETALL cart:user:7b9e7f0e-0b6d-4f8a-95a5-8327d80c12f1:store:2c4d9f91-8d8e-4f2e-ae7f-80c9e5e9a001

storeId
2c4d9f91-8d8e-4f2e-ae7f-80c9e5e9a001
currency
VND
version
3
createdAtUtc
2026-09-16T02:30:00.0000000Z
updatedAtUtc
2026-09-16T02:45:00.0000000Z
expiresAtUtc
2026-09-17T02:45:00.0000000Z
item:aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa:quantity
2
item:aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa:snapshot
{"storeId":"2c4d9f91-8d8e-4f2e-ae7f-80c9e5e9a001","bagNameSnapshot":"Bakery Surprise Bag","unitPriceSnapshot":59000,"imageUrlSnapshot":"https://...","pickupStartUtc":"2026-09-16T03:00:00Z","pickupEndUtc":"2026-09-16T07:00:00Z","addedAtUtc":"2026-09-16T02:30:00Z"}
```

The API still returns a normal cart object after reconstructing the hash:

```json
{
  "userId": "user-id",
  "storeId": "store-id",
  "currency": "VND",
  "items": [
    {
      "bagId": "bag-id",
      "storeId": "store-id",
      "bagNameSnapshot": "Bakery Surprise Bag",
      "unitPriceSnapshot": 59000,
      "quantity": 2,
      "imageUrlSnapshot": "https://...",
      "pickupStartUtc": "2026-09-16T03:00:00Z",
      "pickupEndUtc": "2026-09-16T07:00:00Z",
      "addedAtUtc": "2026-09-16T02:30:00Z"
    }
  ],
  "version": 3,
  "createdAtUtc": "2026-09-16T02:30:00Z",
  "updatedAtUtc": "2026-09-16T02:45:00Z",
  "expiresAtUtc": "2026-09-17T02:45:00Z"
}
```

Notes:

```text
bagNameSnapshot/price/image are for display and order draft.
Store reservation still confirms final inventory.
Payment should never trust frontend totals.
```

## 10. API Step-By-Step

### 10.1 Get Cart

```http
GET /api/cart
```

Behavior:

```text
1. Read userId from JWT.
2. Read Redis set cart:user:{userId}:stores.
3. For each storeId, read Redis key cart:user:{userId}:store:{storeId}.
4. Remove stale storeIds from the index if the cart key already expired.
5. Return all active carts for the user.
```

Optional detail endpoint:

```http
GET /api/cart/stores/{storeId}
```

Behavior:

```text
1. Read userId from JWT.
2. Read Redis key cart:user:{userId}:store:{storeId}.
3. If missing, return empty cart for that store or 404 depending on API decision.
4. If found, return that cart.
```

### 10.2 Add Item

```http
POST /api/cart/items
```

Request:

```json
{
  "bagId": "bag-id",
  "quantity": 2
}
```

Behavior:

```text
1. Read userId from JWT.
2. Validate quantity > 0.
3. Call Store service to get bag snapshot.
4. Reject if bag is inactive/deleted/not purchasable.
5. Use snapshot.StoreId to select Redis key cart:user:{userId}:store:{storeId}.
6. Add item or increase quantity in that store cart.
7. Add storeId to cart:user:{userId}:stores.
8. Update version and updatedAtUtc.
9. Save item fields to Redis Hash and refresh TTL.
10. Return updated store cart.
```

### 10.3 Update Quantity

```http
PATCH /api/cart/items/{bagId}
```

Request:

```json
{
  "quantity": 3
}
```

Behavior:

```text
1. Read userId from JWT.
2. Use storeId route/query/body field to select cart:user:{userId}:store:{storeId}.
3. Read cart from Redis.
4. Find item by bagId.
5. If quantity <= 0, remove item or reject depending on API decision.
6. Update quantity.
7. Update version and updatedAtUtc.
8. Update item quantity in Redis Hash and refresh TTL.
9. Return updated store cart.
```

### 10.4 Remove Item

```http
DELETE /api/cart/items/{bagId}
```

Behavior:

```text
1. Read userId from JWT.
2. Use storeId route/query/body field to select cart:user:{userId}:store:{storeId}.
3. Remove item.
4. If no items remain, delete that store cart key and remove storeId from cart:user:{userId}:stores.
5. Otherwise refresh TTL.
6. Return updated store cart.
```

### 10.5 Clear Cart

```http
DELETE /api/cart
```

Behavior:

```text
1. Read userId from JWT.
2. If clearing one store cart, delete cart:user:{userId}:store:{storeId}.
3. If clearing all carts, delete all store cart keys from cart:user:{userId}:stores.
4. Return NoContent.
```

### 10.6 Checkout From Cart

```http
POST /api/orders/checkout-from-cart
Idempotency-Key: <guid>
```

Behavior:

```text
1. Read userId from JWT.
2. Read selected storeId from request.
3. Acquire checkout lock checkout-lock:user:{userId}:store:{storeId}.
4. Read cart from Redis key cart:user:{userId}:store:{storeId}.
5. Reject if cart is missing or empty.
6. Optionally call Store to pre-check bag active/pickup status.
7. Convert Redis cart items to existing CreateOrder input.
8. Create Pending order using existing Order logic.
9. Publish order.created using existing outbox flow.
10. Delete only that store cart after order creation succeeds.
11. Release checkout lock.
12. Return orderId and Pending status.
```

The existing saga continues:

```text
Order -> order.created
Store -> inventory.reserved / inventory.reservation_failed
Payment -> VNPAY checkout transaction
VNPAY -> return/ipn
Payment -> payment.completed / payment.failed
Payment -> inventory.release_requested when needed
Order/Store update final state
```

## 11. Implementation Plan In 6 Days

### Day 1 - 2026-09-16: Create Cart Service Skeleton

Tasks:

```text
1. Create Services/Cart folder.
2. Create projects:
   - Cart.API
   - Cart.Application
   - Cart.Domain
   - Cart.Infrastructure
3. Add solution file for Cart service.
4. Add project references following existing service style.
5. Add common API setup:
   - controllers
   - JWT auth
   - exception middleware
   - appsettings
   - OpenAPI if other services use it
6. Add Redis config section but do not implement behavior yet.
```

Exit criteria:

```text
Cart service builds.
Cart API starts.
Health/basic endpoint works.
```

### Day 2 - 2026-09-17: Add Redis Infrastructure

Tasks:

```text
1. Add StackExchange.Redis package to Cart.Infrastructure.
2. Add RedisSettings:
   - ConnectionString
   - CartTtlHours
   - CheckoutLockSeconds
3. Register IConnectionMultiplexer as singleton.
4. Create ICartRepository in Domain boundary.
5. Implement RedisCartRepository in Infrastructure.
6. Implement methods:
   - GetCartsAsync(userId)
   - GetAsync(userId, storeId)
   - SetAsync(cart, ttl)
   - DeleteAsync(userId, storeId)
   - DeleteAllAsync(userId)
   - AddOrIncrementItemAsync(userId, item, ttl)
   - SetItemQuantityAsync(userId, storeId, bagId, quantity, ttl)
   - RemoveItemAsync(userId, storeId, bagId, ttl)
   - AcquireCheckoutLockAsync(userId, storeId)
   - ReleaseCheckoutLockAsync(userId, storeId)
7. Store cart as Redis Hash:
   - metadata fields for storeId, currency, version, timestamps
   - item:{bagId}:quantity fields for quantities
   - item:{bagId}:snapshot fields for display/order snapshots
8. Maintain cart:user:{userId}:stores as a Redis Set index of active store carts.
9. Use Lua scripts where a cart mutation needs multiple Redis operations to be atomic.
```

Exit criteria:

```text
Can save cart hash to Redis.
Can read and reconstruct one store cart from Redis hash.
Can list all active carts for a user through the store index.
Can delete cart hash from Redis.
TTL is applied and refreshed on mutations.
Item quantity updates do not overwrite unrelated item updates.
```

### Day 3 - 2026-09-18: Implement Cart Application Logic

Tasks:

```text
1. Create DTOs:
   - CartDto
   - CartItemDto
   - AddCartItemRequest
   - UpdateCartItemRequest
2. Create ICartService.
3. Implement CartService:
   - GetCartAsync
   - AddItemAsync
   - UpdateQuantityAsync
   - RemoveItemAsync
   - ClearCartAsync
4. Add Store service client contract:
   - IStoreCatalogClient
   - GetBagCartSnapshotAsync(bagId)
5. Implement StoreCatalogHttpClient.
6. On add item, fetch bag snapshot from Store.
7. Group bags from the same store into the same Redis cart.
8. Allow one user to have multiple carts, one per store.
9. Use Redis Hash item-level repository methods for mutations.
10. Refresh TTL on every mutation.
```

Exit criteria:

```text
Cart service can manage cart in Redis.
Adding new bag uses Store snapshot.
Remove/update work without Store call.
One user has one Redis cart.
```

### Day 4 - 2026-09-19: Implement Cart API Endpoints

Tasks:

```text
1. Add CartController.
2. Implement:
   - GET /api/cart
   - GET /api/cart/stores/{storeId}
   - POST /api/cart/items
   - PATCH /api/cart/items/{bagId}
   - DELETE /api/cart/items/{bagId}
   - DELETE /api/cart
3. Read userId from JWT, not request body.
4. Return all active store carts for GET /api/cart.
5. Use storeId to update/remove/checkout one specific cart when needed.
6. Add clear error responses:
   - invalid quantity
   - bag not found
   - Redis unavailable
7. Test manually with Postman/Swagger.
```

Exit criteria:

```text
Frontend can replace useState-only writes with Cart API calls.
Reload can restore cart through GET /api/cart.
Cart survives login from another device because keys are based on userId.
Bags from different stores appear as separate carts.
```

### Day 5 - 2026-09-20: Integrate Checkout From Cart

Tasks:

```text
1. Add Cart service client to Order service or expose a trusted internal Cart endpoint.
2. Add Order endpoint:
   - POST /api/orders/checkout-from-cart
3. In checkout handler:
   - read userId from JWT
   - read selected storeId/cartId from request
   - acquire checkout lock for userId + storeId
   - get selected store cart from Cart service
   - reject empty cart
   - optionally pre-check active bags with Store
   - map cart items to existing create order command
   - call existing create order logic
   - delete selected store cart after order creation succeeds
   - release checkout lock
4. Keep existing order saga unchanged after order.created.
5. Add frontend flow:
   - cart page loads from GET /api/cart
   - add/remove/update call Cart API
   - place order calls checkout-from-cart
```

Exit criteria:

```text
Frontend no longer sends list products from useState to create order.
Order is created from the selected Redis store-cart snapshot.
Existing saga still completes end-to-end.
```

### Day 6 - 2026-09-21: Test And Harden

Tasks:

```text
1. Test reload:
   - add item
   - reload browser
   - cart still appears
2. Test cross-device:
   - login same user elsewhere
   - GET /api/cart returns same active store carts
3. Test update quantity.
4. Test remove item.
5. Test clear cart.
6. Test multiple store carts for the same user.
7. Test checkout from empty cart.
8. Test double-click checkout for the same store cart.
9. Test Store reservation failure still works.
10. Test Payment success still completes order.
11. Test Payment failure/expiration still releases inventory.
12. Run dotnet build for Cart, Order, Store, Payment.
13. Document local Redis startup.
```

Exit criteria:

```text
Cart persistence works.
Order saga still works.
Redis failure behavior is understood.
Demo path is stable.
```

## 12. Minimum Implementation Order

If time is limited, implement in this exact order:

```text
1. Cart service skeleton
2. Redis repository
3. Cart DTOs and CartService
4. Store snapshot HTTP client
5. CartController endpoints
6. Order checkout-from-cart endpoint
7. Frontend integration
8. Double-click checkout lock
9. Manual end-to-end tests
```

## 13. Local Redis Setup

Recommended Docker command:

```powershell
docker run --name stealdeal-redis -p 6379:6379 -d redis:7-alpine
```

Check Redis:

```powershell
docker exec -it stealdeal-redis redis-cli ping
```

Expected:

```text
PONG
```

Inspect keys:

```powershell
docker exec -it stealdeal-redis redis-cli
```

```text
KEYS cart:user:*
SMEMBERS cart:user:{userId}:stores
HGETALL cart:user:{userId}:store:{storeId}
TTL cart:user:{userId}:store:{storeId}
```

## 14. Final Target Flow

```text
Buyer adds bag
  -> Frontend calls Cart API
  -> Cart calls Store for bag snapshot
  -> Cart saves Redis key cart:user:{userId}:store:{storeId}
  -> Cart indexes storeId in cart:user:{userId}:stores

Buyer reloads or logs in elsewhere
  -> Frontend calls GET /api/cart
  -> Cart loads all active store carts

Buyer places order
  -> Frontend chooses one store cart
  -> Frontend calls Order checkout-from-cart with storeId
  -> Order reads selected store cart from Cart service
  -> Order creates Pending order
  -> Order publishes order.created
  -> Order deletes selected store cart after successful creation

Existing saga continues
  -> Store reserves inventory
  -> Payment creates VNPAY transaction
  -> VNPAY IPN updates payment
  -> Order and Store receive final events
```

## 15. Key Takeaways

```text
Use Redis for cart persistence, not inventory truth.
Use Cart API for add/remove/update/get cart.
Use Order checkout-from-cart for placing order.
Use Store service for bag snapshot and final inventory reservation.
Do not trust frontend cart details for final order/payment.
Do not add SQL DB for cart detail unless you need long-term abandoned-cart analytics.
```
