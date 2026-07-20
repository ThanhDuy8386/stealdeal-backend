# StealDeal Backend Review And Completion Plan

> Last updated: 2026-07-19  
> Deadline: 2026-08-24  
> Scope: review completed backend work, compare it with the old plan, and define the remaining work needed to complete the backend for the capstone ecommerce surprise-bag project.

## 1. Product And Architecture Context

StealDeal is a capstone ecommerce backend inspired by the surprise bag model of Too Good To Go.

Core roles:

- `Admin`: manages users, categories, store verification, disputes, refunds, and system-level status workflows.
- `Buyer`/`Customer`: registers, logs in, buys surprise bags, views orders/payments/notifications, and reviews stores/bags.
- `Seller`: creates a store, creates surprise bags, manages store-related orders, replies to reviews, and participates in cancellation/refund workflows.

Current backend architecture:

- Microservice architecture.
- Clean Architecture per service: `Domain`, `Application`, `Infrastructure`, `API`.
- Database-per-service direction.
- JWT Bearer authentication shared across services.
- RabbitMQ integration currently implemented for Identity -> Notification OTP flow.
- Event-driven saga is planned but not fully implemented yet.

Current services:

- `Identity`
- `Store`
- `Order`
- `Payment`
- `Notification`

For a compact handoff snapshot, also read:

- `STEALDEAL_BACKEND_CONTEXT_OVERVIEW.md`
- `Identity/IDENTITY_PLAN.md` when touching Identity or RabbitMQ OTP flow.

## 2. Current Completion Status As Of 2026-07-19

### 2.1 Build Status

All 5 service solutions build successfully:

- `Identity/StealDeal.Services.Identity.slnx`: build succeeded, 0 warnings.
- `Store/StealDeal.Services.Store.slnx`: build succeeded, 3 warnings.
- `Order/StealDeal.Services.Order.slnx`: build succeeded, 2 warnings.
- `Payment/StealDeal.Services.Payment.slnx`: build succeeded, 2 warnings.
- `Notification/StealDeal.Services.Notification.slnx`: build succeeded, 2 warnings.

Build warnings to address:

- `Microsoft.OpenApi 2.0.0` has a known high severity vulnerability in Store, Order, Payment, and Notification API projects.
- `Store.Application/DTOs/Requests/CreateBagRequest.cs`: non-nullable `Status` is not initialized.

### 2.2 Completed Work

Identity:

- Register user.
- Login.
- Refresh token rotation.
- Logout using refresh token cookie.
- Email verification OTP generation and verification.
- Resend OTP.
- `GET /api/auth/me` JWT test endpoint.
- Basic user management endpoints.
- Password hashing with BCrypt.
- Refresh token hash storage.
- OTP hash storage.
- Outbox table and background processor.
- RabbitMQ publisher with publisher confirms and mandatory returns.

Notification:

- Notification CRUD/read workflow.
- `EmailVerificationConsumer` background worker.
- RabbitMQ queue/exchange/binding setup for email verification events.
- Consumes `identity.user.email-verification.requested`.
- Persists OTP notification into `NotificationProfile`.

Store:

- Category CRUD.
- Store profile CRUD-like workflows.
- Store verify/toggle-active endpoints.
- Surprise bag CRUD.
- Surprise bag category many-to-many relationship.
- Store review create/read/reply/report workflows.
- Business checks for store ownership, verified/active store before bag creation, rating range, and duplicate review per order.

Order:

- Order create/get/list/update-status.
- Store orders endpoint.
- Pickup dispute create/get/list/update-status.
- Basic role and ownership checks.
- JSON persistence for dispute evidence URLs.

Payment:

- Transaction create/get/list/update-status.
- Refund create/get/list/update-status.
- Prevent duplicate pending/success transactions for one order.
- Refund only successful transactions.
- Refund amount cannot exceed original transaction amount across pending/processed refunds.

Cross-cutting:

- Clean Architecture shape exists for all 5 services.
- Global exception middleware exists in all APIs.
- JWT bearer validation is wired across services.
- EF Core DbContexts exist for all services.

## 3. Comparison With The Old Plan

The old plan was useful as the original direction, but several parts are now outdated or need recalibration.

### 3.1 Completed Or Partially Completed From Old Plan

- Basic CRUD for all services is done.
- Identity auth flow is much more complete than the original early plan.
- Identity outbox and RabbitMQ publisher are implemented.
- Notification RabbitMQ consumer for OTP is implemented.
- Clean Architecture structure is consistent across services.
- Global exception middleware exists across services.

### 3.2 Changed Direction

The old Store plan used a generic ecommerce product model:

- `Product`
- `ProductVariant`
- `StockReservation`

The current code has moved toward the actual capstone domain:

- `StoreProfile`
- `SurpriseBag`
- `Category`
- `StoreReview`

This is a good direction for the surprise-bag product. The plan should now focus on surprise bag stock/quantity reservation rather than generic product variants.

### 3.3 Still Not Done From Old Plan

- Shared BuildingBlocks library.
- Standardized `ApiResponse<T>` or `Result<T>`.
- Shared integration event contracts.
- Shared RabbitMQ consumer/producer abstractions.
- Docker Compose.
- Store stock reservation.
- Order saga coordination.
- Payment gateway or mock payment gateway.
- Payment/order/store integration events.
- Real email sending from Notification.
- Processed message idempotency table.
- API Gateway.
- Health checks.
- Unit/integration tests.
- Deployment guide and Postman/OpenAPI documentation.

### 3.4 Newly Found Issues Since Old Plan

- `Payment.API/Program.cs` uses `GetConnectionString("IdentityDb")`, while `Payment.API/appsettings.json` has `StoreDb`. This must become `PaymentDb`.
- Store authorization is mostly commented out.
- Identity `UserController` admin-style endpoints were not protected with explicit admin authorization. Resolved on 2026-07-20.
- Role naming is inconsistent between product language (`Buyer`) and code (`Customer`).
- Status values are free-form strings in multiple services.
- OTP rate limiting fields exist but are not enforced.
- Store/Order/Payment migrations are not visible in the current tree.
- Notification currently stores OTP notification in DB but does not send real email.

## 4. Target Backend Definition For 2026-08-24

The backend should be considered capstone-ready if the following P0/P1 scope is complete.

### 4.1 P0 Must Have

Identity:

- Stable register/login/refresh/logout.
- Email verification OTP works end-to-end.
- Admin user management protected by Admin authorization.
- Role naming decision documented and consistently handled.

Store:

- Seller can create one store.
- Admin can verify/activate/deactivate stores.
- Seller can create/update/delete surprise bags.
- Surprise bag quantity can be safely reserved/released.
- Buyer can browse bags/stores/categories.
- Buyer can review completed orders.

Order:

- Buyer can create an order from one or more surprise bags.
- Order stores enough snapshot data to remain valid even if bag/store changes later.
- Order status flow is controlled by constants/enums and permission checks.
- Order created event starts the saga.
- Order can be cancelled on stock/payment failure.

Payment:

- Create payment transaction for an order.
- Support a mock payment success/failure flow at minimum.
- Publish payment completed/failed events.
- Refund records and basic refund status management work.

Notification:

- OTP email or OTP notification flow works reliably.
- In-app notifications for key order/payment events.
- Consumer idempotency exists.

Infrastructure:

- All services build with no critical warnings.
- All services have migrations and can create/update local SQL Server databases.
- RabbitMQ event flow works locally.
- Docker Compose or a clear local run guide exists.
- API docs or Postman collection exists for demo.

### 4.2 P1 Should Have

- API Gateway using YARP or Ocelot.
- Health checks for DB and RabbitMQ.
- Shared integration event contracts.
- Shared constants for routing keys/status values.
- Consumer dead-letter queue.
- Basic automated tests for critical flows.
- Trust score updates from order/dispute/no-show events.

