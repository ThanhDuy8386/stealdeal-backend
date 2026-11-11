# User Trust Score — Kế hoạch triển khai backend

Ngày tạo: 2026-11-10. Cập nhật: 2026-11-11. Trạng thái: đã implement bước 1; các bước 2–8 chưa implement.

Phạm vi cập nhật: MVP chỉ hiển thị điểm trong user profile, thông báo hạn chế khi checkout và cho seller đánh dấu NoShow. Không làm giao diện/API quản lý trust score cho admin hoặc seller, không làm trang lịch sử điểm.

## 1. Phạm vi đã chốt

- Giữ nguyên toàn bộ property của `UserTrustScore` và `TrustScoreEvent`.
- Không thêm trạng thái `Completed`, không thêm bước xác nhận đã nhận hàng.
- User nhận hàng bằng cách đọc pickup code, seller đối chiếu thủ công.
- Thanh toán thành công được chấp nhận: cộng điểm, tối đa 100.
- Không trừ điểm do bỏ thanh toán, payment hết hạn, lỗi gateway hoặc lỗi server.
- Cuối ca, seller kiểm kê và chủ động đánh dấu đơn đã thanh toán nhưng không đến lấy là `NoShow`.
- Order chịu trách nhiệm xác thực thao tác seller và lưu `NoShow`; Identity chịu trách nhiệm tính điểm.
- Client dùng API profile hiện có để hiển thị điểm; không cần màn hình quản lý trust score.
- Seller chỉ thao tác với order, không xem/sửa điểm hoặc chọn mức phạt.
- Không làm UI/API admin quản lý điểm, xem lịch sử điểm hoặc miễn/hoàn phạt trong MVP.
- Hoàn tất backend, API contract và kiểm thử trước khi làm FE.
- Không tự động refund hoặc trả tồn kho khi đánh dấu `NoShow`; đây là nghiệp vụ khác.

Cart TTL và thời hạn reservation/payment là các cơ chế khác nhau. Phần này giữ nguyên cơ chế hết hạn/release inventory hiện có, không thay thế nó bằng cart TTL.

## 2. Quy tắc MVP

Các giá trị dưới đây là mặc định của MVP, đặt trong `TrustScoreSettings` để có thể cấu hình:

| Quy tắc | Giá trị |
| --- | --- |
| Điểm khởi tạo | 100, giữ như flow tạo user hiện tại |
| Điểm tối thiểu / tối đa | 0 / 100 |
| Thanh toán thành công | +2 mỗi order, tối đa một lần |
| Seller đánh dấu NoShow | -15 mỗi order, tối đa một lần |
| 80–100 điểm | Mua bình thường theo giới hạn chung hiện có |
| 50–79 điểm | Tối đa một order `Pending` trên toàn bộ cửa hàng |
| 0–49 điểm | Khóa tạo order mới 24 giờ sau một NoShow đủ điều kiện; sau đó mua hạn chế như nhóm 50–79 |

Điểm thưởng thanh toán và phạt NoShow độc lập. Không thu hồi riêng khoản thưởng thanh toán khi xảy ra NoShow.

Sau khi hết 24 giờ, user dưới 50 điểm được tạo order theo mức hạn chế để có thể phục hồi điểm. Một NoShow mới với `ScoreAfter < 50` bắt đầu đợt khóa mới. Không kéo dài khóa chỉ vì user gọi lại API hoặc có event cộng điểm.

Không dùng `User.IsActive = false` để thực hiện chính sách này: user vẫn được đăng nhập, xem đơn, nhận hàng đã trả tiền và khiếu nại.

## 3. Hiện trạng code và điểm cần nối

