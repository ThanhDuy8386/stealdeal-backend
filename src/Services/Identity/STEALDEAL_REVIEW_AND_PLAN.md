# StealDeal Backend — Identity Service Review & 2-Month Development Plan

> **Date**: 2026-06-29  
> **Scope**: Review Identity service hiện tại + Kế hoạch 2 tháng (07/2026 – 08/2026) implement full backend  
> **Architecture**: Microservices (Identity, Store, Order, Payment, Notification)  
> **Patterns**: Outbox Pattern, Saga Choreography

---

## Phần 1: Review Identity Service Hiện Tại

### 1.1 Tổng quan kiến trúc

Dự án tuân thủ Clean Architecture 4 layers rất tốt:

| Layer | Project | Trách nhiệm |
|-------|---------|-------------|
| Domain | `StealDeal.Services.Identity.Domain` | Entities, Repository interfaces |
| Application | `StealDeal.Services.Identity.Application` | DTOs, Service interfaces, Service implementations |
| Infrastructure | `StealDeal.Services.Identity.Infrastructure` | EF Core, RabbitMQ, JWT, BCrypt, Background jobs |
| API | `StealDeal.Services.Identity.API` | Controllers, DI, Middleware |

> [!TIP]
> Dependency direction đúng: API → Application ← Infrastructure → Domain. Domain layer không có dependency ngoài nào.

### 1.2 Các chức năng đã hoàn thiện

- ✅ Register (tạo user + role + trust score + OTP + outbox message)
- ✅ Login (verify password, issue token pair)
- ✅ Refresh Token Rotation (revoke old, issue new)
- ✅ Email Verification OTP (hash OTP, verify, consume)
- ✅ Resend OTP (revoke active OTP, create new, create outbox)
- ✅ GET /me (protected endpoint test JWT)
- ✅ Outbox Pattern (background job scan → publish to RabbitMQ)
- ✅ RabbitMQ publisher (topic exchange, reuse connection, channel per publish)

### 1.3 Điểm mạnh 👍

1. **Outbox Pattern triển khai đúng**: Business data + outbox message trong cùng 1 transaction → đảm bảo at-least-once delivery.
2. **Security tốt**: Password hash bằng BCrypt, refresh token hash bằng SHA256, OTP hash bằng SHA256. Không lưu raw secret trong DB.
3. **Token Rotation**: Refresh token cũ bị revoke khi issue token mới → chống token replay.
4. **RabbitMQ connection management**: Double-check locking pattern với `SemaphoreSlim`, reuse connection, `IAsyncDisposable`.
5. **Background service resilient**: Try-catch per message, retry count, fail after max retry, không crash toàn bộ batch khi 1 message lỗi.
6. **Clean Architecture tuân thủ**: Interface segregation tốt, không leak infrastructure detail vào Application layer.

### 1.4 Các vấn đề cần cải thiện ⚠️

#### 1.4.1 Thiếu Global Exception Handling / Result Pattern

