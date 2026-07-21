using System.Text.Json;
using StealDeal.Services.Identity.Application.DTOs.Events;
using StealDeal.Services.Identity.Application.DTOs.Requests;
using StealDeal.Services.Identity.Application.DTOs.Responses;
using StealDeal.Services.Identity.Application.Exceptions;
using StealDeal.Services.Identity.Application.Services.Interfaces;
using StealDeal.Services.Identity.Domain.Interfaces.Repositories;
using StealDeal.Services.Identity.Domain.Models;

namespace StealDeal.Services.Identity.Application.Services
{
    public class AuthService : IAuthService
    {
        private readonly IUserRepository _userRepository;
        private readonly IRoleRepository _roleRepository;
        private readonly IEmailVerificationRepository _emailVerificationRepository;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IJwtTokenGenerator _jwtTokenGenerator;
        private readonly IOutboxMessageRepository _outboxMessageRepository;
        public AuthService(
            IUserRepository userRepository,
            IRoleRepository roleRepository,
            IEmailVerificationRepository emailVerificationRepository,
            IRefreshTokenRepository refreshTokenRepository, 
            IUnitOfWork unitOfWork, IPasswordHasher passwordHasher, 
            IJwtTokenGenerator jwtTokenGenerator,
            IOutboxMessageRepository outboxMessageRepository)
        {
            _userRepository = userRepository;
            _roleRepository = roleRepository;
            _emailVerificationRepository = emailVerificationRepository;
            _refreshTokenRepository = refreshTokenRepository;
            _unitOfWork = unitOfWork;
            _passwordHasher = passwordHasher;
            _jwtTokenGenerator = jwtTokenGenerator;
            _outboxMessageRepository = outboxMessageRepository;
        }

        public async Task<TokenResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
        {
            ValidateLoginRequest(request);

            var normalizedEmail = NormalizeEmail(request.Email);
            var user = await _userRepository.GetByEmailAsync(normalizedEmail);

            if (user is null || user.IsDeleted || !user.IsActive)
            {
                throw new UnauthorizedException("Invalid credentials.");
            }

            var isPasswordValid = _passwordHasher.Verify(user.PasswordHash, request.Password);
            if (!isPasswordValid)
            {
                throw new UnauthorizedException("Invalid credentials.");
            }

            var response = await IssueTokenPairAsync(user);
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

            if (storedToken.User is null || storedToken.User.IsDeleted || !storedToken.User.IsActive)
            {
                throw new UnauthorizedException("Invalid refresh token.");
            }

            storedToken.IsRevoked = true;
            storedToken.RevokedAt = DateTime.UtcNow;
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
                throw new ConflictException("Email already exists.");
            }

            var user = new User
            {
                Email = normalizedEmail,
                PasswordHash = _passwordHasher.Hash(request.Password),
                FullName = fullName,
                Phone = request.Phone,
                IsEmailVerified = false,
                IsActive = true,
                IsDeleted = false
            };

            var roles = await _roleRepository.GetOrCreateRolesByNamesAsync([normalizedRole]);
            user.Roles.Add(roles.Single());

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

            // OTP for email verification
            var otp = GenerateOtp();
            var otpHash = HashOtp(otp);
            var otpExpiresAt = DateTime.UtcNow.AddMinutes(10);
            var emailVerification = new EmailVerification
            {
                UserId = user.Id,
                OtpHash = otpHash,
                ExpiresAt = otpExpiresAt,
                ConsumedAt = null,
                RevokedAt = null,
                AttemptCount = 0,
                ResendCount = 0
            };
            user.EmailVerifications.Add(emailVerification);
            
            // Outbox message for sending OTP email
            var payload = JsonSerializer.Serialize(new SendEmailVerificationOtpEvent
            {
                UserId = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                Otp = otp,
                ExpiresAt = otpExpiresAt
            });

            await _outboxMessageRepository.AddAsync(new OutboxMessage
            {
                ExchangeName = "stealdeal.events",
                ExchangeType = "topic",
                RoutingKey = "identity.user.email-verification.requested",
                EventType = "SendEmailVerificationOtpEvent",
                Payload = payload,
                Status = "Pending"
            });

            await _userRepository.AddAsync(user);

            var response = await IssueTokenPairAsync(user);
            await _unitOfWork.SaveChangesAsync();

            return response;
        }

