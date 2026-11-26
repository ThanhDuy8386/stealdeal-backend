# Kiểm thử Order / Trust Score trên Docker

Ngày chạy: 2026-11-25 (Asia/Saigon).

Kết quả: **106 assertion, 104 PASS, 2 FAIL**. Hai assertion fail thuộc nhánh gọi trực tiếp API Order để đổi Pending thành Cancelled rồi thanh toán URL cũ. Theo phạm vi FE đã xác nhận, client không dùng nhánh này; đây không phải thao tác hủy trên VNPAY.

## Môi trường và phương pháp

- Gọi HTTP vào các API Docker đang chạy: Identity 5158, Order 5165, Store 5169, Cart 5185, Payment 5155.
- Dùng SQL Server, Redis và RabbitMQ thật của Docker. Đã đối chiếu migration Identity/Order cần cho tính năng đều có trong `__EFMigrationsHistory`.
- Tạo fixture riêng với prefix `dockertrust-7256aae66a`: 10 user, hai store có owner khác nhau và các bag test. Không dùng tài khoản của người dùng hiện có.
- JWT test ký bằng secret đang cấu hình, với subject là user test. Không kiểm tra login, password hoặc email verification; không cần mật khẩu của người dùng.
- IPN gọi vào API thật với chữ ký HMAC SHA-512 đúng cấu hình sandbox. Không mở trang VNPAY hoặc thực hiện chuyển tiền thật.
- Bật tạm consumer Identity trong file appsettings của container rồi restart. Sau test khôi phục `Enabled=false` như ban đầu. Không đổi cấu hình source/Docker Compose.
- NoShow: chỉnh deadline của riêng order test về quá khứ để không phải chờ hết khung pickup. Test deadline chưa verified cũng dùng riêng order test.
- Expiration: chỉnh `ExpiresAt` của transaction test về quá khứ, để worker thật thực hiện expire/release.
- Test message đến sai thứ tự dùng event fixture gửi riêng tới queue Identity qua RabbitMQ Management API. Đây là kiểm thử consumer/retry, không phải order thanh toán thật. Các flow thanh toán chính dùng đầy đủ Order -> Store -> Payment -> IPN.
- Tạm dừng Identity để kiểm tra dependency, sau đó start lại trong `finally`.

## Những flow đã qua

- Order lấy eligibility đúng tier; Pending quota áp dụng toàn user qua nhiều store và cả direct/cart.
- Tạo order dùng giá từ Store, giữ inventory, tạo checkout, xác nhận IPN, chuyển Confirmed và cộng điểm 80 -> 82.
- IPN sai chữ ký/sai số tiền bị từ chối; return URL không sửa trạng thái; IPN lặp không thưởng lại.
- Seller đúng store đánh NoShow sau deadline; buyer, seller khác store, Pending, deadline chưa đến/chưa verified bị chặn.
- Tám request NoShow đồng thời trả idempotent, chỉ một Outbox và một lần phạt 82 -> 67.
- Mười request tạo đơn đồng thời qua hai store chỉ một Pending thành công, chín request quota conflict.
- Cart được giữ khi checkout bị từ chối, được xóa khi tạo order thành công; cancel Pending giải phóng quota.
- Điểm tối đa 100 vẫn ghi actual delta 0 và tăng TotalOrders; SuccessfulPickups không tăng vì chưa có bước xác nhận pickup.
- NoShow thực tế 62 -> 47 bắt đầu cooldown và chặn đơn mới; cooldown hết hạn cho phép mua theo Limited.
- Failed payment/expired payment trả tồn kho và không tác động điểm; success đến sau Expired tạo RefundPending + Refund, không thưởng.
- Bag sai store, quantity không hợp lệ, cộng dồn bag vượt tồn, pickup không giao nhau đều bị từ chối.
- Hai buyer cạnh tranh một bag cuối: chỉ một reservation và một transaction; đơn kia InventoryReservationFailed, tồn không âm.
- RabbitMQ TTL retry thật khôi phục NoShow đến trước payment; duplicate message và message ID mới cùng outcome không phạt/thưởng lặp.
- Một replay do script test tạo sai MessageId giữa broker và payload bị từ chối đúng vào DLQ. Đã sửa replay, xác nhận Identity ghi ProcessedMessage cho bản hợp lệ mà không thưởng lại, rồi loại đúng message test lỗi đó khỏi DLQ. Không có lỗi runtime ứng dụng từ trường hợp này.
- Payment event đến muộn không ghi đè NoShow thành Confirmed.
- Identity offline: tạo order trả 503 kỹ thuật; vẫn đọc được đơn đã trả tiền và xử lý IPN. Identity hoạt động lại sau restart.

