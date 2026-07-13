using StealDeal.Services.Store.Application.DTOs.Requests;
using StealDeal.Services.Store.Application.DTOs.Responses;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Application.Services.Interfaces
{
    public interface ISurpriseBagService
    {
        Task<SurpriseBagResponse> CreateAsync(Guid ownerId, CreateBagRequest request);
        Task<SurpriseBagResponse> UpdateAsync(Guid bagId, Guid ownerId, UpdateBagRequest request);
        Task DeleteAsync(Guid bagId);
        Task<SurpriseBagResponse> GetByIdAsync(Guid id);
        Task<List<SurpriseBagResponse>> GetAllAsync();        // có thể thêm filter/paging sau
        Task<List<SurpriseBagResponse>> GetByStoreIdAsync(Guid storeId);
        Task UpdateStatusAsync(Guid bagId, Guid ownerId, string status);
    }
}