        public async Task VerifyEmailOtpAsync(VerifyEmailOtpRequest request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request.Email))
            {
                throw new BadRequestException("Email is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Otp))
            {
                throw new BadRequestException("OTP is required.");
            }

            var normalizedEmail = NormalizeEmail(request.Email);
            var otpHash = HashOtp(request.Otp);
            var verification  = await _emailVerificationRepository
                .VerifyOtp(normalizedEmail, otpHash);
            if (verification is null)
            {
                throw new BadRequestException("Invalid or expired OTP.");
            }
            verification.ConsumedAt = DateTime.UtcNow;
            verification.User.IsEmailVerified = true;
            _emailVerificationRepository.Update(verification);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task ResendOtpAsync(ResendOtpRequest request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request.Email))
            {
                throw new BadRequestException("Email is required.");
            }
            var normalizedEmail = NormalizeEmail(request.Email);
            var user = await _userRepository.GetByEmailAsync(normalizedEmail);
            if (user is null)
            {
                throw new BadRequestException("User not found.");
            }
            if (user.IsEmailVerified)
            {
                throw new ConflictException("Email is already verified.");
            }
            var activeVerification = await _emailVerificationRepository.GetActiveOtpByUserIdAsync(user.Id);
            if (activeVerification != null)
            {
                activeVerification.RevokedAt = DateTime.UtcNow;
                _emailVerificationRepository.Update(activeVerification);
            }
            var otp = GenerateOtp();
            var otpHash = HashOtp(otp);
            var otpExpiresAt = DateTime.UtcNow.AddMinutes(10);
            // var emailVerification = new EmailVerification
            // {
            //     UserId = user.Id,
            //     OtpHash = otpHash,
            //     ExpiresAt = otpExpiresAt,
            //     AttemptCount = 0,
            // };
            var emailVerification = new EmailVerification
            {
                UserId = user.Id,
                OtpHash = otpHash,
                ExpiresAt = otpExpiresAt,
                ConsumedAt = null,
                RevokedAt = null,
                AttemptCount = 0,
                ResendCount = 0
            };
            await _emailVerificationRepository.AddAsync(emailVerification);
            var payload = JsonSerializer.Serialize(new SendEmailVerificationOtpEvent
            {
                UserId = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                Otp = otp,
                ExpiresAt = otpExpiresAt
            });

            await _outboxMessageRepository.AddAsync(new OutboxMessage
            {
                ExchangeName = "stealdeal.events",
                ExchangeType = "topic",
                RoutingKey = "identity.user.email-verification.requested",
                EventType = "SendEmailVerificationOtpEvent",
                Payload = payload,
                Status = "Pending"
            });
            await _unitOfWork.SaveChangesAsync();
        }

        private async Task<TokenResponse> IssueTokenPairAsync(User user)
        {
            var roles = user.Roles.Select(role => role.Name).ToList();
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
                throw new BadRequestException("Email is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            {
                throw new BadRequestException("Password must be at least 8 characters.");
            }

            if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
            {
                throw new BadRequestException("First name and last name are required.");
            }

            if (string.IsNullOrWhiteSpace(request.Role))
            {
                throw new BadRequestException("Role is required.");
            }
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

            throw new BadRequestException("Role must be Customer or Seller.");
        }

        private static string BuildFullName(string firstName, string lastName)
        {
            return $"{firstName.Trim()} {lastName.Trim()}".Trim();
        }

        private static string GenerateOtp()
        {
            return Random.Shared.Next(100000, 1000000).ToString();
        }

        private static string HashOtp(string otp)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(otp);
            var hash = System.Security.Cryptography.SHA256.HashData(bytes);
            return Convert.ToHexString(hash);
        }

        public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            if(string.IsNullOrWhiteSpace(refreshToken))
            {
                return;
            }

            var refreshTokenHash = _jwtTokenGenerator.HashRefreshToken(refreshToken);
            var storedToken = await _refreshTokenRepository.GetByTokenHashAsync(refreshTokenHash);

            if(storedToken is null || storedToken.IsRevoked || storedToken.ExpiresAt <= DateTime.UtcNow)
            {
                return;
            }

            storedToken.IsRevoked = true;
            storedToken.RevokedAt = DateTime.UtcNow;

            _refreshTokenRepository.Update(storedToken);
            await _unitOfWork.SaveChangesAsync();
        }
    }
}
