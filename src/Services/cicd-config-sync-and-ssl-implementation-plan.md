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

Implementation reference:

~~~text
Backend CI: .github/workflows/ci.yml
Backend CD: .github/workflows/cd.yml
~~~

The frontend can follow the same separation: frontend CI creates a
manifest for image/config changes, while frontend CD uses SSM to fetch the
frontend repository only when Compose or Nginx runtime configuration changes.

### 1. Detect thay đổi

Thêm path detection:

~~~yaml
compose:
  - 'src/Services/docker-compose.yml'
~~~

Changes under `.github/workflows/**` still trigger Backend CI because the
workflow listens to every push on `main`, but they are not runtime Compose
changes and therefore do not set `compose_changed` or cause an EC2 config
checkout/recreate by themselves.

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

Với config-only change, manifest thực tế có dạng:

~~~json
{
  "commit_sha": "<sha>",
  "api_services": [],
  "docker_services": [],
  "application_code_changed": false,
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

`ci-env-file` chỉ chứa các giá trị giả lập cần cho Compose validation, không
chứa production secret và không được đưa vào artifact hoặc log.

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
worktree_status=$(git -c safe.directory=/opt/stealdeal/stealdeal-backend status --porcelain)
test -z "$worktree_status"
git -c safe.directory=/opt/stealdeal/stealdeal-backend fetch origin main --prune
git -c safe.directory=/opt/stealdeal/stealdeal-backend cat-file -e "$DEPLOY_SHA^{commit}"
git -c safe.directory=/opt/stealdeal/stealdeal-backend checkout --detach "$DEPLOY_SHA"
cd src/Services
~~~

SSM có thể chạy với user khác owner của working tree và có thể không có biến
`HOME`. Vì vậy không dùng `git config --global`; mỗi lệnh Git truyền trực tiếp
`safe.directory` cho đúng repository. Nếu working tree không sạch, fetch lỗi,
commit không tồn tại hoặc checkout sai SHA, SSM command phải fail trước khi
recreate container.

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

4. Nếu chỉ thay đổi Compose, không build hoặc pull image mới. CD giữ nguyên các
   biến `*_IMAGE` trong runtime `.env` trên EC2 và chạy:

~~~bash
docker compose up -d --no-build --force-recreate
~~~

Vì `src/Services/docker-compose.yml` là Compose file dùng chung, config-only
deployment hiện recreate toàn bộ API backend. Các container dùng lại image
tag đang có trong runtime `.env`; config commit không tạo image tag mới.

5. Nếu source và Compose cùng thay đổi, CD cập nhật image variables, pull đúng
   image SHA từ ECR rồi recreate theo config mới trong cùng một SSM command.
6. Giữ nguyên flow migrator hiện tại khi manifest yêu cầu migration.
7. Restart Nginx frontend và chạy health check port 80 sau backend deployment.
8. Ghi nhận config commit SHA và SHA-256 của `docker-compose.yml` trong SSM log.

Backend CD manual dispatch hỗ trợ:

~~~text
image_sha: commit SHA cần deploy
services: all, comma-separated service names, hoặc none
run_migrator: true/false
compose_changed: true/false
~~~

`services=none` và `compose_changed=true` dùng để test hoặc apply config-only
deployment mà không yêu cầu image mới trên ECR.

Backend CD chỉ restart Nginx hiện có để refresh backend container IP. Nó không
fetch hoặc deploy `nginx.conf`; việc detect và sync Nginx thuộc Frontend CI/CD
ở Phase 2.

### 4. Environment variable mới

Compose phải truyền biến mới vào container:

~~~yaml
environment:
  VnPay__IpNUrl: ${VNPAY_IPN_URL}
~~~

Nếu là secret mới, giá trị phải được provision trong runtime environment trước
khi deploy. Nếu thiếu biến bắt buộc, CD phải fail trước khi recreate container.

## Phase 2: Frontend CI/CD

### 1. Frontend workflow structure

Implementation files:

~~~text
Frontend CI: .github/workflows/ci.yml
Frontend CD: .github/workflows/cd.yml
~~~

The frontend repository does not use `deploy/**`. Runtime configuration is
limited to the root `docker-compose.yml` and `nginx.conf`.

### 2. Detect thay đổi

Frontend CI detects three independent states:

~~~yaml
frontend_source:
  - '**'
  - '!docker-compose.yml'
  - '!nginx.conf'
  - '!.github/**'
compose:
  - 'docker-compose.yml'
nginx:
  - 'nginx.conf'
~~~

Changes under `.github/workflows/**` still trigger Frontend CI because the
workflow listens to every push on `main`, but they do not set any runtime
change flag and therefore do not fetch or recreate containers by themselves.

The deployment manifest contains:

~~~json
{
  "commit_sha": "<sha>",
  "frontend_image_changed": false,
  "application_code_changed": false,
  "compose_changed": false,
  "nginx_changed": true
}
~~~

For a frontend source change, `frontend_image_changed=true`. For a
config-only change, both `frontend_image_changed` and
`application_code_changed` remain false.

### 3. CI behavior

- Chỉ frontend source thay đổi: chạy `npm ci`, lint, build, rồi build/push image.
- Chỉ `docker-compose.yml` hoặc `nginx.conf` thay đổi: không chạy Node build và
  không build/push image.
- Source và config cùng thay đổi: build/push image và sync config trong cùng CD
  run theo cùng một commit SHA.
- Mọi loại change đều upload manifest để Frontend CD quyết định deployment.
- CI luôn validate Compose bằng env file tạm an toàn:

~~~bash
docker compose --env-file <ci-env-file> \
  -f docker-compose.yml config --quiet
~~~

### 4. Frontend CD behavior

CD quyết định theo manifest:

~~~text
frontend_image_changed=true, compose_changed=false, nginx_changed=false
    -> export/persist FRONTEND_IMAGE
    -> login ECR, verify image SHA, pull image
    -> recreate frontend và nginx

compose_changed=true
    -> SSM fetch/checkout DEPLOY_SHA
    -> validate Compose
    -> validate Nginx config
    -> recreate frontend và nginx bằng --no-build

nginx_changed=true, compose_changed=false
    -> SSM fetch/checkout DEPLOY_SHA
    -> validate Nginx config
    -> recreate nginx bằng --no-build
    -> nginx -t và reload

frontend_image_changed=true và có config change
    -> pull image mới
    -> fetch/checkout config cùng DEPLOY_SHA
    -> validate rồi deploy một lần
~~~

Chỉ khi `compose_changed=true` hoặc `nginx_changed=true`, CD mới fetch
frontend repository trên EC2:

~~~bash
cd /opt/stealdeal/steal-deals-web
worktree_status=$(git -c safe.directory=/opt/stealdeal/steal-deals-web status --porcelain)
test -z "$worktree_status"
git -c safe.directory=/opt/stealdeal/steal-deals-web fetch origin main --prune
git -c safe.directory=/opt/stealdeal/steal-deals-web cat-file -e "$DEPLOY_SHA^{commit}"
git -c safe.directory=/opt/stealdeal/steal-deals-web checkout --detach "$DEPLOY_SHA"
~~~

Các lệnh Git truyền trực tiếp `safe.directory`, không dùng
`git config --global`, vì SSM shell có thể không có `$HOME` và user chạy SSM có
thể khác owner của working tree.

Với config change, CD dùng:

~~~bash
docker compose config --quiet
docker compose run --rm --no-deps nginx nginx -t
~~~

Sau khi validation pass:

~~~bash
docker compose up -d --no-build --force-recreate frontend nginx
docker compose exec -T nginx nginx -t
docker compose exec -T nginx nginx -s reload
~~~

Với Nginx-only change, chỉ recreate/reload Nginx. Với image-only change, CD
pull đúng ECR image SHA và persist `FRONTEND_IMAGE` trong runtime `.env` để các
config-only deployment tiếp theo tiếp tục dùng đúng image hiện tại.

Frontend CD manual dispatch hỗ trợ `image_sha`,
`frontend_image_changed`, `compose_changed` và `nginx_changed`, nên có thể
test config-only deployment mà không cần tạo image mới.

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

## Post-deployment runtime secrets provisioning

Sau khi hoàn thành CI/CD config sync và SSL, runtime secrets sẽ được provision
tự động từ GitHub Actions Secrets xuống EC2. Secrets không được commit vào Git,
không nằm trong deployment manifest, Docker image hoặc log của workflow.

### Secret source và deployment flow

GitHub Environment `production` là nơi lưu các credential dùng cho deployment:

~~~text
GitHub Actions production secrets
    -> CD provision step
    -> secure transport tới EC2
    -> runtime env files ngoài Git repository
    -> Docker Compose env_file
    -> recreate các container bị ảnh hưởng
~~~

Phương án triển khai ban đầu có thể dùng GitHub Actions Secrets kết hợp với SSM
Run Command hiện có. Provisioning step phải ghi các file runtime trên EC2 với
quyền hạn chế, không in giá trị secret ra log và không truyền secret bằng cách
ghép trực tiếp vào command string nếu cơ chế transport cho phép cách an toàn hơn.

Các file runtime không nằm trong vùng được Git quản lý:

~~~text
/opt/stealdeal/runtime/identity.env
/opt/stealdeal/runtime/notification.env
/opt/stealdeal/runtime/store.env
/opt/stealdeal/runtime/payment.env
~~~

Mỗi file phải có permission tương đương `0600` và chỉ user chạy Docker/deploy
được đọc. CD phải kiểm tra sự tồn tại của các biến bắt buộc trước khi recreate
container. Chỉ được log tên biến hoặc trạng thái đã được provision, không log
giá trị.

### Runtime variable mapping

Các secret dùng cấu trúc tên của ASP.NET Core, trong đó dấu `:` được thay bằng
`__`:

~~~env
# Identity và Store
Aws__AccessKeyId=<S3 access key hiện tại>
Aws__SecretAccessKey=<S3 secret key hiện tại>

# Dùng cho tất cả API cần JWT
Jwt__Secret=<production JWT secret>

# Notification
Brevo__ApiKey=<Brevo API key hiện tại>

# Payment
VnPay__HashSecret=<VNPAY hash secret>
VnPay__ReturnUrl=https://<domain>/<frontend-return-route>
VnPay__IpNUrl=https://<domain>/api/payment/vnpay/ipn

# Store, nếu provider tương ứng được bật
Geoapify__ApiKey=<Geoapify API key>
Goong__ApiKey=<Goong API key>
~~~

Compose truyền các file này vào service tương ứng bằng `env_file`. Nếu cùng
một key xuất hiện cả trong `env_file` và block `environment`, giá trị trong
`environment` có độ ưu tiên cao hơn. Production phải dùng
`ASPNETCORE_ENVIRONMENT=Production`; không dựa vào User Secrets trên EC2.

### Provisioning và rotation

Provision secrets phải chạy trong các trường hợp sau:

1. EC2 bootstrap lần đầu.
2. Deploy production lần đầu sau khi hoàn thành SSL.
3. Secret được rotate hoặc thay đổi.
4. Manual CD được chạy với tùy chọn `provision_secrets=true`.

Thay đổi secret không yêu cầu build Docker image. Sau khi file runtime được cập
nhật, CD chạy `docker compose up -d --no-build --force-recreate` cho các service
bị ảnh hưởng.

Nên commit các file mẫu không chứa giá trị thật, ví dụ:

~~~text
identity.env.example
notification.env.example
store.env.example
payment.env.example
~~~

Các file `.env` thật phải bị loại khỏi Git và không được checkout từ repository.
Git chỉ đồng bộ Compose/Nginx/source code; runtime secrets được đồng bộ bằng
provisioning step riêng.

### Required GitHub Actions secrets

Tối thiểu cần cấu hình trong GitHub Environment `production`:

~~~text
S3_ACCESS_KEY_ID
S3_SECRET_ACCESS_KEY
BREVO_API_KEY
JWT_SECRET
VNPAY_HASH_SECRET
VNPAY_RETURN_URL
VNPAY_IPN_URL
GEOAPIFY_API_KEY hoặc GOONG_API_KEY
MSSQL_SA_PASSWORD
~~~

Tên secret trong GitHub có thể khác tên ASP.NET Core, nhưng provisioning step
phải map đúng sang tên `Section__Property` trước khi ghi vào runtime env file.

### Security boundary

GitHub Actions Secrets là nguồn credential cho phase này, không phải Git-tracked
configuration. Workflow production phải được giới hạn trên branch `main`, dùng
GitHub Environment protection/review nếu cần, và chỉ provision secrets sau khi
CI thành công. Nếu sau này cần loại bỏ secret khỏi GitHub Actions và EC2 runtime
file, có thể thay riêng secret source bằng AWS Secrets Manager mà không thay đổi
các tên environment variable hoặc application code.
