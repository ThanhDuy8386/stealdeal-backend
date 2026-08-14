# Admin Role Split Changes

## Muc Tieu

Tach cac account co role `Admin`/`SuperAdmin` ra khoi bang `Users`, luu rieng trong bang `Admins`.

Sau thay doi nay:

- `Users` chi dung cho user/buyer/seller.
- `Admins` dung cho admin va super admin.
- Admin van co the dung cung email de dang ky buyer/user vi email chi unique rieng tung bang.
- Frontend user/buyer/seller hien tai van dung flow login cu.

## Thay Doi Database

Da them entity va table moi:

- `Admins`
- `AdminRoles`

Da cap nhat table lien quan:

- `Roles`
  - Them role moi: `SuperAdmin`.
- `RefreshTokens`
  - `UserId` chuyen sang nullable.
  - Them `AdminId` de refresh token co the thuoc ve admin account.

Migration moi:

- `20260814051030_SplitAdminAccounts`

Migration nay se:

- Tao bang `Admins`.
- Tao bang join `AdminRoles`.
- Them role `SuperAdmin` neu chua ton tai.
- Copy cac user cu dang co role `Admin` hoac `SuperAdmin` tu `Users` sang `Admins`.
- Copy role tu `UserRoles` sang `AdminRoles`.
- Chuyen refresh token cua admin cu tu `UserId` sang `AdminId`.
- Xoa cac admin cu khoi bang `Users`.

Lenh update database:

```powershell
dotnet ef database update -p Identity\StealDeal.Services.Identity.Infrastructure -s Identity\StealDeal.Services.Identity.API
```

Chay tu folder:

```powershell
src\Services
```

## Thay Doi Domain

Them file:

- `StealDeal.Services.Identity.Domain/Models/Admin.cs`
  - Entity moi cho admin/super admin.

Cap nhat file:

- `StealDeal.Services.Identity.Domain/Models/Role.cs`
  - Them navigation `ICollection<Admin> Admins`.

- `StealDeal.Services.Identity.Domain/Models/RefreshToken.cs`
  - Them `AdminId`.
  - Them navigation `Admin`.
  - Cho phep `UserId` nullable.

## Thay Doi Repository

Them file:

- `StealDeal.Services.Identity.Domain/Interfaces/Repositories/IAdminRepository.cs`
- `StealDeal.Services.Identity.Infrastructure/Repositories/AdminRepository.cs`

Cap nhat file:

- `StealDeal.Services.Identity.Infrastructure/Repositories/RoleRepository.cs`
  - Khi check role da duoc assign hay chua, check ca `Users` va `Admins`.

- `StealDeal.Services.Identity.Infrastructure/Repositories/RefreshTokenRepository.cs`
  - Include them `Admin.Roles` khi lay refresh token.

## Thay Doi Application Service

Them file Admin CRUD:

- `DTOs/Requests/CreateAdminRequest.cs`
- `DTOs/Requests/UpdateAdminRequest.cs`
- `DTOs/Requests/GetAdminsQueryRequest.cs`
- `DTOs/Responses/AdminResponse.cs`
- `DTOs/Responses/AdminDetailResponse.cs`
- `Mappings/AdminMapping.cs`
- `Services/Interfaces/IAdminService.cs`
- `Services/AdminService.cs`

Them file Admin Auth rieng:

- `Services/Interfaces/IAdminAuthService.cs`
- `Services/AdminAuthService.cs`

Cap nhat file:

- `Services/UserService.cs`
  - Khong cho tao/update user voi role `Admin` hoac `SuperAdmin`.
  - Neu muon quan ly admin thi dung Admin API rieng.

- `Services/RoleService.cs`
  - Them `SuperAdmin` vao danh sach system role.
  - Khong cho update/delete role `SuperAdmin`.

- `Services/Interfaces/IJwtTokenGenerator.cs`
  - Them overload tao access token cho `Admin`.

## Thay Doi Infrastructure

Cap nhat file:

- `Persistence/ApplicationDbContext.cs`
  - Them `DbSet<Admin> Admins`.
  - Config bang `Admins`.
  - Config many-to-many `Admin` - `Role` qua bang `AdminRoles`.
  - Config refresh token co the lien ket voi `Admin`.

