using StealDeal.Services.Identity.Application.DTOs.Requests;
using StealDeal.Services.Identity.Application.DTOs.Responses;
using StealDeal.Services.Identity.Application.Exceptions;
using StealDeal.Services.Identity.Application.Mappings;
using StealDeal.Services.Identity.Application.Services.Interfaces;
using StealDeal.Services.Identity.Domain.Interfaces.Repositories;
using StealDeal.Services.Identity.Domain.Models;

namespace StealDeal.Services.Identity.Application.Services
{
    public class AdminService : IAdminService
    {
        private readonly IAdminRepository _adminRepository;
        private readonly IRoleRepository _roleRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IPasswordHasher _passwordHasher;

        public AdminService(
            IAdminRepository adminRepository,
            IRoleRepository roleRepository,
            IUnitOfWork unitOfWork,
            IPasswordHasher passwordHasher)
        {
            _adminRepository = adminRepository;
            _roleRepository = roleRepository;
            _unitOfWork = unitOfWork;
            _passwordHasher = passwordHasher;
        }

        public async Task<AdminDetailResponse> CreateAdmin(CreateAdminRequest request)
        {
            ValidateCreateRequest(request);

            var normalizedEmail = NormalizeEmail(request.Email);
            await EnsureEmailIsUnique(normalizedEmail);

            var roles = NormalizeRoles(request.Roles);
            var roleEntities = await _roleRepository.GetRolesByNamesAsync(roles);
            if (roleEntities.Count != roles.Count)
                throw new BadRequestException("One or more roles do not exist.");

            var admin = new Admin
            {
                Email = normalizedEmail,
                PasswordHash = _passwordHasher.Hash(request.Password),
                FullName = request.FullName.Trim(),
                Phone = NormalizeOptional(request.Phone),
                AvatarUrl = NormalizeOptional(request.AvatarUrl),
                IsEmailVerified = true,
                IsActive = true,
                IsDeleted = false
            };

            foreach (var role in roleEntities)
            {
                admin.Roles.Add(role);
            }

            await _adminRepository.AddAsync(admin);
            await _unitOfWork.SaveChangesAsync();

            return admin.ToAdminDetailResponse();
        }

        public async Task<PagedResult<AdminResponse>> GetAdmins(GetAdminsQueryRequest request)
        {
            bool? isActive = request.AccountStatus?.ToLowerInvariant() switch
            {
                "active" => true,
                "inactive" => false,
                _ => null
            };

            var page = request.Page ?? 1;
            var pageSize = request.PageSize ?? 10;

            var (admins, totalCount) = await _adminRepository.GetAdminsAsync(
                request.SearchTerm,
                request.Role,
                isActive,
                page,
                pageSize);

            return new PagedResult<AdminResponse>
            {
                Items = admins.Select(admin => admin.ToAdminResponse()).ToList(),
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount
            };
        }

        public async Task<AdminDetailResponse> GetAdminDetail(Guid id)
        {
            var admin = await _adminRepository.GetByIdAsync(id);
            if (admin == null)
                throw new NotFoundException($"Admin with ID {id} not found.");

            return admin.ToAdminDetailResponse();
        }

        public async Task UpdateAdmin(Guid id, UpdateAdminRequest request)
        {
            var admin = await _adminRepository.GetByIdAsync(id);
            if (admin == null)
                throw new NotFoundException($"Admin with ID {id} not found.");

            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                var normalizedEmail = NormalizeEmail(request.Email);
                await EnsureEmailIsUnique(normalizedEmail, admin.Id);
                admin.Email = normalizedEmail;
            }

            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                if (request.Password.Length < 8)
                    throw new BadRequestException("Password must be at least 8 characters.");

                admin.PasswordHash = _passwordHasher.Hash(request.Password);
            }

            if (!string.IsNullOrWhiteSpace(request.FullName))
                admin.FullName = request.FullName.Trim();

            if (request.Phone != null)
                admin.Phone = NormalizeOptional(request.Phone);

            if (request.AvatarUrl != null)
                admin.AvatarUrl = NormalizeOptional(request.AvatarUrl);

            if (request.IsActive.HasValue)
                admin.IsActive = request.IsActive.Value;

            if (request.Roles != null)
            {
                var roles = NormalizeRoles(request.Roles);
                var roleEntities = await _roleRepository.GetRolesByNamesAsync(roles);
                if (roleEntities.Count != roles.Count)
                    throw new BadRequestException("One or more roles do not exist.");

                admin.Roles.Clear();
                foreach (var role in roleEntities)
                {
                    admin.Roles.Add(role);
                }
            }

            _adminRepository.Update(admin);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task DeleteAdmin(Guid id)
        {
            var admin = await _adminRepository.GetByIdAsync(id);
            if (admin == null)
                throw new NotFoundException($"Admin with ID {id} not found.");

            _adminRepository.Delete(admin);
            await _unitOfWork.SaveChangesAsync();
        }

        private async Task EnsureEmailIsUnique(string email, Guid? excludedAdminId = null)
        {
            if (!await _adminRepository.IsEmailUniqueAsync(email, excludedAdminId))
            {
                throw new ConflictException("Email already exists.");
            }
        }

        private static void ValidateCreateRequest(CreateAdminRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email))
                throw new BadRequestException("Email is required.");

            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
                throw new BadRequestException("Password must be at least 8 characters.");

            if (string.IsNullOrWhiteSpace(request.FullName))
                throw new BadRequestException("Full name is required.");
        }

        private static List<string> NormalizeRoles(List<string>? roles)
        {
            if (roles == null || roles.Count == 0)
                throw new BadRequestException("At least one role is required.");

            var normalizedRoles = new List<string>();

            foreach (var role in roles)
            {
                string normalizedRole;

                if (string.Equals(role?.Trim(), "Admin", StringComparison.OrdinalIgnoreCase))
                {
                    normalizedRole = "Admin";
                }
                else if (string.Equals(role?.Trim(), "SuperAdmin", StringComparison.OrdinalIgnoreCase))
                {
                    normalizedRole = "SuperAdmin";
                }
                else
                {
                    throw new BadRequestException($"Invalid admin role: {role}");
                }

                if (!normalizedRoles.Contains(normalizedRole))
                    normalizedRoles.Add(normalizedRole);
            }

            return normalizedRoles;
        }

        private static string NormalizeEmail(string email)
        {
            return email.Trim().ToLowerInvariant();
        }

        private static string? NormalizeOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