| Khu vực | Hiện trạng | Việc cần làm |
| --- | --- | --- |
| Payment `PaymentCallbackService.MarkPaymentSuccessAsync` | Đổi transaction sang `Success` và ghi `payment.completed` vào Outbox | Bổ sung `UserId` trong payload |
| Payment `MarkLateSuccessForRefundAsync` | Chuyển sang `RefundPending`, không phát `payment.completed` | Giữ nguyên, không thưởng điểm |
| Order `OrderStatusEventHandler` | Nhận `payment.completed` và chuyển `Pending -> Confirmed` | Giữ hành vi, tiếp nhận payload mở rộng tương thích |
| Identity | Có hai model trust score, profile đã trả điểm, `ProcessedMessages`, consumer mẫu `StoreVerifiedConsumer` | Thêm service/repository/consumer và API đánh giá quyền mua; dùng lại profile để hiển thị điểm |
| Order `UpdateOrderStatusAsync` | Seller có thể cập nhật status khá rộng, chưa kiểm tra sở hữu cửa hàng | Siết quyền và transition; không cho bypass NoShow |
| Order `GetStoreOrdersAsync` và `GetOrderByIdAsync` | Chưa kiểm tra sở hữu cửa hàng cho seller | Bổ sung kiểm tra ở backend |
| Order `OrderMapping` | `PickupDeadline` mặc định tạo đơn +24 giờ | Thay bằng thời hạn pickup thực tế do backend lấy từ Store |
| Store `GET /api/store/stores/{id}` | Response có `OwnerId` | Order có thể dùng để xác minh seller, chưa cần endpoint Store mới |

API `GET /api/identity/account/profile` hiện đã trả `UserDetailResponse.UserTrustScore`; repository cũng include trust row và mapping đã có `Score`. Dùng lại flow này để hiển thị điểm, không cần endpoint đọc điểm riêng. Phần Identity bổ sung là xử lý event, lưu lịch sử nội bộ và API đánh giá quyền mua.

## 4. Flow đích

```text
IPN được xác thực, payment thành công theo happy path
  -> Payment lưu Success + Outbox payment.completed
  -> RabbitMQ fan-out theo binding:
       -> queue Order: cập nhật Confirmed
       -> queue Identity riêng: thưởng điểm + ghi TrustScoreEvent

Seller đánh dấu NoShow
  -> Order kiểm tra seller, status và thời hạn pickup
  -> Order lưu NoShow + Outbox order.no-show trong cùng transaction
  -> Identity trừ điểm + tăng NoShowCount + ghi TrustScoreEvent

User tạo order mới
  -> Order hỏi Identity về quyền mua của chính user
  -> Order kiểm tra giới hạn Pending trong Order DB
  -> Được phép mới lưu order + order.created
```

Dùng lại `payment.completed`, không cần phát thêm một event khác có cùng ý nghĩa. Identity phải có queue riêng, không dùng chung queue với Order vì consumer của cùng queue sẽ chia nhau message.

## 5. Danh sách endpoint phải hoàn tất trước FE

Các route mới bên dưới là contract dự kiến. API response sử dụng convention hiện có của từng service; mã lỗi nghiệp vụ phải ổn định để FE xử lý được.

| Endpoint | Quyền | Mục đích |
| --- | --- | --- |
| `GET /api/identity/account/profile` | User đã đăng nhập | Giữ API hiện có, dùng `userTrustScore.score` để hiển thị điểm trên profile |
| `GET /api/identity/account/purchase-eligibility` | User đã đăng nhập; Order forward JWT của user | Đánh giá chính sách trust score trước khi tạo order |
| `GET /api/order/orders/store/{storeId}` | Seller sở hữu cửa hàng hoặc Admin, SuperAdmin | Giữ API hiện có, siết ownership và trả dữ liệu để tìm đơn kiểm kê |
| `GET /api/order/orders/{id}` | Buyer của đơn, seller sở hữu cửa hàng hoặc Admin, SuperAdmin | Giữ API hiện có, siết quyền và bổ sung dữ liệu đủ cho màn hình chi tiết |
| `POST /api/order/orders/{id}/no-show` | Seller sở hữu cửa hàng hoặc Admin, SuperAdmin | Đánh dấu NoShow, phát event trừ điểm |
| `POST /api/order/orders` | User đã đăng nhập | Giữ API hiện có, bắt buộc kiểm tra quyền mua |
| `POST /api/order/orders/checkout-from-cart` | User đã đăng nhập | Giữ API hiện có, dùng chung kiểm tra quyền mua |
| `PATCH /api/order/orders/{id}/status` | Theo từng transition được cho phép | Giữ route tương thích, chặn cập nhật tùy ý Confirmed/NoShow |

Chỉ có hai endpoint mới bắt buộc: purchase eligibility và command NoShow. Các endpoint còn lại đã tồn tại và chỉ cần tích hợp/siết logic liên quan. Quyền Admin/SuperAdmin trên API order giữ cho khả năng hỗ trợ hiện có, không kéo theo yêu cầu làm UI quản lý trust score.

