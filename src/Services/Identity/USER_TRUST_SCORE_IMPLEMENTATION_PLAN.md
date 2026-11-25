# User Trust Score — Kế hoạch triển khai backend

Ngày tạo: 2026-11-10. Cập nhật: 2026-11-25. Trạng thái: đã implement bước 1–5; các bước 6–8 chưa hoàn tất. Consumer bước 3 mặc định chỉ khai báo queue, chưa bật xử lý message.

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
- MVP vẫn chạy một instance Identity, một consumer trust score xử lý tuần tự cả hai routing key trên cùng queue. Điểm/counters đã được cập nhật bằng atomic SQL để không mất update khi có nhiều consumer; không dùng application lock. Khi scale vẫn giữ transaction, chống trùng và retry các xung đột insert/unique key.

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
| Payment `PaymentCallbackService.MarkPaymentSuccessAsync` | Đổi transaction sang `Success`, ghi `payment.completed` có UserId vào Outbox | Đã nối ở bước 3 |
| Payment `MarkLateSuccessForRefundAsync` | Chuyển sang `RefundPending`, không phát `payment.completed` | Giữ nguyên, không thưởng điểm |
| Order `OrderStatusEventHandler` | Nhận `payment.completed` và chuyển `Pending -> Confirmed` | Giữ hành vi, tiếp nhận payload mở rộng tương thích |
| Identity | Đã có service/repository/consumer và API purchase eligibility; profile dùng lại mapping hiện có | Bật consumer theo cutover; bước 7 nối enforcement tại Order |
| Order `UpdateOrderStatusAsync` | Đã kiểm tra owner; generic PATCH chỉ cho Pending -> Cancelled bằng atomic update | Bước 6 thêm command NoShow riêng, không cho bypass bằng generic PATCH |
| Order `GetStoreOrdersAsync` và `GetOrderByIdAsync` | Đã kiểm tra ownership của seller, giữ quyền buyer và Admin/SuperAdmin | Hoàn tất ở bước 5 |
| Order `OrderMapping` | Deadline mới từ giao khung giờ bag; có cờ verified, không dùng mặc định +24 giờ | Apply migration Order trước khi chạy bản mới; xử lý legacy theo mục bước 5 |
| Store `GET /api/store/stores/{id}` | Response có `OwnerId`; Order đã dùng qua StoreCatalogHttpClient | Không cần endpoint Store mới |

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
- Điểm khởi tạo vẫn là 100; câu lệnh ghi điểm khởi tạo trust row còn thiếu khi ghi nhận event đầu tiên, không reset row đang tồn tại. Unique UserId chống tạo trùng; nếu hai transaction cùng insert thì một bên rollback/retry. API đọc eligibility không tạo/sửa row.
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
- Không có application lock: repository dùng `UPDATE SET Score = CASE ... Score + delta ...`, cập nhật TotalOrders/NoShowCount ngay trong DB. `OUTPUT deleted.Score, inserted.Score` trả điểm trước/sau thực tế để ghi ledger đúng khi chạm 0/100, không dùng điểm đọc trước đó từ Application. Transaction bảo đảm điểm/counters, ledger và ProcessedMessage cùng commit/rollback.
- Đụng unique constraint: rollback transaction, không tăng counter lại. Consumer bước 3 xử lý retry/đọc lại để xác nhận duplicate rồi ACK; không thêm vòng retry riêng vào UnitOfWork.
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

- [x] Thêm repository trust score, ledger nội bộ và service tính policy; không tạo API/UI history.
- [x] Tạo migration index/constraint cần thiết, không đổi property hai model.
- [x] Implement thưởng thanh toán và phạt NoShow idempotent, có clamp/counters/transaction.
- [x] Atomic update cho điểm/counters, xử lý row khởi tạo thiếu; không cần application lock.
- [x] Implement cooldown từ ledger; không thêm persisted restriction field.

Chi tiết triển khai bước 2:

