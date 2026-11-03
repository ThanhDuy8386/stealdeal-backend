# StealDeal — tài liệu observability đã triển khai

> Trạng thái ghi nhận: 03/11/2026. Đây là tài liệu **as-built** cho MVP log và metric, không còn là checklist triển khai. Cấu hình được version-control trong `src/Services/observability/`; `.env`, mật khẩu và dữ liệu telemetry chỉ nằm trên EC2. Các nhận xét về trạng thái EC2 dựa trên kết quả người vận hành đã xác nhận, không thay thế một lần audit AWS hiện tại.

## 1. Phạm vi và bức tranh tổng thể

Mục tiêu đã đạt: xem log theo service, tìm log bằng `OrderId`/`MessageId` và xem CPU/RAM/disk của hai EC2 cùng CPU/RAM của container ứng dụng trên Grafana. Không cần vào Docker trên EC2 ứng dụng để xem các dữ liệu đã thu thập. Đây là **log aggregation + infrastructure/container metrics**, chưa phải distributed tracing hay giám sát nghiệp vụ đầy đủ.

```text
EC2 ứng dụng (172.31.28.138)                    EC2 observability (172.31.16.59)
  6 .NET API + RabbitMQ ── Docker logs ── Alloy ──push──> Loki ──┐
  node_exporter :9100 <────────────── scrape ── Prometheus ───────┤
  cAdvisor      :8080 <────────────── scrape ── Prometheus ───────┤
                                                node_exporter ─────┤
                                                Grafana <───────────┘
                                                UI :3000 qua SSM tunnel
```

Hai luồng độc lập:

- **Log là push:** ứng dụng ghi stdout/stderr → Docker `json-file` trên EC2 ứng dụng → Grafana Alloy đọc qua Docker socket → đẩy tới Loki trên EC2 observability → Grafana truy vấn Loki.
- **Metric là pull:** node_exporter/cAdvisor chỉ mở endpoint `/metrics`; Prometheus trên EC2 observability chủ động scrape mỗi **30 giây**, lưu time series; Grafana truy vấn Prometheus. Alloy **không chuyển metric** trong cấu hình hiện tại.

Backend, app-agent và observability là **ba Docker Compose project riêng**. Backend không gọi Grafana/Loki/Prometheus để xử lý request hay Saga; EC2 ứng dụng có thể chạy khi EC2 observability tắt, nhưng sẽ có khoảng trống telemetry.

## 2. Bản đồ cấu hình và nơi chạy

| Thành phần | Vai trò và phiên bản trong repo | File nguồn | Nơi chạy |
| --- | --- | --- | --- |
| Backend APIs, RabbitMQ, SQL Server, Redis | Ứng dụng; Alloy chỉ chọn log sáu API + RabbitMQ từ Compose project `stealdeal-services` | `src/Services/docker-compose.yml` | EC2 ứng dụng, `/opt/stealdeal/stealdeal-backend/src/Services` |
| Alloy `v1.21.3` | Đọc log Docker, gắn label, push tới Loki | `src/Services/observability/app-agent/compose.yml` và `config.alloy` cùng thư mục | EC2 ứng dụng, `/opt/stealdeal/app-observability` |
| node_exporter `v1.12.1` | Metric CPU/RAM/filesystem của host | `src/Services/observability/app-agent/compose.yml` và `src/Services/observability/compose.yml` | Một instance trên **mỗi EC2** |
| cAdvisor `v0.60.6` | Metric CPU/RAM theo container Docker | `src/Services/observability/app-agent/compose.yml` | EC2 ứng dụng |
| Loki `3.7.0` | Nhận, index và lưu log; single-binary, filesystem | `src/Services/observability/compose.yml` và `loki/loki.yml` cùng thư mục | EC2 observability, `/opt/stealdeal/observability` |
| Prometheus `3.15.1` | Scrape và lưu metric | `src/Services/observability/compose.yml` và `prometheus/prometheus.yml` cùng thư mục | EC2 observability |
| Grafana OSS `13.3.0` | UI, datasource và dashboard | `src/Services/observability/compose.yml` và `grafana/provisioning/` cùng thư mục | EC2 observability |

