using StealDeal.Services.Identity.Application.DTOs.Requests;
using StealDeal.Services.Identity.Application.DTOs.Responses;

namespace StealDeal.Services.Identity.Application.Services.Interfaces
{
    public interface IAccountService
    {
        Task<UserDetailResponse> GetProfileAsync(Guid userId);
        Task<UserDetailResponse> UpdateProfileAsync(Guid userId, UpdateMyProfileRequest request);
        Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request);
    }
}