- `IUserTrustScoreRepository` và `ITrustScoreEventRepository` đặt ở Domain theo cấu trúc repository hiện có; implementation EF ở Infrastructure.
- `TrustScoreService` ở Application, dùng settings và `TimeProvider`, không tham chiếu EF/SQL hoặc Infrastructure. Input `RecordPaymentTrustScoreRequest` / `RecordNoShowTrustScoreRequest` chỉ dùng nội bộ, không phải API cho client gửi điểm.
- `ITrustScoreUnitOfWork.ExecuteAsync` chỉ mở transaction, thực hiện DB work rồi save/commit; lỗi thì rollback. Không còn `sp_getapplock`, lock theo user, execution strategy hoặc vòng retry riêng.
- `IUserTrustScoreRepository.ApplyScoreChangeAsync` khởi tạo row nếu thiếu rồi chạy atomic UPDATE, cập nhật điểm/counters trên giá trị hiện tại trong DB. Không cập nhật entity đã đọc bằng EF tracking. `TrustScoreUpdateResult` chỉ là kết quả ScoreBefore/ScoreAfter từ OUTPUT, không phải entity persisted hoặc table mới.
- Mỗi transaction ghi điểm/counters, ledger và ProcessedMessage atomically. SQL UPDATE thực thi ngay bên trong transaction; nếu lưu ledger/ProcessedMessage thất bại thì cả UPDATE đó bị rollback. Context phải không có pending changes/transaction khác khi bắt đầu; query đọc điểm dùng AsNoTracking.
- Chống trùng theo messageId/consumer và unique order outcome; không ACK một payload khác user/order bằng messageId cũ. Nếu DB lỗi thì rollback và trả lỗi cho caller; retry message thuộc consumer bước 3, không tạo thêm cơ chế retry ở UnitOfWork.
- Với hai transaction cùng thấy chưa có ledger/trust row, unique constraint vẫn có thể phát sinh xung đột. Bên lỗi rollback toàn bộ; consumer retry với scope mới thì đọc được record đã commit và bỏ qua duplicate hoặc áp dụng event hợp lệ còn thiếu. Atomic update không tự thay thế cơ chế này.
- Nếu NoShow chưa có ledger thanh toán thì ném `PaymentTrustScoreNotRecordedException`; chưa ghi ledger hoặc ProcessedMessage. Consumer bước 3 cần retry có delay.
- Event time bắt buộc UTC, không được vượt quá thời điểm xử lý hơn 5 phút; `CreatedAt` lưu event time, `LastCalculatedAt` lưu processing time. SQL datetime2 được chuẩn hóa Kind=Utc khi trả thời hạn khóa.
- `GetPurchaseEligibilityAsync` chỉ đọc policy từ DB, chưa enforce tại Order. Nếu user tồn tại nhưng thiếu trust row thì trả policy mặc định 100 điểm, không insert row; consumer sẽ khởi tạo khi có event. Không reset row đã có hoặc gia hạn cooldown khi đọc.
- Migration `20261111102442_AddTrustScoreIntegrity` chỉ thêm unique index chống trùng order outcome và hai check constraints 0–100, giữ index UserId hiện có. Không thêm table/column, không thêm index cooldown hoặc SQL preflight riêng. Nếu dữ liệu cũ vi phạm, SQL Server từ chối tạo constraint/index; không tự sửa hay xóa lịch sử.
- Migration mới chỉ được chạy trên database test riêng, chưa áp dụng lên Identity DB đang dùng. Khi triển khai thực tế, kiểm tra dữ liệu và áp dụng migration trước khi bật consumer bước 3.
- Project `StealDeal.Services.Identity.Tests` đã được dùng để kiểm chứng rồi bỏ khỏi source/solution theo phạm vi MVP đơn giản. Nó không phải dependency runtime; không cần tạo thêm test project để chạy tính năng.

Lịch sử kiểm chứng bước 2 (2026-11-11): phiên bản còn lock đã qua 23 kiểm tra SQL Server trước khi bỏ project test. Bản atomic update được kiểm chứng riêng bằng chương trình tạm ngoài repo: 21 kiểm tra SQL Server pass, gồm 30 update đồng thời, cộng/phạt đồng thời, delta tại trần/sàn, trùng event, đồng thời khởi tạo row với retry và rollback khi lưu ledger lỗi. Không thêm lại project test vào source. Hai model và schema migration không thay đổi; build còn warning NuGet của dependency hiện có.

