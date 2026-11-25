# User Trust Score — Contract MVP

Cập nhật: 2026-11-25. Tổng kết nghiệp vụ và checklist FE/deploy: [implementation plan](USER_TRUST_SCORE_IMPLEMENTATION_PLAN.md).

Trạng thái: backend MVP đã implement và kiểm thử Docker; tiếp theo hoàn thiện frontend trước khi deploy server. DB Docker đã có migration Identity/Order; môi trường server cần cập nhật riêng.

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

Các service có DTO/constants riêng, không tham chiếu project của service khác chỉ để chia sẻ contract. Giữ tên/giá trị wire contract đồng nhất. Bước 3 đã bổ sung UserId phía Payment và DTO/handler của hai event phía Identity. Bước 6 đã nối producer NoShow phía Order.

Atomic UPDATE dùng điểm hiện tại của DB, clamp 0–100 và tăng counters trong cùng câu lệnh; OUTPUT trả ScoreBefore/ScoreAfter thực tế cho ledger. `TrustScoreUpdateResult` chỉ là object kết quả nội bộ, không thêm table/column hoặc thay property hai model hiện có. Nếu ledger/ProcessedMessage bị unique conflict, transaction rollback cả atomic UPDATE; consumer bước 3 retry bằng scope mới. Xung đột đồng thời tạo row còn thiếu cũng theo cơ chế unique UserId + rollback/retry.

## Event contract đã nối ở bước 3

`payment.completed`: `messageId`, `occurredAtUtc`, `orderId`, **`userId`** (mới, từ Transaction), `paymentId`, `amount`, `paymentMethod`, `gatewayRef`. Giữ nguyên routing key và các field cũ; Order vẫn nhận để cập nhật Confirmed, Identity nhận qua queue riêng để thưởng điểm.

`order.no-show`: `messageId`, `occurredAtUtc`, `orderId`, `userId`, `storeId`, `markedByUserId`, `pickupDeadlineUtc`, `reason`. Order đã ghi event vào Outbox cùng transaction cập nhật NoShow ở bước 6; Identity nhận bằng consumer bước 3. Deadline và occurred time đều UTC; occurred time phải sau deadline. Actor từ JWT của caller, user/store/deadline lấy từ order, không lấy trực tiếp từ client. Outbox Id trùng messageId; EventType và RoutingKey đều `order.no-show`.

AMQP `MessageId` phải là Guid khác rỗng và trùng payload `messageId`/Outbox Id. AMQP `Type`, nếu có, phải bằng routing key nghiệp vụ. Payload camelCase, Identity đọc không phân biệt hoa thường. Application service tiếp tục validate user/order, UTC timestamp và mức điểm; không nhận scoreDelta từ producer.

Queue chính `identity.trust-score-events` bind cả hai routing key; một consumer tuần tự, prefetch 1. Retry queue `.retry` có delay 30 giây, tối đa 5 retry; `.dead-letter` giữ message lỗi vĩnh viễn hoặc hết retry. Retry giữ MessageId, routing key gốc qua header; ACK sau commit hoặc sau confirmed publish sang retry/DLQ. NoShow đến trước payment không ghi processed marker và được retry có delay.

Section `TrustScoreConsumer`: source hiện đặt `Enabled=true`, `ExchangeName=stealdeal.events`, `ExchangeType=topic`, `QueueName=identity.trust-score-events`, `BindingKeys=[payment.completed, order.no-show]`, `PrefetchCount=1`, `RetryDelaySeconds=30`, `MaxRetryCount=5`, `ReconnectDelaySeconds=10`. Server có thể override bằng `TrustScoreConsumer__Enabled`. Khi false, Identity vẫn khai báo queue/bindings để giữ event nhưng không consume; eligibility API vẫn hoạt động. Chỉ bật xử lý khi schema, producer và backlog đã sẵn sàng.

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

Service bước 2 đọc eligibility mà không mở transaction ghi hoặc tạo/sửa trust row. Nếu user tồn tại nhưng thiếu trust row, trả policy mặc định ở mức 100; consumer khởi tạo row khi ghi nhận event. Không tạo ledger thưởng/phạt hoặc gia hạn khóa khi đọc. API đã mở ở bước 4; OpenAPI khai báo schema `PurchaseEligibilityResponse` cho response 200. Client/enforcement tại Order đã nối ở bước 7.

## Enforce tạo order: đã implement ở bước 7

