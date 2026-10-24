# CI/CD Config Sync and SSL Implementation Plan

## Trạng thái hiện tại

- Phase 0, 1, 2: hoàn thành.
- Phase 3: hoàn thành cho domain `stealdeal.store`.
- HTTPS hoạt động tại Nginx public entrypoint.
- VNPAY IPN/return URL đã chạy qua HTTPS.
- Certificate renewal: chưa tự động hóa, ghi chú ở cuối file.

## Mục tiêu

- Application code tiếp tục build thành Docker image và deploy qua ECR.
- Thay đổi `docker-compose.yml` hoặc `nginx.conf` được đồng bộ qua SSM từ đúng commit SHA.
- Runtime `.env`, certificate và dữ liệu persistent không bị Git checkout ghi đè.
- Nginx là public entrypoint duy nhất cho frontend và các API.

## Phase 0: EC2 bootstrap — đã hoàn thành

EC2 có clone của hai repository tại:

```text
/opt/stealdeal/stealdeal-backend
/opt/stealdeal/steal-deals-web
```

Các điều kiện nền tảng:

- Repository có remote `origin` đúng và có thể fetch từ GitHub.
- Working tree sạch trước khi CD checkout commit mới.
- SSM user có thể chạy Git/Docker cần thiết.
- Runtime `.env`, certificate và thư mục persistent nằm ngoài vùng config được Git quản lý.

## Phase 1: Backend CI/CD — đã hoàn thành

- Backend CI detect source change và `src/Services/docker-compose.yml` change.
- Source change tạo/push image mới lên ECR.
- Compose-only change không build image mới.
- Backend CD chỉ fetch/checkout repository khi Compose cần đồng bộ.
- SSM checkout đúng `DEPLOY_SHA`, validate Compose rồi mới recreate service.
- Deployment fail nếu working tree dirty, commit không tồn tại, `docker compose config` lỗi hoặc health check fail.

## Phase 2: Frontend CI/CD và config sync — đã hoàn thành

- Frontend source change tạo/push image mới.
- `docker-compose.yml` và `nginx.conf` được detect độc lập.
- Config-only change không build frontend image.
- Frontend CD fetch/checkout đúng `DEPLOY_SHA` qua SSM khi Compose hoặc Nginx thay đổi.
- Compose được validate bằng `docker compose config --quiet`.
- Nginx được validate bằng `nginx -t` trước khi recreate/reload.
- `FRONTEND_IMAGE`, certificate path và các runtime value persistent được giữ ngoài Git checkout.

## Phase 3: SSL cho `stealdeal.store` — đã hoàn thành

### 1. Điều kiện trước khi cấp certificate

- DNS A record của `stealdeal.store` trỏ đến Elastic IP của EC2.
- Security Group mở TCP 80 và 443.
- Các service backend, database, Redis và RabbitMQ không public trực tiếp.
- Nginx, frontend và backend cùng tham gia external Docker network `stealdeal-network`.

### 2. Thư mục persistent trên EC2

Certificate không nằm trong repository và không được commit lên Git:

```text
/opt/stealdeal/certbot/www
/opt/stealdeal/certbot/conf
```

Vai trò:

- `www`: chứa file HTTP-01 ACME challenge.
- `conf`: chứa certificate, private key và metadata của Certbot.

Nginx mount hai thư mục này read-only:

```yaml
volumes:
  - ${CERTBOT_WWW_DIR}:/var/www/certbot:ro
  - ${CERTBOT_CONF_DIR}:/etc/letsencrypt:ro
```

Runtime `.env` trên EC2 giữ:

```env
CERTBOT_WWW_DIR=/opt/stealdeal/certbot/www
CERTBOT_CONF_DIR=/opt/stealdeal/certbot/conf
```

### 3. HTTP và HTTPS routing của Nginx

Nginx có ba nhiệm vụ chính:

1. Port 80 public phục vụ ACME challenge và redirect traffic thông thường sang HTTPS.
2. Port 443 public phục vụ frontend và proxy các route `/api/...` tới backend service tương ứng.
3. Port 80 với `server_name nginx` phục vụ request SSR nội bộ từ frontend container.

SSR dùng:

```dockerfile
ENV SERVER_API_BASE_URL="http://nginx"
```

Điều này đúng vì HTTPS được terminate tại public Nginx. Giao tiếp frontend container → Nginx nội bộ dùng HTTP và không phải Mixed Content.

Không dùng `https://nginx`: certificate chỉ hợp lệ cho `stealdeal.store`, không hợp lệ cho hostname Docker `nginx`.

Internal Nginx server phải proxy trực tiếp các route API, không redirect sang HTTPS. Nếu không, SSR sẽ bị redirect thành `https://nginx/...` và Node báo `ERR_TLS_CERT_ALTNAME_INVALID`.

Browser production dùng URL tương đối:

```text
/api/identity/...
/api/store/...
/api/payment/...
```