Áp dụng migration vào Identity DB bằng Visual Studio Package Manager Console (chưa được chạy lên DB ứng dụng trong quá trình implement):

```powershell
Update-Database -Project StealDeal.Services.Identity.Infrastructure -StartupProject StealDeal.Services.Identity.API
```

Lệnh dùng connection string của startup project và chỉ áp dụng các migration chưa có trong `__EFMigrationsHistory`. File migration `.cs` chứa các thay đổi DB; `.Designer.cs` và model snapshot là metadata EF, không cần chạy riêng.

### Bước 3 — Payment và Identity consumer

- [x] Thêm UserId vào `PaymentCompletedEvent` và factory từ Transaction.
- [x] Giữ phát event chỉ trong happy path `Success`; không phát từ return URL hoặc nhánh RefundPending.
- [x] Thêm Identity consumer, queue riêng, bindings, retry/DLQ và DI/config.
- [x] Retry có giới hạn với scope/DbContext mới cho xung đột DB: duplicate key 2601/2627 và deadlock 1205. Lỗi có thể là SqlException trực tiếp từ atomic SQL hoặc nằm trong DbUpdateException từ SaveChanges. Không ACK trước khi commit; không retry vô hạn lỗi dữ liệu vĩnh viễn.
- [x] Dùng cùng tên queue `identity.trust-score-events` cho cả `payment.completed` và `order.no-show`; MVP một consumer, `ConsumerDispatchConcurrency = 1`, prefetch 1, await handler và ACK sau commit. Không tạo queue riêng theo instance hoặc một consumer ghi điểm khác cho từng loại event.
- [x] Kiểm tra các bản sao DTO/event envelope, camelCase serialization và messageId khớp Outbox.
- [x] Có cách xử lý message cũ thiếu UserId trước khi bật consumer.

Chi tiết triển khai bước 3:

- Payment bổ sung `UserId = transaction.UserId` vào payload hiện có. DTO ở Order nhận thêm field này; logic cập nhật status của Order giữ nguyên. Không thêm event thưởng riêng hoặc thay đổi callback/return URL.
- `TrustScoreEventHandler` đặt ở Application theo `IIntegrationEventHandler<T>` có sẵn, map hai DTO event sang service bước 2. Kiểm tra payload MessageId trùng broker MessageId, UserId/PaymentId bắt buộc; NoShow phải có StoreId và deadline UTC đã qua. Consumer không truy cập Payment/Order DB.
- `TrustScoreConsumer` ở Infrastructure; một queue chính và một handler được await tại mỗi thời điểm. Tên consumer trong ProcessedMessages là `TrustScoreConsumer`; EventType dùng routing key canonical. Mỗi delivery/retry tạo scope mới; service bước 2 chịu trách nhiệm transaction và chống trùng.
- Ba queue durable: `identity.trust-score-events`, `.retry`, `.dead-letter`. Queue chính bind hai routing key trên topic exchange `stealdeal.events`. Retry queue mặc định TTL 30 giây, dead-letter về **queue chính qua default exchange**, không quay về topic exchange để tránh gửi lại payment event sang Order.
- Khi lỗi tạm thời, publish bản sao persistent vào retry queue, giữ MessageId/body và lưu routing key gốc trong header `x-trust-score-routing-key`, tăng `x-trust-score-retry-count`. Sau tối đa 5 retry (ngoài lần đầu), chuyển DLQ. NoShow thiếu ledger payment cũng theo cơ chế này để payment phía sau vẫn được xử lý.
- Retry SQL 2601/2627/1205, timeout và lỗi SQL được provider đánh dấu transient, kể cả exception trực tiếp hoặc EF bọc bên trong. Input/JSON sai, user không tồn tại, MessageId bị dùng sai nghiệp vụ hoặc SQL constraint không transient được chuyển DLQ. Các lỗi chưa phân loại cũng có retry hữu hạn.
- Publish vào retry/DLQ có `mandatory=true` và publisher confirms; chỉ ACK bản gốc sau khi broker xác nhận bản sao. Xử lý thành công thì chỉ ACK sau commit DB. Nếu publish/ACK hoặc kết nối lỗi, đóng session và kết nối lại; delivery chưa ACK có thể được giao lại và được chống trùng bằng bước 2.
- `TrustScoreConsumerSettings` được bind và validate khi startup. `Enabled=false` mặc định: vẫn kết nối RabbitMQ, khai báo topology/bindings để nhận và giữ event, nhưng chưa đăng ký consume. Sau khi bật bằng config phải restart Identity. Không thêm migration hay sửa hai model ở bước này.
- Đã đối chiếu lại với `StoreVerifiedConsumer`/`StoreVerifiedEventHandler` của Identity và `OrderStatusConsumer`/`OrderStatusEventHandler`: dùng constructor thường với private readonly fields, `StartConsumerAsync`, `OnMessageReceivedAsync`, `DispatchToHandlerAsync`, `DeserializeEvent`, `CreateEventContext`; cấu hình có `ExchangeType`, `BindingKeys`, `PrefetchCount=1` giống mẫu consumer nhiều event. Không thêm base consumer hoặc framework messaging mới. Riêng lifecycle giữ reconnect cả session khi publish/ACK hoặc channel lỗi để delivery chưa ACK được giao lại; retry/DLQ là phần bổ sung cần thiết của flow trust score.
- DTO/handler NoShow đã sẵn sàng nhận contract; producer/endpoint NoShow phía Order vẫn thuộc bước 6.

