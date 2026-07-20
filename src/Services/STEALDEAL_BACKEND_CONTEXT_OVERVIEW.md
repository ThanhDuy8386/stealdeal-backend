# StealDeal Backend Context Overview

> Snapshot date: 2026-07-20
> Purpose: this file is a context handoff document for future conversations. Before continuing backend work in a new context window, read this file first to quickly understand the current architecture, implemented services, completed flows, and known gaps.

## 1. Project Summary

StealDeal is the backend for a capstone ecommerce website based on the surprise bag model, similar to Too Good To Go. Buyers can purchase discounted surprise bags from sellers/stores and pick them up during a pickup window or follow a delivery workflow depending on the order type.

The main roles are:

- `Admin`: manages users, categories, store verification/activation, disputes, refunds, and admin status workflows.
- `Buyer`/`Customer`: registers, logs in, buys surprise bags, views orders/transactions/notifications, and reviews stores/bags.
- `Seller`: creates a store, creates surprise bags, views store orders, replies to reviews, and participates in refund/order workflows.

The backend currently lives under `src/Services` and follows a microservice architecture with Clean Architecture. There are 5 services:

- `Identity`
- `Store`
- `Order`
- `Payment`
- `Notification`

Most services are split into 4 projects/layers:

- `*.Domain`: entities/domain models plus repository and unit-of-work interfaces.
- `*.Application`: DTOs, mappings, application services, and custom exceptions.
- `*.Infrastructure`: EF Core DbContext, repositories, security, messaging, and background jobs.
- `*.API`: ASP.NET Core controllers, dependency injection, authentication, and middleware.

Current tech stack:

- .NET `net10.0`
- ASP.NET Core Web API
- EF Core SQL Server
- JWT Bearer authentication
- RabbitMQ.Client `7.2.1`
- BCrypt.Net-Next for Identity password hashing
- OpenAPI is enabled in Store/Order/Payment/Notification; Identity does not currently register OpenAPI in `Program.cs`

## 2. Current Repository Shape

Top-level folders in `src/Services`:

```text
Identity/
Notification/
Order/
Payment/
Store/
STEALDEAL_REVIEW_AND_PLAN.md
STEALDEAL_BACKEND_CONTEXT_OVERVIEW.md
```

Each service has its own `.slnx` file. The older `STEALDEAL_REVIEW_AND_PLAN.md` is a previous review and two-month roadmap that focuses heavily on Identity. This file is the current source-code snapshot and handoff document.

## 3. Cross-Cutting Architecture

### Dependency Direction

Expected direction:

```text
API -> Application + Infra
Infrastructure -> Domain + Application
Application -> Domain
Domain has no external infrastructure dependency
```

Application services orchestrate business use cases. Repositories sit behind interfaces. EF Core changes are committed through `IUnitOfWork`.

### API Conventions

- Controllers use `[ApiController]`.
- Main route style:
  - Identity: `api/auth`, `api/user`
  - Store: `api/categories`, `api/stores`, `api/bags`, `api/reviews`
  - Order: `api/orders`, `api/pickup-disputes`
  - Payment: `api/transactions`, `api/refunds`
  - Notification: `api/notifications`
- Each service has repeated custom exception classes: `BadRequestException`, `UnauthorizedException`, `ForbiddenException`, `ConflictException`, `NotFoundException`.
- Each API has `GlobalExceptionHandlerMiddleware` to map custom exceptions to HTTP responses.
- There is no shared `ApiResponse<T>` wrapper yet. Endpoints currently return raw DTOs, anonymous objects, `NoContent`, or `CreatedAtAction` depending on the controller.

### Auth/JWT

- All services use matching JWT issuer/audience/secret settings from `appsettings.json`.
- Identity issues access tokens and refresh tokens.
- Refresh tokens are stored as hashes in the Identity database and sent to clients through an HttpOnly cookie named `refresh_token` with path `/api/auth`.
- Downstream services read user id from `ClaimTypes.NameIdentifier` or `sub`, and role from `ClaimTypes.Role` or `role`.
- Several Store endpoints have `[Authorize]` or role-based authorization commented out for local testing.

### Persistence

Each service is designed to have its own DbContext and database:

- Identity: `StealDealIdentityDb`
- Store: `StealDealStoreDb`
- Order: `StealDealOrderDb`
- Payment: should have its own Payment DB, but currently has a config inconsistency
- Notification: `StealDealNotificationDb`

Known config inconsistency:

- `Payment.API/Program.cs` currently calls `GetConnectionString("IdentityDb")`.
- `Payment.API/appsettings.json` currently contains a `StoreDb` connection string and does not contain `IdentityDb` or `PaymentDb`.
- This is almost certainly a copy/paste bug and should be fixed before running the Payment service seriously.

## 4. Identity Service

### Responsibility

Identity owns user accounts, roles, authentication tokens, email verification OTP data, trust score data, and outbox events for email OTP delivery.

### Main Domain Models

- `User`: email, password hash, phone, full name, avatar, email verification flag, active/deleted flags, roles, addresses, trust score, refresh tokens, and email verifications.
- `UserRole`: role string assigned to a user.
- `UserAddress`: label, address, district, city, and default flag.
- `UserTrustScore`: score, total orders, successful pickups, no-show count, dispute count.
- `TrustScoreEvent`: audit/event history for trust score changes.
- `RefreshToken`: token hash, expiry, revoked state.
- `EmailVerification`: OTP hash, expiry, consumed/revoked timestamps, attempt/resend counters.
- `OutboxMessage`: integration event metadata, payload, status, retry count, and error information.

### API Endpoints

`AuthController`:

- `POST /api/auth/register`: creates user, role, trust score, OTP, outbox message, and token pair; returns access token and sets refresh token cookie.
- `POST /api/auth/login`: validates credentials, issues token pair, and sets refresh token cookie.
- `POST /api/auth/refresh`: reads refresh token from cookie, rotates refresh token, returns new access token, and refreshes the cookie.
- `POST /api/auth/verify-email`: verifies OTP and marks email as verified.
- `POST /api/auth/resend-otp`: revokes active OTP, creates a new OTP, and creates a new outbox message.
- `GET /api/auth/me`: protected endpoint used to inspect JWT claims.
- `POST /api/auth/logout`: revokes the refresh token from cookie and deletes the cookie.

`UserController`:

- All endpoints require the `Admin` role.
- `POST /api/user`: creates an active, email-verified user with an admin-supplied password, roles, and initial trust score.
- `GET /api/user`: paged/search/filter users.
- `GET /api/user/{id}`: user detail.
- `PUT /api/user/{id}`: admin-style update for full name, email, phone, active flag, and roles.
- `DELETE /api/user/{id}`: deletes user through repository.

`AccountController`:

- All endpoints require authentication and derive the target user from the JWT.
- `GET /api/account/profile`: gets the current user's complete profile.
- `PUT /api/account/profile`: updates the current user's full name, phone, and avatar URL.
- `PUT /api/account/password`: verifies and changes the password, revokes all active refresh tokens, and deletes the refresh cookie.

### Important Business Rules

- Register validates email, password length >= 8, first/last name, and role.
- Register currently accepts roles `Customer` or `Seller`.
- Admin-style user update accepts roles `Customer`, `Seller`, and `Admin`.
- Email is normalized by trimming and lowercasing.
- Passwords are hashed with BCrypt.
- OTP is generated as a 6-digit value and hashed with SHA256 in `EmailVerification`.
- Raw OTP is serialized into the outbox payload so Notification can create/send the OTP notification. This is acceptable for development, but should be hardened later.
- Refresh tokens are generated raw once, then stored only as SHA256 hashes.
- Refresh token rotation revokes the old token and stores a new one.
- Admin-created accounts are immediately active and email-verified and do not receive an OTP or token pair.
- Self-service profile updates cannot change email, roles, active/verified state, or trust score.
- Authenticated password changes require the current password and force a new login by revoking refresh tokens.

### RabbitMQ Producer / Outbox

Identity is the producer for email OTP events.

Event created by register/resend:

- Exchange: `stealdeal.events`
- Exchange type: `topic`
- Routing key: `identity.user.email-verification.requested`
- Event type: `SendEmailVerificationOtpEvent`
- Payload: `UserId`, `Email`, `FullName`, `Otp`, `ExpiresAt`

`OutboxMessageProcessor`:

- Runs as a hosted background service.
- Polls pending outbox messages using `OutboxSettings`.
- Builds `IntegrationMessage` from `OutboxMessage`.
- Publishes through `IMessagePublisher`.
- Marks message as `Processed` on success.
- Increments retry count and stores error message on failure.
- Marks message as `Failed` after the max retry count.

`RabbitMqMessagePublisher`:

- Reuses one RabbitMQ connection with an async lock.
- Creates a channel per publish.
- Uses a durable topic exchange.
- Enables publisher confirms.
- Publishes persistent JSON messages with `mandatory: true`.
- Throws if the broker returns an unroutable message, allowing the outbox processor to retry later.

Detailed `PublishAsync` flow:

```text
OutboxMessageProcessor
  -> reads Pending OutboxMessage rows from Identity DB
  -> maps each OutboxMessage to IntegrationMessage
  -> calls IMessagePublisher.PublishAsync(message)
  -> if PublishAsync succeeds: marks OutboxMessage as Processed
  -> if PublishAsync throws: increments RetryCount and keeps/marks message retryable or Failed
```

`PublishAsync(IntegrationMessage message)` is the Identity service's RabbitMQ publishing boundary. It does not create business events itself. Business/application code first stores an `OutboxMessage` in the database, then `OutboxMessageProcessor` later converts that row into an `IntegrationMessage` and passes it into `PublishAsync`.

The `IntegrationMessage` fields used by the publisher are:

- `MessageId`: usually the `OutboxMessage.Id`; used as the AMQP message id and future idempotency key.
- `ExchangeName`: currently `stealdeal.events`.
- `ExchangeType`: currently `topic`.
- `RoutingKey`: for OTP this is `identity.user.email-verification.requested`.
- `EventType`: for OTP this is `SendEmailVerificationOtpEvent`.
- `Payload`: JSON body, for OTP containing `UserId`, `Email`, `FullName`, `Otp`, `ExpiresAt`.
- `OccurredAt`: original outbox creation time, converted to AMQP timestamp.

Step-by-step behavior inside `PublishAsync`:

1. `ValidateMessage(message)` checks that `MessageId`, `ExchangeName`, `ExchangeType`, `RoutingKey`, `EventType`, and `Payload` are present. If any required metadata is missing, it throws before touching RabbitMQ. This prevents silently publishing malformed events.
2. `GetOrCreateConnectionAsync(cancellationToken)` returns the existing open RabbitMQ connection if possible. If there is no open connection, it uses `_connectionLock` (`SemaphoreSlim`) so only one caller creates/replaces the connection at a time. The `ConnectionFactory` has `AutomaticRecoveryEnabled` and `TopologyRecoveryEnabled` enabled.
3. `CreateConfirmedChannelAsync(connection, cancellationToken)` creates a short-lived channel with publisher confirmations enabled. In RabbitMQ, publisher confirms mean `BasicPublishAsync` completes only after the broker acknowledges the publish at exchange level.
4. `PublishAsync` subscribes to `channel.BasicReturnAsync` before publishing. This matters because when `mandatory: true` is used, RabbitMQ returns a message if the exchange exists but the routing key cannot route to any queue. In AMQP, the return can arrive before the publish ack, so the handler must be registered before `BasicPublishAsync`.
5. `DeclareExchangeAsync(channel, message, cancellationToken)` declares the exchange from the message metadata. It uses `durable: true` and `autoDelete: false`. Exchange declaration is idempotent as long as the existing exchange has the same settings.
6. `PublishOnChannelAsync(channel, message, cancellationToken)` serializes `message.Payload` to UTF-8 bytes, creates AMQP properties, and calls `BasicPublishAsync`.
7. `CreateBasicProperties(message)` marks the message as persistent and sets metadata:
   - `Persistent = true`: RabbitMQ should persist the message if the target queue is durable.
   - `Type = message.EventType`: event type metadata for consumers/logging.
   - `ContentType = "application/json"`: payload format.
   - `MessageId = message.MessageId.ToString()`: stable id for tracing/idempotency.
   - `Timestamp = message.OccurredAt`: original event creation time.
8. `BasicPublishAsync` is called with:
   - `exchange = message.ExchangeName`
   - `routingKey = message.RoutingKey`
   - `mandatory = true`
   - `basicProperties = properties`
   - `body = UTF-8 payload bytes`