### 4.3 P2 Nice To Have

- Real payment gateway integration such as VNPay or Momo.
- Push notification with Firebase FCM.
- Rich admin dashboard endpoints.
- Advanced rate limiting and audit logs.
- Full unit/integration/e2e test coverage.

## 5. Planned Event-Driven Order Flow

Recommended MVP saga choreography:

```text
Buyer creates order
  -> Order Service saves Pending order
  -> Order publishes order.created
  -> Store consumes order.created
  -> Store reserves SurpriseBag.QuantityRemaining
  -> Store publishes store.stock.reserved or store.stock.reservation-failed
  -> Order consumes stock result
  -> If stock failed: Order marks Cancelled
  -> If stock reserved: Payment creates/awaits transaction
  -> Payment publishes payment.completed or payment.failed
  -> Order consumes payment result and marks Confirmed/Cancelled
  -> Store consumes payment.failed/order.cancelled and releases stock if needed
  -> Notification consumes major events and creates notifications
```

Minimum event names:

- `identity.user.email-verification.requested`
- `order.created`
- `order.cancelled`
- `order.confirmed`
- `store.stock.reserved`
- `store.stock.reservation-failed`
- `store.stock.released`
- `payment.completed`
- `payment.failed`
- `payment.refunded`
- `notification.created` or no publish needed for MVP

## 6. Completion Timeline To 2026-08-24

Today is 2026-07-19. Deadline is 2026-08-24. That leaves 36 days.

The plan below prioritizes a demo-ready backend over perfect architecture. If time becomes tight, complete P0 first, then P1, then P2.

### Week 1: 2026-07-20 To 2026-07-26 - Stabilize Foundation

Goal: make the current 5 services reliable enough to build on.

Tasks:

- Fix Payment connection string and database naming.
- Add or verify EF migrations for Store, Order, and Payment.
- Fix build warnings:
  - Upgrade/fix vulnerable `Microsoft.OpenApi` dependency.
  - Fix `CreateBagRequest.Status` nullable warning.
- Decide and document role naming:
  - Option A: keep code role as `Customer`, use Buyer only in UI/product docs.
  - Option B: rename role to `Buyer` everywhere.
- Re-enable Store authorization where needed.
- Protect Identity admin-style user endpoints with Admin authorization.
- Add status constants/enums for core flows:
  - Bag status.
  - Order status.
  - Payment transaction status.
  - Refund status.
  - Outbox status.
- Enforce basic OTP rate limits:
  - Max verify attempts per OTP.
  - Max resend count or resend cooldown.
- Add a local run checklist for SQL Server and RabbitMQ.
- Update `STEALDEAL_BACKEND_CONTEXT_OVERVIEW.md` after these changes.

Exit criteria:

- All 5 services build with no errors.
- Payment has correct DB config.
- Store/Order/Payment migrations are present.
- Auth-protected endpoints no longer accidentally depend on missing claims.
- The current system can be run locally service by service.

### Week 2: 2026-07-27 To 2026-08-02 - Surprise Bag Stock And Order Saga Foundation

Goal: implement the core order-to-stock reservation flow.

Tasks:

- Define shared integration event DTOs.
- Decide whether to create a small shared project or duplicate contracts temporarily for speed.
- Add Store stock reservation logic:
  - Validate bag exists and is active/available.
  - Validate pickup/expiry rules.
  - Validate `QuantityRemaining >= requested quantity`.
  - Decrease `QuantityRemaining` on reserve.
  - Release stock on cancellation/payment failure.
- Add Store event consumer for `order.created`.
- Add Store event publisher/outbox for:
  - `store.stock.reserved`
  - `store.stock.reservation-failed`
  - `store.stock.released`
