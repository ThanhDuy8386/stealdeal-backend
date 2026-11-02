# Kế hoạch self-host observability cho StealDeal

Trạng thái: **P1 và phần vận hành P2 hoàn tất; bắt đầu P3 trên EC2 ứng dụng.** Phạm vi đợt đầu là log và metric. Tracing/Tempo và instrumentation OpenTelemetry trong ứng dụng để dành cho đợt sau.

## 1. Mục tiêu và hiện trạng

- Xem log của Identity, Notification, Store, Cart, Order, Payment, RabbitMQ và các container khác trên một giao diện, không cần SSH hoặc `docker logs` trên EC2 ứng dụng.
- Xem CPU, RAM, dung lượng đĩa của EC2 ứng dụng/observability; CPU và RAM theo container; nhận biết disk gần đầy hoặc exporter ngừng gửi dữ liệu.
- Giữ chi phí observability thấp khi hệ thống chỉ bật lúc demo/kiểm thử. Dữ liệu cũ chỉ cần giữ vài ngày.
- Repo hiện chạy Docker Compose trên **một EC2 ứng dụng tại `us-east-1`**; CI/CD qua ECR và SSM. Sáu API đã có Docker log rotation; bốn API dùng EF Core đã lọc SQL command thành công ở mức `Information`. Compose vẫn mặc định `ASPNETCORE_ENVIRONMENT=Development` nếu không được ghi đè.

## 2. Kiến trúc chọn cho đợt đầu

```text
EC2 ứng dụng (hiện có)                         EC2 observability (mới)
  .NET services -> Docker stdout -> Alloy ─────> Loki (logs, filesystem)
  node_exporter (host metrics) <─────────────── Prometheus (scrape)
  cAdvisor (container metrics) <─────────────── Prometheus (scrape)
                                                Grafana -> Loki + Prometheus
                                                Grafana UI qua SSM port forwarding
```

- **Alloy** chạy trên EC2 ứng dụng, dùng Docker discovery/`loki.source.docker` để đọc log; chỉ thêm `loki.process` nếu sau này cần lọc mẫu log nhiễu cụ thể. Gắn nhãn có số giá trị hữu hạn như `service`, `environment`, `host`; giữ `OrderId`/`MessageId` trong nội dung log, **không dùng chúng làm Loki labels**.
- **node_exporter** và **cAdvisor** chạy trên EC2 ứng dụng; Prometheus trên EC2 mới scrape qua private IP mỗi 30–60 giây. Một node_exporter nữa chạy trên EC2 observability để đo disk/RAM của chính máy lưu log. Prometheus cũng scrape chính nó. Đây là luồng pull đơn giản, không cần bật Prometheus remote-write receiver.
- **Loki monolithic**, **Prometheus single server**, **Grafana OSS** chạy bằng Docker Compose trên EC2 observability. Dùng named volumes/bind mounts trên gp3 EBS; filesystem store của Loki là đủ cho lab một node. Không triển khai Tempo, Mimir, S3, ALB hoặc NAT Gateway ở đợt đầu.
- Việc triển khai Alloy không cản trở dùng OpenTelemetry sau này: khi làm tracing, .NET dùng OTel SDK để gửi OTLP tới Alloy, rồi Alloy chuyển trace sang Tempo.

## 3. EC2, mạng và truy cập

| Hạng mục | Thiết kế ban đầu |
| --- | --- |
| Region/VPC | `us-east-1`, cùng VPC và ưu tiên cùng Availability Zone với EC2 ứng dụng |
| Instance | **`c7i-flex.large`** (2 vCPU, 4 GiB RAM, x86_64) theo lựa chọn của người vận hành; đủ RAM khởi đầu cho lab nhẹ, vẫn phải đo thực tế. Dòng Flex có CPU nền 40%, không dùng tùy chọn CPU credit Standard/Unlimited của T3. |
| Disk | **40 GiB gp3** làm root volume, đủ cho OS, Docker images và volumes của lab; 30 GiB có thể thử nếu đo được mức dùng thấp |
| Tài khoản máy | SSM Agent + IAM instance role tối thiểu cho SSM; không đặt AWS access key trong Compose |
| Kết nối Internet | Có outbound để cài Docker/pull images. Có thể dùng public subnet + public IPv4 nhưng **không mở inbound từ Internet**; tránh dựng NAT Gateway chỉ cho lab này |
| Truy cập Grafana | SSM port forwarding tới `localhost:3000`, tài khoản Grafana riêng, mật khẩu trong secret cục bộ không commit |