Triển khai lần đầu và xử lý dữ liệu cũ:

1. Deploy Identity với `TrustScoreConsumer:Enabled=false`, chạy để khai báo queue/bindings; kiểm tra queue đã tồn tại trên broker trước khi Payment mới publish. Chọn mốc bắt đầu nhận event làm cutover; không tự phát lại toàn bộ order lịch sử.
2. Kiểm tra dữ liệu và apply migration bước 2 vào Identity DB. Deploy Payment có payload UserId trước khi bật xử lý. Payment chưa publish các message Outbox Pending cũ thì đối chiếu từng `OrderId`/`PaymentId` với Transaction để bổ sung đúng UserId; giữ MessageId và các field khác. Không sửa/xóa lịch sử Outbox đã xử lý hàng loạt.
3. Kiểm tra backlog của queue mới. Message cũ đã vào broker nhưng thiếu UserId sẽ được giữ trong `.dead-letter` khi bật consumer, không được cộng cho Guid.Empty, không ghi ProcessedMessage và không bị bỏ im lặng. Người vận hành đối chiếu Transaction của đúng PaymentId/OrderId, xác nhận payment được chấp nhận Success theo cutover rồi bổ sung UserId trước khi replay. Không dùng nhánh RefundPending để backfill điểm.
4. Đặt `TrustScoreConsumer:Enabled=true` (hoặc env `TrustScoreConsumer__Enabled=true`) và restart Identity. Thử payment mới, kiểm tra ledger/counters/ProcessedMessages và profile. Nếu cần replay DLQ sau khi sửa nguyên nhân: publish vào **default exchange** với routing key `identity.trust-score-events`, giữ MessageId trong AMQP property và payload giống nhau, Type là routing key nghiệp vụ, header `x-trust-score-routing-key` là `payment.completed` hoặc `order.no-show`, reset `x-trust-score-retry-count` về integer 0. Chỉ loại bản DLQ sau khi xác nhận publish thành công; replay lặp vẫn được chống trùng. Không publish replay lên topic exchange vì sẽ gửi lại sang Order.
5. Không có auto-backfill cho order đã Confirmed trước cutover. Trước bước 6, chỉ cho NoShow với order đã có ledger payment hoặc thực hiện backfill có kiểm soát theo bước 8; hết retry mà vẫn thiếu prerequisite thì giữ DLQ để xử lý.

Lưu ý vận hành: không đổi tên queue khi scale instance. TTL được khai báo bằng queue argument nên thay `RetryDelaySeconds` trên queue đã tồn tại cần quy trình đổi topology/policy có kiểm soát; không xóa queue còn message. Retry dùng TTL/dead-letter tiêu chuẩn, chưa bổ sung bảo đảm dead-letter at-least-once bằng quorum queue cho sự cố broker/cluster.

