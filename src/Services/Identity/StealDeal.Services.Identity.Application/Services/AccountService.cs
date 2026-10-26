using StealDeal.Services.Identity.Application.DTOs.Requests;
using StealDeal.Services.Identity.Application.DTOs.Responses;
using StealDeal.Services.Identity.Application.Exceptions;
using StealDeal.Services.Identity.Application.Mappings;
using StealDeal.Services.Identity.Application.Services.Interfaces;
using StealDeal.Services.Identity.Domain.Interfaces.Repositories;
using StealDeal.Services.Identity.Domain.Models;
using System.Net;

namespace StealDeal.Services.Identity.Application.Services
{
    public class AccountService : IAccountService
    {
        private readonly IUserRepository _userRepository;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IS3StorageService _s3StorageService;

        public AccountService(
            IUserRepository userRepository,
            IRefreshTokenRepository refreshTokenRepository,
            IPasswordHasher passwordHasher,
            IUnitOfWork unitOfWork,
            IS3StorageService s3StorageService)
        {
            _userRepository = userRepository;
            _refreshTokenRepository = refreshTokenRepository;
            _passwordHasher = passwordHasher;
            _unitOfWork = unitOfWork;
            _s3StorageService = s3StorageService;
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

        public async Task<UserDetailResponse> UploadAvatarAsync(Guid userId, Stream fileStream, string originalFileName, string contentType, long fileSize, CancellationToken cancellationToken = default)
        {
            var user = await GetActiveUserAsync(userId);
            var folder = $"avatars/{userId}";
            var avatarUrl = await _s3StorageService.UploadImageAsync(fileStream, originalFileName, contentType, fileSize, folder, cancellationToken);

            user.AvatarUrl = avatarUrl;
            _userRepository.Update(user);

            await _unitOfWork.SaveChangesAsync();
            return user.ToUserDetailResponse();
        }

        public async Task<List<UserAddressResponse>> GetAddressesAsync(Guid userId)
        {
            var user = await GetActiveUserAsync(userId);

            return user.UserAddresses
                .OrderByDescending(a => a.IsDefault)
                .ThenByDescending(a => a.CreatedAt)
                .Select(ToAddressResponse)
                .ToList();
        }

        public async Task<UserAddressResponse> GetAddressAsync(Guid userId, Guid addressId)
        {
            var user = await GetActiveUserAsync(userId);
            var address = user.UserAddresses.FirstOrDefault(a => a.Id == addressId);
            if (address is null)
            {
                throw new NotFoundException("Address not found.");
            }

            return ToAddressResponse(address);
        }

        public async Task<UserAddressResponse> CreateAddressAsync(Guid userId, CreateUserAddressRequest request)
        {
            var user = await GetActiveUserAsync(userId);

            ValidateAddress(request.Address, request.Province, request.Commune, request.Latitude, request.Longitude);
            var shouldBeDefault = !user.UserAddresses.Any() || request.IsDefault;
            if (shouldBeDefault)
            {
                foreach (var existingAddress in user.UserAddresses)
                {
                    existingAddress.IsDefault = false;
                }
            }
            var address = new UserAddress
            {
                UserId = userId,
                Label = Required(request.Label, "Label"),
                Address = Required(request.Address, "Address"),
                Commune = Required(request.Commune, "Commune"),
                Province = Required(request.Province, "Province"),
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                IsDefault = shouldBeDefault
            };

            await _userRepository.AddAddressAsync(address);
            await _unitOfWork.SaveChangesAsync();

            return ToAddressResponse(address);
        }

        public async Task<UserAddressResponse> UpdateAddressAsync(Guid userId, Guid addressId, UpdateUserAddressRequest request)
        {
            var user = await GetActiveUserAsync(userId);
            var address = user.UserAddresses
                .FirstOrDefault(a => a.Id == addressId);
            if (address is null)
            {
                throw new NotFoundException("Address not found.");
            }

            ValidateAddress(request.Address, request.Province, request.Commune, request.Latitude, request.Longitude);

            var wasDefault = address.IsDefault;
            UserAddress? replacement = null;

            if (request.IsDefault)
            {
                foreach (var existingAddress in user.UserAddresses)
                {
                    existingAddress.IsDefault = false;
                }
            }
            else if (wasDefault)
            {
                replacement = user.UserAddresses
                    .Where(a => a.Id != addressId)
                    .OrderByDescending(a => a.CreatedAt)
                    .FirstOrDefault();

                if (replacement is not null)
                {
                    replacement.IsDefault = true;
                }
            }

            address.Label = Required(request.Label, "Label");
            address.Address = Required(request.Address, "Address");
            address.Province = Required(request.Province, "Province");
            address.Commune = Required(request.Commune, "Commune");
            address.Latitude = request.Latitude;
            address.Longitude = request.Longitude;

            address.IsDefault =
                request.IsDefault ||
                (wasDefault && replacement is null);

            await _unitOfWork.SaveChangesAsync();

            return ToAddressResponse(address);
        }

        public async Task DeleteAddressAsync(Guid userId, Guid addressId)
        {
            var user = await GetActiveUserAsync(userId);
            var address = user.UserAddresses.FirstOrDefault(a => a.Id == addressId);
            if (address is null)
            {
                throw new NotFoundException("Address not found.");
            }
            var replacement = address.IsDefault
                ? user.UserAddresses
                    .Where(a => a.Id != addressId)
                    .OrderByDescending(a => a.CreatedAt)
                    .FirstOrDefault()
                : null;

            if (replacement is not null)
                replacement.IsDefault = true;

            _userRepository.DeleteAddress(address);

            await _unitOfWork.SaveChangesAsync();
        }

        private static UserAddressResponse ToAddressResponse(UserAddress address)
        {
            return new UserAddressResponse
            {
                Id = address.Id,
                Label = address.Label,
                Address = address.Address,
                Province = address.Province,
                Commune = address.Commune,
                Latitude = address.Latitude,
                Longitude = address.Longitude,
                IsDefault = address.IsDefault
            };
        }

        private static string Required(string? value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new BadRequestException($"{fieldName} is required.");

            return value.Trim();
        }

        private static void ValidateAddress(
            string? address,
            string? province,
            string? commune,
            decimal? latitude,
            decimal? longitude)
        {
            Required(address, "Address");
            Required(province, "Province");
            Required(commune, "Commune");

            if (latitude.HasValue != longitude.HasValue)
                throw new BadRequestException(
                    "Latitude and longitude must be provided together.");

            if (latitude is < -90 or > 90)
                throw new BadRequestException("Latitude must be between -90 and 90.");

            if (longitude is < -180 or > 180)
                throw new BadRequestException(
                    "Longitude must be between -180 and 180.");
        }
    }
}
