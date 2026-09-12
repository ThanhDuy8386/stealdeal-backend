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
        Task<(List<StoreReview> Items, int TotalCount)> GetByStoreIdAsync(Guid storeId, int page, int pageSize);
        Task<(List<StoreReview> Items, int TotalCount)> GetByBagIdAsync(Guid bagId, int page, int pageSize);
    }
}