Do đó browser đi qua `https://stealdeal.store`, còn SSR đi qua `http://nginx` trong Docker network.

### 4. Cấp certificate lần đầu

Flow đã thực hiện:

1. Tạo hai thư mục persistent trên EC2.
2. Deploy Nginx có location:

   ```nginx
   location ^~ /.well-known/acme-challenge/ {
       root /var/www/certbot;
       try_files $uri =404;
   }
   ```

3. Tạo file test trong `www/.well-known/acme-challenge/` và xác nhận truy cập được bằng HTTP 200.
4. Chạy Certbot webroot để cấp certificate cho `stealdeal.store`.
5. Mount certificate vào Nginx và bật server HTTPS.
6. Validate bằng `nginx -t`, recreate/reload Nginx và kiểm tra domain.

Nginx dùng stable path:

```nginx
ssl_certificate /etc/letsencrypt/live/stealdeal.store/fullchain.pem;
ssl_certificate_key /etc/letsencrypt/live/stealdeal.store/privkey.pem;
```

Các file trong `live/` là symlink tới `archive/`; đây là cấu trúc bình thường của Certbot và không phải mismatch tên file.

### 5. VNPAY qua HTTPS

Public IPN URL:

```text
https://stealdeal.store/api/payment/vnpay/ipn
```

Public return URL:

```text
https://stealdeal.store/payment/vnpay-return
```

VNPAY chỉ gọi public URL. Nginx tự proxy request IPN tới:

```text
payment-api:5155
```

Không cần expose payment service hoặc dùng Docker hostname trong VNPAY Dashboard.

### 6. Validation quan trọng

Kiểm tra Compose và Nginx:

```bash
sudo docker compose config --quiet
sudo docker compose run --rm --no-deps nginx nginx -t
```

Kiểm tra HTTP redirect và ACME path:

```bash
curl -I http://stealdeal.store/
curl -i http://stealdeal.store/.well-known/acme-challenge/test
```

Kiểm tra HTTPS và certificate:

```bash
curl -I https://stealdeal.store/

sudo openssl x509 \
  -in /opt/stealdeal/certbot/conf/live/stealdeal.store/fullchain.pem \
  -noout -subject -issuer -dates
```

Kiểm tra SSR gọi được API nội bộ:

```bash
sudo docker compose exec frontend \
  wget -qO- http://nginx/api/store/categories
```

Kiểm tra runtime URL:

```bash
sudo docker exec stealdeal-frontend \
  printenv SERVER_API_BASE_URL
```

Kết quả mong đợi:

```text
http://nginx
```

Sau deploy cần kiểm tra thêm:

- frontend truy cập được bằng HTTPS;
- các API public không còn gọi HTTP trực tiếp;
- không còn `ERR_TLS_CERT_ALTNAME_INVALID` trong log frontend;
- không còn Mixed Content do URL asset HTTP sai;
- login/refresh cookie hoạt động;
- VNPAY return và IPN hoạt động qua domain public.

## Certificate renewal — việc cần làm tiếp theo

Certificate hiện tại có thời hạn hữu hạn. Cần thiết lập cron hoặc systemd timer trên EC2 để:

1. chạy `certbot renew` định kỳ bằng chính hai thư mục persistent hiện tại;
2. chạy thử `--dry-run` trước khi bật tự động;
3. reload Nginx sau khi certificate được renew thành công;
4. kiểm tra lại expiry date và log renewal.

Ví dụ flow renewal:

```bash
docker run --rm \
  -v /opt/stealdeal/certbot/www:/var/www/certbot \
  -v /opt/stealdeal/certbot/conf:/etc/letsencrypt \
  certbot/certbot renew --webroot -w /var/www/certbot

docker exec stealdeal-nginx nginx -s reload
```

Renewal không cần commit certificate vào Git, không cần build lại frontend image và không cần copy key thủ công. Sau khi renewal được tự động hóa, chỉ cần monitor expiry và renewal logs.

## Rollback và nguyên tắc an toàn

- Mỗi deployment gắn với commit SHA và image SHA.
- `git fetch`, checkout, Compose validation, `nginx -t` hoặc health check fail thì không recreate service.
- Rollback application dùng lại image SHA trước đó.
- Rollback config thực hiện bằng commit revert trên `main`, không sửa thủ công rồi để working tree dirty trên EC2.
- Không đưa private key, certificate hoặc production secret vào Git, Docker image hay CI log.

## Definition of done

- Backend source/config deployment hoạt động qua ECR và SSM.
- Frontend source/config deployment hoạt động qua ECR và SSM.
- Nginx config-only change được sync, validate và reload tự động.
- `stealdeal.store` chạy HTTPS.
- SSR gọi API nội bộ qua `http://nginx` không bị certificate hostname mismatch.
- VNPAY gọi được IPN HTTPS public URL.
- Certificate renewal được ghi nhận là task vận hành còn lại và không ảnh hưởng flow deploy hiện tại.
