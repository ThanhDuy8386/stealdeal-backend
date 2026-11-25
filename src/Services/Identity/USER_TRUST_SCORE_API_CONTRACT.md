# User Trust Score — Contract MVP

Chốt ngày 2026-11-11; cập nhật bước 5 ngày 2026-11-25, theo `USER_TRUST_SCORE_IMPLEMENTATION_PLAN.md`.

Trạng thái: bước 1–5 đã implement. Bước 1 bổ sung constants, settings có startup validation và DTO. Bước 2 có service/repository xử lý điểm và tính policy nội bộ. Bước 3 đã nối Payment event và Identity consumer; mặc định chỉ khai báo queue, chưa bật xử lý. Bước 4 đã mở purchase eligibility và kiểm chứng profile. Bước 5 đã siết ownership/status và dùng deadline pickup từ Store. Endpoint NoShow và enforcement trust score khi tạo đơn tại Order chưa implement.

## Phạm vi

- Giữ nguyên property của `UserTrustScore` và `TrustScoreEvent`.
- Profile hiện có hiển thị điểm; không thêm màn hình/API quản lý điểm, lịch sử hoặc hoàn phạt cho user/seller/admin.
- Có đúng hai route mới: purchase eligibility của chính user và command NoShow của order.
- Không thêm `Completed`, không phạt việc bỏ thanh toán, không chặn thanh toán của order đã tạo hoặc IPN hợp lệ dựa trên trust score.
- MVP dùng một instance Identity, một consumer xử lý tuần tự trên cùng queue cho cả payment và NoShow. Repository đã dùng atomic update cho điểm/counters để tránh lost update khi có nhiều consumer; không có application lock. Chống trùng và xung đột khởi tạo row vẫn cần unique constraint, transaction và retry consumer.

## Cấu hình và tên cố định

Section `TrustScore` tại Identity:

```json
{
  "PaymentSuccessReward": 2,
  "NoShowPenalty": 15,
  "NormalPurchaseMinimumScore": 80,
  "TemporaryBlockBelowScore": 50,
  "PurchaseSuspensionHours": 24,
  "LimitedMaxPendingOrders": 1
}
```

- `NoShowPenalty` là số dương để trừ, không cấu hình `-15`.
- Reward/penalty hợp lệ trong 1–100; `0 < TemporaryBlockBelowScore < NormalPurchaseMinimumScore <= 100`.
- Suspension hours trong 1–8760; limited pending quota là số nguyên dương.
- Cấu hình sai làm Identity fail startup bằng options validation; không chờ tới request đầu tiên.
- Điểm khởi tạo 100 và biên 0–100 là constants nghiệp vụ, không phải cấu hình thay đổi riêng theo môi trường.
- Settings nằm ở Application để service tính điểm sau này sử dụng mà không phụ thuộc Infrastructure; options validator nằm ở Infrastructure.

| Loại | Giá trị canonical |
| --- | --- |
| Ledger EventType | `PaymentSucceeded`, `OrderNoShow` |
| Ledger ReferenceType | `Order` |
| Routing key Identity subscribe | `payment.completed`, `order.no-show` |
| Order status liên quan | `Pending`, `Confirmed`, `NoShow`, `Cancelled`, `PaymentFailed`, `InventoryReservationFailed` |
| Purchase tier | `Normal`, `Limited`, `TemporarilyBlocked` |
| Role quản trị | `Admin`, `SuperAdmin`, quyền tương đương trong các API order thuộc phạm vi này |

Các service có DTO/constants riêng, không tham chiếu project của service khác chỉ để chia sẻ contract. Giữ tên/giá trị wire contract đồng nhất. Bước 3 đã bổ sung UserId phía Payment và DTO/handler của hai event phía Identity. Producer NoShow phía Order thuộc bước 6.

Atomic UPDATE dùng điểm hiện tại của DB, clamp 0–100 và tăng counters trong cùng câu lệnh; OUTPUT trả ScoreBefore/ScoreAfter thực tế cho ledger. `TrustScoreUpdateResult` chỉ là object kết quả nội bộ, không thêm table/column hoặc thay property hai model hiện có. Nếu ledger/ProcessedMessage bị unique conflict, transaction rollback cả atomic UPDATE; consumer bước 3 retry bằng scope mới. Xung đột đồng thời tạo row còn thiếu cũng theo cơ chế unique UserId + rollback/retry.

## Event contract đã nối ở bước 3

`payment.completed`: `messageId`, `occurredAtUtc`, `orderId`, **`userId`** (mới, từ Transaction), `paymentId`, `amount`, `paymentMethod`, `gatewayRef`. Giữ nguyên routing key và các field cũ; Order vẫn nhận để cập nhật Confirmed, Identity nhận qua queue riêng để thưởng điểm.

