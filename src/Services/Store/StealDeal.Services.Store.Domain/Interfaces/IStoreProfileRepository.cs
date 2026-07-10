using StealDeal.Services.Store.Domain.Models;

namespace StealDeal.Services.Store.Domain.Interfaces
{
    public interface IStoreProfileRepository
    {
        Task AddAsync(StoreProfile entity);
        Task<StoreProfile?> GetByIdAsync(Guid id);
        Task<StoreProfile?> GetByOwnerIdAsync(Guid ownerId);
        Task<bool> ExistsByOwnerIdAsync(Guid ownerId);
        Task<IEnumerable<StoreProfile>> GetAllAsync();
        void Update(StoreProfile entity);
    }
}