9. After publishing, `PublishAsync` checks whether `BasicReturnAsync` marked `messageReturned = true`. If yes, it throws `InvalidOperationException` because the broker accepted the publish but could not route the message to any queue.
10. If no exception occurs, `PublishAsync` returns successfully. `OutboxMessageProcessor` then marks the outbox row as `Processed`.

Important reliability meaning:

- Publisher confirms answer: "Did RabbitMQ accept this publish at exchange level?"
- `mandatory: true` plus `BasicReturnAsync` answers: "Was this message routable to at least one queue?"
- Outbox retry handles failures by keeping the database message unprocessed until a later polling attempt.
- The current guarantee is at-least-once delivery, not exactly-once delivery. Consumers should still be idempotent because duplicate publish/consume can happen during retries, crashes, or future scale-out.

Current limitation:

- `PublishBatchAsync` exists but `OutboxMessageProcessor` currently calls `PublishAsync` per message.
- `OutboxMessageProcessor.GetPendingBatchAsync` does not currently use row-level locking, so multiple Identity instances could pick the same pending rows if the service is scaled out.
- Consumer idempotency is not implemented yet in Notification.

## 5. Notification Service

### Responsibility

Notification owns in-app notification data and consumes RabbitMQ events. It currently consumes email verification OTP events from Identity and stores them as `NotificationProfile` records. It does not yet send real emails through SMTP or an email provider.

### Main Domain Model

- `NotificationProfile`: user id, title, body, type, action url, reference id/type, read flag, and created timestamp.

### API Endpoints

`NotificationController`:

- `GET /api/notifications`: gets notifications for the current user.
- `GET /api/notifications/unread-count`: counts unread notifications.
- `PATCH /api/notifications/{id}/read`: marks one notification as read.
- `PATCH /api/notifications/read-all`: marks all notifications as read.
- `POST /api/notifications`: creates a notification; currently open/commented admin authorization for testing/system use.
- `DELETE /api/notifications/{id}`: deletes the current user's own notification.

### RabbitMQ Consumer

Notification RabbitMQ consuming was refactored so infrastructure concerns and business logic are separated:

- Infrastructure owns RabbitMQ connection, exchange/queue declaration, binding, consume loop, deserialization, and ack/nack.
- Application owns event handling/business behavior through `IIntegrationEventHandler<TEvent>`.
- `EmailVerificationConsumer` no longer creates `NotificationProfile` directly and no longer resolves repositories/unit of work directly.

Current implementation pieces:

- `EmailVerificationConsumer`:
  - Hosted service in `Notification.Infrastructure.BackgroundServices`.
  - Reads RabbitMQ broker settings from `RabbitMqSettings`.
  - Reads queue/binding settings from `EmailVerificationConsumerSettings`.
  - Declares exchange `stealdeal.events`, durable queue `notification.email-verification`, binding key `identity.user.email-verification.#`, and QoS/prefetch.
  - Consumes messages with `autoAck: false`.
  - Deserializes the payload into `SendEmailVerificationOtpEvent`.
  - Creates a scoped DI lifetime and resolves `IIntegrationEventHandler<SendEmailVerificationOtpEvent>`.
  - Calls the handler, then `BasicAckAsync` on success.
  - Logs and `BasicNackAsync(..., requeue: false)` on failure.
- `IIntegrationEventHandler<TEvent>`:
  - Generic Application-layer abstraction for handling integration events.
  - Defines `HandleAsync(TEvent @event, CancellationToken cancellationToken = default)`.
  - Keeps RabbitMQ-specific code out of business handlers.
- `SendEmailVerificationOtpEventHandler`:
  - Application-layer handler for `SendEmailVerificationOtpEvent`.
  - Maps the event into `CreateNotificationRequest`.
  - Calls `INotificationService.CreateNotificationAsync(...)`.
  - Produces the current in-app notification with title `Verify Email OTP`, type `EmailVerification`, and body containing the OTP.
- `Program.cs`:
  - Registers `INotificationService`.
  - Registers `IIntegrationEventHandler<SendEmailVerificationOtpEvent>` to `SendEmailVerificationOtpEventHandler`.
  - Registers `EmailVerificationConsumer` via `AddHostedService<EmailVerificationConsumer>()`.

