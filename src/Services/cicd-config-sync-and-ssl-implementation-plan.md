# CI/CD Config Sync and SSL Implementation Plan

## Mục tiêu

Sau khi hoàn thành:

- Code backend/frontend vẫn build và deploy bằng Docker image trên ECR như hiện tại.
- Thay đổi docker-compose.yml hoặc nginx.conf được deploy tự động qua CI/CD.
- Config được checkout từ đúng commit SHA qua SSM trong cùng CD run.
- HTTPS chạy tại Nginx public entrypoint.
- VNPAY/third-party có thể gọi IPN bằng HTTPS.

Không dùng S3. Đồng bộ config sử dụng SSM đang có.

## Cơ chế đồng bộ config qua SSM

SSM config synchronization is conditional. An application-code-only deployment
does not fetch the Git repository and follows the existing ECR image flow. The
repository is fetched and checked out only when the deployment manifest says
that Compose or Nginx configuration changed.

EC2 sẽ có bản clone của hai repository:

~~~text
/opt/stealdeal/stealdeal-backend
/opt/stealdeal/steal-deals-web
~~~

Khi CI/CD chạy, GitHub Actions gửi SSM command tới EC2. SSM command thực hiện:

~~~text
cd vào repository tương ứng
kiểm tra working tree sạch
git fetch origin main --prune
git checkout --detach "$DEPLOY_SHA"
validate Compose/Nginx
recreate/reload service nếu cần
~~~

Như vậy file Compose/Nginx mới được lấy trực tiếp từ GitHub thông qua Git trên
EC2. Không dùng Base64, checksum hoặc S3.

Application code vẫn chạy từ Docker image trên ECR. Việc repository tồn tại
trên EC2 chỉ nhằm lấy deployment configuration; EC2 không build image từ source.

Prerequisites:

- hai repository đã được clone đúng các path trên EC2;
- EC2 có quyền read-only tới GitHub repositories, tốt nhất bằng deploy key;
- remote `origin` trỏ đúng repository;
- branch deploy là `main`;
- working tree trên EC2 phải sạch;
- không dùng `git reset --hard` hoặc tự động xóa local changes.

Ví dụ SSM command backend:

~~~bash
cd /opt/stealdeal/stealdeal-backend
test -z "$(git status --porcelain)"
git fetch origin main --prune
git cat-file -e "$DEPLOY_SHA^{commit}"
git checkout --detach "$DEPLOY_SHA"
~~~

Ví dụ SSM command frontend:

~~~bash
cd /opt/stealdeal/steal-deals-web
test -z "$(git status --porcelain)"
git fetch origin main --prune
git cat-file -e "$DEPLOY_SHA^{commit}"
git checkout --detach "$DEPLOY_SHA"
~~~

Nếu working tree không sạch, fetch thất bại, commit không tồn tại hoặc checkout
không thành công, SSM command phải fail và không được tiếp tục deploy.

`DEPLOY_SHA` được lấy từ `github.event.workflow_run.head_sha` đối với CD tự động,
hoặc từ SHA được nhập khi chạy CD thủ công. Vì vậy image ECR và deployment
configuration đều dùng cùng một commit.

## Phase 0: EC2 bootstrap

Thực hiện một lần trước khi bật flow tự động:

1. Tạo read-only deploy key cho từng repository.
2. Thêm public key vào GitHub repository tương ứng.
3. Cài private key trên EC2 với quyền chỉ user deploy đọc được.
4. Clone hai repository vào đúng path:

~~~bash
git clone git@github.com:<org>/stealdeal-backend.git \
  /opt/stealdeal/stealdeal-backend
git clone git@github.com:<org>/steal-deals-web.git \
  /opt/stealdeal/steal-deals-web
~~~

5. Kiểm tra git remote -v, branch main và quyền pull bằng user chạy Docker.
6. Đảm bảo các file runtime .env, certificate và dữ liệu persistent không nằm
   trong vùng bị Git quản lý hoặc không bị overwrite bởi checkout.

## Phase 1: Backend CI/CD

### 1. Detect thay đổi

Thêm path detection:

~~~yaml
deployment_config:
  - 'src/Services/docker-compose.yml'
  - 'src/Services/**/deploy/**'
  - '.github/workflows/**'
~~~

Manifest cần có:

~~~json
{
  "commit_sha": "<sha>",
  "api_services": ["identity"],
  "docker_services": ["identity"],
  "application_code_changed": true,
  "run_migrator": false,
  "compose_changed": true
}
~~~

### 2. CI behavior

- Chỉ source API thay đổi: giữ nguyên build/test/push image.
- Chỉ Compose thay đổi: không cần build image mới.
- Source và Compose cùng thay đổi: build image và pull config mới qua SSM.
- Config-only change vẫn phải tạo manifest để CD chạy.
- Luôn validate Compose với giá trị CI an toàn:

~~~bash
docker compose --env-file <ci-env-file> \
  -f src/Services/docker-compose.yml config --quiet
~~~

### 3. Backend CD behavior

CD quyết định theo manifest:

~~~text
compose_changed=false
    -> không fetch/checkout repository
    -> giữ nguyên flow pull image và recreate hiện tại

compose_changed=true
    -> gửi SSM command fetch/checkout DEPLOY_SHA
    -> validate Compose mới
    -> apply config và recreate service cần thiết

source_changed=true và compose_changed=true
    -> build/push image mới
    -> fetch/checkout config của cùng DEPLOY_SHA
    -> deploy một lần
~~~

Chỉ khi `compose_changed=true`, CD mới gửi SSM command để cập nhật repository:

~~~bash
cd /opt/stealdeal/stealdeal-backend
test -z "$(git status --porcelain)"
git fetch origin main --prune
git cat-file -e "$DEPLOY_SHA^{commit}"
git checkout --detach "$DEPLOY_SHA"
cd src/Services
~~~

Sau đó:

1. Chạy:

~~~bash
docker compose config --quiet
~~~

2. Nếu có image mới, pull image đúng SHA từ ECR.
3. Recreate service bị ảnh hưởng:

~~~bash
docker compose up -d --no-build --no-deps --force-recreate <service>
~~~

4. Nếu chỉ thay đổi Compose, chạy docker compose up -d --no-build cho service
   bị ảnh hưởng.
5. Giữ nguyên flow migrator hiện tại khi manifest yêu cầu migration.
6. Chạy health check và ghi nhận commit SHA.

### 4. Environment variable mới

Compose phải truyền biến mới vào container:

~~~yaml
environment:
  VnPay__IpNUrl: ${VNPAY_IPN_URL}
~~~

Nếu là secret mới, giá trị phải được provision trong runtime environment trước
khi deploy. Nếu thiếu biến bắt buộc, CD phải fail trước khi recreate container.

## Phase 2: Frontend CI/CD

### 1. Detect thay đổi

Thêm path detection:

~~~yaml
deployment_config:
  - 'docker-compose.yml'
  - 'nginx.conf'
  - 'deploy/**'
  - '.github/workflows/**'
~~~

Manifest cần có:

~~~json
{
  "commit_sha": "<sha>",
  "frontend_image_changed": false,
  "compose_changed": false,
  "nginx_changed": true
}
~~~

### 2. CI behavior

- Chỉ frontend source thay đổi: giữ nguyên build/test/push image.
- Chỉ Compose hoặc Nginx thay đổi: không cần build image.
- Source và config cùng thay đổi: build image và pull config mới qua SSM.
- Config-only change vẫn phải trigger CD.

### 3. Frontend CD behavior

CD quyết định theo manifest:

~~~text
frontend_image_changed=true, compose_changed=false, nginx_changed=false
    -> giữ nguyên flow pull image và recreate hiện tại

compose_changed=true hoặc nginx_changed=true
    -> gửi SSM command fetch/checkout DEPLOY_SHA
    -> validate config mới
    -> apply Compose/Nginx config

frontend_image_changed=true và có config change
    -> build/push image mới
    -> fetch/checkout config của cùng DEPLOY_SHA
    -> deploy một lần
~~~

Chỉ khi có config change, CD mới gửi SSM command để cập nhật repository:

~~~bash
cd /opt/stealdeal/steal-deals-web
test -z "$(git status --porcelain)"
git fetch origin main --prune
git cat-file -e "$DEPLOY_SHA^{commit}"
git checkout --detach "$DEPLOY_SHA"
~~~

Sau đó dùng các file mới trong repository. Với Compose:

~~~bash
docker compose config --quiet
docker compose up -d --no-build <affected-services>
~~~

Với Nginx:

~~~bash
docker compose exec -T nginx nginx -t
docker compose exec -T nginx nginx -s reload
~~~

Nếu service không tên nginx, dùng tên service thực tế. Nếu frontend image cũng
thay đổi, pull image mới và recreate frontend/Nginx sau khi config mới được
validate.

## Phase 3: SSL cho domain

### 1. Prerequisites

- DNS A record trỏ tới Elastic IP của EC2.
- Security Group mở TCP 80 và 443.
- API, SQL Server, Redis và RabbitMQ không public.
- Nginx frontend tham gia external network stealdeal-network.

### 2. Persistent certificate directories

Tạo trên EC2:

~~~text
/opt/stealdeal/certbot/www
/opt/stealdeal/certbot/conf
~~~

Frontend Compose mount read-only:

~~~yaml
volumes:
  - ./nginx.conf:/etc/nginx/nginx.conf:ro
  - /opt/stealdeal/certbot/www:/var/www/certbot:ro
  - /opt/stealdeal/certbot/conf:/etc/letsencrypt:ro
~~~

Không đưa certificate/private key vào Git và không để config sync xóa hai thư
mục persistent này.

### 3. Initial certificate

1. Deploy HTTP Nginx config có ACME challenge:

~~~nginx
location ^~ /.well-known/acme-challenge/ {
    root /var/www/certbot;
    try_files $uri =404;
}
~~~

2. Kiểm tra challenge file qua port 80.
3. Chạy Certbot webroot để cấp certificate.
4. Deploy HTTPS Nginx config:

~~~nginx
server {
    listen 443 ssl;
    server_name <domain> www.<domain>;

    ssl_certificate /etc/letsencrypt/live/<domain>/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/<domain>/privkey.pem;
    ssl_protocols TLSv1.2 TLSv1.3;

    # Giữ các location frontend/backend hiện tại.
}
~~~

5. Redirect HTTP sang HTTPS, ngoại trừ ACME challenge.
6. Chạy nginx -t, reload/recreate Nginx và kiểm tra HTTPS.

### 4. VNPAY IPN

Public IPN URL:

~~~text
https://<domain>/api/payment/vnpay/ipn
~~~

Nginx route tới:

~~~text
http://payment-api:5155/api/payment/vnpay/ipn
~~~

Payment API verify chữ ký và cập nhật database. VNPAY chỉ cần biết public
HTTPS URL, không cần biết Docker network hay database.

Runtime values:

~~~env
VNPAY_RETURN_URL=https://<domain>/<frontend-return-route>
VNPAY_IPN_URL=https://<domain>/api/payment/vnpay/ipn
~~~

IPN URL phải được cấu hình trong VNPAY sandbox/merchant configuration.

### 5. Certificate renewal

Thiết lập cron hoặc systemd timer trên EC2 để:

1. chạy certbot renew;
2. dùng persistent certificate directories;
3. reload Nginx sau khi certificate được renew;
4. hỗ trợ dry-run.

Renewal không cần GitHub Actions hoặc manual copy certificate.

## Rollback và validation

Mỗi deployment ghi lại:

~~~text
commit SHA
image SHA
config SHA-256
deployment time
~~~

Nếu git fetch, checkout, docker compose config, nginx -t hoặc health check fail:

- không recreate service;
- giữ file config active cũ;
- in lỗi rõ ràng trong CD log.

Rollback application image dùng lại commit SHA trước đó.
Rollback config nên thực hiện bằng một commit revert mới trên `main`, sau đó
để CD pull và apply commit revert đó. Không tự động rollback database migration.

## Definition of done

Sau khi merge vào main, các trường hợp sau đều tự động hoạt động:

1. Backend source change: build/push/deploy như flow hiện tại.
2. Backend Compose-only change: sync Compose qua SSM và recreate service cần thiết.
3. Frontend source change: build/push/deploy như flow hiện tại.
4. Frontend Compose-only change: sync Compose qua SSM và apply config.
5. Nginx-only change: sync Nginx qua SSM, nginx -t, reload/recreate.
6. Code + config change: build image, SSM pull config mới và deploy một lần.
7. Domain chạy HTTPS.
8. VNPAY gọi được IPN URL và Payment API trả response đúng.
9. Không cần manual edit hoặc manual checkout trên EC2; fetch và checkout đúng
   SHA được CD gọi tự động qua SSM.
