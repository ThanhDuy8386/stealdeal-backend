# Identity Service Plan

This file is the running implementation log for the Identity service.

Purpose:
- Keep the current plan in one place.
- Record architecture decisions so later sessions do not need to rediscover them.
- Append new phases and notes instead of rewriting the whole file.

How to use:
- Add a new dated section when we start a new task.
- Mark checklist items as done as implementation progresses.
- Keep short notes about decisions, assumptions, and follow-up work.
- Do not treat this file as the source of truth for code. The code is still the source of truth.

## 2026-05-22 - Implement `IAuthService`

### Current status
- Done: `IUserRepository`
- Done: `IRefreshTokenRepository`
- Done: `IUnitOfWork`
- Done: `PasswordHasher`
- Done: `JwtTokenGenerator`
- Next: implement `IAuthService`

### Before coding
- Fix `IAuthService.cs`: it currently declares `IRefreshTokenRepository` instead of `IAuthService`.
- Confirm DTO shapes for:
  - `RegisterRequest`
  - `LoginRequest`
  - `TokenResponse`
- Confirm `User` supports the fields needed for registration:
  - `Email`
  - `PasswordHash`
  - `FullName` or `FirstName` + `LastName`
  - `IsActive`
  - `IsDeleted`

### `IAuthService` responsibilities
- `RegisterAsync`
  - Validate request fields.
  - Normalize email.
  - Check email uniqueness.
  - Validate role is `Customer` or `Seller`.
  - Hash password.
  - Create user.
  - Add default trust score if needed.
  - Add selected user role.
  - Generate access token.
  - Generate refresh token.
  - Store refresh token hash.
  - Commit with `IUnitOfWork`.
  - Return `TokenResponse`.
- `LoginAsync`
  - Normalize email.
  - Find user by email.
  - Reject deleted or inactive user.
  - Verify password.
  - Generate access token.
  - Generate refresh token.
  - Store refresh token hash.
  - Commit with `IUnitOfWork`.
  - Return `TokenResponse`.
- `RefreshAsync`
  - Hash incoming raw refresh token.
  - Find stored refresh token by hash.
  - Reject missing, revoked, expired token.
  - Load related user.
  - Reject deleted or inactive user.
  - Revoke old refresh token.
  - Generate new access token.
  - Generate new refresh token.
  - Store new refresh token hash.
  - Commit with `IUnitOfWork`.
  - Return `TokenResponse`.

### Dependencies for `AuthService`
- `IUserRepository`
- `IRefreshTokenRepository`
- `IUnitOfWork`
- `IPasswordHasher`
- `IJwtTokenGenerator`

### Suggested file layout
- `StealDeal.Services.Identity.Application/Services/Interfaces/IAuthService.cs`
- `StealDeal.Services.Identity.Application/Services/AuthService.cs`

### Validation rules for now
- Email must not be empty.
- Password must not be empty.
- Password should be at least 8 characters.
- Role must be `Customer` or `Seller`.
- Login should return a generic invalid-credentials message instead of revealing whether email exists.

### Token policy for now
- Access token expiration comes from `Jwt:AccessTokenMinutes`.
- Refresh token expiration comes from `Jwt:RefreshTokenDays`.
- Store only refresh token hash in database.
- Return the raw refresh token to the client only once.

### Architecture rules
- Domain interfaces:
  - Use for abstractions that belong to the core model and persistence boundary.
  - Example: repositories, unit of work.
- Application interfaces:
  - Use for abstractions required by use cases.
  - Example: `IAuthService`, `IPasswordHasher`, `IJwtTokenGenerator`.
- Application implementations:
  - Put use-case orchestration here.
  - Example: `AuthService`.
- Infrastructure implementations:
  - Put technical details here.
  - Example: EF repositories, BCrypt hashing, JWT generation.

### Rule of thumb
- If the code answers "how does this use case flow work?" -> Application.
- If the code answers "how do we talk to a framework, database, crypto library, or external system?" -> Infrastructure.
- If the code is a stable business concept/entity -> Domain.

### Notes
- This file helps reduce repeated re-explaining across sessions, but it does not replace reading code.
- We should still inspect the relevant files before changing behavior.
- The best use of this file is decisions, checklist, and intent.
- JWT settings decision:
  - Use typed `JwtSettings` with `IOptions<JwtSettings>` in Infrastructure.
  - Do not inject `IConfiguration` into `AuthService`.
  - `JwtTokenGenerator` owns token expiry values from configuration.

### Next implementation sequence
- [x] Correct `IAuthService` interface file and name.
- [x] Create `AuthService` class.
- [x] Add request validation and normalization.
- [x] Implement `RegisterAsync`.
- [x] Implement `LoginAsync`.
- [x] Implement `RefreshAsync`.
- [ ] Wire DI in `Program.cs`.
- [ ] Build and fix compile issues.
- [ ] Add controller endpoints if not already present.
- [x] Add Swagger UI with Bearer auth support for local API testing.
- [x] Remove Swagger UI setup and return to Postman-based API testing.

## 2026-05-29 - Current Auth Status

### Existing API endpoints
- `POST /api/auth/register`: creates a user, creates email OTP data, creates an outbox message, creates refresh token, returns access/refresh token.
- `POST /api/auth/login`: verifies email/password, creates refresh token, returns access/refresh token.
- `POST /api/auth/refresh`: rotates refresh token and returns a new access/refresh token pair.
- `POST /api/auth/verify-email`: verifies OTP, consumes OTP, marks user email as verified.
- `POST /api/auth/resend-otp`: revokes current active OTP, creates a new OTP, creates a new outbox message.
- `GET /api/auth/me`: protected endpoint used to test JWT authentication.

### Completed
- [x] Register works.
- [x] Login works.
- [x] Refresh token works.
- [x] Resend OTP works.
- [x] Verify OTP works.
- [x] Outbox message is created when OTP email should be sent.
- [x] Refresh token is stored as hash.
- [x] OTP is stored as hash in `EmailVerifications`.
- [x] Raw OTP is stored in outbox payload so Notification service can send it by email.

### Important notes
- Identity service owns user, token, OTP, and outbox database updates.
- Notification service should only send email. It should not update Identity database.
- User verifies email by calling Identity service with the OTP.
- Old JWT access tokens remain valid until they expire. This is normal.
- Old refresh tokens should become unusable after refresh token rotation.
- Raw OTP in outbox is acceptable for development, but later we should protect it better.

### Outbox and RabbitMQ direction
- `OutboxMessage` should contain publish metadata:
  - `ExchangeName`
  - `ExchangeType`
  - `RoutingKey`
  - `EventType`
  - `Payload`
- `IMessagePublisher.PublishAsync` should accept `IntegrationMessage`.
- `RabbitMqMessagePublisher` should reuse one RabbitMQ connection.
- `RabbitMqMessagePublisher` should create a channel when publishing.
- Prefer `topic` exchange for integration events.
- Recommended exchange: `stealdeal.events`.
- Recommended routing key example: `identity.user.email-verification.requested`.

### Next work
- [ ] Finish flexible `RabbitMqMessagePublisher`.
- [ ] Implement background job to scan pending outbox messages.
- [ ] Publish pending outbox messages to RabbitMQ.
- [ ] Mark message as processed or failed.
- [ ] Build Notification service to consume the queue and send OTP email.
