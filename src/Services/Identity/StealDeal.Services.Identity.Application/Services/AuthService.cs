using StealDeal.Services.Identity.Application.DTOs.Requests;
using StealDeal.Services.Identity.Application.DTOs.Responses;
using StealDeal.Services.Identity.Application.Services.Interfaces;
using StealDeal.Services.Identity.Domain.Interfaces.Repositories;
using StealDeal.Services.Identity.Domain.Models;

namespace StealDeal.Services.Identity.Application.Services
{
    public class AuthService : IAuthService
    {
        private readonly IUserRepository _userRepository;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IJwtTokenGenerator _jwtTokenGenerator;
        public AuthService(
            IUserRepository userRepository, 
            IRefreshTokenRepository refreshTokenRepository, 
            IUnitOfWork unitOfWork, IPasswordHasher passwordHasher, 
            IJwtTokenGenerator jwtTokenGenerator)
        {
            _userRepository = userRepository;
            _refreshTokenRepository = refreshTokenRepository;
            _unitOfWork = unitOfWork;
            _passwordHasher = passwordHasher;
            _jwtTokenGenerator = jwtTokenGenerator;
        }

        public async Task<TokenResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
        {
            ValidateLoginRequest(request);

            var normalizedEmail = NormalizeEmail(request.Email);
            var user = await _userRepository.GetByEmailAsync(normalizedEmail);

            if (user is null || user.IsDeleted || !user.IsActive)
            {
                throw new UnauthorizedAccessException("Invalid credentials.");
            }

            var isPasswordValid = _passwordHasher.Verify(user.PasswordHash, request.Password);
            if (!isPasswordValid)
            {
                throw new UnauthorizedAccessException("Invalid credentials.");
            }

            var response = await IssueTokenPairAsync(user);
            await _unitOfWork.SaveChangesAsync();

            return response;
        }

        public async Task<TokenResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                throw new UnauthorizedAccessException("Invalid refresh token.");
            }

            var refreshTokenHash = _jwtTokenGenerator.HashRefreshToken(refreshToken);
            var storedToken = await _refreshTokenRepository.GetByTokenHashAsync(refreshTokenHash);

            if (storedToken is null || storedToken.IsRevoked || storedToken.ExpiresAt <= DateTime.UtcNow)
            {
                throw new UnauthorizedAccessException("Invalid refresh token.");
            }

            if (storedToken.User is null || storedToken.User.IsDeleted || !storedToken.User.IsActive)
            {
                throw new UnauthorizedAccessException("Invalid refresh token.");
            }

            storedToken.IsRevoked = true;
            _refreshTokenRepository.Update(storedToken);

            var response = await IssueTokenPairAsync(storedToken.User);
            await _unitOfWork.SaveChangesAsync();

            return response;
        }

        public async Task<TokenResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
        {
            ValidateRegisterRequest(request);

            var normalizedEmail = NormalizeEmail(request.Email);
            var normalizedRole = NormalizeRole(request.Role);
            var fullName = BuildFullName(request.FirstName, request.LastName);

            var isEmailUnique = await _userRepository.IsEmailUniqueAsync(normalizedEmail);
            if (!isEmailUnique)
            {
                throw new InvalidOperationException("Email already exists.");
            }

            var user = new User
            {
                Email = normalizedEmail,
                PasswordHash = _passwordHasher.Hash(request.Password),
                FullName = fullName,
                Phone = request.Phone,
                IsEmailVerify = false,
                IsActive = true,
                IsDeleted = false
            };

            user.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                Role = normalizedRole
            });

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

            var response = await IssueTokenPairAsync(user);
            await _unitOfWork.SaveChangesAsync();

            return response;
        }

        private async Task<TokenResponse> IssueTokenPairAsync(User user)
        {
            var roles = user.UserRoles.Select(role => role.Role).ToList();
            var accessTokenExpiresAt = _jwtTokenGenerator.GetAccessTokenExpiresAt();
            var refreshTokenExpiresAt = _jwtTokenGenerator.GetRefreshTokenExpiresAt();
            var accessToken = _jwtTokenGenerator.GenerateAccessToken(user, roles);
            var rawRefreshToken = _jwtTokenGenerator.GenerateRefreshToken();
            var refreshTokenHash = _jwtTokenGenerator.HashRefreshToken(rawRefreshToken);

            var refreshTokenEntity = new RefreshToken
            {
                UserId = user.Id,
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

        private static void ValidateRegisterRequest(RegisterRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email))
            {
                throw new InvalidOperationException("Email is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            {
                throw new InvalidOperationException("Password must be at least 8 characters.");
            }

            if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
            {
                throw new InvalidOperationException("First name and last name are required.");
            }

            if (string.IsNullOrWhiteSpace(request.Role))
            {
                throw new InvalidOperationException("Role is required.");
            }
        }

        private static void ValidateLoginRequest(LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                throw new UnauthorizedAccessException("Invalid credentials.");
            }
        }

        private static string NormalizeEmail(string email)
        {
            return email.Trim().ToLowerInvariant();
        }

        private static string NormalizeRole(string role)
        {
            if (string.Equals(role, "Customer", StringComparison.OrdinalIgnoreCase))
            {
                return "Customer";
            }

            if (string.Equals(role, "Seller", StringComparison.OrdinalIgnoreCase))
            {
                return "Seller";
            }

            throw new InvalidOperationException("Role must be Customer or Seller.");
        }

        private static string BuildFullName(string firstName, string lastName)
        {
            return $"{firstName.Trim()} {lastName.Trim()}".Trim();
        }
    }
}
