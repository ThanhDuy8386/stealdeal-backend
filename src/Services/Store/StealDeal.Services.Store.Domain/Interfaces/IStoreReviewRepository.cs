using StealDeal.Services.Store.Domain.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Domain.Interfaces
{
    public interface IStoreReviewRepository
    {
        Task<StoreReview?> GetByIdAsync(Guid id);
        Task<StoreReview?> GetByOrderAndBagAsync(Guid orderId, Guid bagId);
        Task AddAsync(StoreReview entity);
        void Update(StoreReview entity);
        void Delete(StoreReview entity);
        Task<(List<StoreReview> Items, int TotalCount)> GetByStoreIdAsync(
            Guid storeId, int page, int pageSize,
            int? ratingScore = null, bool? hasReply = null, string? search = null, bool? isReported = null    
        );
        // no isReported here since it's meant for buyers to see. Might add later if seller also need it
        Task<(List<StoreReview> Items, int TotalCount)> GetByBagIdAsync(
            Guid bagId, int page, int pageSize,
            int? ratingScore = null, bool? hasReply = null, string? search = null
        );

        Task<(List<StoreReview> Items, int TotalCount)> GetReportedAsync(int page, int pageSize);
        Task<(int Count, decimal Sum)> GetRatingStatsAsync(Guid storeId);
    }
}
