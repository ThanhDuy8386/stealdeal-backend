using System.Collections.Generic;
using System.Threading.Tasks;
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
        Task<IEnumerable<StoreProfile>> GetPendingVerificationAsync();
        Task<List<StoreProfile>> GetNearbyCandidatesAsync(decimal minLat, decimal maxLat, decimal minLng, decimal maxLng);
        void Update(StoreProfile entity);
        void Delete(StoreProfile entity);
    }
}