`order.no-show`: `messageId`, `occurredAtUtc`, `orderId`, `userId`, `storeId`, `markedByUserId`, `pickupDeadlineUtc`, `reason`. Identity đã có DTO/handler, Order sẽ phát ở bước 6. Deadline và occurred time đều UTC; occurred time phải sau deadline. Actor từ backend Order, không lấy trực tiếp từ client.

AMQP `MessageId` phải là Guid khác rỗng và trùng payload `messageId`/Outbox Id. AMQP `Type`, nếu có, phải bằng routing key nghiệp vụ. Payload camelCase, Identity đọc không phân biệt hoa thường. Application service tiếp tục validate user/order, UTC timestamp và mức điểm; không nhận scoreDelta từ producer.

Queue chính `identity.trust-score-events` bind cả hai routing key; một consumer tuần tự, prefetch 1. Retry queue `.retry` có delay 30 giây, tối đa 5 retry; `.dead-letter` giữ message lỗi vĩnh viễn hoặc hết retry. Retry giữ MessageId, routing key gốc qua header; ACK sau commit hoặc sau confirmed publish sang retry/DLQ. NoShow đến trước payment không ghi processed marker và được retry có delay.

Section `TrustScoreConsumer`: `Enabled=false`, `ExchangeName=stealdeal.events`, `ExchangeType=topic`, `QueueName=identity.trust-score-events`, `BindingKeys=[payment.completed, order.no-show]`, `PrefetchCount=1`, `RetryDelaySeconds=30`, `MaxRetryCount=5`, `ReconnectDelaySeconds=10`. Các field exchange/bindings/prefetch theo convention consumer hiện có; startup validation giữ đúng hai binding và prefetch 1 của MVP. Dù chưa bật xử lý, Identity vẫn khai báo các queue/bindings. Bật `Enabled=true` và restart sau khi migration, producer và backlog đã được kiểm tra. Quy trình cutover/enrich payload cũ/replay DLQ nằm trong mục bước 3 của plan; không replay qua topic exchange sang cả Order.

## Profile: dùng API hiện có

`GET /api/identity/account/profile`, yêu cầu JWT. Hiển thị `userTrustScore.score` từ response hiện có. Không cần gọi thêm API trust score và không trả ledger/ghi chú nội bộ.

Bước 4 đã kiểm chứng profile qua HTTP sau khi handler payment/NoShow commit điểm vào SQL Server. Giữ nguyên DTO/mapping/repository hiện có, không thêm endpoint profile trust score riêng.

## Purchase eligibility: đã implement ở bước 4

`GET /api/identity/account/purchase-eligibility`, yêu cầu JWT. UserId chỉ lấy từ JWT, không nhận query/body userId. Order forward JWT của user khi gọi.

Controller dùng `IAccountService` theo flow account hiện có; AccountService kiểm tra account active/chưa deleted rồi gọi trust-score service. JWT thiếu/sai/hết hạn, subject không hợp lệ hoặc account không còn active/tồn tại trả `401`. Kết quả bị hạn chế trust score vẫn là response `200` với `canCreateOrder=false`; `403` chỉ áp dụng khi Order enforce từ chối tạo đơn ở bước 7. Không có ghi chú nội bộ, ledger hoặc thông tin user khác trong response.

Response `200`: `PurchaseEligibilityResponse` của Identity; Order đọc bằng `PurchaseEligibilityClientResponse`.

```json
{
  "score": 45,
  "tier": "TemporarilyBlocked",
  "canCreateOrder": false,
  "restrictedUntilUtc": "2026-11-12T10:00:00Z",
  "maxPendingOrders": 1,
  "reasonCode": "TrustScorePurchaseSuspended",
  "message": "Tạm hạn chế đặt đơn mới do điểm tín nhiệm thấp sau khi không đến nhận hàng."
}
```

| Trường hợp | canCreateOrder | restrictedUntilUtc | maxPendingOrders | reasonCode/message |
| --- | --- | --- | --- | --- |
| Normal: điểm >= 80 | true | null | null | null |
| Limited: điểm < 80, không đang bị khóa | true | null | 1 | null |
| TemporarilyBlocked: điểm < 50, cooldown còn hiệu lực | false | Mốc UTC hết cooldown | 1 | Lý do hạn chế |

Ngưỡng và quota trong bảng là mặc định; response theo settings hiệu lực. Khi cooldown hết hạn, trả null cho `restrictedUntilUtc`, không tiếp tục trả deadline khóa đã hết. `canCreateOrder` chỉ đánh giá trust score, không khẳng định quota Pending hoặc hàng tồn còn đủ.