## Case ngoài flow FE hiện tại: API Cancelled rồi thanh toán

Order tái hiện: `4c2638bb-a27d-4db0-9eac-05a3b5518c5e`.

1. Tạo order mới và chờ Payment tạo checkout URL.
2. Buyer gọi PATCH status để hủy Pending thành Cancelled.
3. Giả lập success IPN hợp lệ cho transaction/URL đã tạo.
4. Payment trả RspCode 00 và chuyển transaction thành Success.
5. Order vẫn Cancelled vì consumer chỉ chuyển trạng thái từ Pending.
6. Identity vẫn ghi một PaymentSucceeded ledger; không có refund cho order này.

Hai assertion fail:

- Tiền nhận sau khi hủy chưa được đưa vào RefundPending.
- Order đã hủy vẫn được ghi nhận thưởng thanh toán. Fixture dùng score 100 nên ScoreDelta là 0 tại trần, nhưng ledger và TotalOrders vẫn tăng; với điểm dưới trần sẽ được cộng điểm.

Nguyên nhân đối chiếu source: thao tác hủy hiện chỉ cập nhật Order DB; chưa có event đồng bộ cancellation sang Payment/Store. Nhánh late-success của Payment chỉ nhận biết transaction Failed/Expired, không biết order đã Cancelled. Identity tin event payment.completed được Payment chấp nhận. Không sửa lỗi bằng cách chỉ cho Identity gọi thêm Order, vì vẫn còn tiền thanh toán và tồn kho cần được xử lý nhất quán.

FE hiện chỉ hủy qua trang VNPAY; IPN thất bại đưa transaction sang Failed, Order sang PaymentFailed và release inventory. Hai assertion trên không chứng minh flow đó bị lỗi. Nhánh API Pending -> Cancelled vẫn tồn tại và chưa được sửa/chặn; không nối thao tác này vào FE nếu chưa xử lý đồng bộ cancellation. Backend MVP được chốt để chuyển sang FE, không có thay đổi runtime từ việc cập nhật tài liệu này.

## Trạng thái sau kiểm thử và giới hạn

- Giữ dữ liệu test để đối chiếu; store test đã chuyển IsActive=false và bag test thành Inactive để không tiếp tục bán trên catalog.
- Các transaction test Pending còn lại sẽ hết hạn theo worker hiện có. Không xóa/reset dữ liệu người dùng có sẵn hoặc purge queue.
- Đã kiểm tra queue trust/retry/DLQ hết message trước khi khôi phục consumer. API được khởi động lại sau thử nghiệm dependency.
- Script và state/result tạm được xóa sau khi tổng hợp vào báo cáo này.
- Chưa thử giao dịch VNPAY thật, nhiều instance service, broker crash/network partition, publisher-confirm failure hoặc quy trình DLQ/replay lỗi vĩnh viễn. Không đánh dấu hoàn tất toàn bộ bước 8 chỉ dựa trên lượt chạy này.

## Executed assertions

