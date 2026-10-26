# AWS Parameter Store Runtime Secrets — Implementation Runbook

## 1. Mục tiêu

Đưa secret của backend lên AWS Systems Manager Parameter Store và inject vào container khi deploy trên EC2.

Mục tiêu cuối cùng:

- Secret không còn nằm trong source code, `appsettings.json`, `.env.example`, Dockerfile, image hoặc GitHub Actions.
- EC2 lấy secret bằng Instance IAM Role.
- Secret được materialize thành `/opt/stealdeal/config/backend.env` với permission `600`.
- Docker Compose truyền secret vào đúng section của ASP.NET Core.
- Có thể refresh secret và recreate đúng service mà không cần build image mới.

Flow runtime:

```text
AWS Parameter Store
    -> EC2 Instance Role
    -> sync-backend-secrets.sh
    -> /opt/stealdeal/config/backend.env
    -> Docker Compose
    -> Container environment variables
    -> ASP.NET Core IConfiguration
```

Application không cần AWS SDK hoặc Parameter Store configuration provider. ASP.NET Core tự đọc environment variables; `Jwt__Secret` được map thành `Jwt:Secret`.

Tài liệu Microsoft: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/index?tabs=basicconfiguration>

## 2. Hiện trạng repository cần xử lý trước

Các file liên quan:

- Compose: `src/Services/docker-compose.yml`
- Backend CD: `.github/workflows/cd.yml`
- Backend CI: `.github/workflows/ci.yml`
- Runtime plan này: `src/Services/aws-parameter-store-runtime-secrets-implementation-plan.md`

Hiện tại:

- Compose chưa inject `Jwt`, `Aws`, `Brevo`, `Geoapify`, `VnPay` secret.
- Các API đang hard-code `ASPNETCORE_ENVIRONMENT: Development`.
- `Payment/appsettings.json` đang có giá trị ở `VnPay.TmnCode` và `VnPay.HashSecret`; nếu đây là credential thật thì phải rotate.
- `.env.example` có các giá trị không rỗng; phải thay bằng placeholder hoặc để trống.
- `.env` thật được ignore bởi `.gitignore`; file này chỉ được dùng local/giữ image tag trên EC2, không chứa production secret.
- CD hiện chưa truyền runtime env file cho mọi lệnh Compose.

## 3. Quyết định kiến trúc

### 3.1. Parameter Store

- Region: `us-east-1`.
- Type: `SecureString`.
- Tier: `Standard`.
- Phase đầu dùng AWS-managed key `aws/ssm` để giảm operational overhead.
- Không cho EC2 quyền `PutParameter` hoặc `DeleteParameter`.

### 3.2. S3 cross-account

Giữ nguyên `Aws__AccessKeyId` và `Aws__SecretAccessKey` trong phase này vì bucket đang ở account khác. S3 IAM Role migration là phase riêng sau khi runtime secret flow ổn định.

### 3.3. Hai env file trên EC2

Repository hiện dùng `.env` để giữ image tag. Vì vậy production phải dùng cả hai file, theo đúng thứ tự:

```bash
docker compose \
  --env-file .env \
  --env-file /opt/stealdeal/config/backend.env \
  ...
```

`backend.env` đứng sau để override secret và `ASPNETCORE_ENVIRONMENT=Production`; `.env` vẫn cung cấp image tag. Docker Compose đọc nhiều env file theo thứ tự và file sau override file trước.

Tài liệu Docker: <https://docs.docker.com/compose/how-tos/environment-variables/variable-interpolation/>

## 4. Thứ tự implementation bắt buộc

Thực hiện theo thứ tự dưới đây. Chỉ sang bước kế tiếp khi tiêu chí `Done` của bước hiện tại đạt.

- [x] Step 1 — Làm sạch repository và chuyển credential hiện tại sang local `.env`.
- [x] Step 2 — Viết script đồng bộ secret.
- [x] Step 3 — Sửa Docker Compose.
- [x] Step 4 — Cập nhật CI validation.
- [ ] Step 5 — Cập nhật CD và secret-only deployment.
- [ ] Step 6 — Cấp IAM cho EC2.
- [ ] Step 7 — Tạo Parameter Store parameters.
- [ ] Step 8 — Bootstrap script trên EC2.
- [ ] Step 9 — Deploy lần đầu.
- [ ] Step 10 — Kiểm thử business flow.
- [ ] Step 11 — Kiểm thử rotation, rollback và secret-only refresh.

