# Redis Cart Service Guide

> Updated: 2026-09-22  
> Status: implemented and integrated with frontend checkout flow.

This document summarizes the Redis-backed Cart service, local Redis setup, the
important service boundaries, and the checkout flow that now creates orders from
server-side cart state.

## 1. What Was Implemented

Cart is now a separate service:

```text
Services/Cart
  StealDeal.Services.Cart.API
  StealDeal.Services.Cart.Application
  StealDeal.Services.Cart.Domain
  StealDeal.Services.Cart.Infrastructure
```

Cart state is stored in Redis, not SQL Server.

Implemented behavior:

```text
Authenticated users can add bags to cart.
Cart survives reload and cross-device login.
User can have multiple store carts.
Bags from the same store are grouped in the same cart.
Quantity update, item remove, store clear, and clear-all are supported.
Checkout uses Order service endpoint checkout-from-cart.
Order service reads Cart service state instead of trusting client order items.
Checkout pre-checks bag availability through Store service.
Selected store cart is deleted after Pending order creation succeeds.
Checkout lock prevents duplicate order creation from double-click/retry.
```

## 2. Local Redis Setup

Start Redis:

```powershell
docker run --name stealdeal-redis -p 6379:6379 -d redis:7-alpine
```

If the container already exists:

```powershell
docker start stealdeal-redis
```

Check Redis:

```powershell
docker exec -it stealdeal-redis redis-cli ping
```

Expected:

```text
PONG
```

Open Redis console:

```powershell
docker exec -it stealdeal-redis redis-cli
```

Useful inspect commands:

```redis
KEYS cart:user:*
SMEMBERS cart:user:<userId>:stores
HGETALL cart:user:<userId>:store:<storeId>
TTL cart:user:<userId>:store:<storeId>
GET checkout-lock:user:<userId>:store:<storeId>
```

`KEYS` is fine for local debugging. Do not use it in production paths.

## 3. Service Ports And Config

Default local ports:

```text
Identity: http://localhost:5158
Store:    http://localhost:5169
Order:    http://localhost:5165
Cart:     http://localhost:5185
Redis:    localhost:6379
```

Cart config:

```json
"Redis": {
  "ConnectionString": "localhost:6379",
  "CartTtlHours": 24,
  "CheckoutLockSeconds": 5
},
"StoreService": {
  "BaseUrl": "http://localhost:5169"
}
```

Order checkout integration config:

```json
"CartService": {
  "BaseUrl": "http://localhost:5185"
},
"StoreService": {
  "BaseUrl": "http://localhost:5169"
}
```

Run services for cart checkout testing:

```powershell
dotnet run --project .\Identity\StealDeal.Services.Identity.API\StealDeal.Services.Identity.API.csproj --launch-profile http
dotnet run --project .\Store\StealDeal.Services.Store.API\StealDeal.Services.Store.API.csproj --launch-profile http
dotnet run --project .\Cart\StealDeal.Services.Cart.API\StealDeal.Services.Cart.API.csproj --launch-profile http
dotnet run --project .\Order\StealDeal.Services.Order.API\StealDeal.Services.Order.API.csproj --launch-profile http
```

RabbitMQ is needed for the full saga after order creation.

## 4. Redis Data Model

Each user has a store index:

```text
cart:user:{userId}:stores
```

This is a Redis Set containing active `storeId` values.

Each store cart is a Redis Hash:

```text
cart:user:{userId}:store:{storeId}
```

Hash fields:

```text
storeId
currency
version
createdAtUtc
updatedAtUtc
expiresAtUtc
item:{bagId}:quantity
item:{bagId}:snapshot
```

The snapshot is JSON used for cart display and checkout mapping. Store service
is still checked again during checkout.

Cart TTL:

```text
Default: 24 hours
Refreshed on cart mutations
```

Checkout lock key:

```text
checkout-lock:user:{userId}:store:{storeId}
```

Default lock TTL:

```text
5 seconds
```

## 5. Cart API

All Cart APIs require:

```http
Authorization: Bearer <access-token>
```

Endpoints:

```http
GET    /api/cart
GET    /api/cart/stores/{storeId}
POST   /api/cart/items
PATCH  /api/cart/stores/{storeId}/items/{bagId}
PATCH  /api/cart/items/{bagId}?storeId={storeId}
DELETE /api/cart/stores/{storeId}/items/{bagId}
DELETE /api/cart/items/{bagId}?storeId={storeId}
DELETE /api/cart/stores/{storeId}
DELETE /api/cart
```

Add item:

```http
POST /api/cart/items
```

```json
{
  "bagId": "bag-id",
  "quantity": 2
}
```

Update quantity:

```http
PATCH /api/cart/stores/{storeId}/items/{bagId}
```

```json
{
  "quantity": 3
}
```

Checkout lock endpoints used by Order service:

```http
POST   /api/cart/stores/{storeId}/checkout-lock
DELETE /api/cart/stores/{storeId}/checkout-lock
```

Frontend normally does not need to call lock endpoints directly.

## 6. Checkout From Cart

Frontend should use the new Order endpoint:

```http
POST /api/orders/checkout-from-cart
Authorization: Bearer <access-token>
```

Request:

```json
{
  "storeId": "store-id",
  "contactNameSnapshot": "Nguyen Van A",
  "contactPhoneSnapshot": "0900000000",
  "deliveryType": "Pickup",
  "deliveryAddress": ""
}
```

The frontend does not send order items, prices, totals, delivery fee, or voucher
discount to this endpoint.

Implemented checkout flow:

```text
Frontend calls Order checkout-from-cart
Order reads userId from JWT
Order forwards the same Bearer token to Cart
Order asks Cart to acquire checkout lock for userId + storeId
Order reads selected store cart from Cart
Order pre-checks each bag through Store
Order builds CreateOrderRequest internally
Order reuses existing CreateOrderAsync logic
Order creates Pending order and outbox order.created
Order deletes only the selected store cart
Order releases checkout lock
Existing saga continues unchanged
```

The existing saga still decides final inventory/payment result:

```text
Order -> order.created
Store -> inventory.reserved or inventory.reservation_failed
Payment -> payment.completed or payment.failed
Order/Store update final state from events
```

## 7. Important Boundaries

Cart owns temporary buyer intent:

```text
User wants bag A quantity 2
User wants bag B quantity 1
Cart survives reload/device switch
Cart expires if abandoned
```

Cart does not own:

```text
Final order history
Final inventory truth
Payment state
Reservation/release logic
Long-term audit
```

Store owns:

```text
Bag existence
Bag status
Bag current price/name/store
Bag pickup window
Quantity remaining
Final inventory reservation
```

Order owns:

```text
Order creation
Order items snapshot
Order status
Order saga outbox
```

Payment owns:

```text
Payment transaction
Payment success/failure/expiration
```

## 8. Why Checkout Does Not Trust Client Items

The old `POST /api/orders` accepts full order details from the client. That is
useful for early development, but it is not ideal for real checkout because a
user can send fake item names, prices, quantities, or totals with curl.

The new checkout endpoint fixes that by letting the backend build the order from:

```text
JWT userId
Redis cart state
Store service bag pre-check
Server-side CreateOrderAsync logic
```

Client is allowed to send checkout-level data:

```text
storeId selector
contact info
delivery type/address
note later if needed
idempotency key later if implemented
```

Client should not be trusted for:

```text
bag list
unit price
subtotal/total
delivery fee
discount
order status
```

## 9. Checkout Lock

Checkout lock prevents duplicate order creation from the same store cart.

Main duplicate cases:

```text
User double-clicks Place Order
Browser retries the request
User sends two checkout curl requests in parallel
```

Lock key:

```text
checkout-lock:user:{userId}:store:{storeId}
```

Conceptual Redis operation:

```text
SET key lockToken NX EX 5
```

Meaning:

```text
NX: acquire only if no checkout is already in progress
EX: auto-expire if the process crashes
lockToken: release only the lock owned by this request
```

If another checkout request arrives while the lock exists, Cart returns `409
Conflict`.

The lock does not reserve inventory. Store saga remains the final inventory
truth.

## 10. Common Test Checklist

Cart API:

```text
GET /api/cart returns empty list for new user
POST /api/cart/items creates Redis hash and store index
POST same bag increments quantity
PATCH quantity updates Redis field
PATCH quantity 0 removes item
DELETE item removes item
DELETE last item deletes store cart and removes store index entry
DELETE /api/cart clears all store carts for user
```

Checkout:

```text
Checkout with valid cart creates Pending order
Selected store cart is removed after order creation
Other store carts remain
Checkout empty cart returns bad request
Checkout unavailable/expired bag returns bad request before order creation
Concurrent checkout returns one success and one 409 conflict
Saga still handles reservation failure after Pending order is created
```

Redis inspect after add:

```redis
SMEMBERS cart:user:<userId>:stores
HGETALL cart:user:<userId>:store:<storeId>
TTL cart:user:<userId>:store:<storeId>
```

Redis inspect after checkout:

```redis
HGETALL cart:user:<userId>:store:<checkedOutStoreId>
SMEMBERS cart:user:<userId>:stores
```

The checked-out store cart should be gone.

## 11. Future Development Notes

Recommended next improvements:

```text
Add Idempotency-Key support for checkout-from-cart.
Decide whether old POST /api/orders should be internal-only or removed from FE.
Add rate limiting on checkout endpoints.
Add clearer cart/store pre-check error messages.
Add delivery fee and voucher validation from trusted backend services.
Add integration tests for concurrent checkout.
Add observability around checkout lock conflicts and cart delete failures.
Consider service-to-service auth instead of forwarding user Bearer token.
```

Production cautions:

```text
Do not use Redis KEYS in application code.
Do not treat Redis cart as inventory reservation.
Do not trust client totals or item prices.
Do not delete cart before order creation succeeds.
Do not restore cart automatically after saga failure unless a clear UX policy is defined.
```

## 12. Key Takeaways

```text
Redis Cart persists temporary buyer intent.
Frontend state is only a UI cache.
Order checkout-from-cart is the real checkout path.
Order service builds trusted order input from Cart and Store.
Store saga remains the final stock truth.
Checkout lock prevents duplicate orders from the same cart.
Only the checked-out store cart is deleted after Pending order creation.
```
