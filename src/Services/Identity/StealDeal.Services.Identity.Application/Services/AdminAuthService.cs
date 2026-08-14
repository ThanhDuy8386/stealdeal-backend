using StealDeal.Services.Identity.Application.DTOs.Requests;
using StealDeal.Services.Identity.Application.DTOs.Responses;
using StealDeal.Services.Identity.Application.Exceptions;
using StealDeal.Services.Identity.Application.Services.Interfaces;
using StealDeal.Services.Identity.Domain.Interfaces.Repositories;
using StealDeal.Services.Identity.Domain.Models;

namespace StealDeal.Services.Identity.Application.Services
{
    public class AdminAuthService : IAdminAuthService
    {
        private readonly IAdminRepository _adminRepository;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IJwtTokenGenerator _jwtTokenGenerator;

        public AdminAuthService(
            IAdminRepository adminRepository,
            IRefreshTokenRepository refreshTokenRepository,
            IUnitOfWork unitOfWork,
            IPasswordHasher passwordHasher,
            IJwtTokenGenerator jwtTokenGenerator)
        {
            _adminRepository = adminRepository;
            _refreshTokenRepository = refreshTokenRepository;
            _unitOfWork = unitOfWork;
            _passwordHasher = passwordHasher;
            _jwtTokenGenerator = jwtTokenGenerator;
        }

        public async Task<TokenResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
        {
            ValidateLoginRequest(request);

            var normalizedEmail = NormalizeEmail(request.Email);
            var admin = await _adminRepository.GetByEmailAsync(normalizedEmail);

            if (admin is null || admin.IsDeleted || !admin.IsActive || !HasAdminRole(admin))
            {
                throw new UnauthorizedException("Invalid credentials.");
            }

            var isPasswordValid = _passwordHasher.Verify(admin.PasswordHash, request.Password);
            if (!isPasswordValid)
            {
                throw new UnauthorizedException("Invalid credentials.");
            }

            var response = await IssueTokenPairAsync(admin);
            await _unitOfWork.SaveChangesAsync();

            return response;
        }

        public async Task<TokenResponse> RefreshAsync(RefreshTokenRequest refreshToken, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(refreshToken.RefreshToken))
            {
                throw new UnauthorizedException("Invalid refresh token.");
            }

            var refreshTokenHash = _jwtTokenGenerator.HashRefreshToken(refreshToken.RefreshToken);
            var storedToken = await _refreshTokenRepository.GetByTokenHashAsync(refreshTokenHash);

            if (storedToken is null || storedToken.IsRevoked || storedToken.ExpiresAt <= DateTime.UtcNow)
            {
                throw new UnauthorizedException("Invalid refresh token.");
            }

            if (storedToken.Admin is null || storedToken.Admin.IsDeleted || !storedToken.Admin.IsActive || !HasAdminRole(storedToken.Admin))
            {
                throw new UnauthorizedException("Invalid refresh token.");
            }

            storedToken.IsRevoked = true;
            storedToken.RevokedAt = DateTime.UtcNow;
            _refreshTokenRepository.Update(storedToken);

            var response = await IssueTokenPairAsync(storedToken.Admin);
            await _unitOfWork.SaveChangesAsync();

            return response;
        }

        public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return;
            }

            var refreshTokenHash = _jwtTokenGenerator.HashRefreshToken(refreshToken);
            var storedToken = await _refreshTokenRepository.GetByTokenHashAsync(refreshTokenHash);

            if (storedToken is null || storedToken.IsRevoked || storedToken.ExpiresAt <= DateTime.UtcNow)
            {
                return;
            }

            if (storedToken.Admin is null)
            {
                return;
            }

            storedToken.IsRevoked = true;
            storedToken.RevokedAt = DateTime.UtcNow;

            _refreshTokenRepository.Update(storedToken);
            await _unitOfWork.SaveChangesAsync();
        }

        private async Task<TokenResponse> IssueTokenPairAsync(Admin admin)
        {
            var roles = admin.Roles.Select(role => role.Name).ToList();
            var accessTokenExpiresAt = _jwtTokenGenerator.GetAccessTokenExpiresAt();
            var refreshTokenExpiresAt = _jwtTokenGenerator.GetRefreshTokenExpiresAt();
            var accessToken = _jwtTokenGenerator.GenerateAccessToken(admin, roles);
            var rawRefreshToken = _jwtTokenGenerator.GenerateRefreshToken();
            var refreshTokenHash = _jwtTokenGenerator.HashRefreshToken(rawRefreshToken);

            var refreshTokenEntity = new RefreshToken
            {
                AdminId = admin.Id,
                TokenHash = refreshTokenHash,
                ExpiresAt = refreshTokenExpiresAt,
                IsRevoked = false
            };

            await _refreshTokenRepository.AddAsync(refreshTokenEntity);

            return new TokenResponse
            {
                AccessToken = accessToken,
                RefreshToken = rawRefreshToken,
                AccessTokenExpiresAt = accessTokenExpiresAt,
                RefreshTokenExpiresAt = refreshTokenExpiresAt
            };
        }

        private static void ValidateLoginRequest(LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                throw new UnauthorizedException("Invalid credentials.");
            }
        }

        private static string NormalizeEmail(string email)
        {
            return email.Trim().ToLowerInvariant();
        }

        private static bool HasAdminRole(Admin admin)
        {
            return admin.Roles.Any(role =>
                string.Equals(role.Name, "Admin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(role.Name, "SuperAdmin", StringComparison.OrdinalIgnoreCase));
        }
    }
}