Quyết định hiện tại: tiếp tục dùng credential hiện có, không rotate trong phase implementation này.

## 5. Step 1 — Làm sạch repository và credential

**Trạng thái: đã implement ngày 2026-10-26 trong phạm vi đã thống nhất.**

### 5.1. Xóa secret khỏi application configuration

Trong tất cả `appsettings.json`, giữ placeholder hoặc chuỗi rỗng cho:

```json
"Jwt": { "Secret": "" }
"Aws": { "AccessKeyId": "", "SecretAccessKey": "" }
"Brevo": { "ApiKey": "" }
"Geoapify": { "ApiKey": "" }
"VnPay": { "TmnCode": "", "HashSecret": "" }
```

Không xóa các giá trị không nhạy cảm như URL, issuer, audience, timeout hoặc bucket name.

### 5.2. Thiết lập local `.env`

`src/Services/.env` đã được Git ignore và giữ credential hiện tại để chạy local bằng Docker Compose:

```env
ASPNETCORE_ENVIRONMENT=Development
MSSQL_SA_PASSWORD=
JWT_SECRET=
AWS_ACCESS_KEY=
AWS_SECRET_KEY=
BREVO_API_KEY=
GEOAPIFY_API_KEY=
VNPAY_TMN_CODE=
VNPAY_HASH_SECRET=
```

Không commit file `.env`. `.env.example` được giữ nguyên theo quyết định hiện tại và không thuộc phạm vi Step 1.

Giá trị AWS và Brevo được copy từ .NET User Secrets hiện có; các User Secrets cũ vẫn được giữ. JWT chung được chuyển từ sáu `appsettings.json` sang `.env`. Geoapify đã được bổ sung vào local `.env`.

Compose local map toàn bộ secret vào đúng service và dùng fail-fast cho tất cả biến bắt buộc.

Ví dụ Payment:

```yaml
VnPay__TmnCode: ${VNPAY_TMN_CODE:?VNPAY_TMN_CODE is required}
VnPay__HashSecret: ${VNPAY_HASH_SECRET:?VNPAY_HASH_SECRET is required}
```

Chạy local bằng Docker Compose từ `src/Services` để Compose tự đọc `.env`:

```bash
docker compose up
```

`dotnet run` trực tiếp không tự đọc file `.env`; nếu chạy service trực tiếp thì export biến vào shell hoặc dùng .NET User Secrets.

### 5.3. Quyết định về rotation

Không rotate credential trong phase này. Mục tiêu của Step 1 là loại secret khỏi snapshot Git ở commit tiếp theo và tiếp tục dùng giá trị hiện tại từ `.env`.

Lưu ý: secret từng commit vẫn tồn tại trong Git history. Đây là rủi ro được chấp nhận cho phase hiện tại; việc rotate hoặc rewrite history chỉ thực hiện khi có quyết định riêng.

### 5.4. Done criteria

- [x] `Jwt.Secret` trong cả sáu API và VNPAY secret trong Payment đã rỗng ở file tracked.
- [x] JWT, AWS, Brevo, Geoapify, VNPAY và MSSQL tồn tại trong local `.env` bị Git ignore.
- [x] Database README đã thay password thật bằng placeholder.
- [x] Compose local inject secret vào đúng sáu API.
- [x] CI dùng dummy values, không dùng local hoặc production secret.
- [x] `.env.example` được giữ nguyên và loại khỏi phạm vi cleanup theo quyết định hiện tại.

## 6. Step 2 — Viết `sync-backend-secrets.sh`

**Trạng thái: đã implement ngày 2026-10-26; kiểm thử tích hợp với AWS/EC2 thực tế chờ Step 6–8.**

Tạo script không chứa secret tại:

```text
src/Services/deployment/sync-backend-secrets.sh
```

