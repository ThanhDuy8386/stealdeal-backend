using StealDeal.Services.Identity.Application.DTOs.Requests;
using StealDeal.Services.Identity.Application.DTOs.Responses;
using StealDeal.Services.Identity.Application.Exceptions;
using StealDeal.Services.Identity.Application.Mappings;
using StealDeal.Services.Identity.Application.Services.Interfaces;
using StealDeal.Services.Identity.Domain.Interfaces.Repositories;
using StealDeal.Services.Identity.Domain.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Identity.Application.Services
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly IRoleRepository _roleRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IPasswordHasher _passwordHasher;

        public UserService(
            IUserRepository userRepository,
            IRoleRepository roleRepository,
            IUnitOfWork unitOfWork,
            IPasswordHasher passwordHasher)
        {
            _userRepository = userRepository;
            _roleRepository = roleRepository;
            _unitOfWork = unitOfWork;
            _passwordHasher = passwordHasher;
        }

        public async Task<UserDetailResponse> CreateUser(AdminCreateUserRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email))
                throw new BadRequestException("Email is required.");

            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
                throw new BadRequestException("Password must be at least 8 characters.");

            if (string.IsNullOrWhiteSpace(request.FullName))
                throw new BadRequestException("Full name is required.");

            var roles = NormalizeRoles(request.Roles);
            var normalizedEmail = request.Email.Trim().ToLowerInvariant();

            if (!await _userRepository.IsEmailUniqueAsync(normalizedEmail))
                throw new ConflictException("Email already exists.");

            var user = new User
            {
                Email = normalizedEmail,
                PasswordHash = _passwordHasher.Hash(request.Password),
                FullName = request.FullName.Trim(),
                Phone = NormalizeOptional(request.Phone),
                IsEmailVerified = true,
                IsActive = true,
                IsDeleted = false
            };

            var roleEntities = await _roleRepository.GetOrCreateRolesByNamesAsync(roles);
            foreach (var role in roleEntities)
            {
                user.Roles.Add(role);
            }

            user.UserTrustScore = new UserTrustScore
            {
                UserId = user.Id,
                Score = 100,
                TotalOrders = 0,
                SuccessfulPickups = 0,
                NoShowCount = 0,
                DisputeCount = 0,
                LastCalculatedAt = DateTime.UtcNow
            };

            await _userRepository.AddAsync(user);
            await _unitOfWork.SaveChangesAsync();

            return user.ToUserDetailResponse();
        }

        public async Task<PagedResult<UserResponse>> GetUsers(GetUsersQueryRequest request)
        {
            // these can be nulls
            bool? isActive = request.AccountStatus?.ToLower() switch
            {
                "active" => true,
                "inactive" => (bool?)false,
                _ => null
            };

            // this can't be null, so need default values
            int page = request.Page ?? 1;
            int pageSize = request.PageSize ?? 10;

            var (users, totalCount) = await _userRepository.GetUsersAsync(request.SearchTerm, request.Role, isActive, page, pageSize);

            var userResponses = users.Select(u => u.ToUserResponse()).ToList();

            return new PagedResult<UserResponse>
            {
                Items = userResponses,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount
            };
        }

        public async Task<UserDetailResponse> GetUserDetail(Guid id)
        {
            var user = await _userRepository.GetByIdAsync(id);
            if (user == null)
            {
                throw new NotFoundException($"User with ID {id} not found.");
            }

            var userDetailResponse = user.ToUserDetailResponse();

            return userDetailResponse;
        }

        public async Task UpdateUser(Guid id, AdminUpdateUserRequest request)
        {
            var user = await _userRepository.GetByIdAsync(id);
            if (user == null)
            {
                throw new NotFoundException($"User with ID {id} not found.");
            }

            if (!String.IsNullOrWhiteSpace(request.FullName))
            {
                user.FullName = request.FullName;
            }

            if (!String.IsNullOrWhiteSpace(request.Email))
            {
                user.Email = request.Email;
            }

            if (!String.IsNullOrWhiteSpace(request.Phone))
            {
                user.Phone = request.Phone;
            }

            if (request.IsActive.HasValue)
            {
                user.IsActive = request.IsActive.Value;
            }

            if (request.Roles != null && request.Roles.Count > 0)
            {
                var roles = NormalizeRoles(request.Roles);
                var roleEntities = await _roleRepository.GetOrCreateRolesByNamesAsync(roles);

                user.Roles.Clear();
                foreach (var role in roleEntities)
                {
                    user.Roles.Add(role);
                }
            }

            _userRepository.Update(user);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task DeleteUser(Guid id)
        {
            var user = await _userRepository.GetByIdAsync(id);
            if (user == null)
            {
                throw new NotFoundException($"User with ID {id} not found.");
            }
            _userRepository.Delete(user);
            await _unitOfWork.SaveChangesAsync();
        }

        private static List<string> NormalizeRoles(List<string>? roles)
        {
            if (roles == null || roles.Count == 0)
                throw new BadRequestException("At least one role is required.");

            var normalizedRoles = new List<string>();

            foreach (var role in roles)
            {
                string normalizedRole;

                if (string.Equals(role?.Trim(), "Customer", StringComparison.OrdinalIgnoreCase))
                {
                    normalizedRole = "Customer";
                }
                else if (string.Equals(role?.Trim(), "Seller", StringComparison.OrdinalIgnoreCase))
                {
                    normalizedRole = "Seller";
                }
                else if (string.Equals(role?.Trim(), "Admin", StringComparison.OrdinalIgnoreCase))
                {
                    normalizedRole = "Admin";
                }
                else
                {
                    throw new BadRequestException($"Invalid role: {role}");
                }

                if (!normalizedRoles.Contains(normalizedRole))
                    normalizedRoles.Add(normalizedRole);
            }

            return normalizedRoles;
        }

        private static string? NormalizeOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