- `POST /api/order/orders` và `POST /api/order/orders/checkout-from-cart` đều đi qua OrderService.CreateOrderAsync. Backend lấy userId/JWT của caller, gọi Identity eligibility trước khi lưu order; không dựa vào lần kiểm tra trước đó của FE.
- IdentityService.BaseUrl trong Order appsettings mặc định `http://localhost:5158`; env Docker là `IdentityService__BaseUrl=http://identity-api:5158`. Typed client dùng bearer header riêng cho từng request và timeout 10 giây. Không cache kết quả dùng chung giữa user, không dùng default authorization header.
- Bị khóa: `403`, reasonCode `TrustScorePurchaseSuspended`, detail là thông báo Identity, kèm score/restrictedUntilUtc/maxPendingOrders. Hết cooldown vẫn dưới ngưỡng thì mua theo quota Limited.
- Đã đủ Pending toàn user, kể cả khác store: `409`, reasonCode `PendingOrderLimitReached`, detail yêu cầu thanh toán/hủy đơn hiện có, kèm score/maxPendingOrders và restrictedUntilUtc=null. Normal maxPendingOrders=null không bị quota bổ sung của trust score. Chỉ count Status=Pending, không count Confirmed/NoShow/Cancelled/failed.
- Check Pending và ghi order + Outbox ở cùng transaction, dùng COUNT với SQL update/key-range locks (`UPDLOCK,HOLDLOCK`) và index `(UserId,Status)`, kể cả khi chưa có Pending row. Cả direct/cart và Normal/Limited đều dùng cơ chế đồng bộ này, tránh hai request cùng vượt quota. Các HTTP call Identity/Store nằm ngoài transaction DB.
- Identity trả 401 thì Order trả 401 để user đăng nhập lại. Network/timeout/HTTP khác, JSON sai, payload thiếu field hoặc policy không hợp lệ trả `503 / DependencyUnavailable`, không kèm score/restriction giả định. Payload hợp lệ được validate theo tier/quota/deadline, không tự hardcode ngưỡng điểm tại Order.
- Khi từ chối, không có order mới hoặc order.created Outbox và không xóa cart. Cleanup checkout lock là best effort có log, giữ nguyên response ban đầu nếu cleanup lỗi; lock còn TTL. Chỉ xóa cart sau khi tạo order thành công.
- Các API đọc/hủy đơn, NoShow, xử lý payment event và VNPAY IPN không gọi eligibility; hạn chế chỉ áp dụng cho tạo đơn mới. Không chặn thanh toán đơn đã tạo trước đó. Chấp nhận độ trễ event/policy snapshot giữa các service của MVP.

## Order ownership, deadline và eligibility NoShow: đã implement ở bước 5

- `GET /api/order/orders/{id}`: buyer của đơn, seller sở hữu store hoặc Admin/SuperAdmin. `GET /api/order/orders/store/{storeId}`: owner có role Seller hoặc Admin/SuperAdmin. Backend xác minh OwnerId qua API Store hiện có; role Seller một mình không đủ quyền. Ownership sai trả `403` với `reasonCode=StoreOwnershipRequired`.
- Store lookup lỗi mạng/timeout/HTTP/JSON trả `503` với `reasonCode=DependencyUnavailable`; không trả lỗi tín nhiệm cho lỗi kỹ thuật. Buyer/Admin xem chi tiết đơn không phụ thuộc vào Store lookup. Không thêm quyền xem điểm buyer cho seller.
- OrderResponse giữ các field cũ, bổ sung `canMarkNoShow: boolean` và `noShowIneligibleReason: string | null`. Policy nghiệp vụ: Pickup + Confirmed + deadline đã verified + nowUtc > deadline. Các field này không cấp quyền thao tác cho caller và không khẳng định user chưa nhận hàng; command bước 6 kiểm tra quyền và policy lần nữa.
- Cả `POST /api/order/orders` và `POST /api/order/orders/checkout-from-cart` đều qua validation Store chung ở OrderService: kiểm tra store/bag/quantity/stock/expiry, dùng tên và giá từ Store. Client/cart không quyết định deadline. Với nhiều bag Pickup, cần giao khung giờ có độ dài dương; deadline là min(PickupEndTime), với max(PickupStartTime) < min(PickupEndTime). Không có giao nhau trả `400`, không ghi order/Outbox; checkout không xóa cart khi validation lỗi.
- Deadline trả UTC, giữ snapshot lúc tạo đơn; thay lịch bag sau mua không sửa deadline của order. OrderProfile có cờ nội bộ PickupDeadlineVerified, không nhận từ request. Migration `20261125041036_AddVerifiedPickupDeadline` giữ đơn cũ ở false, nên các đơn cũ chưa xác minh có canMarkNoShow=false. Không tự backfill từ lịch bag hiện tại.
- `PATCH /api/order/orders/{id}/status` hiện chỉ cho buyer/owner/admin hủy Pending, body `{ "status": "Cancelled" }`. Lặp Cancelled trả `200` sau authorization. Đơn đã đổi status hoặc cố đặt Confirmed/NoShow/Completed/status khác trả `409`; status null/rỗng trả `400`. Hủy dùng atomic update trên điều kiện Pending. NoShow dùng command riêng đã implement ở bước 6.