Khi bootstrap, copy script tới:

```text
/opt/stealdeal/scripts/sync-backend-secrets.sh
```

### 6.1. Hành vi bắt buộc của script

Script phải:

1. Dùng `#!/usr/bin/env bash`, `set -Eeuo pipefail`, `umask 077`.
2. Dùng `flock` để tránh hai deployment ghi file đồng thời.
3. Kiểm tra `aws` và `jq` đã cài.
4. Gọi `aws ssm get-parameters-by-path` với:
   - `--region us-east-1`
   - `--path /stealdeal/prod`
   - `--recursive`
   - `--with-decryption`
   - không dùng `--no-paginate`.
5. Lấy đúng tám leaf name sau:

```text
JWT_SECRET
AWS_ACCESS_KEY
AWS_SECRET_KEY
BREVO_API_KEY
GEOAPIFY_API_KEY
VNPAY_TMN_CODE
VNPAY_HASH_SECRET
MSSQL_SA_PASSWORD
```

6. Fail nếu thiếu key hoặc key có giá trị rỗng.
7. Reject giá trị chứa NUL, CR hoặc LF.
8. Ghi file tạm cùng filesystem với file đích.
9. Ghi thêm `ASPNETCORE_ENVIRONMENT=Production`.
10. Set owner `root:root`, mode `600`.
11. Replace file chính bằng `mv` atomic.
12. Chỉ in trạng thái thành công hoặc lỗi tổng quát; không in parameter value, JSON hoặc toàn bộ environment.

### 6.2. Done criteria

- [x] Script có Bash syntax hợp lệ.
- [x] Script kiểm tra đúng tám required `SecureString`.
- [x] Script reject parameter thiếu, dư, rỗng, chỉ có whitespace hoặc chứa NUL/CR/LF.
- [x] Script dùng `flock`, temporary file cùng filesystem và `mv` atomic.
- [x] Script ghi `ASPNETCORE_ENVIRONMENT=Production`, owner `root:root`, mode `600`.
- [x] Script không dùng `set -x` và không truyền secret qua command arguments/log.
- [ ] Chạy nhiều lần liên tiếp trên EC2 thật sau khi hoàn thành Parameter Store và IAM.
- [ ] Xác nhận file runtime cũ được giữ nguyên khi AWS call/validation thất bại trên EC2.

## 7. Step 3 — Sửa Docker Compose

**Trạng thái: đã implement và validate ngày 2026-10-26.**

### 7.1. Environment mode

Đổi trên sáu API:

```yaml
ASPNETCORE_ENVIRONMENT: ${ASPNETCORE_ENVIRONMENT:-Development}
```

Local mặc định là `Development`; runtime file trên EC2 sẽ đặt `Production`.

### 7.2. Mapping configuration

| Service | Environment variables |
|---|---|
| `identity-api` | `Jwt__Secret`, `Aws__AccessKeyId`, `Aws__SecretAccessKey` |
| `notification-api` | `Jwt__Secret`, `Brevo__ApiKey` |
| `store-api` | `Jwt__Secret`, `Aws__AccessKeyId`, `Aws__SecretAccessKey`, `Geoapify__ApiKey` |
| `cart-api` | `Jwt__Secret` |
| `order-api` | `Jwt__Secret` |
| `payment-api` | `Jwt__Secret`, `VnPay__TmnCode`, `VnPay__HashSecret` |

Không map `Goong__ApiKey`.

### 7.3. Fail-fast interpolation

Dùng syntax bắt buộc cho toàn bộ secret:

```yaml
Jwt__Secret: ${JWT_SECRET:?JWT_SECRET is required}
Brevo__ApiKey: ${BREVO_API_KEY:?BREVO_API_KEY is required}
```

Áp dụng tương tự cho AWS, Geoapify, VNPAY và MSSQL.

### 7.4. Done criteria

- [x] Local Compose resolve `Development` cho cả sáu API.
- [x] Production simulation resolve `Production` cho cả sáu API.
- [x] `docker compose ... config --quiet` thành công khi đủ biến.
- [x] Compose trả exit code khác `0` khi thiếu required secret.
- [x] Sáu service nhận đúng configuration section.
- [x] MSSQL fail-fast tại SQL Server, migrator và năm connection string.
- [x] Compose không map `Goong__ApiKey`.

