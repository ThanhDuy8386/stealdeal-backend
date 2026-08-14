using StealDeal.Services.Identity.Domain.Models;

namespace StealDeal.Services.Identity.Domain.Interfaces.Repositories
{
    public interface IAdminRepository
    {
        Task<Admin?> GetByIdAsync(Guid id);
        Task<Admin?> GetByEmailAsync(string email);
        Task<IEnumerable<Admin>> GetAllAsync();
        Task<(IEnumerable<Admin>, int totalCount)> GetAdminsAsync(string? searchTerm, string? role, bool? isActive, int page, int pageSize);
        Task AddAsync(Admin entity);
        void Update(Admin entity);
        void Delete(Admin entity);
        Task<bool> IsEmailUniqueAsync(string email, Guid? excludedAdminId = null);
    }
}
