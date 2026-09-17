using StealDeal.Services.Store.Application.DTOs.Requests;
using StealDeal.Services.Store.Application.DTOs.Responses;
using System;
using System.Threading.Tasks;

namespace StealDeal.Services.Store.Application.Services.Interfaces
{
    public interface IStoreReviewService
    {
        Task<StoreReviewResponse> CreateAsync(Guid buyerId, string buyerName, CreateReviewRequest request);
        Task ReplyAsync(Guid reviewId, Guid ownerId, ReplyReviewRequest request);
        Task ReportAsync(Guid reviewId, Guid userId);
        Task<PagedResult<StoreReviewResponse>> GetByStoreIdAsync(Guid storeId, ReviewFilterRequest filter);
        Task<PagedResult<StoreReviewResponse>> GetByBagIdAsync(Guid bagId, ReviewFilterRequest filter);
        Task DeleteReplyAsync(Guid reviewId, Guid ownerId);
        Task UnreportAsync(Guid reviewId, Guid userId, bool isAdmin);
        Task DeleteReviewAsync(Guid reviewId);
        Task<PagedResult<StoreReviewResponse>> GetByStoreOwnerAsync(Guid ownerId, ReviewFilterRequest filter);
        Task<PagedResult<StoreReviewResponse>> GetReportedAsync(int page, int pageSize);
    }
}