Các đường dẫn file trong bảng là tương đối với root repo. Trên EC2 observability, checkout Git dùng để lấy file trước đây ở `/opt/stealdeal/source`; **Compose chạy từ `/opt/stealdeal/observability`**, không chạy trực tiếp từ checkout. Trên EC2 ứng dụng, agent cũng chạy từ thư mục riêng, không phải một service trong backend Compose. File trong repo là nguồn để tái triển khai, không tự đồng bộ xuống EC2 khi merge Git.

### File cấu hình cục bộ, không commit

- EC2 ứng dụng: `/opt/stealdeal/app-observability/.env` có `APP_PRIVATE_IP=172.31.28.138` và `LOKI_PUSH_URL=http://172.31.16.59:3100/loki/api/v1/push`. Mẫu: `app-agent/.env.example`.
- EC2 observability: `/opt/stealdeal/observability/.env` có `OBS_PRIVATE_IP=172.31.16.59`. Mẫu: `observability/.env.example`. Mật khẩu Grafana nằm ở `secrets/grafana_admin_password`, được Compose mount làm file-backed secret; không đưa mật khẩu vào Git hoặc chat.
- Hai private IP trên là của **hai instance hiện tại**. Stop/start chính instance giữ primary private IP; **thay instance/account** cần cập nhật `.env`, URL Alloy, Prometheus targets và security group.

## 3. Log: tạo, thu thập, tìm kiếm

### 3.1. Chuẩn hóa tại .NET và Docker

Identity, Store, Order và Payment đặt category `Microsoft.EntityFrameworkCore.Database.Command=Warning` trong `*.API/appsettings.json`; `Default=Information` vẫn giữ. Điều này bỏ các dòng EF Core `Executed DbCommand` thành công ở mức Information, **không chỉ query outbox**, nhưng vẫn giữ warning/error SQL và log nghiệp vụ. Tần suất worker quét bảng không thay đổi. Đo khi idle trong 2 phút trước thay đổi: Identity/Store/Order mỗi service 12 dòng SQL, Payment 16; sau thay đổi cả bốn trả `0` dòng trong cửa sổ đo. Notification và log của SQL Server container không nằm trong filter này. Nếu cần xem SQL lúc debug local, override `Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=Information` cho đúng service.

Sáu API trong backend Compose dùng `json-file` với `max-size: 10m`, `max-file: 3`; cấu hình này đã được kiểm tra trên container sau deploy. Docker root đã quan sát trên EC2 ứng dụng là `/var/lib/docker`; log JSON nằm dưới `containers/` trong root đó. Giới hạn này bảo vệ **đĩa EC2 ứng dụng**, không giới hạn dữ liệu Loki. Các container Alloy/exporter và stack observability cũng có rotation tương tự trong Compose riêng. **RabbitMQ, SQL Server, Redis và frontend/nginx không được bao phủ bởi rule rotation sáu API** trong backend Compose; không suy diễn rằng toàn bộ Docker logs đã có giới hạn.

Log nghiệp vụ còn lại ghi các mốc publish/receive/process/compensation và lỗi. Outbox tạo `IntegrationMessage.MessageId` từ `OutboxMessage.Id`; RabbitMQ publisher đưa ID đó vào AMQP `BasicProperties.MessageId`, consumer log lại ID nhận được. Mỗi message/event mới có `MessageId` riêng; `OrderId` nối các message thuộc cùng đơn khi log tương ứng có trường này. `DeliveryTag` chỉ gắn với delivery trên RabbitMQ channel, **không** phải ID xuyên service. Đây là cách tương quan log bằng text, chưa phải `trace_id`/`span_id`.

Flow đã kiểm tra: `order.created` → `inventory.reserved`; khi hủy thanh toán, Payment publish `payment.failed` và `inventory.release_requested`, Order nhận trạng thái thất bại, Store trả tồn kho cho cùng `OrderId`. Không cố ý tạo lỗi kỹ thuật RabbitMQ/database chỉ để kiểm tra dashboard.

### 3.2. Alloy → Loki