Security group:

1. EC2 observability: chỉ nhận TCP `3100` **từ security group EC2 ứng dụng** cho Alloy gửi log. Không mở công khai `3000`, `3100`, `9090`, `9100`, `8080` hay SSH.
2. EC2 ứng dụng: chỉ nhận TCP `9100` (node_exporter) và `8080` (cAdvisor) **từ security group EC2 observability**. Giữ các rule ứng dụng hiện có, rà soát rằng exporter không có rule mở `0.0.0.0/0`.
3. Cả hai instance có outbound cần thiết cho SSM, pull images và kết nối private trong VPC. Khi stop/start, private IP của instance được giữ; Prometheus vẫn phải kiểm tra target sau khi máy bật lại.

Alloy cần truy cập Docker socket trên EC2 ứng dụng; quyền này có thể điều khiển Docker daemon. Chỉ cấp socket cho agent tin cậy, không công khai cổng điều khiển Alloy. Nếu dùng cAdvisor container, giới hạn mount và quyền theo hướng dẫn của phiên bản được chọn.

## 4. Kiểm soát dữ liệu và dung lượng

### 4.1. Giảm log ngay tại nguồn

Luồng lưu log là **ứng dụng → stdout → file log Docker trên EC2 ứng dụng → Alloy → Loki trên EC2 observability**. Mỗi biện pháp tác động ở một điểm khác nhau:

| Biện pháp | Tác động lên EC2 ứng dụng | Tác động lên Loki |
| --- | --- | --- |
| Lọc EF Core command log tại ứng dụng | Không tạo log SQL thành công, giảm file Docker và I/O | Không ingest các dòng đó |
| Docker log rotation | Giới hạn log cục bộ theo container, kể cả khi lỗi lặp liên tục | Không giới hạn lượng log Loki ingest |
| Alloy `stage.drop` | Log vẫn được Docker ghi cho tới khi xoay vòng | Bỏ dòng trước khi gửi, giảm dung lượng Loki |
| Loki retention | Không giải phóng ổ EC2 ứng dụng | Xóa log đã lưu theo thời hạn |

1. Trên Identity, Store, Order và Payment (các service có worker quét outbox bằng EF Core), đặt riêng category `Microsoft.EntityFrameworkCore.Database.Command` ở mức `Warning`; vẫn giữ `Logging:LogLevel:Default=Information`. Hiện worker trả về khi batch rỗng, nhưng EF Core vẫn ghi `Executed DbCommand` ở mức `Information` cho câu query. Cấu hình này không thay đổi tần suất quét hoặc hành vi outbox; nó bỏ **mọi log EF Core SQL command thành công ở mức `Information` trong bốn API**, kể cả query từ request thông thường, không riêng query outbox. Cảnh báo/lỗi SQL và log nghiệp vụ vẫn xuất hiện. Notification cũng dùng EF Core nhưng chưa áp dụng filter này; log của SQL Server container là nguồn riêng. Bước triển khai đã chọn: thêm category vào `appsettings.json` của bốn API để CI/CD chỉ build/deploy bốn image tương ứng. Cấu hình này cũng áp dụng khi chạy local; nếu cần xem SQL query lúc debug local, override riêng category bằng biến môi trường `Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=Information`.
2. Giữ log `Information` cho các mốc nghiệp vụ có ý nghĩa: bắt đầu worker/consumer, batch outbox có message, publish thành công với `MessageId`/`RoutingKey`, consumer nhận/xử lý message, compensation; giữ `Warning`/`Error` cho retry, publish/consume thất bại và exception. Không log mỗi lần quét rỗng, không lọc theo từ `Outbox`, và không nâng toàn bộ ứng dụng lên `Warning`. Khi chỉnh code, dùng `ILogger` message templates với các trường ổn định (`OrderId`, `MessageId`, `EventType`, `RetryCount`); không ghi payload chứa secret/OTP. `DeliveryTag` chỉ hữu ích trong phạm vi channel RabbitMQ, không thay thế `MessageId` để đối chiếu giữa service.
3. Xác nhận `ASPNETCORE_ENVIRONMENT` thực trên EC2. Compose đang mặc định `Development`; có thể đặt biến log level ở runtime Compose trước khi đổi môi trường. Chỉ chuyển sang `Production` sau khi kiểm tra các khác biệt cấu hình ứng dụng.
4. Thêm Docker `json-file` log rotation cho sáu API, `max-size: 10m`, `max-file: 3`. Cấu hình chỉ áp dụng sau khi **recreate** container. Nhánh CD xử lý thay đổi Compose dùng `compose up -d --no-build` để Compose tự tạo lại những service có cấu hình thay đổi; không ép tạo lại RabbitMQ, SQL Server, Redis. Giữ driver tương thích với Alloy Docker source và kiểm thử `docker logs` vẫn hoạt động. Các container hạ tầng/frontend sẽ được xem xét rotation riêng sau khi xác nhận cách giữ dữ liệu và rollout của chúng.
5. Nếu còn log nhiễu sau khi lọc tại nguồn, dùng `loki.process stage.drop` để bỏ **đúng các dòng `Executed DbCommand` thành công** trước khi gửi Loki; đo `loki_process_dropped_lines_total`. Đây là phương án bổ sung cho Loki, không thay thế lọc tại ứng dụng hoặc Docker rotation. Không dùng allowlist chỉ giữ `Error`, vì sẽ mất các bước nghiệp vụ cần debug.