Không tạo endpoint để seller truyền `userId` và số điểm muốn trừ. UserId lấy từ order, mức điểm lấy từ policy backend. Không mở API sửa điểm tổng tùy ý, đọc lịch sử điểm hoặc miễn/hoàn phạt trong MVP. Các API admin quản lý user có sẵn không thuộc phạm vi phải xây mới.

Quy ước HTTP cho các API mới: GET thành công `200`; đánh dấu NoShow `200` sau khi status và Outbox đã commit. Response không khẳng định điểm đã cập nhật. Input sai `400`, thiếu JWT `401`, sai quyền hoặc bị khóa mua `403`, không tồn tại `404`, status/deadline không hợp lệ hoặc đã đủ Pending `409`, dependency không khả dụng `503`. Phân biệt bằng reasonCode như `TrustScorePurchaseSuspended`, `PendingOrderLimitReached`, `NoShowNotEligible`, `StoreOwnershipRequired`, `DependencyUnavailable`.

### 5.1. Hiển thị điểm trong profile

- Giữ `GET /api/identity/account/profile`, lấy userId từ JWT như hiện tại.
- FE chỉ hiển thị `userTrustScore.score`, ví dụ `Điểm tín nhiệm: 85/100`.
- Không bắt buộc hiển thị counters, tier, lịch sử điểm hoặc thêm màn hình quản lý.
- Không thêm API history/profile trust score riêng; `TrustScoreEvent` vẫn được lưu nội bộ cho policy, audit và chống trùng.
- Sau khi có event cộng/trừ điểm, lần load profile tiếp theo đọc điểm hiện tại từ backend; không yêu cầu realtime/WebSocket hoặc polling liên tục.

### 5.2. Purchase eligibility

Ví dụ response, không phải entity mới:

```json
{
  "score": 45,
  "tier": "TemporarilyBlocked",
  "canCreateOrder": false,
  "restrictedUntilUtc": "2026-11-11T10:00:00Z",
  "maxPendingOrders": 1,
  "reasonCode": "TrustScorePurchaseSuspended",
  "message": "Tạm hạn chế đặt đơn mới do điểm tín nhiệm thấp sau khi không đến nhận hàng."
}
```

- User chưa bị hạn chế: `restrictedUntilUtc = null`.
- Hết hạn khóa nhưng điểm vẫn thấp: tier `Limited`, `canCreateOrder = true`, `maxPendingOrders = 1`.
- Nhóm bình thường: `maxPendingOrders = null` nghĩa là policy trust score không đặt giới hạn bổ sung, không vô hiệu giới hạn chung khác.
- Identity chỉ là API đọc; không tạo/mở rộng đợt khóa khi được gọi.
- `canCreateOrder` chỉ đánh giá trust score; Order vẫn kiểm tra số Pending, hàng hóa và các điều kiện khác.
- Order dùng HttpClient typed, forward bearer token qua từng request; không đặt token vào default headers dùng chung giữa nhiều user.
- Identity không truy vấn Order DB. Số Pending được kiểm tra bởi Order.
- Identity không khả dụng: Order trả lỗi dependency/503, không tạo order và không xóa cart; không mặc định user bị phạt hay bị ban.
- FE không bắt buộc gọi eligibility trước. Khi user checkout, Order tự kiểm tra và nếu từ chối thì trả ProblemDetails với `detail` là thông báo, cùng `reasonCode`, `score`, `restrictedUntilUtc`, `maxPendingOrders` phù hợp để FE thông báo ngay trên flow hiện tại. `message` chỉ dùng trong response eligibility thành công, theo contract bước 1.
- Đây là hạn chế tạo đơn mới, không chặn thanh toán của order đã tạo, không từ chối IPN hợp lệ vì điểm vừa thay đổi. Không đưa kiểm tra trust score vào callback VNPAY.
- Không trả lý do tín nhiệm cho lỗi kỹ thuật 503; FE phải phân biệt dependency lỗi với user bị hạn chế.
- FE chỉ hiển thị thông tin từ response, không tự tính điểm, deadline khóa hoặc chính sách.

### 5.3. Danh sách order cho seller