- Add Order outbox publisher for `order.created`.
- Add Order consumer for Store stock result.
- Update Order status flow:
  - `Pending`
  - `StockReserved`
  - `PaymentPending`
  - `Confirmed`
  - `Cancelled`
  - `Completed`
  - `Disputed`
- Add minimal idempotency strategy for stock reservation event handling.

Exit criteria:

- Creating an order publishes `order.created`.
- Store consumes it and reserves/rejects stock.
- Order reacts to stock result.
- Stock does not go negative in normal local testing.

### Week 3: 2026-08-03 To 2026-08-09 - Payment Flow And Notification Expansion

Goal: connect payment into the saga and make Notification useful beyond OTP.

Tasks:

- Implement mock payment flow:
  - Create transaction for order.
  - Mark transaction `Success` or `Failed` through endpoint or mock callback.
  - Publish `payment.completed` or `payment.failed`.
- Add Payment outbox publisher.
- Add Payment consumer if needed for `order.cancelled`.
- Add Order consumers for:
  - `payment.completed`
  - `payment.failed`
- Add Store consumer for:
  - `payment.failed`
  - `order.cancelled`
- Add Notification consumers for:
  - `order.created`
  - `order.confirmed`
  - `order.cancelled`
  - `payment.completed`
  - `payment.failed`
  - `payment.refunded`
- Add `ProcessedMessage` or equivalent idempotency table in Notification.
- Decide whether OTP is real email or in-app-only for capstone demo.
- If real email is required:
  - Add SMTP/SendGrid abstraction.
  - Add appsettings configuration.
  - Send real OTP email from Notification consumer.

Exit criteria:

- Happy path works locally:
  - Buyer creates order.
  - Store reserves stock.
  - Payment succeeds.
  - Order becomes Confirmed.
  - Notification records are created.
- Failure path works locally:
  - Payment fails.
  - Order becomes Cancelled.
  - Store releases stock.

### Week 4: 2026-08-10 To 2026-08-16 - Gateway, Hardening, And Admin Workflows

Goal: make the backend easier to demo and less fragile.

Tasks:

- Add API Gateway using YARP or Ocelot if time allows.
- Centralize frontend-facing routing through gateway.
- Add CORS configuration for frontend.
- Add health checks:
  - Database health per service.
  - RabbitMQ health for messaging services.
- Add role/ownership hardening:
  - Seller can only access own store/bags/orders.
  - Buyer can only access own orders/payments/notifications.
  - Admin endpoints require Admin role.
- Add input validation cleanup for DTOs.
- Add standardized response wrapper only if it does not slow delivery.
- Add refund compensation behavior:
  - On order cancellation after successful payment, create refund record or mark refund needed.
  - Publish `payment.refunded` when refund processed.
- Add trust score MVP if time allows:
  - Successful pickup increases count.
  - No-show/dispute can reduce score.

Exit criteria:

- Gateway or direct-service run guide is ready.
- Health endpoints work.
- Main authorization gaps are closed.
- Refund workflow is demoable.

### Week 5: 2026-08-17 To 2026-08-23 - Testing, Documentation, And Demo Polish

Goal: reduce risk before the 2026-08-24 deadline.

Tasks:

- Add unit tests for critical application services:
  - `AuthService`
  - Store reservation service
  - `OrderService` status transitions
  - `TransactionService`
  - `RefundService`
- Add integration tests or manual verification scripts for:
  - Identity outbox -> RabbitMQ -> Notification consumer.
  - Order -> Store reserve -> Payment -> Order confirm.
  - Payment failure -> Store release -> Order cancel.
- Prepare API documentation:
  - OpenAPI/Swagger endpoints.
  - Postman collection.
  - Example request bodies.
  - Required auth/roles per endpoint.
- Prepare deployment/local run documentation:
  - Required tools.
  - SQL Server setup.
  - RabbitMQ setup.
  - Migration commands.
  - Service run order.