## 8. Step 4 — Cập nhật CI validation

**Trạng thái: đã implement và validate ngày 2026-10-26.**

Trong `.github/workflows/ci.yml`:

1. Tạo temporary env file chỉ dùng dummy values và `ASPNETCORE_ENVIRONMENT=Production`.
2. Bổ sung toàn bộ tám biến bắt buộc vào file đó.
3. Đặt `umask 077`, permission `600` và cleanup file bằng `trap`.
4. Chạy:

```bash
docker compose \
  --env-file "$CI_ENV_FILE" \
  -f src/Services/docker-compose.yml \
  config --quiet
```

5. Tạo file thứ hai cố tình thiếu `JWT_SECRET` và xác nhận Compose phải fail.
6. Không dùng secret production.
7. Không in resolved Compose config ra log.

### Done criteria

- [x] Bash syntax của validation step hợp lệ.
- [x] CI cung cấp đủ `8/8` required variables bằng dummy values.
- [x] Compose config pass với file đầy đủ.
- [x] Compose config fail khi thiếu `JWT_SECRET`.
- [x] Temporary files được cleanup dù step thành công hay thất bại.

## 9. Step 5 — Cập nhật CD

### 9.1. Luồng deploy mới

Mọi backend CD phải chạy:

```text
sync-backend-secrets.sh
→ docker compose config --quiet
→ ECR login nếu deploy image
→ pull image nếu deploy image
→ database migrator nếu được yêu cầu
→ recreate service
→ health check
```

Mọi lệnh Compose trong CD phải dùng:

```bash
--env-file .env --env-file /opt/stealdeal/config/backend.env
```

### 9.2. Manual secret-only deployment

Thêm workflow input:

```yaml
refresh_secrets:
  description: Sync secrets and recreate selected services
  required: true
  default: false
  type: boolean
```

Khi `refresh_secrets=true`:

- `image_sha` không bắt buộc.
- Không login ECR.
- Không pull image.
- Không build image.
- Không chạy migrator.
- Chỉ sync secret và recreate service được chọn.

### 9.3. Mapping refresh

| Parameter thay đổi | Service cần recreate |
|---|---|
| `JWT_SECRET` | Tất cả API |
| `AWS_ACCESS_KEY`, `AWS_SECRET_KEY` | `identity-api`, `store-api` |
| `BREVO_API_KEY` | `notification-api` |
| `GEOAPIFY_API_KEY` | `store-api` |
| `VNPAY_TMN_CODE`, `VNPAY_HASH_SECRET` | `payment-api` |
| `MSSQL_SA_PASSWORD` | Quy trình SQL đặc biệt ở Step 11 |

Không dùng `set -x`. Không chạy `docker compose config` không có `--quiet`.

### 9.4. Done criteria

- Automatic CD sync secret trước Compose.
- Manual secret-only deployment chạy được mà không cần image mới.
- CD không truyền runtime secret qua GitHub Actions.

## 10. Step 6 — IAM cho EC2

### 10.1. EC2 runtime policy