### 4.2. Retention tự động

| Nơi lưu | Thiết lập khởi đầu | Ghi chú |
| --- | --- | --- |
| Docker trên EC2 ứng dụng | `10m` × 3 file/container | Giới hạn bản log cục bộ; riêng Loki retention không giải phóng ổ ứng dụng |
| Loki trên EC2 mới | **3 ngày** (`retention_period: 72h`) | Bật Compactor/`retention_enabled`, `delete_request_store: filesystem`, TSDB index period 24h; xóa bất đồng bộ, không tức thì |
| Prometheus trên EC2 mới | **3 ngày hoặc 2 GB**, cấu hình trong `storage.tsdb.retention` của `prometheus.yml` | Dung lượng thực có thể vượt ngưỡng trong lúc WAL/compaction; đo rồi điều chỉnh |
| Grafana | Giữ dashboards/provisioning trên volume riêng | Không xóa Grafana volume khi muốn dọn telemetry |

Không xóa thủ công thư mục dữ liệu Loki/Prometheus khi chúng đang chạy. Đặt cảnh báo hoặc kiểm tra disk ở ngưỡng khoảng **70% và 85%**; Loki filesystem retention không tự phản ứng với ổ sắp đầy. Khi cả hai EC2 tắt, không có dữ liệu mới; job dọn dữ liệu sẽ tiếp tục khi observability EC2 bật. EBS tính theo **GiB đã cấp**, nên retention giữ ổ không đầy nhưng không tự hạ hóa đơn volume 40 GiB.

## 5. Các bước triển khai

### P0 — Đo và chốt chi phí trước khi tạo tài nguyên

- [ ] Xem **AWS Billing > Credits**: credit thực nhận, ngày hết hạn và tổng chi tiêu của tài khoản hiện tại. “Up to $200” không đồng nghĩa tài khoản đã có $200; credit dùng chung với EC2 ứng dụng/ECR/EBS và các tài nguyên khác.
- [ ] Kiểm tra EC2 ứng dụng đang dùng instance type, VPC/AZ, IAM/SSM và số giờ bật trung bình mỗi tháng. Đo `df -h` trên filesystem chứa Docker, `docker system df -v` cho image/cache và dung lượng file log Docker riêng; `docker system df` không cho biết ổ đĩa thực sự còn trống.
- [ ] Tạo AWS Budget cho **tổng tài khoản**, cảnh báo ở $25/$50/$100 trong tháng; gắn tag `Project=StealDeal`, `Purpose=Observability` cho tài nguyên mới. Budget là cảnh báo, **không phải giới hạn cứng**, và báo cáo chi phí có thể trễ.
- [ ] Đặt quy trình tắt EC2 observability sau mỗi buổi demo; kiểm tra cả EC2 ứng dụng đã tắt. Không mua Savings Plan/Reserved Instance cho workload không liên tục.

### P1 — Chuẩn hóa log ứng dụng