Order tự kiểm tra eligibility khi tạo order. FE không bắt buộc gọi trước và không tự tính policy. Identity không gọi Order để đếm Pending. API này chỉ đọc và không gia hạn khóa.

Service bước 2 đọc eligibility mà không mở transaction ghi hoặc tạo/sửa trust row. Nếu user tồn tại nhưng thiếu trust row, trả policy mặc định ở mức 100; consumer khởi tạo row khi ghi nhận event. Không tạo ledger thưởng/phạt hoặc gia hạn khóa khi đọc. API đã mở ở bước 4; OpenAPI khai báo schema `PurchaseEligibilityResponse` cho response 200. Bước 7 sẽ thêm client/enforcement tại Order.

## Order ownership, deadline và eligibility NoShow: đã implement ở bước 5

- `GET /api/order/orders/{id}`: buyer của đơn, seller sở hữu store hoặc Admin/SuperAdmin. `GET /api/order/orders/store/{storeId}`: owner có role Seller hoặc Admin/SuperAdmin. Backend xác minh OwnerId qua API Store hiện có; role Seller một mình không đủ quyền. Ownership sai trả `403` với `reasonCode=StoreOwnershipRequired`.
- Store lookup lỗi mạng/timeout/HTTP/JSON trả `503` với `reasonCode=DependencyUnavailable`; không trả lỗi tín nhiệm cho lỗi kỹ thuật. Buyer/Admin xem chi tiết đơn không phụ thuộc vào Store lookup. Không thêm quyền xem điểm buyer cho seller.
- OrderResponse giữ các field cũ, bổ sung `canMarkNoShow: boolean` và `noShowIneligibleReason: string | null`. Policy nghiệp vụ: Pickup + Confirmed + deadline đã verified + nowUtc > deadline. Các field này không cấp quyền thao tác cho caller và không khẳng định user chưa nhận hàng; command bước 6 sẽ kiểm tra quyền và policy lần nữa.
- Cả `POST /api/order/orders` và `POST /api/order/orders/checkout-from-cart` đều qua validation Store chung ở OrderService: kiểm tra store/bag/quantity/stock/expiry, dùng tên và giá từ Store. Client/cart không quyết định deadline. Với nhiều bag Pickup, cần giao khung giờ có độ dài dương; deadline là min(PickupEndTime), với max(PickupStartTime) < min(PickupEndTime). Không có giao nhau trả `400`, không ghi order/Outbox; checkout không xóa cart khi validation lỗi.
- Deadline trả UTC, giữ snapshot lúc tạo đơn; thay lịch bag sau mua không sửa deadline của order. OrderProfile có cờ nội bộ PickupDeadlineVerified, không nhận từ request. Migration `20261125041036_AddVerifiedPickupDeadline` giữ đơn cũ ở false, nên các đơn cũ chưa xác minh có canMarkNoShow=false. Không tự backfill từ lịch bag hiện tại.
- `PATCH /api/order/orders/{id}/status` hiện chỉ cho buyer/owner/admin hủy Pending, body `{ "status": "Cancelled" }`. Lặp Cancelled trả `200` sau authorization. Đơn đã đổi status hoặc cố đặt Confirmed/NoShow/Completed/status khác trả `409`; status null/rỗng trả `400`. Hủy dùng atomic update trên điều kiện Pending. Command NoShow riêng sẽ được thêm ở bước 6.

## NoShow: endpoint mới ở bước 6

`POST /api/order/orders/{id:guid}/no-show`.

- Role `Seller`, `Admin` hoặc `SuperAdmin`; seller phải sở hữu store của order.
- Body hoàn toàn tùy chọn. Khi implement controller phải cho phép empty body, không chỉ khai báo `Reason` nullable trong DTO.
- DTO `MarkOrderNoShowRequest`: `{ "reason": "Ghi chú tùy chọn" }`. Reason null/rỗng/toàn khoảng trắng dùng `DefaultReason`; lý do có nội dung được trim.
- Reason tối đa 800 ký tự, giữ chỗ cho actor và audit context trong `Note` 1000 ký tự. Không nhận userId, scoreDelta hoặc actorId từ client.
- Order phải Pickup, Confirmed từ thanh toán hợp lệ, có deadline đáng tin và đã qua deadline. Quá thời hạn nghĩa là `nowUtc > PickupDeadline`.
- Response `200` sử dụng `OrderResponse` hiện có, với `status = NoShow`; không cần DTO response mới chỉ để bọc lại order.
- Không trả điểm buyer cho seller hoặc khẳng định Identity đã trừ điểm. Status + Outbox được commit trước response; Identity xử lý bất đồng bộ.
- Gọi lại trên NoShow trả kết quả idempotent sau kiểm tra quyền, không tạo thêm event.
- Các endpoint danh sách/chi tiết order giữ contract hiện có; eligibility NoShow và generic PATCH status đã được siết trong bước 5. Command dùng lại OrderNoShowPolicy và vẫn phải authorize/check trạng thái tại thời điểm xử lý.