- Dùng danh sách và chi tiết order hiện có; chưa bắt buộc thêm search engine, phân trang hoặc đổi response shape chỉ để phục vụ trust score.
- Response cần có orderId, pickup code, status và pickup deadline để seller tìm đúng đơn. Có thể dùng filter của dashboard hiện có; thêm query `status` nếu thực sự cần, không bắt buộc các bộ lọc mới khác trong MVP.
- Dashboard cuối ca tìm đơn Confirmed đã qua deadline để kiểm kê; đây không phải kết luận tự động user vắng mặt.
- DTO bổ sung `canMarkNoShow` và `noShowIneligibleReason`, tính từ cùng quy tắc backend của command; endpoint command vẫn kiểm tra lại.
- Không trả toàn bộ order của cửa hàng khác chỉ vì caller có role Seller.
- Không thêm thông tin quản lý trust score hoặc quyền chọn mức phạt vào seller dashboard.

### 5.4. Đánh dấu NoShow

Body không bắt buộc. Nếu muốn ghi chú bổ sung, có thể gửi:

```json
{
  "reason": "Cuối ca kiểm kê, khách chưa đến nhận túi hàng."
}
```

- `reason` tùy chọn, trim, giới hạn độ dài phù hợp `TrustScoreEvent.Note` 1000 ký tự, bao gồm cả metadata audit nếu có. Nếu bỏ trống, backend dùng lý do mặc định. FE chỉ cần nút xác nhận, không bắt buộc form nhập lý do.
- Chỉ cho phép đơn `DeliveryType = Pickup`, `Status = Confirmed`, có deadline thực tế và đã qua deadline.
- Seller là chủ cửa hàng; Admin/SuperAdmin được thao tác hỗ trợ và vẫn phải thỏa điều kiện nghiệp vụ.
- `Confirmed` phải có nguồn từ event thanh toán hợp lệ; generic status API không được tạo trạng thái này thủ công.
- Khóa/kiểm soát đồng thời theo order khi kiểm tra và cập nhật. Hai request đồng thời chỉ tạo một lần chuyển status và một Outbox event.
- Lặp request khi đã NoShow: trả kết quả idempotent, không phát thêm event.
- Response thành công trả order đã NoShow và thông tin tác động điểm được xử lý bất đồng bộ; không trả điểm user giả định đã cập nhật.
- Không tự refund, release inventory hoặc thay đổi trạng thái Payment.

### 5.5. Phần quản trị được hoãn

- Không implement endpoint/UI admin xem lịch sử điểm, sửa điểm hoặc miễn/hoàn phạt ở MVP.
- Seller không được đổi order NoShow về Confirmed rồi đánh dấu lại; generic status API phải chặn đường này.
- MVP chưa có flow tự phục vụ để sửa thao tác NoShow nhầm. FE cần xác nhận trước khi gửi, backend ghi actor, thời điểm và order liên quan để hỗ trợ điều tra nếu cần.
- Nếu nhu cầu sửa nhầm phát sinh, bổ sung command điều chỉnh và event bù có audit trong giai đoạn sau; không xóa/sửa lịch sử hoặc reset điểm user để che mất nghiệp vụ cũ.
- Hạn chế này phải được ghi trong tài liệu bàn giao; không coi admin management là điều kiện bắt buộc để làm FE.

## 6. Sử dụng nguyên trạng hai model

### UserTrustScore

| Property | Quy ước MVP |
| --- | --- |
| `Score` | Điểm hiện tại trong khoảng 0–100 |
| `TotalOrders` | Số order đã thanh toán thành công được ghi nhận bởi tính năng này, không phải tất cả lần tạo order |
| `SuccessfulPickups` | Không tăng; không có dữ liệu xác nhận nhận hàng. FE chưa hiển thị chỉ số này |
| `NoShowCount` | Số order được ghi nhận NoShow, tăng đúng một lần mỗi order |
| `DisputeCount` | Giữ nguyên theo nghiệp vụ hiện có, không ảnh hưởng điểm trong phạm vi này |
| `LastCalculatedAt` | UTC lúc thực sự xử lý cập nhật ledger/counters; không dùng làm thời điểm bắt đầu khóa |

### TrustScoreEvent

| Property | Quy ước MVP |
| --- | --- |
| `EventType` | `PaymentSucceeded`, `OrderNoShow` |
| `ReferenceType` | `Order` |
| `ReferenceId` | OrderId dạng GUID chuẩn thống nhất |
| `ScoreDelta` | Delta thực tế sau clamp, kể cả 0 |
| `ScoreAfter` | Điểm sau xử lý |
| `Note` | Lý do và actorId nếu có, không làm nguồn dữ liệu tính policy |
| `CreatedAt` | Thời điểm nghiệp vụ UTC từ event đã được backend tạo và xác thực; dùng nhất quán cho thời hạn khóa |