- [x] Đo baseline khi không có user: trong 2 phút, Identity/Store/Order mỗi service có 12 dòng `Executed DbCommand`, Payment có 16; `docker system df` không thể hiện file log, thư mục Docker containers hiện 5,3 MB, EC2 ứng dụng còn 34 GB trống trên ổ 48 GB. Payment có thể có thêm query từ worker khác ngoài outbox.
- [x] Đặt riêng EF Core command log mức `Warning` cho Identity, Store, Order, Payment; giữ `Default: Information` và `ILogger` hiện có, chưa cần thêm OTel SDK hay Serilog. Sau deploy, đo lại 2 phút không có user: cả bốn API đều `tong_dong=0, sql_command=0` (baseline lần lượt 12/12/12/16 dòng SQL).
- [x] Rà soát log nghiệp vụ publish, consume, retry, compensation: giữ mốc thành công/lỗi cần debug; thêm `MessageId` vào log nhận/xử lý/lỗi của consumer luồng order và `OrderId` vào log xử lý/lỗi khi đã parse được event. Outbox publish/lỗi đã ghi `MessageId` và retry count. Không log payload nhạy cảm, không dùng ID làm Loki label. Đợt đầu có thể tìm text; JSON structured logging là cải tiến tiếp theo.
- [x] Kiểm tra log options và volume RabbitMQ trên EC2: tất cả container dùng `json-file` với `options={}`; RabbitMQ có volume ẩn danh tại `/var/lib/rabbitmq`. Đổi nhánh CD `compose_changed` sang `compose up -d --no-build` để chỉ tạo lại service có cấu hình đổi; kiểm tra ID container hạ tầng trước/sau deploy.
- [x] Đặt Docker `json-file` rotation (`max-size: 10m`, `max-file: 3`) cho sáu API; sau CD, `docker inspect` xác nhận đủ sáu container có hai options này và ID của RabbitMQ/SQL Server/Redis giữ nguyên. Rotation giới hạn ổ EC2 ứng dụng, không giới hạn dung lượng Loki. Cân nhắc container hạ tầng/frontend trong bước riêng.
- [x] Thử flow thành công trên Docker: Order publish `order.created`, Store consume/xử lý rồi publish `inventory.reserved`, Payment consume/xử lý; `MessageId` khớp ở từng cặp publish/receive và Store/Payment có cùng `OrderId`. Log `Information` nghiệp vụ vẫn hiện.
- [x] Thử compensation khi hủy thanh toán: Payment publish `payment.failed` và `inventory.release_requested`; Order/Store nhận đúng `MessageId`, Store ghi đã trả tồn kho cho cùng `OrderId`. Đây là nhánh nghiệp vụ dự kiến, không phải kiểm thử log `Error` kỹ thuật.
- [x] Xác nhận trên ứng dụng đơn chuyển sang `PaymentFailed` và tồn kho được hoàn lại sau compensation (người vận hành xác nhận P1 hoàn tất).

Kiểm thử lỗi kỹ thuật `Warning`/`Error` và tìm lại flow qua Grafana/Loki thuộc P3, khi pipeline log đã chạy; không cố tình làm hỏng RabbitMQ/database ở P1 chỉ để tạo log lỗi. Chỉ thêm Alloy `stage.drop` nếu còn mẫu log nhiễu rõ ràng.

### P2 — Provision EC2 observability

- [x] Tạo `c7i-flex.large` với Ubuntu Server 26.04 x86_64, root volume 40 GiB (filesystem hiển thị 38 GiB), SSM hoạt động, cùng default VPC/subnet với EC2 ứng dụng; inbound TCP 3100 chỉ từ security group của EC2 ứng dụng. Loại volume gp3 và thông tin instance/SG ID sẽ đối chiếu trong bước bàn giao P4.
- [x] Cài Docker Engine/Compose trên EC2 observability; `docker run --rm hello-world` chạy thành công (người vận hành xác nhận).
- [x] Đưa cấu hình đã commit trên `duy-dev` (`af57bb5`) từ `src/Services/observability` lên `/opt/stealdeal/observability`; tạo `.env` với private IP và Grafana admin secret riêng trên EC2; `docker compose config --quiet` và pull image thành công.
- [x] Chạy Compose riêng cho Grafana OSS, Loki monolithic, Prometheus và node_exporter với volumes, health checks, restart policy. Grafana chỉ bind loopback và truy cập được qua SSM tunnel; Prometheus/Loki datasources đều Test thành công. Prometheus scrape cả ba target nội bộ (`up=1`) và node_exporter trả metric RAM/đĩa.
- [x] Reboot EC2 observability: Docker và các service tự chạy lại, Grafana/Loki endpoint vẫn sẵn sàng. Loki `/services` báo compactor `Running`; `/config` xác nhận retention 3 ngày, `retention_enabled: true`, `delete_request_store: filesystem`. Prometheus metric xác nhận retention 259200 giây và 2 GiB; root filesystem dùng 16% (32 GiB trống), RAM `available` khoảng 2,8 GiB. Xóa dữ liệu thật sau mốc retention thuộc P4.