Kiểm chứng bước 3 (2026-11-25): build Identity, Payment và Order thành công. Harness tạm ngoài repo đã qua 30 kiểm tra contract/handler/consumer với channel giả, gồm ACK sau handler, publish retry trước ACK, giới hạn retry, giữ payload lỗi trong DLQ, scope mới, SQL exception trực tiếp/EF-wrapped và các nhánh IPN không được thưởng. Thêm 6 kiểm tra integration với SQL Server LocalDB trên DB test riêng đã pass: consumer ghi điểm/ledger/processed thật, chống trùng cùng message/cùng order, phạt NoShow, NoShow trước payment không ghi dữ liệu và retry thành công sau payment với scope mới; DB test được xóa sau chạy. Chưa kiểm chứng TTL, publisher confirms hoặc reconnect trên RabbitMQ thật vì môi trường hiện tại không có broker đang chạy. Không thêm project test vào solution, chưa bật consumer hoặc apply migration lên DB ứng dụng. Build ban đầu còn warning NuGet bảo mật của dependency sẵn có.

Kiểm chứng sau review cấu trúc: build Identity thành công; chạy lại 31 kiểm tra contract/consumer/config và 6 kiểm tra SQL integration đều pass. Kiểm tra thêm binding từ appsettings thực tế: `BindingKeys` không khai báo sẵn phần tử trong settings để tránh configuration binder nối trùng với mảng JSON; startup validation yêu cầu đúng hai key và prefetch 1. Đã xóa `Program.cs`, `Step3Checks.csproj` cùng output build của harness trong thư mục tạm ngoài repo theo yêu cầu; không có dependency runtime vào các file đó.

### Bước 4 — Profile và endpoint purchase eligibility

- [x] Kiểm tra profile hiện có trả `userTrustScore.score` từ DB đúng sau khi event được xử lý; dùng lại DTO/mapping hiện có.
- [x] Implement GET purchase eligibility cho chính user, xác thực qua JWT.
- [x] Chốt response lý do hạn chế và thời điểm hết hạn; không lộ ghi chú nội bộ.
- [x] Không thêm endpoint trust score riêng, history hoặc admin management.

Chi tiết triển khai bước 4:

- Thêm `GET /api/identity/account/purchase-eligibility` vào `AccountController` đang có `[Authorize]`. UserId lấy bằng helper JWT hiện có, không nhận userId từ query/body; response `200` dùng `PurchaseEligibilityResponse` và có metadata schema cho OpenAPI.
- Theo cấu trúc API account hiện tại: controller gọi `IAccountService.GetPurchaseEligibilityAsync`; `AccountService` kiểm tra tài khoản còn active/chưa deleted bằng `GetActiveUserAsync`, rồi chuyển sang `ITrustScoreService.GetPurchaseEligibilityAsync` của bước 2. Không lặp logic policy tại controller hoặc AccountService. CancellationToken được chuyển đến service tính policy.
- JWT thiếu/sai/hết hạn, subject không hợp lệ hoặc account không còn active/tồn tại: `401` theo convention profile hiện có. User bị giới hạn trust score vẫn nhận `200` cùng `canCreateOrder=false`, reasonCode/message và restrictedUntilUtc; đây là API đọc policy, chưa phải enforcement tạo đơn.
- Profile giữ nguyên controller/service/DTO/mapping: `UserRepository.GetByIdAsync` đã include `UserTrustScore`; mapping lấy điểm/counters hiện tại từ DB và không include ledger/Note. Đã xác minh qua HTTP ở request mới sau khi handler payment/NoShow commit vào DB.
- Eligibility giữ đúng policy bước 2: default 100 nếu thiếu trust row, ngưỡng 49/50/79/80, cooldown UTC từ ledger; tại đúng thời điểm hết hạn trả Limited và restrictedUntilUtc=null. GET không tạo row, sửa counters, ghi ledger/processed hoặc gia hạn khóa.
- Không thêm config/DI/migration mới: `AccountService` và `ITrustScoreService` đã được đăng ký. Không sửa hai model, không thêm history/admin endpoint hoặc enforcement phía Order; bước 7 sẽ nối kiểm tra quyền mua vào tạo đơn.