[AuthController.cs](file:///c:/Users/ADMIN/Desktop/Capstone-BE/stealdeal-backend/src/Services/Identity/StealDeal.Services.Identity.API/Controllers/AuthController.cs) dùng try-catch lặp lại ở mọi action. Hiện tại catch `InvalidOperationException` ở register nhưng lại catch `UnauthorizedAccessException` ở verify-email (mà verify-email throw `InvalidOperationException` → sẽ trả 500 thay vì 400).

```csharp
// VerifyEmail catch UnauthorizedAccessException nhưng VerifyEmailOtpAsync throw InvalidOperationException
// → Bug: khi OTP invalid sẽ trả 500 Internal Server Error thay vì 400 Bad Request
```

> [!WARNING]
> **Bug ở endpoint verify-email và resend-otp**: Exception type không khớp với catch block. Nên triển khai **global exception handler middleware** hoặc **Result pattern** (`Result<T>`) thay vì throw exception cho business logic.

#### 1.4.2 Magic Strings cho Status

[OutboxMessage.cs](file:///c:/Users/ADMIN/Desktop/Capstone-BE/stealdeal-backend/src/Services/Identity/StealDeal.Services.Identity.Domain/Models/OutboxMessage.cs#L10) dùng magic string `"Pending"`, `"Processed"`, `"Failed"`. Nên dùng `enum` hoặc `static class` constants.

```csharp
// Hiện tại
public string Status { get; set; } = "Pending";

// Nên
public static class OutboxStatus
{
    public const string Pending = "Pending";
    public const string Processed = "Processed";
    public const string Failed = "Failed";
}
```

#### 1.4.3 Thiếu CancellationToken propagation

[AuthService.cs](file:///c:/Users/ADMIN/Desktop/Capstone-BE/stealdeal-backend/src/Services/Identity/StealDeal.Services.Identity.Application/Services/AuthService.cs): Các method nhận `CancellationToken` nhưng **không truyền** xuống repository calls. Ví dụ `LoginAsync` nhận `cancellationToken` nhưng `_userRepository.GetByEmailAsync()` không nhận token.

#### 1.4.4 Repository method inconsistency

- [IUserRepository](file:///c:/Users/ADMIN/Desktop/Capstone-BE/stealdeal-backend/src/Services/Identity/StealDeal.Services.Identity.Domain/Interfaces/Repositories/IUserRepository.cs): `UpdateAsync` trả `Task` nhưng [UserRepository.UpdateAsync](file:///c:/Users/ADMIN/Desktop/Capstone-BE/stealdeal-backend/src/Services/Identity/StealDeal.Services.Identity.Infrastructure/Repositories/UserRepository.cs#L54-L57) chỉ gọi `_context.Users.Update()` (synchronous) → method async nhưng không await gì.
- `DeleteAsync` cũng tương tự, return `Task.CompletedTask`.
- [IRefreshTokenRepository.Update](file:///c:/Users/ADMIN/Desktop/Capstone-BE/stealdeal-backend/src/Services/Identity/StealDeal.Services.Identity.Domain/Interfaces/Repositories/IRefreshTokenRepository.cs#L9) là `void` nhưng `IUserRepository.UpdateAsync` là `Task` → không nhất quán. Nên thống nhất: `Update` là `void` (vì EF change tracking, save ở UoW).

#### 1.4.5 `IsEmailUniqueAsync` performance

[UserRepository.IsEmailUniqueAsync](file:///c:/Users/ADMIN/Desktop/Capstone-BE/stealdeal-backend/src/Services/Identity/StealDeal.Services.Identity.Infrastructure/Repositories/UserRepository.cs#L48-L52) load toàn bộ User entity chỉ để check tồn tại. Nên dùng `AnyAsync`:

```csharp
public async Task<bool> IsEmailUniqueAsync(string email)
{
    return !await _context.Users.AnyAsync(u => u.Email == email);
}
```

#### 1.4.6 Domain model thiếu encapsulation

Tất cả entity dùng `public set` → cho phép thay đổi state từ bất kỳ đâu. Với DDD approach, nên dùng `private set` + factory method/domain method. Tuy nhiên vì dùng EF Core, đây là trade-off chấp nhận được cho tốc độ phát triển.

#### 1.4.7 Thiếu Rate Limiting cho OTP

- Không giới hạn số lần resend OTP (chỉ có `ResendCount` field nhưng không check).
- Không giới hạn số lần verify OTP sai (chỉ có `AttemptCount` field nhưng không tăng khi verify sai).

#### 1.4.8 Outbox `GetPendingBatchAsync` không có locking

Nếu chạy nhiều instance Identity service (scale out), 2 instance có thể pick cùng 1 batch outbox messages → duplicate publish. Cần row-level locking hoặc `SELECT ... WITH (UPDLOCK, READPAST)` cho SQL Server.

#### 1.4.9 Thiếu Logging ở Application layer

[AuthService](file:///c:/Users/ADMIN/Desktop/Capstone-BE/stealdeal-backend/src/Services/Identity/StealDeal.Services.Identity.Application/Services/AuthService.cs) không inject `ILogger`. Chỉ có logging ở `OutboxMessageProcessor`. Nên thêm structured logging cho register, login, refresh failures.

#### 1.4.10 Thiếu Standardized API Response

Mỗi endpoint trả response format khác nhau: `TokenResponse`, `{ message = "..." }`, raw object. Nên có `ApiResponse<T>` wrapper thống nhất.

### 1.5 Tóm tắt đánh giá

| Tiêu chí | Đánh giá | Ghi chú |
|----------|----------|---------|
| Clean Architecture | ⭐⭐⭐⭐⭐ | Tuân thủ tốt |
| Security | ⭐⭐⭐⭐ | Tốt, cần thêm rate limiting |
| Error Handling | ⭐⭐ | Bug exception mismatch, thiếu global handler |
| Code Consistency | ⭐⭐⭐ | Repository interface không nhất quán |
| Messaging/Outbox | ⭐⭐⭐⭐ | Tốt, cần locking cho scale-out |
| Testing | ⭐ | Chưa có unit test / integration test |
| Logging & Observability | ⭐⭐ | Chỉ có ở background service |

---

## Phần 2: Kế Hoạch 2 Tháng (07/2026 – 08/2026)

### 2.1 Tổng quan Services

```
┌──────────┐    ┌──────────┐    ┌──────────┐    ┌──────────┐    ┌──────────────┐
│ Identity │    │  Store   │    │  Order   │    │ Payment  │    │ Notification │
│ Service  │    │ Service  │    │ Service  │    │ Service  │    │   Service    │
└────┬─────┘    └────┬─────┘    └────┬─────┘    └────┬─────┘    └──────┬───────┘
     │               │               │               │                │
     └───────────────┴───────────────┴───────────────┴────────────────┘
                              RabbitMQ (stealdeal.events)
```

### 2.2 Saga Choreography — Order Flow

```
Customer tạo order
    │
    ▼
[Order Service] ── order.created ──► [Store Service] kiểm kho & reserve
    ▲                                        │
    │                                        ▼
    │                              store.stock.reserved ──► [Payment Service] xử lý thanh toán
    │                              store.stock.failed ────► [Order Service] cancel order
    │                                                              │
    │                                                              ▼
    │                                               payment.completed ──► [Order Service] confirm
    │                                               payment.failed ──────► [Store Service] release stock
    │                                                                      [Order Service] cancel
    │
    ▼
[Notification Service] lắng nghe tất cả event → gửi email/push
```

---

### 2.3 Timeline Chi Tiết

#### 🗓️ Tuần 1 (01/07 – 06/07): Foundation & Shared Infrastructure

**Mục tiêu**: Xây dựng shared components dùng chung cho tất cả services.

- [ ] **Shared Kernel / BuildingBlocks library**
  - `ApiResponse<T>` standardized response wrapper
  - `Result<T>` pattern thay thế exception cho business logic
  - Global exception handler middleware
  - Base entity classes (`BaseEntity`, `AuditableEntity`)
  - Shared outbox infrastructure (tái sử dụng từ Identity)
  - Shared RabbitMQ consumer base class
  - Common integration event contracts (shared DTOs giữa services)
  - Constants cho exchange names, routing keys
- [ ] **Fix Identity Service issues** (từ review)
  - Fix verify-email / resend-otp exception mismatch bug
  - Thêm global exception handler middleware
  - Thêm `ApiResponse<T>` wrapper
  - Dùng constants cho outbox status
  - Thêm `CancellationToken` propagation
  - Thống nhất repository interfaces
  - Fix `IsEmailUniqueAsync` dùng `AnyAsync`
  - Thêm OTP rate limiting (max resend, max attempts)
- [ ] **Docker Compose setup**
  - SQL Server container
  - RabbitMQ container
  - Mỗi service 1 container
  - Network configuration

---

#### 🗓️ Tuần 2-3 (07/07 – 20/07): Store Service

**Mục tiêu**: Hoàn thiện quản lý sản phẩm, danh mục, kho.

**Domain Models:**
- `Category` (Id, Name, Slug, ParentId, Image, IsActive)
- `Product` (Id, SellerId, CategoryId, Name, Slug, Description, Condition, Images, Status)
- `ProductVariant` (Id, ProductId, Sku, Price, OriginalPrice, Stock, Attributes)
- `StockReservation` (Id, VariantId, OrderId, Quantity, Status, ExpiresAt)

**Endpoints:**

| Method | Route | Mô tả |
|--------|-------|-------|
| GET | `/api/categories` | Danh sách danh mục (tree) |
| GET | `/api/categories/{slug}` | Chi tiết danh mục |
| POST | `/api/categories` | Tạo danh mục (Admin) |
| PUT | `/api/categories/{id}` | Sửa danh mục (Admin) |
| DELETE | `/api/categories/{id}` | Xóa danh mục (Admin) |
| GET | `/api/products` | Danh sách sản phẩm (paging, filter, search) |
| GET | `/api/products/{slug}` | Chi tiết sản phẩm |
| GET | `/api/products/seller/{sellerId}` | Sản phẩm theo seller |
| POST | `/api/products` | Đăng sản phẩm (Seller) |
| PUT | `/api/products/{id}` | Sửa sản phẩm (Seller) |
| DELETE | `/api/products/{id}` | Xóa sản phẩm (Seller) |
| PATCH | `/api/products/{id}/status` | Approve/Reject sản phẩm (Admin) |
| POST | `/api/products/{id}/variants` | Thêm variant |
| PUT | `/api/products/{id}/variants/{variantId}` | Sửa variant |
| DELETE | `/api/products/{id}/variants/{variantId}` | Xóa variant |

**Integration Events (Publish):**
- `store.stock.reserved` — kho đã reserve thành công
- `store.stock.reservation-failed` — không đủ hàng
- `store.stock.released` — trả lại kho (khi order cancel/payment fail)

**Integration Events (Consume):**
- `order.created` → Reserve stock
- `payment.failed` → Release stock
- `order.cancelled` → Release stock

---

#### 🗓️ Tuần 3-4 (14/07 – 27/07): Order Service

**Mục tiêu**: Quản lý đơn hàng, trạng thái, saga coordination.

**Domain Models:**
- `Order` (Id, BuyerId, SellerId, Status, TotalAmount, ShippingAddress, Notes)
- `OrderItem` (Id, OrderId, ProductId, VariantId, ProductName, Quantity, UnitPrice)
- `OrderStatusHistory` (Id, OrderId, FromStatus, ToStatus, ChangedBy, Note)

**Order Status Flow:**
```
Pending → StockReserved → PaymentProcessing → Confirmed → Shipping → Delivered → Completed
    │           │                │                                         │
    ▼           ▼                ▼                                         ▼
 Cancelled   Cancelled       Cancelled                                  Disputed → Resolved
```

**Endpoints:**

| Method | Route | Mô tả |
|--------|-------|-------|
| POST | `/api/orders` | Tạo đơn hàng (Customer) |
| GET | `/api/orders` | Danh sách đơn hàng (paging, filter theo role) |
| GET | `/api/orders/{id}` | Chi tiết đơn hàng |
| PATCH | `/api/orders/{id}/cancel` | Hủy đơn (Customer/Seller) |
| PATCH | `/api/orders/{id}/confirm-shipping` | Xác nhận gửi hàng (Seller) |
| PATCH | `/api/orders/{id}/confirm-delivery` | Xác nhận nhận hàng (Customer) |
| PATCH | `/api/orders/{id}/complete` | Hoàn thành đơn |
| GET | `/api/orders/{id}/history` | Lịch sử trạng thái |

**Integration Events (Publish):**
- `order.created` — đơn hàng mới (trigger reserve stock)
- `order.cancelled` — đơn đã hủy (trigger release stock, refund)
- `order.confirmed` — đơn đã xác nhận (sau payment success)
- `order.completed` — đơn hoàn thành

**Integration Events (Consume):**
- `store.stock.reserved` → Chuyển trạng thái → PaymentProcessing
- `store.stock.reservation-failed` → Cancel order
- `payment.completed` → Confirm order
- `payment.failed` → Cancel order

---

#### 🗓️ Tuần 5 (28/07 – 03/08): Payment Service

**Mục tiêu**: Xử lý thanh toán, tích hợp payment gateway.

**Domain Models:**
- `Payment` (Id, OrderId, BuyerId, Amount, Method, Status, TransactionId, GatewayResponse)
- `PaymentMethod`: Enum (COD, BankTransfer, EWallet, VnPay, Momo)
- `Refund` (Id, PaymentId, Amount, Reason, Status)

**Endpoints:**

| Method | Route | Mô tả |
|--------|-------|-------|
| POST | `/api/payments` | Tạo payment intent |
| GET | `/api/payments/{id}` | Chi tiết payment |
| GET | `/api/payments/order/{orderId}` | Payment theo order |
| POST | `/api/payments/{id}/confirm` | Xác nhận thanh toán (webhook/manual) |
| POST | `/api/payments/webhook/vnpay` | VNPay callback |
| POST | `/api/payments/{id}/refund` | Yêu cầu hoàn tiền |
| GET | `/api/payments/history` | Lịch sử thanh toán (user) |

**Integration Events (Publish):**
- `payment.completed` — thanh toán thành công
- `payment.failed` — thanh toán thất bại
- `payment.refunded` — hoàn tiền thành công

**Integration Events (Consume):**
- `store.stock.reserved` → Khởi tạo payment processing
- `order.cancelled` → Cancel pending payment / Process refund

---

#### 🗓️ Tuần 6 (04/08 – 10/08): Notification Service

**Mục tiêu**: Email, push notification, in-app notification.

**Domain Models:**
- `NotificationTemplate` (Id, Type, Subject, Body, Channel)
- `NotificationLog` (Id, UserId, Channel, Type, Status, SentAt)
- `ProcessedMessage` (MessageId, ProcessedAt) — consumer idempotency

**Channels:**
- Email (SMTP / SendGrid)
- In-app notification (lưu DB, query qua API)
- Push notification (Firebase FCM — phase 2)

**Endpoints:**

| Method | Route | Mô tả |
|--------|-------|-------|
| GET | `/api/notifications` | Danh sách notification (user) |
| GET | `/api/notifications/unread-count` | Số notification chưa đọc |
| PATCH | `/api/notifications/{id}/read` | Đánh dấu đã đọc |
| PATCH | `/api/notifications/read-all` | Đánh dấu tất cả đã đọc |

**Integration Events (Consume):**
- `identity.user.email-verification.requested` → Gửi OTP email
- `order.created` → Thông báo seller có đơn mới
- `order.confirmed` → Thông báo customer đơn đã xác nhận
- `order.cancelled` → Thông báo bên liên quan
- `payment.completed` → Thông báo thanh toán thành công
- `payment.refunded` → Thông báo hoàn tiền
- `order.completed` → Thông báo hoàn thành + mời đánh giá

---

#### 🗓️ Tuần 7 (11/08 – 17/08): Identity Enhancement + Cross-Service Features

**Mục tiêu**: Hoàn thiện Identity service, thêm các feature cross-service.

- [ ] **Identity Service enhancements**
  - `POST /api/auth/logout` — revoke all refresh tokens
  - `POST /api/auth/change-password`
  - `POST /api/auth/forgot-password` — send reset link via outbox
  - `POST /api/auth/reset-password`
  - `GET /api/users/{id}/profile` — public profile
  - `PUT /api/users/profile` — update profile (name, phone, avatar)
  - `CRUD /api/users/addresses` — quản lý địa chỉ
  - `GET /api/admin/users` — admin quản lý user (paging, filter, ban)
  - `PATCH /api/admin/users/{id}/ban` — ban user
  - Trust score consumer (consume events từ Order service)
- [ ] **API Gateway** (YARP hoặc Ocelot)
  - Routing tới các services
  - JWT validation tập trung
  - Rate limiting
  - CORS configuration
- [ ] **Health Checks** cho tất cả services
  - Database health
  - RabbitMQ health
  - Custom health endpoints

---

#### 🗓️ Tuần 8 (18/08 – 24/08): Testing, Polish & Documentation

**Mục tiêu**: Đảm bảo chất lượng, hoàn thiện.

- [ ] **Unit Tests** (xUnit + Moq/NSubstitute)
  - Test AuthService (register, login, refresh, verify OTP)
  - Test Order saga transitions
  - Test Payment processing
  - Test Stock reservation logic
- [ ] **Integration Tests**
  - Test outbox → RabbitMQ → consumer flow
  - Test saga end-to-end (order → stock → payment → confirm)
  - Test API endpoints với WebApplicationFactory
- [ ] **Saga Compensation Tests**
  - Stock reservation fail → order cancelled
  - Payment fail → stock released + order cancelled
  - Timeout scenarios
- [ ] **Documentation**
  - API documentation (Swagger/OpenAPI cho mỗi service)
  - Architecture decision records
  - Deployment guide
  - Postman collection cho tất cả endpoints
- [ ] **Final Polish**
  - Review tất cả TODO items
  - Security review (input validation, authorization checks)
  - Performance review (N+1 queries, missing indexes)
  - Cleanup dead code, commented code

---

### 2.4 Shared Integration Event Contracts

```
stealdeal.events (topic exchange)
├── identity.user.email-verification.requested
├── identity.user.registered
├── identity.user.trust-score.updated
├── store.stock.reserved
├── store.stock.reservation-failed
├── store.stock.released
├── store.product.created
├── store.product.updated
├── order.created
├── order.confirmed
├── order.cancelled
├── order.completed
├── order.shipping
├── order.delivered
├── payment.completed
├── payment.failed
├── payment.refunded
└── notification.sent
```

### 2.5 Database Strategy

Mỗi service có database riêng (Database-per-Service pattern):

| Service | Database | Ghi chú |
|---------|----------|---------|
| Identity | `StealDealIdentityDb` | ✅ Đã có |
| Store | `StealDealStoreDb` | Products, Categories, Variants, Stock |
| Order | `StealDealOrderDb` | Orders, OrderItems, StatusHistory |
| Payment | `StealDealPaymentDb` | Payments, Refunds |
| Notification | `StealDealNotificationDb` | Templates, Logs, ProcessedMessages |

### 2.6 Rủi ro & Mitigation

| Rủi ro | Mức độ | Mitigation |
|--------|--------|-----------|
| Saga compensation phức tạp | Cao | Start simple (happy path), thêm compensation dần |
| Duplicate message processing | Trung bình | Idempotency key (`ProcessedMessages` table) |
| Outbox concurrent processing | Trung bình | Row-level locking hoặc single-instance processor |
| Payment gateway integration | Cao | Mock gateway cho dev, tích hợp real gateway khi deploy |
| Timeline quá tight | Cao | Ưu tiên happy path trước, edge cases sau |
| Cross-service data consistency | Trung bình | Eventual consistency + proper saga compensation |

### 2.7 Priority Matrix (Nếu thiếu thời gian)

| Priority | Feature | Lý do |
|----------|---------|-------|
| P0 (Must) | Store CRUD, Order Flow, Payment basic, Notification email | Core business |
| P0 (Must) | Saga happy path (create → reserve → pay → confirm) | Core flow |
| P1 (Should) | Saga compensation (rollback scenarios) | Data integrity |
| P1 (Should) | API Gateway | Production readiness |
| P2 (Nice) | Admin endpoints, Trust score | Enhancement |
| P2 (Nice) | Push notification, FCM | Enhancement |
| P3 (Later) | Full test coverage, Performance optimization | Quality |

---

> [!IMPORTANT]
> **Nguyên tắc quan trọng**: Mỗi service cần có cùng Clean Architecture structure như Identity (Domain → Application → Infrastructure → API) và cùng Outbox Pattern setup. Tái sử dụng BuildingBlocks library để tránh duplicate code.