Loki 3.7.0 trả HTTP 500 cho `/config?mode=diff` nhưng `/config` thường trả 200; không thấy ảnh hưởng tới readiness, compactor hoặc Grafana datasource. Ghi nhận để đối chiếu khi cập nhật bản vá, không coi riêng lỗi endpoint này là bằng chứng retention thất bại.

### P3 — Thu thập từ EC2 ứng dụng

- [x] Từ EC2 ứng dụng, gọi `http://172.31.16.59:3100/ready` qua private network thành công trước khi cài Alloy (người vận hành xác nhận).
- [x] Chạy Alloy riêng trên EC2 ứng dụng từ commit `77e7274`, với Docker discovery và `loki.source.docker`; chọn sáu API và RabbitMQ, thêm label `service`/`environment`/`host`, volume lưu vị trí đọc log và chỉ một agent trên host. `docker compose config` và `alloy validate` thành công, container `Up`; Grafana/Loki hiển thị log mới của một flow đặt hàng (người vận hành xác nhận).
- [x] Chạy node_exporter trên EC2 ứng dụng từ commit `6d7d33f`, bind `172.31.28.138:9100`, đo được `node_memory_MemAvailable_bytes` và `node_filesystem_avail_bytes` của host. Observability EC2 gọi được endpoint qua private network sau khi xử lý security group; cần rà soát rule thực tế không mở `9100` ra Internet.
- [x] Thêm target `application-node` vào Prometheus từ commit `aa7b844`, kiểm tra `promtool` thành công rồi recreate riêng Prometheus. Grafana Explore trả `up{job="application-node"}=1` và `node_memory_MemAvailable_bytes` khoảng 5,4 tỷ byte (người vận hành xác nhận); metric đĩa `/` đã được kiểm tra qua endpoint exporter.
- [x] Chạy cAdvisor `v0.60.6` trên EC2 ứng dụng từ commit `add04fc`, bind `172.31.28.138:8080`. Container healthy, Docker factory đăng ký thành công; endpoint local trả 15 series `container_memory_working_set_bytes` và 15 series `container_cpu_usage_seconds_total` với `name` theo container. Observability EC2 gọi `/metrics` được HTTP 200 qua private IP; cần rà soát rule inbound `8080` thực tế chỉ từ security group observability.
- [x] Thêm target `application-containers` vào Prometheus từ commit `d9e9211`; cấu hình Prometheus hợp lệ, container healthy, Grafana trả `up=1`, RAM container và CPU `order-api` theo `rate(...[5m])` (người vận hành xác nhận).
- [x] Provision dashboard Grafana **StealDeal / Infrastructure** từ commit `467cf06` cho CPU/RAM/disk host, CPU/RAM container và `up` của scrape targets; cả hai exporter `UP`, tất cả panel có dữ liệu (người vận hành xác nhận).
- [ ] Provision log view theo service, lọc được `OrderId`/`MessageId` và xem số dòng log theo service.
- [ ] Bổ sung quan sát tốc độ ingest và dung lượng thực tế Loki/Prometheus; phân biệt metric ứng dụng từ log với metric nội bộ của backend.
- [ ] Thử một flow order thành công và một lỗi publish/consumer có kiểm soát; tìm theo `OrderId`/`MessageId`. Tạm thời phân biệt thời gian EC2 ứng dụng **chủ động tắt** với sự cố exporter để tránh báo động giả.

### P4 — Kiểm thử vận hành và bàn giao

- [ ] Stop/start **cả hai EC2**, xác nhận private connectivity và log/metric mới xuất hiện lại; dữ liệu chưa hết hạn vẫn xem được khi bật EC2 observability.
- [ ] Chạy qua mốc retention bằng dữ liệu thử, xác nhận Loki/Prometheus dọn dữ liệu và disk không tăng mãi; kiểm tra Docker log rotation trên EC2 ứng dụng.
- [ ] Ghi lại instance ID/SG ID trong tài liệu vận hành, xác nhận volume là gp3 và tags/budget thực tế; lưu ảnh dashboard, sơ đồ, chi phí và quy trình SSM port forwarding trong README/portfolio. Dùng `docker compose config` để kiểm tra cấu hình trước mỗi lần deploy.
- [ ] Nếu muốn hoàn nguyên: dừng Alloy/exporters, dừng EC2 observability; chỉ xóa volume/instance sau khi đã quyết định không cần dữ liệu và dashboard cũ.