- Thêm index/constraint bằng EF configuration/migration được phép; không thêm property vào hai model.
- Điểm khởi tạo vẫn là 100; nếu user thiếu trust row, khởi tạo idempotent dưới cơ chế khóa phù hợp, không reset điểm của row đang tồn tại.
- Không suy ra SuccessfulPickups bằng TotalOrders trừ NoShowCount vì còn đơn chưa hết giờ nhận hàng.
- Không đổi ý nghĩa dữ liệu lịch sử đang có một cách âm thầm: kiểm tra dữ liệu thực tế trước migration, ghi rõ mốc bắt đầu tính nếu chưa có backfill.

## 7. Event contract và xử lý bất đồng bộ

### payment.completed — mở rộng payload hiện có

- Giữ các field hiện có; bổ sung `UserId` lấy từ `Transaction.UserId`.
- Sửa DTO/factory phía Payment và DTO consumer tương ứng; consumer Order vẫn xử lý như cũ.
- Identity chỉ thưởng một lần mỗi order, kể cả có nhiều PaymentId/messageId.
- Message cũ thiếu UserId không được cộng cho Guid.Empty hoặc âm thầm bỏ qua: đưa vào luồng xử lý contract cũ/backfill trước khi bật consumer.

### order.no-show — mới

```text
MessageId, OccurredAtUtc, OrderId, UserId, StoreId,
MarkedByUserId, PickupDeadlineUtc, Reason
```

### Bảo đảm xử lý

- Queue Identity riêng, ví dụ `identity.trust-score-events`, bind hai routing key `payment.completed` và `order.no-show`; manual ACK sau DB commit.
- `ProcessedMessages` chống redelivery theo messageId + consumerName.
- Unique business index cho ledger theo `(UserId, ReferenceType, ReferenceId, EventType)` với filter giới hạn hai loại event của tính năng này; chống producer phát lại cùng nghiệp vụ bằng messageId khác.
- Trước khi thêm unique index: kiểm tra/trình bày cách xử lý dữ liệu trùng đang tồn tại; không xóa lịch sử tùy tiện.
- Điểm, counters, TrustScoreEvent và ProcessedMessage lưu trong cùng DB transaction.
- SQL Server: dùng transaction và khóa theo user, ví dụ `UPDLOCK, HOLDLOCK` trên trust row; không thêm property RowVersion, không chỉ dùng lock trong memory.
- Đụng unique constraint do message trùng: rollback transaction, xác nhận nghiệp vụ đã được xử lý rồi ACK; không tăng counter lại.
- NoShow đến trước PaymentSucceeded: retry có delay đến khi đã có ledger thanh toán của order.
- Không retry nóng vô hạn. Có retry queue/DLQ, log dependency thiếu và cách replay sau khi khắc phục. Không copy nguyên cách NACK `requeue=false` nếu message có thể bị mất vì chưa có DLQ.
- Clamp khiến kết quả phụ thuộc thứ tự, nên cần kiểm thử thứ tự giữa thưởng và phạt của cùng order, không chỉ chống trùng message.

## 8. Tính quyền mua mà không thêm property

1. Đọc điểm hiện tại và các event `OrderNoShow` có `ScoreAfter < 50`.
2. Lấy thời điểm nghiệp vụ gần nhất, cộng 24 giờ làm `restrictedUntilUtc`.
3. Nếu điểm hiện tại dưới 50 và hạn này còn hiệu lực: khóa tạo đơn mới.
4. Nếu điểm dưới 80 nhưng không còn thời hạn khóa: giới hạn một Pending.
5. Nếu điểm từ 80 trở lên: policy trust score không hạn chế bổ sung.

Không suy luận hạn khóa từ `LastCalculatedAt`, không parse `Note`, không tự cộng điểm theo thời gian.

Order kiểm tra quota toàn user, không chỉ trong một store. Đếm `Status = Pending`, không đếm Confirmed hoặc NoShow.