Current `EmailVerificationConsumer` flow:

```text
Notification API startup
  -> Program.cs registers IIntegrationEventHandler<SendEmailVerificationOtpEvent>
  -> Program.cs registers AddHostedService<EmailVerificationConsumer>()
  -> ASP.NET Core host starts EmailVerificationConsumer
  -> ExecuteAsync creates RabbitMQ connection/channel
  -> consumer declares exchange, queue, binding, and QoS
  -> BasicConsumeAsync starts listening with autoAck: false
  -> OnMessageReceivedAsync handles each delivered message
  -> consumer deserializes payload into SendEmailVerificationOtpEvent
  -> consumer resolves IIntegrationEventHandler<SendEmailVerificationOtpEvent>
  -> SendEmailVerificationOtpEventHandler maps event to CreateNotificationRequest
  -> NotificationService creates and saves NotificationProfile
  -> consumer BasicAckAsync on success, BasicNackAsync on failure
```

Startup behavior inside `ExecuteAsync`:

1. Reads RabbitMQ connection settings from `RabbitMqSettings`:
   - `HostName`
   - `Port`
   - `UserName`
   - `Password`
2. Reads consumer-specific settings from `EmailVerificationConsumerSettings`:
   - `ExchangeName`, currently `stealdeal.events`
   - `ExchangeType`, currently `topic`
   - `QueueName`, currently `notification.email-verification`
   - `BindingKey`, currently `identity.user.email-verification.#`
   - `PrefetchCount`
3. Creates a RabbitMQ `ConnectionFactory` with automatic connection and topology recovery enabled.
4. Opens one RabbitMQ connection and one channel for this consumer.
5. Declares the exchange with `durable: true` and `autoDelete: false`. This is idempotent and lets Notification start before Identity if needed.
6. Declares durable queue `notification.email-verification` with:
   - `durable: true`: queue survives broker restart.
   - `exclusive: false`: not tied to only this connection.
   - `autoDelete: false`: not deleted automatically when consumer disconnects.
7. Binds the queue to the exchange using `identity.user.email-verification.#`. Because the exchange is `topic`, this binding catches routing keys beginning with `identity.user.email-verification.`.
8. Configures QoS with `BasicQosAsync`:
   - `prefetchSize = 0`
   - `prefetchCount = _consumerSettings.PrefetchCount`
   - `global = false`
   This limits how many unacked messages RabbitMQ can deliver to this consumer at one time.
9. Creates `AsyncEventingBasicConsumer` and attaches `OnMessageReceivedAsync` to `ReceivedAsync`.
10. Calls `BasicConsumeAsync` with `autoAck: false`, meaning the consumer must explicitly ack/nack each message.
11. Calls `Task.Delay(Timeout.Infinite, stoppingToken)` so the background service stays alive while RabbitMQ event callbacks process messages.

Per-message behavior inside `OnMessageReceivedAsync`:

1. Reads the raw body from `BasicDeliverEventArgs.Body`.
2. Converts the body bytes to a UTF-8 JSON string.
3. Logs routing key and delivery tag for observability.
4. Deserializes JSON into `SendEmailVerificationOtpEvent` with case-insensitive property matching.
5. Throws if the payload cannot be deserialized into the expected event type.
6. Creates a scoped DI lifetime using `_scopeFactory.CreateScope()`. This is important because the consumer itself is a singleton hosted service, while handlers/application services/repositories are scoped.
7. Resolves `IIntegrationEventHandler<SendEmailVerificationOtpEvent>`.
8. Calls `handler.HandleAsync(@event, args.CancellationToken)`.
9. Calls `BasicAckAsync(args.DeliveryTag, multiple: false)` if processing succeeds. This tells RabbitMQ the message can be removed from the queue.
10. If any exception occurs, logs the error and calls `BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false)`. This rejects the message without putting it back into the same queue, avoiding an infinite retry loop for malformed messages.

Application handler behavior for email verification:

```text
SendEmailVerificationOtpEventHandler.HandleAsync(...)
  -> builds CreateNotificationRequest
  -> UserId = event.UserId
  -> Title = "Verify Email OTP"
  -> Body = "Hello {FullName}, your OTP is {Otp}..."
  -> Type = "EmailVerification"
  -> calls INotificationService.CreateNotificationAsync(request)
  -> NotificationService maps request to NotificationProfile and saves through repository + unit of work
```