## 6. Dự toán và giới hạn chi phí

Giá **dự trù, cần xác nhận trên AWS Pricing Calculator/Launch Instance trước khi tạo** cho Linux/UNIX on-demand tại `us-east-1`: `c7i-flex.large` lấy tròn khoảng **$0.085/giờ** để giữ dư ngân sách; gp3 giả định **$0.08/GiB-tháng**; public IPv4 khoảng **$0.005/giờ khi được cấp**. Bảng dưới chỉ tính **EC2 observability mới**, 40 GiB gp3 tồn tại cả tháng, IPv4 tính trong giờ EC2 chạy, chưa tính snapshot, outbound data transfer, thuế hoặc tài nguyên hệ thống đã có. Ubuntu Server LTS bản thường dùng giá Linux/UNIX; Ubuntu Pro, Windows, RHEL/SUSE hoặc AMI Marketplace có thể có phí OS/software riêng.

| Instance | Thời gian chạy/tháng | Compute | EBS 40 GiB | Public IPv4 | Tổng gần đúng |
| --- | ---: | ---: | ---: | ---: | ---: |
| `c7i-flex.large` | 100 giờ | ~$8.50 | ~$3.20 | ~$0.50 | **~$12.20** |
| `c7i-flex.large` | 730 giờ (quên tắt) | ~$62.05 | ~$3.20 | ~$3.65 | **~$68.90** |

Mục tiêu vận hành: **≤100 giờ/tháng cho EC2 observability**, khoảng **$12–13/tháng** cho phần mới trong điều kiện trên. Sáu tháng ở mức 100 giờ/tháng sẽ khoảng **$73**, chừa phần credit cho EC2 ứng dụng và tài nguyên khác. Nếu quên tắt cả tháng, riêng EC2 mới có thể khoảng $69; vì thế không thể cam kết tổng tài khoản dưới $200 chỉ từ cấu hình này. Kiểm tra credit còn lại, AWS Cost Explorer/Budgets và thời gian bật thực tế hằng tuần. Sau khi chạy stack, đo `docker stats`, `free -h`, OOM events và độ trễ dashboard để xác nhận 4 GiB RAM đủ dùng.

Không thêm NAT Gateway, ALB, EKS, Amazon Managed Grafana, Amazon Managed Service for Prometheus hoặc S3 trong đợt đầu. Nếu cần truy cập Grafana công khai sau này, lập dự toán riêng cho DNS/TLS và bảo mật trước khi mở cổng.

## 7. Điều kiện hoàn thành đợt đầu

1. Xem được log từng service qua Grafana, bao gồm ít nhất một lỗi outbox/consumer, mà không đăng nhập EC2 ứng dụng.
2. Dashboard có metric host và container; khi EC2 ứng dụng chủ động tắt, dashboard thể hiện khoảng trống dữ liệu hợp lý.
3. SQL polling log giảm rõ rệt; log Docker tại nguồn có rotation; Loki và Prometheus tự dọn theo retention sau khi chạy lại.
4. Chỉ Grafana được truy cập qua SSM; endpoint lưu trữ/exporter không công khai. Chi phí thực tế và credit còn lại được kiểm tra sau buổi demo đầu tiên.

## Tài liệu tham khảo

- [AWS Free Tier và credit](https://aws.amazon.com/free/) · [C7i-flex specs](https://aws.amazon.com/ec2/instance-types/c7i/) · [EC2 On-Demand pricing](https://aws.amazon.com/ec2/pricing/on-demand/) · [EBS pricing](https://aws.amazon.com/ebs/pricing/) · [Public IPv4 pricing](https://aws.amazon.com/vpc/pricing/)
- [Alloy Docker log source](https://grafana.com/docs/alloy/latest/reference/components/loki/loki.source.docker/) · [Loki filesystem/retention](https://grafana.com/docs/loki/latest/operations/storage/filesystem/) · [Prometheus storage](https://prometheus.io/docs/prometheus/latest/storage/)
- [Docker logging drivers và rotation](https://docs.docker.com/engine/logging/configure/) · [EF Core logging categories](https://learn.microsoft.com/ef/core/logging-events-diagnostics/simple-logging) · [Alloy log filtering](https://grafana.com/docs/alloy/latest/tutorials/send-logs-to-loki/) · [AWS Billing Free/Paid plans](https://docs.aws.amazon.com/awsaccountbilling/latest/aboutv2/free-tier-plans.html)