Kiểm tra quota và insert order phải ở cùng transaction có cơ chế đồng bộ theo user trên SQL Server, ví dụ `sp_getapplock` transaction-owned. Không dùng riêng thao tác Count rồi Insert vì hai request đồng thời có thể cùng vượt giới hạn. Không giữ transaction DB trong lúc gọi HTTP tới Identity/Store.

Eligibility được kiểm tra ở server tại thời điểm tạo order; kết quả FE gọi trước đó chỉ phục vụ hiển thị. Chấp nhận độ trễ event giữa các service của MVP, không hứa khóa quyền mua đồng bộ ngay khi seller bấm NoShow.

## 9. Thứ tự implement

### Bước 1 — Chốt contract và cấu hình

- [x] Thêm constants cho eventType, routing key, status và reasonCode; canonical `Confirmed`, `NoShow`.
- [x] Thêm `TrustScoreSettings`, validate ngưỡng và mức điểm khi khởi động.
- [x] Chốt DTO request/response và error convention của các endpoint trong mục 5.
- [x] Thống nhất role `Admin,SuperAdmin` trong các API mới và các API cũ phải chỉnh thuộc phạm vi này.
- [x] Ghi rõ policy thưởng/phạt mới không áp dụng retroactive tự động cho toàn bộ dữ liệu cũ.

Contract chi tiết: [USER_TRUST_SCORE_API_CONTRACT.md](USER_TRUST_SCORE_API_CONTRACT.md). DTO đã thêm: `PurchaseEligibilityResponse`, `PurchaseEligibilityClientResponse`, `MarkOrderNoShowRequest`; response NoShow dùng lại `OrderResponse`. Settings được bind và validate khi Identity khởi động. Chưa nối endpoint/consumer hoặc enforcement; error convention được áp dụng ở các bước triển khai tương ứng. Ownership và giới hạn chuyển status vẫn được siết theo bước 5/6.

Kiểm chứng bước 1 (2026-11-11): build API Identity và Order thành công. Chạy harness tạm gồm 22 kiểm tra cấu hình hợp lệ/sai, fail startup với options sai, bind appsettings thực tế, JSON Identity -> Order, routing key, quyền Admin/SuperAdmin và giới hạn ghi chú NoShow. Đây chưa phải test end-to-end hoặc test transaction của các bước sau.

### Bước 2 — Identity persistence và domain service

- [ ] Thêm repository trust score, ledger nội bộ và service tính policy; không tạo API/UI history.
- [ ] Tạo migration index/constraint cần thiết, không đổi property hai model.
- [ ] Implement thưởng thanh toán và phạt NoShow idempotent, có clamp/counters/transaction.
- [ ] Thêm cơ chế khóa theo user và xử lý row khởi tạo thiếu.
- [ ] Implement cooldown từ ledger; không thêm persisted restriction field.

### Bước 3 — Payment và Identity consumer

- [ ] Thêm UserId vào `PaymentCompletedEvent` và factory từ Transaction.
- [ ] Giữ phát event chỉ trong happy path `Success`; không phát từ return URL hoặc nhánh RefundPending.
- [ ] Thêm Identity consumer, queue riêng, bindings, retry/DLQ và DI/config.
- [ ] Kiểm tra các bản sao DTO/event envelope, camelCase serialization và messageId khớp Outbox.
- [ ] Có cách xử lý message cũ thiếu UserId trước khi bật consumer.

### Bước 4 — Profile và endpoint purchase eligibility

- [ ] Kiểm tra profile hiện có trả `userTrustScore.score` từ DB đúng sau khi event được xử lý; dùng lại DTO/mapping hiện có.
- [ ] Implement GET purchase eligibility cho chính user, xác thực qua JWT.
- [ ] Chốt response lý do hạn chế và thời điểm hết hạn; không lộ ghi chú nội bộ.
- [ ] Không thêm endpoint trust score riêng, history hoặc admin management.

### Bước 5 — Order ownership, deadline và danh sách kiểm kê

- [ ] Thêm client xác minh OwnerId của store từ API Store hiện có.
- [ ] Siết quyền xem đơn, xem danh sách cửa hàng và cập nhật status.
- [ ] Deadline lấy từ dữ liệu bag đã được Store xác thực, không tin client truyền tự do.
- [ ] Với nhiều bag, chỉ nhận đơn có khung giờ pickup giao nhau; deadline là thời điểm kết thúc của khung giao nhau. Không có giao nhau thì từ chối order thay vì tạo deadline sai.
- [ ] Áp dụng cho cả direct create và checkout-from-cart; direct create không bỏ qua validation.
- [ ] Xử lý đơn cũ có deadline +24 giờ: chưa cho mark NoShow nếu chưa xác minh/sửa deadline; không tự suy luận deadline lịch sử từ lịch bag hiện tại nếu bag đã bị chỉnh sửa.
- [ ] Tái sử dụng danh sách/chi tiết order hiện có, bổ sung eligibility NoShow trong DTO; giữ response shape tương thích, không bắt buộc search/pagination mới.