- `Security/JwtTokenGenerator.cs`
  - Them overload generate access token cho `Admin`.
  - Access token cua admin co role `Admin`/`SuperAdmin`.

- `Migrations/ApplicationDbContextModelSnapshot.cs`
  - Cap nhat snapshot theo schema moi.

## Thay Doi API

Them controller:

- `Controllers/AdminController.cs`
  - CRUD admin.
  - Route: `/api/admin`
  - Require role: `Admin` hoac `SuperAdmin`.

- `Controllers/AdminAuthController.cs`
  - Login/refresh/logout/me rieng cho admin.
  - Route: `/api/admin-auth`

Admin login endpoint moi:

```http
POST /api/admin-auth/login
```

Body giu format cu:

```json
{
  "email": "admin@example.com",
  "password": "your-password"
}
```

Cap nhat controller cu:

- `Controllers/UserController.cs`
  - Cho phep ca `Admin` va `SuperAdmin` truy cap.

- `Controllers/RoleController.cs`
  - Cho phep ca `Admin` va `SuperAdmin` truy cap.

Cap nhat DI:

- `Program.cs`
  - Dang ky `IAdminRepository`.
  - Dang ky `IAdminService`.
  - Dang ky `IAdminAuthService`.

## Nhung Thu Duoc Giu Nguyen

- `AuthService` van la flow login cho user/buyer/seller.
- `AuthController` van dung endpoint cu:

```http
POST /api/auth/login
```

- `LoginRequest` van giu contract cu:

```json
{
  "email": "user@example.com",
  "password": "your-password"
}
```

Khong them field moi vao request login cu, de frontend user/buyer/seller khong can sua.

## Luu Y Frontend

Frontend hien tai van thay va quan ly buyer/seller qua endpoint user cu.

Neu truoc day dashboard lay admin bang cach query `GET /api/user` roi filter role `Admin`, thi bay gio admin se khong con nam trong response do nua.

Can tach frontend auth cho admin rieng, khong dung chung file/flow auth cua user.

Nen co file/service rieng, vi du:

- `AuthAdmin`
- `adminAuthService`
- `AdminAuthApi`

File nay handle cac request rieng cua admin:

```http
POST /api/admin-auth/login
POST /api/admin-auth/refresh
POST /api/admin-auth/logout
GET /api/admin-auth/me
```

Khong doi request body login cu. Admin login van gui:

```json
{
  "email": "admin@example.com",
  "password": "your-password"
}
```

Nhung endpoint phai doi tu:

```http
POST /api/auth/login
```

sang:

```http
POST /api/admin-auth/login
```

De hien thi/quan ly admin tren frontend, can map them endpoint moi:

```http
GET /api/admin
POST /api/admin
PUT /api/admin/{id}
DELETE /api/admin/{id}
```

Neu dashboard truoc day get user co role `Admin` bang endpoint cu:

```http
GET /api/user?Role=Admin
```

thi phai doi sang endpoint moi:

```http
GET /api/admin?Role=Admin
```

Hoac neu can lay super admin:

```http
GET /api/admin?Role=SuperAdmin
```

Nen truyen pagination hop ly:

```http
GET /api/admin?Role=Admin&Page=1&PageSize=10
```

Khong nen de Swagger default nhu:

```http
SearchTerm=string&AccountStatus=string&Page=2410&PageSize=2410
```

vi `SearchTerm=string` se filter theo email/full name co chu `string`, va `Page=2410` se skip rat nhieu record nen de tra list rong.

Checklist frontend can review:

- User/buyer/seller login van dung `POST /api/auth/login`.
- Admin/super admin login dung `POST /api/admin-auth/login`.
- User/buyer/seller list/dashboard van dung `/api/user`.
- Admin/super admin list/dashboard doi sang `/api/admin`.
- Neu co filter role `Admin` tren `/api/user`, doi sang `/api/admin?Role=Admin`.
- Neu co route/dashboard rieng cho super admin, dung `/api/admin?Role=SuperAdmin`.
- Neu frontend co service file dang gom chung auth, tach admin auth ra file rieng de tranh sua logic user hien tai.
```