Gắn policy tối thiểu vào role đang attach với EC2:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "ReadStealDealProdParameters",
      "Effect": "Allow",
      "Action": "ssm:GetParametersByPath",
      "Resource": "arn:aws:ssm:us-east-1:<ACCOUNT_ID>:parameter/stealdeal/prod/*"
    }
  ]
}
```

Không cấp `ssm:PutParameter`, `ssm:DeleteParameter` hoặc `ssm:DeleteParameters` cho EC2 runtime role.

Nếu dùng customer-managed KMS key, thêm `kms:Decrypt` chỉ cho key đó.

### 10.2. Admin/deployment bootstrap policy

User hoặc role dùng để tạo/cập nhật parameter cần:

```text
ssm:PutParameter
ssm:DeleteParameter
ssm:GetParameter
ssm:GetParameters
ssm:GetParametersByPath
```

Nếu quản lý tags bằng IAM, thêm quyền tag tương ứng.

### 10.3. Done criteria

- EC2 đọc được path `/stealdeal/prod`.
- EC2 không ghi/xóa được parameter.
- Policy resource bị giới hạn ở `arn:aws:ssm:us-east-1:<ACCOUNT_ID>:parameter/stealdeal/prod/*`.

## 11. Step 7 — Tạo Parameter Store parameters

Tại Region `us-east-1`, tạo tám parameter sau:

```text
/stealdeal/prod/JWT_SECRET
/stealdeal/prod/AWS_ACCESS_KEY
/stealdeal/prod/AWS_SECRET_KEY
/stealdeal/prod/BREVO_API_KEY
/stealdeal/prod/GEOAPIFY_API_KEY
/stealdeal/prod/VNPAY_TMN_CODE
/stealdeal/prod/VNPAY_HASH_SECRET
/stealdeal/prod/MSSQL_SA_PASSWORD
```

Mỗi parameter phải có:

- Type: `SecureString`.
- Tier: `Standard`.
- Tags: `Application=stealdeal`, `Environment=prod`, `ManagedBy=parameter-store`.

Với `MSSQL_SA_PASSWORD`, lần đầu phải dùng password `sa` đang thực sự hoạt động với SQL volume hiện tại.

Không dùng lệnh `echo` hoặc command line có plaintext secret trong shell history.

### Done criteria

Có đủ tám parameter, đúng Region, đúng type, đúng tags và không có giá trị nào đi qua GitHub Actions log.

## 12. Step 8 — Bootstrap trên EC2

### 12.1. Tạo thư mục

```bash
sudo install -d -o root -g root -m 700 /opt/stealdeal/config
sudo install -d -o root -g root -m 700 /opt/stealdeal/scripts
```

### 12.2. Cài script

```bash
sudo install -o root -g root -m 700 \
  sync-backend-secrets.sh \
  /opt/stealdeal/scripts/sync-backend-secrets.sh
```

EC2 cần có `aws`, `jq`, `flock`, Docker và Docker Compose plugin.

### 12.3. Test IAM không làm lộ secret

```bash
aws ssm get-parameters-by-path \
  --region us-east-1 \
  --path /stealdeal/prod \
  --recursive \
  --with-decryption \
  --query 'length(Parameters)' \
  --output text
```

Kết quả mong đợi: `8`.

### 12.4. Chạy sync và kiểm tra permission

```bash
sudo /opt/stealdeal/scripts/sync-backend-secrets.sh
sudo stat -c '%U %G %a' /opt/stealdeal/config/backend.env
```

Kết quả mong đợi: `root root 600`.

Không chạy `cat backend.env`, `printenv` hoặc `docker inspect` theo cách in toàn bộ environment.

### Done criteria

- File runtime nằm ngoài Git checkout.
- File thuộc `root:root`, mode `600`.
- Script sync thành công và không in secret.

## 13. Step 9 — Deploy lần đầu

Tại thư mục Compose trên EC2:

```bash
docker compose \
  --env-file .env \
  --env-file /opt/stealdeal/config/backend.env \
  config --quiet
```

Sau đó thực hiện theo thứ tự:

1. ECR login.
2. Pull image.
3. Start `sqlserver`, `rabbitmq`, `redis`.
4. Chạy database migrator nếu cần.
5. Recreate API services.
6. Kiểm tra `docker compose ps`.
7. Kiểm tra health endpoint qua nginx/backend.

Không xóa volume `sqlserver-data`.

### Done criteria

- Compose config pass không warning missing variable.
- Sáu API running.
- Database migrator kết nối được SQL Server.
- Deploy source/compose không xóa `/opt/stealdeal/config/backend.env`.

## 14. Step 10 — Kiểm thử application

Thực hiện các test sau:

1. Login và nhận JWT.
2. Refresh token.
3. Dùng JWT gọi Cart, Order, Store và Payment.
4. Trigger gửi email qua Brevo.
5. Tìm địa chỉ bằng Geoapify.
6. Tạo VNPAY checkout URL.
7. Kiểm tra VNPAY return/IPN qua HTTPS.
8. Upload ảnh lên S3 cross-account.
9. Chạy database migration.

JWT secret phải giống nhau trên cả sáu API.

### Done criteria

JWT, Brevo, Geoapify, VNPAY, S3 và database đều hoạt động trên EC2.

## 15. Step 11 — Secret rotation và rollback

### 15.1. Rotation thông thường

1. Giữ credential cũ hoạt động.
2. Tạo credential mới ở provider.
3. Update parameter trong Parameter Store.
4. Chạy sync script.
5. Recreate các service bị ảnh hưởng.
6. Test flow liên quan.
7. Nếu đây là một đợt rotation, revoke credential cũ sau khi xác nhận thành công.
8. Nếu thất bại, restore parameter cũ và recreate lại service.

### 15.2. JWT rotation

JWT rotation sẽ invalidate token đang tồn tại. Thực hiện trong maintenance window và kiểm tra login/refresh sau deploy.

### 15.3. MSSQL password rotation

Không chỉ update `MSSQL_SA_PASSWORD` trong Parameter Store. Với SQL volume cũ, SQL Server có thể bỏ qua environment value mới.

Quy trình:

1. Backup database.
2. Dùng `ALTER LOGIN sa WITH PASSWORD=...` để đổi password thật trong SQL Server.
3. Update `/stealdeal/prod/MSSQL_SA_PASSWORD` ngay sau đó.
4. Sync `backend.env`.
5. Recreate các service dùng database và migrator.
6. Kiểm tra migration và API health.

Microsoft reference: <https://learn.microsoft.com/en-us/sql/linux/containers/troubleshoot?view=sql-server-ver16>

### Done criteria

- Secret-only refresh chạy được.
- Rollback parameter cũ chạy được.
- Image rollback không làm mất runtime env file.
- Nếu có rotation, credential cũ đã được revoke sau khi verification hoàn tất.

## 16. Validation checklist cuối cùng

### Parameter Store và IAM

- [ ] Tất cả parameter ở `us-east-1`.
- [ ] Tất cả dùng `SecureString`, tier `Standard`.
- [ ] EC2 role chỉ đọc đúng path.
- [ ] EC2 role không có quyền Put/Delete.
- [ ] Không có plaintext secret trong SSM output/log.

### Runtime và Compose

- [ ] `/opt/stealdeal/config/backend.env` tồn tại.
- [ ] File có owner `root:root`, mode `600`.
- [ ] Mọi Compose command production dùng cả `.env` và `backend.env`.
- [ ] `docker compose config --quiet` thành công.
- [ ] Compose fail-fast khi thiếu required secret.
- [ ] Container được recreate sau khi secret thay đổi.

### Application

- [ ] JWT login/refresh hoạt động.
- [ ] Brevo gửi email thành công.
- [ ] Geoapify tìm kiếm thành công.
- [ ] VNPAY checkout/return/IPN hoạt động.
- [ ] S3 upload cross-account hoạt động.
- [ ] Database migrator hoạt động.

### Deployment và rollback

- [ ] Deploy source không xóa runtime env file.
- [ ] Deploy compose không xóa runtime env file.
- [ ] Secret-only refresh không cần build image.
- [ ] Rollback image không làm mất secret.
- [ ] Có thể restore parameter cũ khi rotation lỗi.

## 17. Tiêu chí hoàn thành plan

Plan hoàn thành khi đồng thời đạt tất cả điều kiện:

1. Không còn secret thật trong application source/config.
2. EC2 tự lấy secret bằng IAM Role.
3. CD không truyền runtime secret qua GitHub Actions.
4. Tất cả service nhận đúng configuration section.
5. Secret refresh recreate đúng service mà không build image.
6. JWT, Brevo, Geoapify, VNPAY và S3 cross-account chạy thành công trên EC2.
7. Rotation và rollback đã được kiểm thử.

## 18. Việc ngoài phạm vi secret flow cần xác nhận

`docker-compose.yml` hiện dùng `MSSQL_PID: Developer`. Nếu đây là production thương mại, cần xác nhận license SQL Server hoặc chuyển sang edition/dịch vụ được cấp phép trước khi go-live.
