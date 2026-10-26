# AWS Parameter Store Runtime Secrets Implementation Plan

## 1. Mục tiêu

Đưa các secret/config nhạy cảm của backend lên AWS Parameter Store và inject vào container khi deploy trên EC2, không commit secret vào Git và không đóng gói secret vào Docker image.

Phạm vi configuration section của application:

- `Aws`
- `Brevo`
- `Geoapify`
- `VnPay`
- `Jwt`

`Goong` không còn được sử dụng và không thuộc phạm vi triển khai này.

## 2. Quyết định kiến trúc

### 2.1. Secret store

Sử dụng AWS Systems Manager Parameter Store tại Region `us-east-1`.

Mỗi secret được lưu dưới dạng `SecureString`, tier `Standard`.

Application không tự gọi Parameter Store. Flow runtime là:

```text
AWS Parameter Store
        |
        v
EC2 IAM Role
        |
        v
SSM Run Command / deployment script
        |
        v
/opt/stealdeal/config/backend.env
        |
        v
docker compose --env-file ...
        |
        v
Container environment variables
        |
        v
ASP.NET Core IConfiguration
```

Ưu điểm của flow này:

- Không cần thêm AWS SDK hoặc Parameter Store provider vào application.
- Không đưa secret vào Dockerfile, image hoặc ECR.
- Không truyền giá trị secret qua GitHub Actions.
- Local vẫn có thể dùng .NET User Secrets.
- EC2 chỉ cần lấy secret khi deploy hoặc khi chủ động refresh secret.

### 2.2. S3 trong giai đoạn hiện tại

Giữ nguyên cách xác thực S3 bằng `AccessKeyId` và `SecretAccessKey` vì bucket hiện đang ở account khác. Hai giá trị này sẽ được lưu trong Parameter Store.

Chưa refactor S3 sang EC2 IAM Role trong phase này.

Phase migrate S3 sang bucket cùng account/Region và IAM Role sẽ được thực hiện riêng sau khi toàn bộ runtime secret flow ổn định.

## 3. Phân loại configuration

### 3.1. Secret cần lưu trong Parameter Store

Đề xuất parameter path:

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

`MSSQL_SA_PASSWORD` đã được sử dụng bởi Compose hiện tại; parameter này được giữ lại trong cùng runtime env file.

### 3.2. Configuration không nhạy cảm

Các giá trị không nhạy cảm có thể tiếp tục nằm trong `appsettings.json` hoặc Compose:

```text
Aws__Region
Aws__PublicBucketName
Brevo__BaseUrl
Brevo__FromEmail
Brevo__FromName
Geoapify__BaseUrl
Geoapify__TimeoutSeconds
Geoapify__CacheMinutes
VnPay__PaymentUrl
VnPay__ReturnUrl
VnPay__IpnUrl
VnPay__Version
VnPay__Command
VnPay__CurrCode
VnPay__Locale
VnPay__ExpireMinutes
Jwt__Issuer
Jwt__Audience
Jwt__AccessTokenMinutes
Jwt__RefreshTokenDays
```

Nếu muốn tách hoàn toàn production configuration khỏi repository, các giá trị này có thể đưa vào Parameter Store dạng `String` ở phase sau. Không cần thiết cho phase đầu.

## 4. Mapping giữa Parameter Store, env file và .NET configuration

Runtime file `/opt/stealdeal/config/backend.env` sử dụng tên biến trung gian:

```env
JWT_SECRET=...
AWS_ACCESS_KEY=...
AWS_SECRET_KEY=...
BREVO_API_KEY=...
GEOAPIFY_API_KEY=...
VNPAY_TMN_CODE=...
VNPAY_HASH_SECRET=...
MSSQL_SA_PASSWORD=...
```

Docker Compose map chúng vào đúng section của ASP.NET Core:

```yaml
identity-api:
  environment:
    Jwt__Secret: ${JWT_SECRET}
    Aws__AccessKeyId: ${AWS_ACCESS_KEY}
    Aws__SecretAccessKey: ${AWS_SECRET_KEY}

notification-api:
  environment:
    Jwt__Secret: ${JWT_SECRET}
    Brevo__ApiKey: ${BREVO_API_KEY}

store-api:
  environment:
    Jwt__Secret: ${JWT_SECRET}
    Aws__AccessKeyId: ${AWS_ACCESS_KEY}
    Aws__SecretAccessKey: ${AWS_SECRET_KEY}
    Geoapify__ApiKey: ${GEOAPIFY_API_KEY}

payment-api:
  environment:
    Jwt__Secret: ${JWT_SECRET}
    VnPay__TmnCode: ${VNPAY_TMN_CODE}
    VnPay__HashSecret: ${VNPAY_HASH_SECRET}
```