### Bước 6 — Command NoShow

- [ ] Implement POST NoShow, lock order, lưu status + Outbox atomic.
- [ ] Command chỉ nhận orderId và ghi chú tùy chọn; actor lấy từ JWT, mức trừ điểm do Identity quyết định.
- [ ] Generic PATCH status không cho caller tự đặt Confirmed, NoShow hoặc thoát NoShow để phạt lại.
- [ ] Event payment đến muộn không được ghi đè NoShow về Confirmed.
- [ ] Tích hợp event `order.no-show` với Identity consumer đã tạo ở bước 3.
- [ ] Rà các API đang phụ thuộc Completed, đặc biệt review eligibility hiện cho Completed/Pending: chuyển sang quy tắc phù hợp Confirmed đã thanh toán, bỏ bypass Pending và không cho NoShow đủ điều kiện review. Ghi rõ quy tắc review này không xác nhận user đã nhận hàng.

### Bước 7 — Enforce quyền mua ở mọi đường tạo đơn

- [ ] Thêm client Identity purchase eligibility, forward JWT và map lỗi dependency.
- [ ] Đặt enforcement tại application flow chung trước lưu order/Outbox; không chỉ đặt tại controller checkout.
- [ ] Kiểm tra Pending quota atomic theo user.
- [ ] User bị hạn chế hoặc dependency lỗi: không tạo order, không phát order.created, không xóa cart.
- [ ] Response lỗi checkout trả ProblemDetails với reasonCode, detail và thông tin policy đủ để FE thông báo; không cần màn hình quản lý điểm.
- [ ] Các API xem order và thao tác phục vụ đơn đã thanh toán vẫn hoạt động khi user bị khóa tạo đơn mới.
- [ ] Không chặn thanh toán của order đã tạo hoặc IPN thành công dựa trên trust score mới.

### Bước 8 — Kiểm thử, tài liệu và triển khai

- [ ] Hoàn tất test matrix mục 10 và chạy build các service bị ảnh hưởng.
- [ ] Cập nhật `STEALDEAL_API_REFERENCE.md`, OpenAPI và mẫu request/response/error.
- [ ] Chuẩn bị collection hoặc `.http` requests cho profile, happy path, NoShow, quota và cooldown.
- [ ] Viết migration/deployment notes: khai báo durable queue/bindings trước khi producer phát sự kiện cho Identity; migrate và deploy producer có UserId trước khi bật xử lý consumer mới.
- [ ] Kiểm tra backlog Payment Outbox/message cũ; không bật consumer mới rồi bỏ qua các event thiếu UserId.
- [ ] Với order đã Confirmed trước rollout: chọn cutover rõ ràng; chỉ cho NoShow của order đã được seed ledger thanh toán hoặc backfill có kiểm soát để không retry vô hạn vì thiếu PaymentSucceeded.
- [ ] Backfill nếu thực hiện phải có marker chống trùng, chỉ ghi nhận payment thành công thực tế, không thưởng hai lần hoặc reset điểm hiện tại.
- [ ] Kiểm tra retry/DLQ và quy trình replay khi Identity dừng rồi khởi động lại.

## 10. Test matrix bắt buộc

