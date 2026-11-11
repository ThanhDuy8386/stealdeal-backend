# User Trust Score — Contract MVP

Chốt ngày 2026-11-11, thuộc bước 1 của `USER_TRUST_SCORE_IMPLEMENTATION_PLAN.md`.

Đây là contract cho các bước tiếp theo, chưa phải danh sách endpoint mới đã chạy. Bước 1 bổ sung constants, settings có startup validation và DTO; chưa bật consumer, cộng/trừ điểm hoặc hạn chế mua.

## Phạm vi

- Giữ nguyên property của `UserTrustScore` và `TrustScoreEvent`.
- Profile hiện có hiển thị điểm; không thêm màn hình/API quản lý điểm, lịch sử hoặc hoàn phạt cho user/seller/admin.
- Có đúng hai route mới: purchase eligibility của chính user và command NoShow của order.
- Không thêm `Completed`, không phạt việc bỏ thanh toán, không chặn thanh toán của order đã tạo hoặc IPN hợp lệ dựa trên trust score.

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

Các service có DTO/constants riêng, không tham chiếu project của service khác chỉ để chia sẻ contract. Giữ tên/giá trị wire contract đồng nhất. Payload Payment bổ sung UserId và event NoShow được nối vào producer/consumer ở bước 3/6, không phát event mới trong bước 1.

## Profile: dùng API hiện có

`GET /api/identity/account/profile`, yêu cầu JWT. Hiển thị `userTrustScore.score` từ response hiện có. Không cần gọi thêm API trust score và không trả ledger/ghi chú nội bộ.

## Purchase eligibility: endpoint mới ở bước 4

`GET /api/identity/account/purchase-eligibility`, yêu cầu JWT. UserId chỉ lấy từ JWT, không nhận query/body userId. Order forward JWT của user khi gọi.

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
- Các endpoint danh sách/chi tiết order giữ contract hiện có; eligibility NoShow trong DTO được bổ sung ở bước 5. Generic PATCH status được siết ở bước 6.

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
- Trước khi bật consumer ở bước 3 và NoShow ở bước 6, phải chốt cutover, xử lý message cũ thiếu UserId và các order Confirmed chưa có ledger thanh toán như plan bước 8.
- Backfill nếu cần là tác vụ riêng có kiểm soát/idempotency, không reset trust row hoặc chạy lại toàn bộ điểm một cách âm thầm.
- Chưa có flow miễn/hoàn phạt hoặc đảo NoShow trong MVP. Không yêu cầu UI quản trị để hoàn thành backend/FE của phạm vi này.