Kiểm chứng bước 4 (2026-11-25): build Identity API thành công, còn warning NuGet dependency hiện có. Harness tạm chạy host HTTP riêng với controller/middleware thật, JWT hợp lệ/sai và SQL Server LocalDB trên DB `StealDeal_TrustScoreStep4_Tests_<GUID>` đã qua 26 kiểm tra: auth/active account, không đọc chéo user qua query, profile sau cộng/trừ điểm, policy/cooldown tại biên, không lộ ghi chú, đọc không ghi DB và OpenAPI schema. Không chạy các hosted consumer hoặc dùng database ứng dụng. DB test và source/output của harness đã được dọn sau kiểm tra; không thêm test project vào solution.

### Bước 5 — Order ownership, deadline và danh sách kiểm kê

- [x] Thêm client xác minh OwnerId của store từ API Store hiện có.
- [x] Siết quyền xem đơn, xem danh sách cửa hàng và cập nhật status.
- [x] Deadline lấy từ dữ liệu bag đã được Store xác thực, không tin client truyền tự do.
- [x] Với nhiều bag, chỉ nhận đơn có khung giờ pickup giao nhau; deadline là thời điểm kết thúc của khung giao nhau. Không có giao nhau thì từ chối order thay vì tạo deadline sai.
- [x] Áp dụng cho cả direct create và checkout-from-cart; direct create không bỏ qua validation.
- [x] Xử lý đơn cũ có deadline +24 giờ: chưa cho mark NoShow nếu chưa xác minh/sửa deadline; không tự suy luận deadline lịch sử từ lịch bag hiện tại nếu bag đã bị chỉnh sửa.
- [x] Tái sử dụng danh sách/chi tiết order hiện có, bổ sung eligibility NoShow trong DTO; giữ response shape tương thích, không bắt buộc search/pagination mới.

Chi tiết triển khai bước 5:

- Mở rộng `IStoreCatalogClient`/`StoreCatalogHttpClient` đang có bằng `GetStoreAsync`; tái sử dụng typed HttpClient và section `StoreService`, không tạo client/framework riêng. DTO store chỉ nhận Id, OwnerId, Name, IsActive và IsVerify. Client kiểm tra ID/owner/body hợp lệ; not found trả null; HTTP lỗi, timeout, lỗi kết nối hoặc JSON sai trả `DependencyUnavailableException`. Timeout client Store là 10 giây. Caller cancellation được giữ nguyên.
- OrderService kiểm tra role Seller + OwnerId từ Store trước khi xem chi tiết đơn người khác, liệt kê đơn cửa hàng hoặc cập nhật status. Buyer vẫn xem đơn của mình; Admin/SuperAdmin được hỗ trợ mọi store. Buyer/Admin không cần gọi Store để xem chi tiết, nên vẫn xem được đơn đã trả tiền khi Store không khả dụng. Kiểm tra quyền trước cả request lặp/idempotent để không lộ dữ liệu order.
- Middleware Order giữ ProblemDetails hiện có, bổ sung `403 / StoreOwnershipRequired` và `503 / DependencyUnavailable`. Lỗi Store không được hiểu thành user bị phạt hoặc mặc định cho phép truy cập.
- Validation catalog được đặt tại flow chung `OrderService.CreateOrderAsync`. Checkout service chỉ khóa/đọc cart rồi chuyển bagId/quantity vào flow này; bỏ phần đọc/validate bag trùng tại checkout. Cả direct create và checkout đều kiểm tra store active/verified, bag thuộc store, tổng quantity của bag lặp, tồn kho, trạng thái bag, khung giờ pickup và expiry. Tên store/bag và đơn giá lấy từ Store, ghi đè snapshot client/cart gửi. Reservation thực tế vẫn do atomic update ở Store xử lý sau order.created; catalog check không thay thế reservation.
- Với Pickup: chuẩn hóa UTC như convention client Cart/Order hiện có (SQL datetime2 không có Kind được hiểu là UTC); tính start=max(PickupStartTime), end=min(PickupEndTime). Start >= end bị từ chối, kể cả hai khung chỉ chạm tại một thời điểm. Deadline là end, lưu snapshot trong order lúc tạo. Khi xem đơn không đọc lại lịch bag để sửa deadline. Non-Pickup không có deadline và không đủ điều kiện NoShow.
- Thêm đúng một property **OrderProfile.PickupDeadlineVerified** và migration `20261125041036_AddVerifiedPickupDeadline`: một cột bit NOT NULL, default false. Hai model Identity không đổi. Order pickup mới được validate từ Store thì lưu deadline + verified=true; dữ liệu cũ giữ nguyên deadline và verified=false. Không tự đánh dấu đơn cũ bằng ngày cutover hoặc suy luận từ lịch bag hiện tại. Nếu cần mở NoShow cho đơn cũ, phải có bằng chứng lịch pickup tại thời điểm mua và payment ledger, rồi mới thực hiện tác vụ sửa/backfill riêng có kiểm soát; bước này không thêm endpoint sửa cờ/deadline.
- `OrderNoShowPolicy` ở Domain trả lý do nếu không phải Pickup, chưa Confirmed, deadline thiếu/chưa verified, hoặc chưa qua deadline. Mapping dùng cùng policy thêm `canMarkNoShow` và `noShowIneligibleReason` vào OrderResponse của danh sách/chi tiết, giữ các field cũ. Đây là business eligibility, không thay thế kiểm tra quyền caller hoặc kết luận user thực sự vắng mặt. Command bước 6 sẽ dùng lại policy và kiểm tra lại trong thao tác update atomic.
- Generic PATCH status đã được siết ngay trong bước 5: chỉ buyer/owner/admin được hủy Pending; lặp Cancelled trả idempotent sau kiểm tra quyền. Không cho tự đặt Confirmed, NoShow, Completed hoặc status tùy ý, cũng không cho thoát NoShow. Repository dùng atomic `UPDATE ... WHERE Status = Pending` để thao tác hủy không ghi đè status đã thay đổi; không thêm lock. Chưa thêm command/event NoShow ở bước này, review eligibility vẫn thuộc bước 6.

Migration Order phải được apply trước khi chạy API mới; chưa apply lên DB ứng dụng trong quá trình implement:

```powershell
Update-Database -Project StealDeal.Services.Order.Infrastructure -StartupProject StealDeal.Services.Order.API
```

Kiểm chứng bước 5 (2026-11-25): Order API build thành công. Harness tạm chạy controller/middleware/JWT thật, SQL LocalDB riêng và Store/Cart giả đã qua **77 kiểm tra**: nâng migration từ schema trước bước 5 với đơn legacy, snapshot khớp model, phân quyền seller/buyer/admin, ProblemDetails 403/503, generic status, handler payment, policy NoShow tại deadline, snapshot không đổi theo bag sửa sau mua, overlap/quantity/stock/expiry, cả direct/cart, giữ cart và không ghi order/Outbox khi thất bại, atomic cancel và lỗi HTTP/JSON/timeout/cancellation của client. Không chạy RabbitMQ hoặc gọi Store/Cart/DB ứng dụng thật. DB test và source/output tạm được dọn sau kiểm tra; không thêm test project vào solution.

### Bước 6 — Command NoShow

- [ ] Implement POST NoShow, lock order, lưu status + Outbox atomic.
- [ ] Command chỉ nhận orderId và ghi chú tùy chọn; actor lấy từ JWT, mức trừ điểm do Identity quyết định.
- [x] Generic PATCH status không cho caller tự đặt Confirmed, NoShow hoặc thoát NoShow để phạt lại (đã siết trong bước 5).
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

Các kiểm tra transaction, unique index và đồng thời cần thực hiện trên SQL Server tương thích môi trường thật; EF InMemory không chứng minh được các bảo đảm này. Bản MVP không bắt buộc duy trì project test riêng trong solution; dùng checklist và request kiểm tra khi tích hợp, chỉ thêm automated test khi thực sự cần.

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