`config.alloy` dùng `discovery.docker`, giữ **Compose project `stealdeal-services`** và chính xác các service `identity-api`, `notification-api`, `store-api`, `cart-api`, `order-api`, `payment-api`, `rabbitmq`. `loki.source.docker` đọc stdout/stderr, thêm các label giá trị thấp `service`, `environment=capstone`, `host=application-ec2`, rồi `loki.write` push tới URL trong `.env`. Không có `loki.process stage.drop`: hiện **không lọc thêm log theo pattern tại Alloy**. `OrderId`/`MessageId` nằm trong nội dung log, không là Loki label để tránh cardinality cao.

Alloy mount `/var/run/docker.sock:ro` và chạy root; `:ro` không làm Docker API thành read-only về mặt quyền điều khiển. Chỉ một Alloy agent với cấu hình này nên chạy trên host để tránh ingest trùng. Named volume `alloy_data` giữ vị trí đọc Docker log qua restart, **không phải** kho lưu vô hạn các log chưa gửi. `loki.write` hiện không bật WAL và dùng retry hữu hạn mặc định; nếu Loki tắt lâu, log có thể bị drop. Docker rotation cũng có thể xóa bản cục bộ trước khi thu lại.

### 3.3. Loki và dashboard log

Loki chạy một instance, `auth_enabled: false`, TSDB schema `v13`, index period `24h`, object store `filesystem`, replication factor `1`, ring in-memory. Dữ liệu nằm trong named volume `loki_data` mount `/loki`. `limits_config.retention_period=72h`; compactor bật retention với `delete_request_store: filesystem`. Retention xóa bất đồng bộ, **không phải giới hạn dung lượng cứng** và chưa được kiểm chứng bằng một chu kỳ 72 giờ thực tế. Không xóa tay thư mục dữ liệu khi Loki đang chạy.

Khi kiểm tra trên EC2, Loki `/services` báo compactor `Running`; `/config` trả HTTP 200 và hiện retention đã bật. Riêng `/config?mode=diff` từng trả HTTP 500 trong Loki 3.7.0; readiness, datasource và truy vấn log vẫn hoạt động. Ghi nhận để đối chiếu khi nâng phiên bản, không lấy riêng endpoint này làm bằng chứng retention thất bại.

Grafana datasource Loki có UID `loki`, URL nội bộ `http://loki:3100`. Dashboard **StealDeal / Service Logs** (file `grafana/provisioning/dashboards/json/service-logs.json`) có dropdown tĩnh cho sáu API và RabbitMQ, ô regex text mặc định `.*`, panel log và biểu đồ `count_over_time(...[5m])` theo service. Chọn `All` để xem xuyên service; dán `OrderId` hoặc `MessageId` vào ô text để tìm. Biểu đồ đếm **không chịu bộ lọc text**, chỉ chịu bộ lọc service; đó là số dòng được query từ Loki, không phải metric ingest/storage nội bộ. Người vận hành đã tìm được log bằng cả hai loại ID. Khi thêm service mới, phải cập nhật **cả filter Alloy và danh sách tĩnh dashboard**. Ví dụ truy vấn trong Explore: `{environment="capstone",service="order-api"} |= "<OrderId>"`.

## 4. Metric: exporters, Prometheus, Grafana

### 4.1. Exporters và scrape targets

node_exporter trên EC2 ứng dụng dùng host network/PID, mount host root read-only và bind `172.31.28.138:9100`. cAdvisor bind `172.31.28.138:8080`, chạy `privileged`, mount host/Docker paths read-only, dùng `--docker_only=true`, `--store_container_labels=false` và housekeeping `30s`. Nó cung cấp metric container như `container_cpu_usage_seconds_total` và `container_memory_working_set_bytes`; `name!=""` bỏ root cgroup trong truy vấn. cAdvisor filesystem metric **không được coi là số byte Docker JSON log của từng container**.

Prometheus có `scrape_interval=30s`, `evaluation_interval=30s` và đúng năm job:

| Job | Target | Nội dung |
| --- | --- | --- |
| `prometheus` | `prometheus:9090` | Metric bản thân Prometheus |
| `loki` | `loki:3100` | Metric nội bộ Loki; chưa có dashboard chuyên sâu |
| `observability-node` | `node-exporter:9100` | CPU/RAM/disk EC2 observability |
| `application-node` | `172.31.28.138:9100` | CPU/RAM/disk EC2 ứng dụng |
| `application-containers` | `172.31.28.138:8080` | CPU/RAM theo container EC2 ứng dụng |

