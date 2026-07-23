using StealDeal.Services.Identity.Domain.Models;

namespace StealDeal.Services.Identity.Domain.Interfaces.Repositories
{
    public interface IRoleRepository
    {
        Task<Role?> GetByIdAsync(Guid id);
        Task<Role?> GetByNameAsync(string name);
        Task<List<Role>> GetAllAsync();
        Task<List<Role>> GetRolesByNamesAsync(IEnumerable<string> roleNames);
        Task<bool> IsRoleAssignedAsync(Guid roleId);
        Task AddAsync(Role role);
        void Update(Role role);
        void Delete(Role role);
    }
}