How to add another Notification consumer/event later:

1. Add the event DTO in `Notification.Application/DTOs/Events`, matching the producer's payload contract.
2. Add an Application handler implementing `IIntegrationEventHandler<NewEvent>`.
3. Put business behavior in the handler, usually by calling an existing Application service or adding a focused method to an Application service.
4. Add consumer-specific settings in Infrastructure configuration if the new event needs its own queue/binding.
5. Add a hosted RabbitMQ consumer for the new queue/binding. Keep it focused on RabbitMQ mechanics and delegate business work to `IIntegrationEventHandler<NewEvent>`.
6. Register the handler and hosted consumer in `Notification.API/Program.cs`.

Current extension rule:

- If a new event can share the same queue and deserialize safely based on a known event type/routing key, consider a small dispatcher later.
- If a new event has a separate queue/binding and only one handler, add a new simple consumer first. Avoid introducing a full event bus abstraction until multiple consumers start duplicating enough code to justify it.

Shutdown behavior:

- `StopAsync` closes the channel first.
- Then it closes the connection.
- Finally it calls `base.StopAsync(cancellationToken)`.

Important reliability meaning:

- `autoAck: false` prevents message loss when the consumer receives a message but fails before saving it.
- Manual ack means the message is only removed after the DB save succeeds.
- `requeue: false` avoids poison-message infinite loops, but because no DLQ is configured yet, failed messages may be dropped by RabbitMQ after nack.
- `PrefetchCount` controls backpressure and prevents one consumer from receiving too many unprocessed messages.
- The current consumer is not idempotent yet. If the same OTP event is delivered twice, it can create duplicate `NotificationProfile` rows.

Current limitations:

- It stores the OTP notification in the database but does not actually send email through SMTP, SendGrid, or another provider.
- There is no processed-message/idempotency table yet.
- There is no dead-letter queue yet.

## 6. Store Service

### Responsibility

Store owns seller store profiles, categories, surprise bags, and store reviews. This service contains the core Too Good To Go-style domain concepts.

### Main Domain Models

- `Category`: name, slug, icon url, active flag, and many-to-many relation with surprise bags.
- `StoreProfile`: owner id, name, description, address, latitude/longitude, avatar, phone, bank account, rating, license, verification flag, and active flag.
- `SurpriseBag`: store id, name, description, original price, sale price, total/remaining quantity, pickup start/end, expiry date, status, categories, and reviews.
- `StoreReview`: order id, buyer id, store id, bag id, rating, comment, store reply, and reported flag.
- `OutboxMessage`: exists in Domain/Infrastructure, but no Store outbox processor or publisher is currently wired.

### API Endpoints

`CategoryController`:

- `GET /api/categories`
- `GET /api/categories/{slug}`
- `POST /api/categories`
- `PUT /api/categories/{id}`
- `DELETE /api/categories/{id}`

`StoreProfileController`:

- `GET /api/stores`
- `GET /api/stores/{id}`
- `GET /api/stores/me`
- `POST /api/stores`
- `PUT /api/stores/{id}`
- `PATCH /api/stores/{id}/verify`
- `PATCH /api/stores/{id}/toggle-active`

`SurpriseBagController`:

- `GET /api/bags`
- `GET /api/bags/{id}`
- `GET /api/bags/store/{storeId}`
- `POST /api/bags`
- `PUT /api/bags/{id}`
- `DELETE /api/bags/{id}`
- `PATCH /api/bags/{id}/status`

`StoreReviewController`:

- `GET /api/reviews/store/{storeId}`
- `GET /api/reviews/bag/{bagId}`
- `POST /api/reviews`
- `PATCH /api/reviews/{id}/reply`
- `PATCH /api/reviews/{id}/report`

### Important Business Rules

- Category slug must be unique.
- One owner can have only one store (`OwnerId` is unique).
- A seller can create a surprise bag only if their store exists, is active, and is verified.
- A surprise bag can belong to many categories; EF Core creates the join table `SurpriseBagCategory`.
- Review rating must be between 1 and 5.
- One order can have only one review (`OrderId` is unique).
- Store reply requires the seller owner to own the reviewed store.
- Reporting a review sets `IsReported`; duplicate reporting is rejected.

