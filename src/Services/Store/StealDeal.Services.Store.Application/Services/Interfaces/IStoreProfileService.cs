using StealDeal.Services.Store.Application.DTOs.Requests;
using StealDeal.Services.Store.Application.DTOs.Responses;

namespace StealDeal.Services.Store.Application.Services.Interfaces
{
    public interface IStoreProfileService
    {
        Task<StoreProfileResponse> CreateAsync(Guid ownerId, CreateStoreRequest request);
        Task<StoreProfileResponse> UpdateAsync(Guid storeId, Guid ownerId, UpdateStoreRequest request);
        Task<StoreProfileResponse> GetByIdAsync(Guid id);
        Task<StoreProfileResponse> GetMyStoreAsync(Guid ownerId);
        Task<List<StoreProfileResponse>> GetAllAsync();

        // Admin only
        Task VerifyStoreAsync(Guid storeId);
        Task ToggleActiveAsync(Guid storeId);
    }
}
