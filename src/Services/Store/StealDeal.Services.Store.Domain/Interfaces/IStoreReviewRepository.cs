using StealDeal.Services.Store.Domain.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Domain.Interfaces
{
    public interface IStoreReviewRepository
    {
        Task<StoreReview?> GetByIdAsync(Guid id);                
        Task<StoreReview?> GetByOrderIdAsync(Guid orderId); 
        Task AddAsync(StoreReview entity);
        void Update(StoreReview entity);                                                                         
        Task<IEnumerable<StoreReview>> GetByStoreId(Guid storeId);
        Task<IEnumerable<StoreReview>> GetByBagId(Guid bagId);
    }
}