| Result | Assertion |
| --- | --- |
| PASS | Unauthenticated create rejected |
| PASS | Eligibility normal |
| PASS | Eligibility limited |
| PASS | Eligibility blocked |
| PASS | Eligibility expired |
| PASS | Eligibility missing |
| PASS | Direct order created |
| PASS | Server catalog price overrides client |
| PASS | Real Store consumer reserves and Payment creates checkout |
| PASS | Stock deducted exactly once |
| PASS | Invalid IPN signature rejected |
| PASS | Signed IPN wrong amount rejected |
| PASS | Return callback verifies without mutation |
| PASS | Signed successful IPN accepted |
| PASS | Real Order consumer confirms paid order |
| PASS | Real Identity consumer rewards 80 to 82 |
| PASS | Duplicate IPN idempotent |
| PASS | Reward counters and no pickup confirmation |
| PASS | Buyer cannot mark NoShow |
| PASS | Seller other store cannot mark NoShow |
| PASS | Seller cannot list other store orders |
| PASS | NoShow before deadline rejected |
| PASS | Generic status cannot set Confirmed |
| PASS | Generic status cannot set NoShow |
| PASS | Generic status cannot set Completed |
| PASS | Elapsed verified deadline exposes NoShow button |
| PASS | 8 concurrent NoShow requests idempotent |
| PASS | Real NoShow consumer applies 82 to 67 |
| PASS | Exactly one NoShow Outbox event |
| PASS | NoShow counter once and payment reward retained |
| PASS | NoShow ineligible for review |
| PASS | Blocked checkout returns policy 403 |
| PASS | Expired cooldown allows Limited recovery order |
| PASS | Limited first Pending allowed |
| PASS | Pending order cannot be NoShow |
| PASS | Pending quota enforced across stores |
| PASS | Cart API accepts test bag |
| PASS | Cart checkout shares Pending quota |
| PASS | Rejected checkout retains cart |
| PASS | Cancel Pending frees quota |
| PASS | Cart checkout succeeds after quota released |
| PASS | Successful checkout clears cart |
| PASS | Normal permits multiple Pending |
| PASS | 10 concurrent cross-store creates respect quota |
| PASS | Race persisted exactly one Pending |
| PASS | Normal checkout ready |
| PASS | Payment at score ceiling accepted |
| PASS | 100 ceiling still increments TotalOrders |
| PASS | Ceiling ledger records actual zero delta |
| PASS | Eligibility missing row remains read-only |
| PASS | Failed-payment test order created |
| PASS | Failed-payment checkout ready |
| PASS | Signed failed IPN accepted |
| PASS | Failed IPN updates order PaymentFailed |
| PASS | Failed payment releases reserved stock |
| PASS | Failed payment neither rewards nor penalizes |
| PASS | Expiry test order created |
| PASS | Expiry checkout ready |
| PASS | Actual expiration worker expires transaction |
| PASS | Expired payment releases stock |
| PASS | Late success IPN acknowledged |
| PASS | Late success becomes RefundPending |
| PASS | Late success creates pending refund |
| PASS | Expired and late-success payment never rewards |
| PASS | Low-score test order created |
| PASS | Low-score checkout ready |
| PASS | Low-score payment rewards 60 to 62 |
| PASS | Unverified legacy-style deadline cannot NoShow |
| PASS | NoShow accepts optional trimmed reason |
| PASS | Real NoShow 62 to 47 starts cooldown |
| PASS | New order denied after real NoShow |
| PASS | NoShow leaves payment successful |
| PASS | Synthetic NoShow routed to actual Identity queue |
| PASS | NoShow before payment moves to real TTL retry queue |
| PASS | Out-of-order NoShow has no premature penalty |
| PASS | Prerequisite payment event routed |
| PASS | Real TTL retry reapplies NoShow after payment with no lost event |
| PASS | Both out-of-order messages eventually recorded once |
| PASS | Same-message duplicate replay routed |
| PASS | New message ID same outcome consumed idempotently |
| PASS | Broker redelivery and business duplicate never double penalize |
| PASS | Late duplicate payment event reaches real Order consumer |
| PASS | Late payment cannot overwrite NoShow |
| PASS | Business duplicate payment does not reward again |
| PASS | Bag from another store rejected |
| PASS | Zero quantity rejected |
| PASS | Duplicate bag quantities aggregated before stock check |
| PASS | Non-overlapping pickup windows rejected |
| PASS | Two buyers created orders competing for last item |
| PASS | Actual Store consumer rejects one last-stock reservation |
| PASS | Last-stock race never makes inventory negative |
| PASS | Only reserved order gets payment transaction |
| PASS | Inventory failure and unpaid order never affect score |
| PASS | Identity offline returns dependency 503 |
| PASS | Existing paid order readable while Identity offline |
| PASS | IPN not blocked by Identity outage |
| PASS | Identity recovers after restart |
| PASS | Cancellation/payment race fixture created |
| PASS | Payment URL exists before cancellation |
| PASS | Buyer cancels pending order with existing payment URL |
| PASS | Paid-after-cancel IPN acknowledged |
| FAIL | Paid-after-cancel routes accepted money to RefundPending |
| FAIL | Cancelled order does not earn payment trust event |
| PASS | Malformed broker/payload MessageId replay is safely dead-lettered |
| PASS | Corrected payment business duplicate recorded by Identity |
| PASS | Validated duplicate leaves buyer score at 67 |