| Nhóm | Trường hợp và kết quả mong đợi |
| --- | --- |
| Thanh toán | IPN thành công hợp lệ phát event và cộng +2, không vượt 100 |
| Thanh toán | Invalid signature, return URL, payment failed/expired, late success RefundPending không thưởng |
| Idempotency | IPN/message lặp hoặc messageId mới cùng order không thưởng hai lần |
| NoShow | Seller đúng store, Pickup, Confirmed và hết deadline: đổi status + phạt đúng một lần |
| Phân quyền | Buyer, seller store khác, caller giả userId không được đánh dấu hoặc đọc dữ liệu trái quyền |
| Validation | Pending, payment failed, delivery khác Pickup, chưa hết deadline, deadline không đáng tin: không NoShow |
| Bypass | Generic PATCH không thể đặt Confirmed/NoShow hoặc đổi NoShow để phạt lại |
| Đồng thời | Hai request NoShow chỉ tạo một nghiệp vụ; hai event của cùng user không mất cập nhật |
| Bất đồng bộ | NoShow trước payment: retry đúng thứ tự, không mất message |
| Điểm | NoShow trừ -15, không xuống dưới 0; điểm thưởng tại 99 chỉ tăng đến 100 |
| Giới hạn | Điểm 49/50/79/80, thời điểm ngay trước/đúng/sau hết 24 giờ cho kết quả đúng |
| Quota | Tối đa một Pending trên toàn user, kể cả gửi đồng thời direct create và checkout ở hai store |
| Dependency | Identity/Store không khả dụng không tạo đơn trái phép, không xóa cart, trả lỗi rõ ràng |
| Counter | Payment tại 100 vẫn ghi event delta 0 và tăng TotalOrders; SuccessfulPickups không bị tăng |
| Profile | API profile hiện có hiển thị đúng điểm sau khi Identity xử lý event, không cần API đọc điểm riêng |
| Checkout | Lỗi hạn chế có reasonCode, detail và mốc hết khóa phù hợp; 503 không bị trình bày thành user bị phạt |
| Thanh toán đã tạo | User đang bị hạn chế tạo đơn vẫn thanh toán được order đã tạo; IPN hợp lệ tiếp tục được xử lý |
| Dữ liệu cũ | Missing trust row, message thiếu UserId, order trước cutover được xử lý theo deployment notes |
| Regression | Flow đặt đơn, reserve, VNPAY, release khi hết hạn và quyền truy cập đơn trả tiền không bị hỏng |

Các test transaction, unique index và đồng thời cần chạy trên SQL Server tương thích môi trường thật; EF InMemory không chứng minh được các bảo đảm này. Chỉ thêm test cho hành vi nghiệp vụ và rủi ro thực tế, không viết test chỉ sao chép mapping.

## 11. Điều kiện chuyển sang FE

Chỉ chuyển sang FE khi:

- [ ] Toàn bộ endpoint trong mục 5 đã chạy được bằng HTTP client, có auth/ownership và API contract rõ ràng.
- [ ] Demo end-to-end: thanh toán -> Confirmed + thưởng; seller NoShow -> phạt; tải lại profile thấy điểm đã cập nhật.
- [ ] Demo user dưới ngưỡng bị chặn hoặc giới hạn; sau cooldown được mua hạn chế để phục hồi.
- [ ] Retry, duplicate và concurrency không tạo thêm lần cộng/trừ.
- [ ] Tài liệu API có bảng reasonCode và quy ước trạng thái async để FE tích hợp nút NoShow.

FE sau đó chỉ cần:

1. Trang user profile hiện có hiển thị điểm từ `userTrustScore.score`; không làm trang lịch sử hoặc quản lý điểm.
2. Checkout hiện thông báo lý do, điểm hoặc thời điểm hết khóa phù hợp từ response khi bị hạn chế; không tự suy ra policy.
3. Seller dùng danh sách/chi tiết order hiện có, thêm nút NoShow khi backend cho phép và hộp xác nhận trước thao tác. Ghi chú là tùy chọn, không bắt buộc thêm form.
4. FE gọi một command NoShow, cập nhật status order; việc cộng/trừ điểm do hệ thống xử lý qua event. Không cần hiển thị điểm của buyer cho seller.

Không có yêu cầu làm UI trust score management cho Admin/SuperAdmin trong MVP.

## 12. Ngoài phạm vi MVP

- Thay đổi property hai model, thêm Completed/QR/verify pickup.
- Phạt user bỏ thanh toán hoặc tự động kết luận NoShow.
- Tự động ban vĩnh viễn, tăng điểm theo thời gian hoặc thay đổi điểm theo giá trị order.
- Tự động phạt khi có dispute, tự refund hoặc tự restock cho NoShow.
- API/UI quản lý trust score cho admin/seller, API/UI lịch sử điểm cho user và flow miễn/hoàn phạt hoặc đảo NoShow.
- Đảm bảo đồng bộ tức thời giữa Payment, Order, Identity; MVP dùng Outbox và eventual consistency.