### Current Limitations

- Many `[Authorize]` attributes in Store controllers are commented out, so `GetCurrentUserId()` can fail if endpoints are called without authentication while still expecting claims.
- There is no real stock reservation or event consumer yet.
- `OutboxMessage` exists, but Store does not currently publish integration events.

## 7. Order Service

### Responsibility

Order owns order profiles, order items, order status changes, and pickup disputes.

### Main Domain Models

- `OrderProfile`: buyer/user id, store id, store name snapshot, delivery fee, voucher discount, total amount, delivery type/address, optional pickup code/deadline, status, timestamps, items, and disputes.
- `OrderItem`: bag id, bag name snapshot, unit price snapshot, quantity, and subtotal.
- `PickupDispute`: order id, reporter id, dispute type, evidence urls, description, and status.

### API Endpoints

`OrderController`:

- `POST /api/orders`: creates an order; authentication required.
- `GET /api/orders/{id}`: gets order by id; authentication required.
- `GET /api/orders/my-orders`: gets current user's orders.
- `GET /api/orders/store/{storeId}`: gets store orders; Seller/Admin.
- `PATCH /api/orders/{id}/status`: updates order status.

`PickupDisputeController`:

- `POST /api/pickup-disputes`: creates a dispute.
- `GET /api/pickup-disputes/{id}`: gets a dispute.
- `GET /api/pickup-disputes`: admin gets all disputes.
- `PATCH /api/pickup-disputes/{id}/status`: admin updates dispute status.

### Important Business Rules

- Creating an order requires at least one item.
- Order access:
  - Admin can view.
  - Buyer can view their own order.
  - Any Seller role can currently view, with comments noting that real seller/store ownership should be verified through cross-service calls or claims.
- Cancel status:
  - Buyer, Seller, or Admin can cancel only while the current status is `Pending`.
- Non-cancel progression:
  - Seller or Admin can update order progression status.
- Pickup dispute access:
  - Admin, reporter, or order buyer can view.
  - Seller/reporting ownership checks are not fully enforced yet.
- `EvidenceUrls` is serialized as JSON into SQL Server `nvarchar(max)`.

### Current Limitations

- There is no saga/event publishing or consuming yet.
- There is no cross-service verification for store ownership.
- There is no stock reservation or payment orchestration yet.
- Status values are free-form strings, not enums/constants.

## 8. Payment Service

### Responsibility

Payment owns transaction and refund records. It does not integrate with a real payment gateway yet.

### Main Domain Models

- `Transaction`: order id, user id, amount, payment method, gateway ref, status, failure reason, paid timestamp, timestamps, and refunds.
- `Refund`: transaction id, order id, amount, reason, status, and processed timestamp.

### API Endpoints

`TransactionController`:

- `POST /api/transactions`: creates a transaction for the current user.
- `GET /api/transactions/{id}`: views transaction if owner/admin.
- `GET /api/transactions/order/{orderId}`: views transaction by order if owner/admin.
- `GET /api/transactions/my-transactions`: gets current user's transactions.
- `PATCH /api/transactions/{id}/status`: Admin updates status, gateway ref, and failure reason.

`RefundController`:

- `POST /api/refunds`: Seller/Admin creates a refund.
- `GET /api/refunds/{id}`: owner/admin can view.
- `GET /api/refunds/transaction/{transactionId}`: owner/admin can view transaction refunds.
- `GET /api/refunds`: Admin gets all refunds.
- `PATCH /api/refunds/{id}/status`: Admin updates refund status.

### Important Business Rules

- Creating a transaction is rejected if an existing transaction for the same order is `Success` or `Pending`.
- Updating a transaction to `Success` sets `PaidAt`.
- Refunds can only be created for successful transactions.
- The sum of pending + processed refunds must not exceed the original transaction amount.
- Updating a refund to `Processed` sets `ProcessedAt`.

### Current Limitations

- Payment gateway integration is not implemented.
- There is no RabbitMQ integration/event publish/consume yet.
- Important config bug: `Program.cs` reads `IdentityDb`, while `appsettings.json` has `StoreDb`; this should become `PaymentDb`/`StealDealPaymentDb`.

