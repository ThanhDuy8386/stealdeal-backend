# User Trust Score — Backend và phần frontend còn lại

Cập nhật: 2026-11-25. Backend MVP đã implement và kiểm thử trên Docker. Tiếp theo hoàn thiện FE, test tích hợp rồi mới triển khai server.

## 1. Phạm vi

- Giữ nguyên property của `UserTrustScore` và `TrustScoreEvent`.
- Khách đọc pickup code, seller đối chiếu và giao hàng; không thêm `Completed` hoặc bước xác nhận pickup.
- Chỉ hiển thị điểm trên profile, thông báo hạn chế khi checkout và nút NoShow cho seller.
- Không làm UI/API quản lý điểm, lịch sử hoặc miễn/hoàn phạt cho user, seller hay admin.

## 2. Chính sách điểm và quyền mua

| Điều kiện | Chính sách mặc định |
| --- | --- |
| Điểm khởi tạo / giới hạn | 100 / từ 0 đến 100 |
| Thanh toán thành công được chấp nhận | +2 mỗi order, một lần |
| Seller đánh dấu NoShow hợp lệ | -15 mỗi order, một lần |
| 80–100 điểm | Mua bình thường theo giới hạn chung |
| 50–79 điểm | Tối đa một order Pending trên toàn bộ cửa hàng |
| Dưới 50 và còn cooldown sau NoShow | Chặn tạo đơn mới 24 giờ từ thời điểm đánh dấu NoShow |
| Dưới 50 nhưng không còn cooldown hiệu lực | Được mua lại theo quota một Pending để phục hồi điểm |

- Thưởng thanh toán và phạt NoShow độc lập: `80 → thanh toán 82 → NoShow 67`.
- NoShow mới có `ScoreAfter < 50` bắt đầu cooldown mới; đọc eligibility hoặc cộng điểm không gia hạn cooldown.
- Không phạt bỏ thanh toán, payment failed/expired hay lỗi hệ thống. Không khóa tài khoản bằng `User.IsActive`.
- Hạn chế chỉ áp dụng tạo đơn mới; vẫn xem đơn, thanh toán và nhận hàng của đơn đã tạo.

## 3. Flow backend đã apply

**Tạo đơn:** direct create và checkout cart đều đi qua OrderService. Order forward JWT gọi Identity eligibility, xác thực dữ liệu Store/bag, rồi kiểm tra quota Pending và lưu order + Outbox trong cùng transaction. Identity chỉ tính policy; Order tự đếm Pending.

Order dùng tên/giá từ Store. Các bag phải có khung pickup giao nhau; deadline là thời điểm kết thúc sớm nhất của khung chung, lưu snapshot cùng `PickupDeadlineVerified=true`. Ví dụ `17–19h` và `18–20h` cho deadline `19h`. Đơn cũ chưa verified không được NoShow.

```text
Order Pending + order.created
  → Store giữ/trừ tồn kho → inventory.reserved
  → Payment tạo checkout VNPAY
  → IPN thành công: Success + payment.completed
       → queue Order: Pending → Confirmed
       → queue Identity: +2 điểm
```

Return URL chỉ hiển thị/xác minh kết quả, không cập nhật payment. Hủy trên VNPAY được xử lý theo IPN thất bại hoặc worker hết hạn. Failed/expired phát event thất bại và release inventory; success đến sau Failed/Expired chuyển RefundPending và tạo Refund Pending, không thưởng điểm.

**Nhận hàng:** khách đọc pickup code, seller giao hàng; order giữ Confirmed. Confirmed chứng minh thanh toán, không chứng minh đã nhận hàng.

**NoShow:** seller tự kiểm kê, gọi command riêng. Backend xác minh owner hoặc Admin/SuperAdmin; yêu cầu Pickup + Confirmed + deadline verified + đã quá deadline. Order đổi NoShow và lưu Outbox `order.no-show` trong một transaction; Identity trừ điểm bất đồng bộ. Không tự refund/restock.

Payment tăng `TotalOrders`; NoShow tăng `NoShowCount`. `SuccessfulPickups` không tăng vì không có bước xác nhận pickup. Profile đọc điểm hiện tại sau khi consumer xử lý.

## 4. Các case đã xử lý

| Case | Kết quả |
| --- | --- |
| Seller khác store, buyer gọi NoShow | Từ chối; không đổi status/điểm |
| Pending, chưa qua deadline hoặc deadline chưa verified | Không được NoShow |
| Nhiều request NoShow đồng thời / gọi lại | Một lần đổi status, một Outbox, một lần phạt |
| Nhiều request tạo đơn khi Limited | Transaction + SQL khóa vùng Pending giữ đúng quota; 10 request test chỉ một đơn thành công |
| Message trùng ID hoặc ID mới cùng order outcome | ProcessedMessages + unique ledger ngăn thưởng/phạt lại |
| Nhiều event cập nhật cùng user | Atomic SQL cộng/trừ điểm và counters, giữ biên 0–100 |
| NoShow đến Identity trước ledger thanh toán | Chưa phạt; retry chờ payment được ghi nhận |
| SQL lỗi tạm thời | Retry queue dùng chung: chờ 30 giây, tối đa 5 retry; hết retry/lỗi vĩnh viễn vào DLQ |
| Payment event đến muộn sau NoShow | Không ghi đè NoShow về Confirmed |
| Identity/Store không khả dụng khi tạo đơn | 503, không tạo order/Outbox, không xóa cart |
| Checkout bị policy/quota/validation từ chối | Giữ cart; tạo đơn thành công mới xóa cart |
| Payment failed/expired hoặc inventory reservation thất bại | Không thưởng/phạt; release inventory theo flow hiện có |

