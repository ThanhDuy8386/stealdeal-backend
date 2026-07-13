using StealDeal.Services.Store.Application.DTOs.Requests;
using StealDeal.Services.Store.Application.DTOs.Responses;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Application.Services.Interfaces
{
    public interface IStoreReviewService
    {
        Task<StoreReviewResponse> CreateAsync(Guid buyerId, CreateReviewRequest request);
        Task ReplyAsync(Guid reviewId, Guid ownerId, ReplyReviewRequest request);
        Task ReportAsync(Guid reviewId, Guid userId);
        Task<List<StoreReviewResponse>> GetByStoreIdAsync(Guid storeId);
        Task<List<StoreReviewResponse>> GetByBagIdAsync(Guid bagId);
    }
}