`cart-api` và `order-api` cũng cần nhận `Jwt__Secret` vì chúng dùng JWT validation.

Không map `Goong__ApiKey` vì Goong không còn được sử dụng.

## 5. IAM implementation

### 5.1. EC2 instance role

Bổ sung quyền tối thiểu cho role đang gắn với EC2:

```text
ssm:GetParametersByPath
```

Resource chỉ giới hạn ở:

```text
arn:aws:ssm:us-east-1:<account-id>:parameter/stealdeal/prod/*
```

Nếu dùng customer-managed KMS key thì bổ sung `kms:Decrypt` cho đúng key. Phase đầu có thể dùng AWS managed key của Parameter Store để giảm operational overhead.

### 5.2. Quyền của người tạo/cập nhật parameter

Người quản trị/deployment bootstrap cần quyền:

```text
ssm:PutParameter
ssm:DeleteParameter
ssm:GetParameter
ssm:GetParameters
```

Các quyền ghi parameter không cần cấp cho EC2 runtime role.

## 6. Bootstrap Parameter Store

Thực hiện một lần bằng AWS Console hoặc AWS CLI từ máy quản trị:

1. Tạo các parameter theo danh sách ở mục 3.1.
2. Chọn type `SecureString`.
3. Chọn Region `us-east-1`.
4. Gán tag, ví dụ:

```text
Application=stealdeal
Environment=prod
ManagedBy=parameter-store
```

5. Xác nhận EC2 role đọc được parameter mà không làm lộ giá trị trong terminal/log.

Không commit giá trị thật vào `.env.example`, `appsettings.json`, GitHub workflow hoặc tài liệu.

## 7. Runtime secret synchronization trên EC2

Tạo một script runtime, ví dụ:

```text
/opt/stealdeal/scripts/sync-backend-secrets.sh
```

Script cần:

1. Gọi `aws ssm get-parameters-by-path` với `--with-decryption`.
2. Lấy path `/stealdeal/prod`.
3. Convert parameter name thành biến trong `backend.env`.
4. Ghi file tạm ở cùng filesystem.
5. Validate các key bắt buộc không rỗng.
6. Set owner/permission:

```text
owner: root
mode: 600
```

7. Replace file chính bằng thao tác atomic.
8. Không in secret ra stdout, SSM output hoặc GitHub Actions log.

File runtime phải nằm ngoài Git checkout:

```text
/opt/stealdeal/config/backend.env
```

## 8. Thay đổi docker-compose.yml

### 8.1. Runtime environment

Thêm các mapping vào đúng service sử dụng configuration section tương ứng.

Các service cần `Jwt__Secret`:

```text
identity-api
notification-api
store-api
cart-api
order-api
payment-api
```

Các service cần secret riêng:

```text
identity-api: Aws
notification-api: Brevo
store-api: Aws, Geoapify
payment-api: VnPay
```

### 8.2. Required variable validation

Các secret bắt buộc nên dùng syntax fail-fast của Compose khi phù hợp:

```yaml
Brevo__ApiKey: ${BREVO_API_KEY:?BREVO_API_KEY is required}
```

Không để container khởi động với secret rỗng rồi chỉ phát hiện lỗi khi có request thật.

### 8.3. Production environment

Compose trên EC2 nên dùng:

```yaml
ASPNETCORE_ENVIRONMENT: Production
```

Local có thể override thành `Development` trong file env local.

Production container không phụ thuộc vào .NET User Secrets trên máy developer.

## 9. Thay đổi CI/CD

### 9.1. Không đưa secret vào CI

GitHub Actions chỉ gửi SSM command. Secret được EC2 lấy trực tiếp bằng instance role.

Không thêm API key của Brevo, VNPAY, Geoapify hoặc S3 vào GitHub Secrets trừ khi có mục đích khác rõ ràng.

### 9.2. Backend CD flow mới

Thứ tự đề xuất:

```text
SSM command bắt đầu
    -> sync-backend-secrets.sh
    -> cd tới Compose directory
    -> docker compose --env-file /opt/stealdeal/config/backend.env config --quiet
    -> ECR login
    -> pull image
    -> recreate service cần deploy
    -> health check
```

Tất cả lệnh `docker compose` trong CD phải dùng cùng runtime env file, gồm:

- `config`
- `pull`
- database migrator
- `up`
- `ps`

