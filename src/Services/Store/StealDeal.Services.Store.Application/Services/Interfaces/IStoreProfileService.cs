using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
        Task<List<StoreProfileResponse>> GetNearbyAsync(NearbyQueryRequest request);
        Task<List<PendingStoreResponse>> GetPendingVerificationAsync();
        // Admin only
        Task VerifyStoreAsync(Guid storeId);
        Task RejectPendingStoreAsync(Guid storeId);
        Task ToggleActiveAsync(Guid storeId);
    }
}