## 9. Database/Migrations Status

Observed migrations:

- Identity has migration `20260629043013_InitDatabase`.
- Notification has migration `20260717070827_InitNotiDb`.
- Store/Order/Payment have DbContexts but no migrations visible in the current tree.

DbContext highlights:

- Identity configures unique user email, cascade relationships, OTP indexes, and outbox.
- Store configures unique category slug, unique store owner, many-to-many bag/category relation, review indexes, and unique review by order.
- Order configures order/items/disputes and JSON conversion for dispute evidence URLs.
- Payment configures transaction/refund relationship with restricted delete.
- Notification configures notification profile columns.

## 10. Messaging/Event Status

Fully implemented and wired:

```text
Identity register/resend OTP
  -> Identity DB transaction creates EmailVerification + OutboxMessage
  -> OutboxMessageProcessor polls Pending message
  -> RabbitMqMessagePublisher publishes to stealdeal.events
  -> Notification EmailVerificationConsumer consumes from notification.email-verification
  -> Notification creates NotificationProfile record containing OTP
```

Event contract classes are currently duplicated by service:

- Identity Application DTO: `SendEmailVerificationOtpEvent`, `IntegrationMessage`
- Notification Application DTO: `SendEmailVerificationOtpEvent`
- Store Application has `IntegrationMessage`, but no publisher is currently wired.

Planned but not implemented:

- Store stock reservation events.
- Order saga events.
- Payment completed/failed/refunded events.
- Notification consumers for order/payment events.
- Consumer idempotency table / processed message tracking.
- Dead-letter queue and consumer retry policy.

## 11. Security Notes

Implemented:

- BCrypt password hashing.
- Refresh token hash storage.
- OTP hash storage.
- JWT bearer validation across services.
- HttpOnly refresh token cookie in Identity.
- Admin role authorization on all Identity user-management endpoints.
- Authenticated self-service profile and password endpoints.
- Global exception middleware per service.

Needs attention:

- JWT secret is a placeholder in `appsettings.json`.
- Raw OTP is stored in the outbox payload and then in the notification body; acceptable for development, but should be hardened.
- OTP rate-limiting fields exist but are not enforced.
- Store authorization attributes are mostly commented out.
- Cross-service ownership checks are incomplete for seller/store/order/payment flows.
- Status values and roles are string-based.

## 12. Known Gaps / TODO For Next Context

High priority:

- Fix Payment DB config (`PaymentDb` connection string + `Program.cs`).
- Decide role naming consistency: code uses `Customer` at registration, while the product language says buyer; controller comments often use Buyer wording.
- Re-enable and verify `[Authorize]` on Store endpoints once the auth flow is ready.
- Add migrations for Store, Order, and Payment if they were not generated elsewhere.
- Add real email sending in Notification or explicitly rename the current behavior as an in-app OTP notification.
- Add tests for Identity auth/OTP/outbox and CRUD services.

Architecture improvements:

- Add consumer idempotency and DLQ. (Later when complete all happy path workflow)
- Add cancellation token propagation through repositories and EF calls.
- Add logging in application services for important flows.
- Add API Gateway later, likely YARP or Ocelot, for centralized routing/JWT/CORS/rate limiting.

Business flow still pending:

- Surprise bag stock reservation.
- Order-payment-store saga choreography.
- Payment gateway or mock gateway.
- Refund/order cancellation compensation.
- Trust score updates from order/dispute/no-show events.
- Seller ownership verification across services.

## 13. How To Continue Efficiently

When opening a new context window:

1. Read this file first.
2. If the task touches Identity or messaging, also read `Identity/IDENTITY_PLAN.md` and the relevant Identity/Notification files.
3. If the task touches roadmap/history, skim `STEALDEAL_REVIEW_AND_PLAN.md`.
4. Then inspect only the service files needed for the exact change.

Recommended next implementation sequence:

1. Fix Payment config and ensure all services build.
2. Add or verify migrations for Store, Order, and Payment.
3. Harden authorization on Store endpoints.
4. Add real Notification email sending for OTP.
5. Start event-driven order flow: `order.created` -> Store reserve -> Payment create/process -> Order confirm/cancel.