### 9.3. Secret-only deployment

Thêm manual deployment option:

```text
refresh_secrets: true/false
services: all hoặc danh sách service
```

Khi chỉ thay secret:

1. Sync Parameter Store.
2. Recreate các service bị ảnh hưởng.
3. Không cần build/push image mới.

Mapping service khi refresh:

```text
JWT_SECRET       -> tất cả API
AWS_*            -> identity-api, store-api
BREVO_API_KEY    -> notification-api
GEOAPIFY_API_KEY -> store-api
VNPAY_*          -> payment-api
```

## 10. S3 cross-account phase hiện tại

Giữ nguyên:

```text
Aws__AccessKeyId
Aws__SecretAccessKey
```

Các key này được inject từ Parameter Store trên EC2 và User Secrets khi chạy local.

Kiểm tra thêm:

- IAM user của account chứa bucket chỉ có quyền cần thiết trên bucket/object.
- Bucket policy cho phép đúng principal.
- Access key có giới hạn scope, không dùng root credential.
- Region thực tế của bucket.
- Nếu bucket khác Region với EC2, đánh giá chi phí cross-region transfer.

Account khác không tự động có nghĩa là bị tính phí transfer. Điều quan trọng là Region của EC2 và S3.

## 11. S3 migration phase sau

Chỉ thực hiện sau khi Parameter Store/runtime secret flow ổn định:

1. Tạo bucket mới trong account phù hợp và Region phù hợp.
2. Copy/migrate object và kiểm tra URL public.
3. Cấu hình bucket policy/IAM role cho EC2.
4. Sửa `S3StorageService` để dùng AWS SDK default credential chain thay vì `BasicAWSCredentials`.
5. Local dùng AWS profile hoặc User Secrets tùy môi trường.
6. Xóa `AccessKeyId` và `SecretAccessKey` khỏi application configuration.
7. Thu hồi access key cross-account cũ sau khi verify hoàn tất.

Việc đổi bucket không bắt buộc thay đổi application code nếu vẫn dùng static key. Thay đổi code chỉ cần thiết khi chuyển sang IAM Role/default credential chain.

## 12. Security cleanup trước khi triển khai

1. Xóa secret thật khỏi `appsettings.json`.
2. Nếu VNPAY hash secret hoặc credential đã từng commit và là credential thật, rotate credential trước khi dùng production.
3. Chỉ để placeholder trong `appsettings.json`.
4. Kiểm tra `.gitignore` không cho phép commit `.env`.
5. Không chạy lệnh in toàn bộ environment trong CI/CD.
6. Không dùng `docker compose config` theo cách làm lộ resolved secret ra log.
7. Không để secret xuất hiện trong exception message hoặc application log.

## 13. Validation checklist

### Parameter Store/IAM

- [ ] Parameters nằm ở `us-east-1`.
- [ ] Parameters dùng `SecureString` Standard.
- [ ] EC2 role đọc được đúng path.
- [ ] EC2 role không có quyền Put/Delete parameter.
- [ ] Không có secret trong SSM output.

### Compose/runtime

- [ ] `/opt/stealdeal/config/backend.env` tồn tại trên EC2.
- [ ] File có permission `600`.
- [ ] `docker compose --env-file ... config --quiet` thành công.
- [ ] Không còn warning thiếu biến bắt buộc.
- [ ] Container được recreate sau khi secret thay đổi.

### Application

- [ ] JWT login/refresh hoạt động.
- [ ] Brevo gửi email thành công.
- [ ] Geoapify tìm kiếm địa chỉ thành công.
- [ ] VNPAY tạo checkout URL thành công.
- [ ] VNPAY return/IPN qua HTTPS hoạt động.
- [ ] Upload ảnh lên S3 cross-account hoạt động.

### Deployment/rollback

- [ ] Deploy source code không xóa runtime env file.
- [ ] Deploy compose config không xóa runtime env file.
- [ ] Secret-only refresh không cần build image.
- [ ] Rollback image không làm mất secret.
- [ ] Có thể khôi phục parameter cũ khi rotate lỗi.

## 14. Tiêu chí hoàn thành

Implementation được xem là hoàn thành khi:

1. Không còn secret thật trong application source/config.
2. EC2 tự lấy secret bằng IAM Role.
3. CD không truyền secret qua GitHub Actions.
4. Tất cả service nhận đúng configuration section.
5. Secret refresh có thể recreate đúng service mà không build image.
6. Các flow JWT, Brevo, Geoapify, VNPAY và S3 cross-account chạy thành công trên EC2.
