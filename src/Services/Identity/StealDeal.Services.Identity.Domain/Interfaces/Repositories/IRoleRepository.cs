using StealDeal.Services.Identity.Domain.Models;

namespace StealDeal.Services.Identity.Domain.Interfaces.Repositories
{
    public interface IRoleRepository
    {
        Task<Role?> GetByIdAsync(Guid id);
        Task<Role?> GetByNameAsync(string name);
        Task<List<Role>> GetAllAsync();
        Task<List<Role>> GetOrCreateRolesByNamesAsync(IEnumerable<string> roleNames);
        Task AddAsync(Role role);
        void Update(Role role);
        void Delete(Role role);
    }
}