`up=1` nghĩa là lần scrape target thành công, **không chứng minh ứng dụng đang xử lý nghiệp vụ đúng**. Khi EC2 ứng dụng cố ý tắt, `up=0` cho hai job application là bình thường. Nếu Prometheus/EC2 observability tắt thì không có samples mới; quãng trống đó không được tự backfill. Chưa instrument .NET HTTP request, RabbitMQ depth, outbox latency hay business metrics. Ví dụ kiểm tra trong Explore: `up{job="application-containers"}` hoặc `container_memory_working_set_bytes{job="application-containers",name="stealdeal-services-order-api-1"}`.

### 4.2. Lưu trữ và dashboard metric

Prometheus dùng named volume `prometheus_data` mount `/prometheus`, retention **3 ngày hoặc 2 GB** theo `storage.tsdb.retention` (đạt ngưỡng trước thì dọn block trước). WAL/head/compaction có thể làm dung lượng thực vượt 2 GB tạm thời; đây không phải hard quota của EBS. Grafana datasource Prometheus UID `prometheus`, URL nội bộ `http://prometheus:9090`, là datasource mặc định.

Dashboard **StealDeal / Infrastructure** (`grafana/provisioning/dashboards/json/infrastructure.json`) hiển thị `up` của hai exporter trên EC2 ứng dụng; RAM và root disk còn trống của EC2 ứng dụng; CPU/RAM/root disk sử dụng của cả hai host; CPU và working-set RAM theo container. Container CPU dùng `100 * rate(container_cpu_usage_seconds_total[5m])`: **100% tương đương một CPU core đầy tải**, có thể hơn 100% nếu dùng nhiều core. `rate(...[5m])` cần vài phút mẫu sau khi bật máy. Người vận hành đã xác nhận hai exporter `UP` và tất cả panel hạ tầng có data.

## 5. Grafana, volumes và truy cập mạng

Grafana provision hai datasource từ `grafana/provisioning/datasources/datasources.yml` và hai dashboard vào folder `StealDeal` từ provider `grafana/provisioning/dashboards/dashboards.yml`. Provider scan file mỗi **30 giây**, `allowUiUpdates=false`: sửa dashboard JSON trong repo rồi triển khai file, không sửa UI và kỳ vọng thay đổi được lưu ngược về Git. `grafana_data` giữ state/tài khoản; không xóa volume khi đổi dashboard. Tài khoản `admin` lấy mật khẩu khởi tạo qua secret file; Grafana không bật anonymous access hay self sign-up.

| Cổng | Nơi bind theo Compose | Chiều truy cập cần thiết |
| --- | --- | --- |
| `3000` Grafana | `127.0.0.1` trên EC2 observability | Người vận hành dùng **SSM port forwarding**; không mở inbound Internet |
| `3100` Loki | `172.31.16.59` trên EC2 observability | Chỉ Alloy từ SG EC2 ứng dụng |
| `9090` Prometheus | Chỉ mạng Docker của project observability | Grafana và Prometheus nội bộ; không publish host port |
| `9100` node_exporter observability | Chỉ mạng Docker của project observability | Prometheus nội bộ |
| `9100` node_exporter ứng dụng | `172.31.28.138` qua host network | Chỉ SG EC2 observability |
| `8080` cAdvisor ứng dụng | `172.31.28.138` | Chỉ SG EC2 observability |

Hai EC2 ở cùng default VPC/subnet theo lúc triển khai. Loki, Prometheus và exporters không có lớp xác thực riêng trên các endpoint `/metrics`/push nội bộ; **SG là ranh giới bảo vệ quan trọng**. Grafana UI có đăng nhập riêng và chỉ bind loopback. Inbound `3100` từ SG ứng dụng đã được xác nhận lúc dựng EC2 observability. EC2 observability đã kết nối được `9100` và `8080`; tuy nhiên **source SG chính xác của hai inbound rule trên EC2 ứng dụng chưa được audit lại sau khi thêm rule**, cần xác nhận không có `0.0.0.0/0` hoặc `::/0`. cAdvisor có quyền `privileged`; Alloy có Docker socket, nên không mở các endpoint/agent UI ra public.

