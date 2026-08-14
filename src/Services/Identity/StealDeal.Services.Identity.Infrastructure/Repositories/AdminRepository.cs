using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Identity.Domain.Interfaces.Repositories;
using StealDeal.Services.Identity.Domain.Models;
using StealDeal.Services.Identity.Infrastructure.Persistence;

namespace StealDeal.Services.Identity.Infrastructure.Repositories
{
    public class AdminRepository : IAdminRepository
    {
        private readonly ApplicationDbContext _context;

        public AdminRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(Admin entity)
        {
            await _context.Admins.AddAsync(entity);
        }

        public void Delete(Admin entity)
        {
            entity.IsDeleted = true;
            entity.IsActive = false;
            _context.Admins.Update(entity);
        }

        public async Task<IEnumerable<Admin>> GetAllAsync()
        {
            return await _context.Admins
                .Include(admin => admin.Roles)
                .Where(admin => !admin.IsDeleted)
                .ToListAsync();
        }

        public async Task<(IEnumerable<Admin>, int totalCount)> GetAdminsAsync(string? searchTerm, string? role, bool? isActive, int page, int pageSize)
        {
            var query = _context.Admins
                .Include(admin => admin.Roles)
                .Where(admin => !admin.IsDeleted)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLowerInvariant();
                query = query.Where(admin =>
                    admin.Email.ToLower().Contains(term) ||
                    admin.FullName.ToLower().Contains(term));
            }

            if (!string.IsNullOrWhiteSpace(role))
            {
                var normalizedRole = role.Trim();
                query = query.Where(admin => admin.Roles.Any(adminRole => adminRole.Name == normalizedRole));
            }

            if (isActive.HasValue)
            {
                query = query.Where(admin => admin.IsActive == isActive.Value);
            }

            var totalCount = await query.CountAsync();

            var admins = await query
                .OrderByDescending(admin => admin.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (admins, totalCount);
        }

        public async Task<Admin?> GetByEmailAsync(string email)
        {
            return await _context.Admins
                .Include(admin => admin.Roles)
                .FirstOrDefaultAsync(admin => admin.Email == email);
        }

        public async Task<Admin?> GetByIdAsync(Guid id)
        {
            return await _context.Admins
                .Include(admin => admin.Roles)
                .FirstOrDefaultAsync(admin => admin.Id == id);
        }

        public async Task<bool> IsEmailUniqueAsync(string email, Guid? excludedAdminId = null)
        {
            return !await _context.Admins.AnyAsync(admin =>
                admin.Email == email &&
                (!excludedAdminId.HasValue || admin.Id != excludedAdminId.Value));
        }

        public void Update(Admin entity)
        {
            _context.Admins.Update(entity);
        }
    }
}
