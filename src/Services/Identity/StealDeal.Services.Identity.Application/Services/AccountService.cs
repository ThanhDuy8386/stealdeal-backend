using StealDeal.Services.Identity.Application.DTOs.Requests;
using StealDeal.Services.Identity.Application.DTOs.Responses;
using StealDeal.Services.Identity.Application.Exceptions;
using StealDeal.Services.Identity.Application.Mappings;
using StealDeal.Services.Identity.Application.Services.Interfaces;
using StealDeal.Services.Identity.Domain.Interfaces.Repositories;
using StealDeal.Services.Identity.Domain.Models;

namespace StealDeal.Services.Identity.Application.Services
{
    public class AccountService : IAccountService
    {
        private readonly IUserRepository _userRepository;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IUnitOfWork _unitOfWork;

        public AccountService(
            IUserRepository userRepository,
            IRefreshTokenRepository refreshTokenRepository,
            IPasswordHasher passwordHasher,
            IUnitOfWork unitOfWork)
        {
            _userRepository = userRepository;
            _refreshTokenRepository = refreshTokenRepository;
            _passwordHasher = passwordHasher;
            _unitOfWork = unitOfWork;
        }

        public async Task<UserDetailResponse> GetProfileAsync(Guid userId)
        {
            var user = await GetActiveUserAsync(userId);
            return user.ToUserDetailResponse();
        }

        public async Task<UserDetailResponse> UpdateProfileAsync(Guid userId, UpdateMyProfileRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.FullName))
                throw new BadRequestException("Full name is required.");

            var user = await GetActiveUserAsync(userId);

            user.FullName = request.FullName.Trim();
            user.Phone = NormalizeOptional(request.Phone);
            user.AvatarUrl = NormalizeOptional(request.AvatarUrl);

            _userRepository.Update(user);

            await _unitOfWork.SaveChangesAsync();
            return user.ToUserDetailResponse();
        }

        public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.CurrentPassword))
                throw new BadRequestException("Current password is required.");

            if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
                throw new BadRequestException("New password must be at least 8 characters.");

            var user = await GetActiveUserAsync(userId);

            if (!_passwordHasher.Verify(user.PasswordHash, request.CurrentPassword))
                throw new BadRequestException("Current password is incorrect.");

            if (_passwordHasher.Verify(user.PasswordHash, request.NewPassword))
                throw new BadRequestException("New password must be different from the current password.");

            user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
            _userRepository.Update(user);

            var activeRefreshTokens = await _refreshTokenRepository.GetActiveRefreshTokensByUserIdAsync(userId);
            foreach (var refreshToken in activeRefreshTokens)
            {
                refreshToken.IsRevoked = true;
                refreshToken.RevokedAt = DateTime.UtcNow;
                _refreshTokenRepository.Update(refreshToken);
            }

            await _unitOfWork.SaveChangesAsync();
        }

        private async Task<User> GetActiveUserAsync(Guid userId)
        {
            var user = await _userRepository.GetByIdAsync(userId);

            if (user is null || user.IsDeleted || !user.IsActive)
                throw new UnauthorizedException("Account is inactive or no longer exists.");

            return user;
        }

        private static string? NormalizeOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