EC2 observability dùng `c7i-flex.large`, Ubuntu 26.04, root EBS 40 GiB (filesystem hiển thị khoảng 38 GiB). Grafana, Loki, Prometheus có named volumes trên EBS; bản config là bind mount từ thư mục triển khai. EC2 có SSM; Grafana chỉ xem qua tunnel `localhost:3000`. Khi EC2 `stopped`, compute ngừng tính nhưng EBS vẫn tính theo dung lượng cấp; retention không tự hạ kích thước volume. Kiểm tra `df -h /`, `docker system df -v` và chi phí AWS định kỳ; chưa có alert disk tự động.

## 6. Vận hành bật/tắt và kiểm tra nhanh

| Trạng thái | Ứng dụng | Telemetry |
| --- | --- | --- |
| Cả hai EC2 bật | Backend và ba agent/exporter chạy | Grafana có log/metric mới |
| Chỉ EC2 ứng dụng bật | Backend vẫn chạy theo cấu hình hiện tại, không phụ thuộc Loki/Prometheus | Alloy không gửi được log, có thể drop sau retry; không có Prometheus samples trong khoảng này. `docker logs` cục bộ còn trong giới hạn rotation. |
| Chỉ EC2 observability bật | Không có backend mới trên EC2 ứng dụng | Grafana vẫn xem dữ liệu cũ chưa hết retention; hai application target có thể `up=0` đúng dự kiến. |
| Cả hai EC2 tắt | Không phục vụ request | Không thu/hiển thị telemetry mới; EBS vẫn tồn tại và tính phí. |

Khi cần đầy đủ quan sát, **bật observability trước, rồi ứng dụng**. Khi tắt sau demo, ngừng tạo traffic và xác nhận log cuối đã tới Grafana **khi cả hai EC2 còn chạy**, sau đó tắt ứng dụng rồi tắt observability. Nếu chỉ cần chạy ứng dụng, có thể để observability tắt và chấp nhận thiếu log/metric của khoảng đó. Stop EC2 bằng AWS Console/API, **không cần** `docker compose stop`/`down` trước đó. Các service liên tục trong ba project dùng `restart: unless-stopped`: sẽ tự lên khi Docker daemon boot nếu trước đó không bị dừng thủ công. `database-migrator` và `loki-volume-init` là job một lần, không cần ở trạng thái Up. EC2 observability đã được kiểm tra reboot và tự khôi phục; **stop/start cả hai EC2 cùng nhau chưa được xác nhận thực tế**. Frontend/nginx nằm ở project khác, restart policy không được xác minh trong tài liệu này.

Sau khi bật, mở **SSM tunnel mới** cho Grafana và kiểm tra:

```bash
# Trên từng EC2
sudo systemctl is-active docker
sudo systemctl is-enabled docker
sudo docker ps --format '{{.Names}} {{.Status}}'

# Chỉ trên EC2 ứng dụng
cd /opt/stealdeal/app-observability
sudo docker compose ps

# Chỉ trên EC2 observability
cd /opt/stealdeal/observability
sudo docker compose ps
curl -fsS http://127.0.0.1:3000/api/health
curl -fsS http://172.31.16.59:3100/ready
df -h /
```

Trong Grafana: kiểm tra **StealDeal / Infrastructure** (`UP`, host/container charts) và **StealDeal / Service Logs** bằng một log mới. Nếu chỉ vừa khởi động, đợi ít nhất vài phút cho biểu đồ CPU `rate[5m]`. Nếu đã **manually** chạy `docker compose stop`, restart policy `unless-stopped` không tự bật lại container đó; nếu chạy `down`, container đã bị xóa. Khi ấy cần `docker compose config --quiet` rồi `docker compose up -d --no-build` trong đúng project. Backend có secret/env và image tag do CD quản lý, nên không chạy backend Compose thiếu các env-file mà CD sử dụng. Khi thay mới instance chứ không phải stop/start, rà soát lại private IP, SG, `.env` và Prometheus scrape targets.

