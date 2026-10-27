using StealDeal.Services.Identity.Application.DTOs.Requests;
using StealDeal.Services.Identity.Application.DTOs.Responses;

namespace StealDeal.Services.Identity.Application.Services.Interfaces
{
    public interface IAccountService
    {
        Task<UserDetailResponse> GetProfileAsync(Guid userId);
        Task<UserDetailResponse> UpdateProfileAsync(Guid userId, UpdateMyProfileRequest request);
        Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request);
        Task<UserDetailResponse> UploadAvatarAsync(Guid userId, Stream fileStream, string originalFileName, string contentType, long fileSize, CancellationToken cancellationToken = default);
        Task<List<UserAddressResponse>> GetAddressesAsync(Guid userId);
        Task<UserAddressResponse> GetAddressAsync(Guid userId, Guid addressId);
        Task<UserAddressResponse> CreateAddressAsync(Guid userId, CreateUserAddressRequest request);
        Task<UserAddressResponse> UpdateAddressAsync(Guid userId, Guid addressId, UpdateUserAddressRequest request);
        Task DeleteAddressAsync(Guid userId, Guid addressId);
    }
}
