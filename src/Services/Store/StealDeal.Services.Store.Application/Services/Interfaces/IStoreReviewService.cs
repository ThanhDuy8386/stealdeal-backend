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
        Task<PagedResult<StoreReviewResponse>> GetByStoreIdAsync(Guid storeId, int page, int pageSize);
        Task<PagedResult<StoreReviewResponse>> GetByBagIdAsync(Guid bagId, int page, int pageSize);
    }
}