## 7. Triển khai thay đổi và CI/CD

Hiện observability được **deploy thủ công** từ source trong repo lên hai EC2. Backend CI chỉ tự chạy khi push `main`; workflow nhận diện thay đổi service/backend Compose để build ECR image. Backend CD sau CI thành công có thể vẫn chạy SSM đồng bộ secret/kiểm tra backend config đối với commit chỉ đổi tài liệu/observability, nhưng **không deploy** `app-agent/` hoặc stack observability. Merge cấu hình vào `main` là để lưu source of truth, không tự thay dashboard đang chạy. Không cần sửa CI/CD cho MVP hiện tại.

Khi sửa trong tương lai: chỉnh file tương ứng trong repo → review/commit → lấy đúng commit trên đúng EC2 → cập nhật **thư mục triển khai** → `docker compose config --quiet` và validate chuyên biệt nếu có → recreate **chỉ service liên quan** → kiểm tra Grafana/endpoint. Không ghi đè `.env`, Grafana secret hay named volumes. README ở `src/Services/observability/README.md` và `app-agent/README.md` chứa lệnh triển khai/validate theo từng project. Nếu cập nhật dashboard, provider có thể scan file sau 30 giây; cách triển khai đã dùng là recreate riêng Grafana để xác nhận load. Nếu đổi target Prometheus, kiểm tra cấu hình trước khi recreate riêng Prometheus. Nếu đổi Alloy, chạy `alloy validate` trước khi recreate Alloy. Nếu đổi Docker log options của API, cần recreate API qua backend CD.

## 8. Những gì chưa có và điểm cần kiểm tra tiếp

- **Chưa có tracing:** không có OTel SDK trong flow hiện tại, chưa propagate `traceparent` qua RabbitMQ, chưa có Tempo/trace dashboard. `MessageId` và `OrderId` chỉ hỗ trợ tìm log, không thay thế trace/span ID. Khi bổ sung tracing cần thiết kế propagation qua publish/consume và compensation, OTel instrumentation, collector/receiver/exporter và trace backend riêng; Alloy đang chạy **không** có OTLP receiver.
- **Chưa có alerting** và dashboard ingest/storage nội bộ Loki–Prometheus. Dashboard log-line count không phải số byte ghi xuống đĩa. Host root-disk panel và kiểm tra `df`/Docker volume là cách theo dõi dung lượng hiện tại.
- **Chưa kiểm chứng** retention bằng dữ liệu vượt 72 giờ, full EC2 stop/start, số giờ/chi phí billing thực, hay một lỗi kỹ thuật publish/consumer cố ý tạo. Đừng ghi các mục này là đã test trong CV/README.
- **Cần rà soát SG** của port `9100`/`8080` và giới hạn log của các container ngoài sáu API nếu vận hành lâu dài. Không xóa named volumes Loki/Prometheus/Grafana để “dọn” khi chưa quyết định mất dữ liệu cũ.
- Nếu chuyển sang account/instance khác, repo đủ cấu hình ứng dụng nhưng **không tự tạo EC2, SG, Docker, SSM role, `.env`, secret hay volumes có dữ liệu**. Cần bổ sung IaC (ví dụ CloudFormation) hoặc runbook bootstrap riêng.

## Tài liệu tham khảo vận hành

- [Docker restart policy](https://docs.docker.com/engine/containers/start-containers-automatically/) · [AWS EC2 stop/start, private IP và EBS](https://docs.aws.amazon.com/AWSEC2/latest/UserGuide/ec2-instance-lifecycle.html)
- [Alloy Docker source/positions](https://grafana.com/docs/alloy/latest/reference/components/loki/loki.source.docker/) · [Alloy `loki.write` retry/WAL](https://grafana.com/docs/alloy/latest/reference/components/loki/loki.write/)
- [Loki filesystem và retention](https://grafana.com/docs/loki/latest/operations/storage/filesystem/) · [Prometheus storage](https://prometheus.io/docs/prometheus/latest/storage/) · [Grafana file provisioning](https://grafana.com/docs/grafana/latest/administration/provisioning/)