## Error convention cho các bước triển khai endpoint

Dùng `ProblemDetails` như middleware hiện có; không tạo một wrapper lỗi mới song song. Giữ `status`, `title`, `detail`, `instance`; thêm flat extensions `reasonCode`, `score`, `restrictedUntilUtc`, `maxPendingOrders` khi phù hợp. `detail` là thông báo cho người dùng; trường `message` chỉ thuộc response eligibility thành công, tránh hai thông báo lỗi khác nhau trong cùng ProblemDetails.

```json
{
  "status": 403,
  "title": "Forbidden",
  "detail": "Tạm hạn chế đặt đơn mới do điểm tín nhiệm thấp sau khi không đến nhận hàng.",
  "instance": "/api/order/orders/checkout-from-cart",
  "reasonCode": "TrustScorePurchaseSuspended",
  "score": 45,
  "restrictedUntilUtc": "2026-11-12T10:00:00Z",
  "maxPendingOrders": 1
}
```

| HTTP | reasonCode | Ý nghĩa |
| --- | --- | --- |
| 400 | Theo validation API hiện có | Input sai, ghi chú quá dài |
| 401 | Theo authentication hiện có | JWT thiếu/không hợp lệ |
| 403 | `TrustScorePurchaseSuspended` | Tạm khóa tạo order mới; kèm thông tin policy |
| 403 | `StoreOwnershipRequired` | Seller không sở hữu cửa hàng của order |
| 404 | Theo API hiện có | Không có order/store |
| 409 | `PendingOrderLimitReached` | Đã đủ quota Pending, không phải đang bị khóa tài khoản; kèm score/quota |
| 409 | `NoShowNotEligible` | Order/status/deadline không đủ điều kiện NoShow |
| 503 | `DependencyUnavailable` | Identity/Store không khả dụng; không giả lập lý do user bị phạt |

Không thêm policy fields hoặc lộ điểm buyer trong lỗi seller đánh dấu NoShow. Chỉ trả thông tin điểm của chính user bị từ chối tạo order. Khi middleware được nối ở bước 5/7, giữ các lỗi cũ tương thích; không ghi chú nội bộ hoặc exception kỹ thuật vào response.

## Cutover và dữ liệu cũ

- Không tự động thưởng/phạt lại toàn bộ order lịch sử khi deploy bước 1 hoặc bật tính năng sau này.
- Chưa bật consumer mới hay backfill trong bước 1, nên chưa có thay đổi điểm/quyền mua cho user đang tồn tại.
- Bước 2 đăng ký service/repository nội bộ, chưa có consumer hay controller gọi tự động. Migration `20261111102442_AddTrustScoreIntegrity` cần được áp dụng trước khi bật consumer; unique index và check constraints từ chối dữ liệu cũ trùng/điểm ngoài 0–100. Không có SQL preflight riêng hoặc index tối ưu cooldown trong bản MVP. Chưa áp dụng migration lên database ứng dụng trong quá trình implement.
- Bước 3 đã có consumer nhưng `TrustScoreConsumer:Enabled=false` mặc định; vẫn khai báo queue để tránh mất event do chưa có binding riêng cho Identity. Payload cũ thiếu UserId được giữ trong DLQ, phải đối chiếu Transaction rồi enrich/replay theo runbook bước 3, không bỏ qua hoặc tự backfill tất cả lịch sử. Trước khi bật NoShow ở bước 6, xử lý order Confirmed chưa có ledger thanh toán theo cutover/backfill trong plan bước 8.
- Bước 5 thêm migration Order một cột PickupDeadlineVerified default false; cần Update-Database cho Order trước khi chạy bản mới. Chưa apply lên DB ứng dụng. Đơn cũ phải xác minh cả deadline lịch sử và payment ledger nếu muốn mở NoShow; không cung cấp API sửa cờ hoặc tự suy luận theo lịch bag hiện tại.
- Backfill nếu cần là tác vụ riêng có kiểm soát/idempotency, không reset trust row hoặc chạy lại toàn bộ điểm một cách âm thầm.
- Chưa có flow miễn/hoàn phạt hoặc đảo NoShow trong MVP. Không yêu cầu UI quản trị để hoàn thành backend/FE của phạm vi này.