## NoShow: đã implement ở bước 6

`POST /api/order/orders/{id:guid}/no-show`.

- Role `Seller`, `Admin` hoặc `SuperAdmin`; seller phải sở hữu store của order.
- Body hoàn toàn tùy chọn: controller cho phép empty body hoặc JSON null bằng EmptyBodyBehavior.Allow và request nullable.
- DTO `MarkOrderNoShowRequest`: `{ "reason": "Ghi chú tùy chọn" }`. Reason null/rỗng/toàn khoảng trắng dùng `DefaultReason`; lý do có nội dung được trim.
- Reason tối đa 800 ký tự, giữ chỗ cho actor và audit context trong `Note` 1000 ký tự. Không nhận userId, scoreDelta hoặc actorId từ client.
- Order phải Pickup, Confirmed từ thanh toán hợp lệ, có deadline đáng tin và đã qua deadline. Quá thời hạn nghĩa là `nowUtc > PickupDeadline`.
- Response `200` sử dụng `OrderResponse` hiện có, với `status = NoShow`; không cần DTO response mới chỉ để bọc lại order.
- Không trả điểm buyer cho seller hoặc khẳng định Identity đã trừ điểm. Status + Outbox được commit trước response; Identity xử lý bất đồng bộ.
- Gọi lại trên NoShow trả kết quả idempotent sau kiểm tra quyền, không tạo thêm event.
- Hai request đồng thời dùng conditional UPDATE trong transaction; chỉ request đổi được Confirmed -> NoShow mới ghi Outbox. Request còn lại đọc lại NoShow và trả idempotent. Lỗi lưu Outbox rollback cả status. Không tự refund hoặc restock.
- Điều kiện nghiệp vụ sai trả `409` ProblemDetails với `reasonCode=NoShowNotEligible`; seller sai store trả `403 / StoreOwnershipRequired`, Store lỗi trả `503 / DependencyUnavailable`. Input sai trả `400`, thiếu JWT `401`, thiếu order `404`. Response lỗi không chứa điểm user hoặc ghi chú nội bộ của Identity.
- Các endpoint danh sách/chi tiết order giữ contract hiện có; eligibility NoShow và generic PATCH status đã được siết trong bước 5. Command dùng lại OrderNoShowPolicy và vẫn phải authorize/check trạng thái tại thời điểm xử lý.

Review eligibility giữ route GET Store đang gọi, chỉ cho order Confirmed và đúng buyer/bag; Pending/NoShow/Completed không đủ điều kiện. Confirmed chứng minh thanh toán đã được xác nhận trong flow hiện tại, không chứng minh user đã nhận túi hàng.

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

- DB Docker đã có Identity `20261111102442_AddTrustScoreIntegrity` và hai migration Order `20261125041036_AddVerifiedPickupDeadline`, `20261125045044_AddOrderPendingQuotaIndex`. Server apply migration riêng trước khi chạy bản mới; lệnh nằm cuối implementation plan.
- Deploy Payment có UserId trong event, kiểm tra backlog rồi bật consumer. Event cũ thiếu UserId cần đối chiếu đúng Transaction/PaymentId/OrderId để enrich trước replay; không thưởng nhánh RefundPending.
- Replay trực tiếp vào default exchange, routing key `identity.trust-score-events`; giữ MessageId giữa AMQP/payload, Type là routing key nghiệp vụ, header `x-trust-score-routing-key` là `payment.completed` hoặc `order.no-show`, reset `x-trust-score-retry-count` về integer 0. Không replay qua topic exchange sang cả Order; chỉ bỏ bản DLQ sau khi publish thành công.
- Đơn cũ có PickupDeadlineVerified=false chưa được NoShow. Muốn mở cần xác minh deadline lịch sử và ledger thanh toán; không suy luận từ lịch bag hiện tại.
- Không tự thưởng/phạt lại toàn bộ lịch sử, reset điểm hoặc tự backfill. Backfill nếu cần phải có kiểm soát và chống trùng; MVP chưa có miễn/hoàn phạt hoặc đảo NoShow.
