using StealDeal.Services.Store.Domain.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Domain.Interfaces
{
    public interface ISurpriseBagRepository
    {
        Task AddAsync(SurpriseBag entity);
        Task<SurpriseBag?> GetByIdAsync(Guid id);
        void Update(SurpriseBag entity);
        Task<IEnumerable<SurpriseBag>> GetAllAsync();
        Task<IEnumerable<SurpriseBag>> GetByStoreIdAsync(Guid storeId);
    }
}
