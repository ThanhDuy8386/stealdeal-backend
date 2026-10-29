using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StealDeal.Services.Store.Application.DTOs.Requests;
using StealDeal.Services.Store.Application.DTOs.Responses;

namespace StealDeal.Services.Store.Application.Services.Interfaces
{
    public interface ISurpriseBagService
    {
        Task<SurpriseBagResponse> CreateAsync(Guid ownerId, CreateBagRequest request, FileUploadRequest? image = null, CancellationToken cancellationToken = default);
        Task<SurpriseBagResponse> UpdateAsync(Guid bagId, Guid ownerId, UpdateBagRequest request, FileUploadRequest? image = null, CancellationToken cancellationToken = default);
        Task DeleteAsync(Guid bagId);
        Task<SurpriseBagResponse> GetByIdAsync(Guid id);
        Task<List<SurpriseBagResponse>> GetAllAsync();        // có thể thêm filter/paging sau
        Task<List<SurpriseBagResponse>> GetByStoreIdAsync(Guid storeId);
        Task<List<SurpriseBagResponse>> GetNearbyAsync(NearbyQueryRequest request);
        Task UpdateStatusAsync(Guid bagId, Guid ownerId, string status);
    }
}
