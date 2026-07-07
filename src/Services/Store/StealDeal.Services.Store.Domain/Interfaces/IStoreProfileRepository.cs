using StealDeal.Services.Store.Domain.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Domain.Interfaces
{
    public interface IStoreProfileRepository
    {
        Task AddAsync(StoreProfile entity);
        Task<StoreProfile?> GetByIdAsync(Guid id);
        void Update(StoreProfile entity);
        void ToggleActive(StoreProfile entity);
        Task<IEnumerable<StoreProfile>> GetAllAsync();

    }
}