- Clean up commented-out authorization and stale TODOs.
- Update both docs:
  - `STEALDEAL_BACKEND_CONTEXT_OVERVIEW.md`
  - `STEALDEAL_REVIEW_AND_PLAN.md`

Exit criteria:

- Main flows are tested or manually verified.
- Demo script is ready.
- New context can understand the project by reading the docs.
- No known P0 blocker remains.

### Final Day: 2026-08-24 - Buffer And Submission

Goal: only fix blockers and prepare final demo/submission material.

Tasks:

- Freeze feature work unless a P0 bug appears.
- Run all builds.
- Run tests/manual flow checklist.
- Verify database migrations from clean database.
- Verify RabbitMQ queues/bindings.
- Verify frontend integration routes if frontend is available.
- Record remaining limitations honestly in documentation.

## 7. Priority Matrix

### P0 Must Finish

- Payment DB config fix.
- Store/Order/Payment migrations.
- Authorization hardening for obvious Admin/Seller/Buyer routes.
- Surprise bag stock reserve/release.
- Order created event.
- Store stock result event.
- Payment success/failure event.
- Order status reaction to stock/payment.
- Notification for key events.
- Local run guide and API docs.

### P1 Should Finish

- API Gateway.
- Health checks.
- Consumer idempotency.
- DLQ/retry policy.
- Refund compensation.
- Basic automated tests.
- Real email sending.

### P2 Nice To Have

- Real VNPay/Momo integration.
- Push notification.
- Full BuildingBlocks refactor.
- Full standardized response/result pattern.
- Trust score event consumers.
- Advanced admin analytics.

## 8. Risk Register

| Risk | Level | Mitigation |
|------|-------|------------|
| Saga implementation becomes too large | High | Start with happy path and one failure path only. Add edge cases after demo path works. |
| Cross-service ownership checks are hard | High | Use JWT claims or temporary trusted internal assumptions for MVP, then harden key endpoints. |
| RabbitMQ duplicate delivery | Medium | Add `ProcessedMessage` table for consumers. Keep handlers idempotent. |
| Stock can go negative | High | Use database transaction and conditional update/check before decrementing quantity. |
| Payment gateway takes too long | High | Use mock payment for capstone demo, keep real gateway as P2. |
| Documentation falls behind code | Medium | Update context overview after each major completed task. |
| OpenAPI vulnerability warning remains | Medium | Upgrade/remove vulnerable package or document if framework package controls it. |
| Timeline gets tight | High | Cut P2 first, then P1. Do not cut the order-stock-payment happy path. |

## 9. Suggested Working Rules From Now To Deadline

- After each major feature, update `STEALDEAL_BACKEND_CONTEXT_OVERVIEW.md`.
- After each week, update this file's progress section.
- Keep each service buildable before moving to the next service.
- Prefer small, working event flows over large incomplete abstractions.
- Do not spend too much time on a shared BuildingBlocks refactor until the saga happy path works.
- Use explicit constants/enums for statuses and routing keys before the saga expands.
- Keep manual API request examples for every completed flow.

## 10. Progress Log

### 2026-07-17

- Basic CRUD setup completed for all 5 services.
- RabbitMQ connection/settings completed locally for Identity and Notification.
- `register` and `resend-otp` flow completed:
  - Identity creates OTP and outbox event.
  - Identity outbox processor publishes event to RabbitMQ.
  - Notification consumer receives event and stores `NotificationProfile`.

### 2026-07-19

- Added `STEALDEAL_BACKEND_CONTEXT_OVERVIEW.md` as a durable context handoff file.
- Reviewed current source code and recalibrated this completion plan.
- Verified all 5 service solutions build successfully.
- Recorded build warnings and high-priority config/security gaps.

### 2026-07-20

- Protected all Identity user-management endpoints with the `Admin` role.
- Added Admin user creation for active, verified test accounts.
- Added authenticated account profile retrieval/update and password change.
- Password change now revokes all active refresh tokens and requires a new login.
- Deferred forgot-password and email-change verification flows.