## 5. API dùng cho frontend

| Endpoint | FE sử dụng |
| --- | --- |
| `GET /api/identity/account/profile` | Hiển thị `userTrustScore.score` |
| `GET /api/identity/account/purchase-eligibility` | Đọc policy nếu cần; checkout đã tự kiểm tra, FE không bắt buộc gọi trước |
| `POST /api/order/orders` và `/checkout-from-cart` | Tạo đơn; hiển thị lỗi hạn chế từ backend |
| `GET /api/order/orders/store/{storeId}` và `/{id}` | Danh sách/chi tiết, pickup code, deadline, `canMarkNoShow`, `noShowIneligibleReason` |
| `POST /api/order/orders/{id}/no-show` | Seller xác nhận NoShow; body tùy chọn `{ "reason": "..." }`, tối đa 800 ký tự |

NoShow thành công trả 200 với order NoShow; không khẳng định Identity đã trừ điểm ngay. FE không truyền userId, actorId hoặc số điểm muốn trừ.

Checkout trả ProblemDetails: `detail` là thông báo, kèm `reasonCode`, `score`, `restrictedUntilUtc`, `maxPendingOrders` khi bị hạn chế:

- `403 / TrustScorePurchaseSuspended`: còn cooldown.
- `409 / PendingOrderLimitReached`: đủ quota Pending.
- `409 / NoShowNotEligible`, `403 / StoreOwnershipRequired`: thao tác NoShow không hợp lệ/sai quyền.
- `503 / DependencyUnavailable`: lỗi kỹ thuật, không phải user bị phạt.

Chi tiết payload và cấu hình: [API contract](USER_TRUST_SCORE_API_CONTRACT.md).

## 6. Kết quả kiểm thử và phạm vi hủy đơn

Docker thật: **106 assertion, 104 PASS, 2 FAIL**; đã kiểm tra payment/IPN, NoShow, quota đồng thời, stock, cart, cooldown, retry TTL, chống trùng và Identity offline. Xem [báo cáo test](USER_TRUST_SCORE_DOCKER_TEST_REPORT.md).

Hai assertion fail thuộc nhánh gọi trực tiếp `PATCH status=Cancelled` rồi thanh toán URL cũ: Order Cancelled nhưng Payment Success, vẫn ghi thưởng và chưa refund. FE hiện không dùng nhánh này; đây không phải flow khách hủy trên VNPAY. API Cancelled vẫn còn trong backend, chưa được sửa/chặn; không nối nút hủy này vào FE trong phạm vi hiện tại.

## 7. Frontend cần hoàn thiện trước khi deploy

- [ ] Profile hiện điểm `x/100` từ API sẵn có.
- [ ] Checkout hiển thị `detail` và thời gian hết hạn nếu bị hạn chế; phân biệt 403/409 nghiệp vụ với 503 kỹ thuật.
- [ ] Seller dùng danh sách/chi tiết hiện có, hiển thị deadline và nút NoShow theo `canMarkNoShow`; có xác nhận, ghi chú tùy chọn, reload order sau thành công.
- [ ] Không thêm UI quản lý điểm/admin, Completed hay xác nhận pickup; không tự tính điểm/policy tại FE.
- [ ] Test tích hợp FE: thanh toán → Confirmed → profile; NoShow → cập nhật status/điểm; checkout bị quota/cooldown.

## 8. Database và triển khai sau khi xong FE

DB Docker test đã có các migration: Identity `AddTrustScoreIntegrity` (unique ledger, biên điểm); Order `AddVerifiedPickupDeadline` (cờ deadline) và `AddOrderPendingQuotaIndex` (UserId/Status). Mỗi môi trường/server cần apply migration riêng.

```powershell
Update-Database -Project StealDeal.Services.Identity.Infrastructure -StartupProject StealDeal.Services.Identity.API
Update-Database -Project StealDeal.Services.Order.Infrastructure -StartupProject StealDeal.Services.Order.API
```

Kiểm tra đúng connection string trước khi chạy. EF chỉ áp dụng migration còn thiếu; không chạy riêng Designer/snapshot. Dữ liệu Identity trùng outcome/điểm ngoài biên cần được kiểm tra nếu migration bị từ chối.

Cấu hình điểm ở `TrustScore`; consumer ở `TrustScoreConsumer` trong Identity appsettings. Source hiện đặt `Enabled=true`; server có thể override qua `TrustScoreConsumer__Enabled`. Khi false, queue vẫn giữ event nhưng chưa cập nhật điểm.

Triển khai: apply schema → deploy producer Payment có UserId và các API/FE mới → kiểm tra backlog → bật consumer → smoke test. Không tự thưởng/phạt lại toàn bộ dữ liệu cũ; đơn cũ cần deadline đáng tin và ledger thanh toán trước khi dùng NoShow.
