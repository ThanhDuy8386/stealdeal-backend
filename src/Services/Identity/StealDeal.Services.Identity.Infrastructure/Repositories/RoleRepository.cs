using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Identity.Domain.Interfaces.Repositories;
using StealDeal.Services.Identity.Domain.Models;
using StealDeal.Services.Identity.Infrastructure.Persistence;

namespace StealDeal.Services.Identity.Infrastructure.Repositories
{
    public class RoleRepository : IRoleRepository
    {
        private readonly ApplicationDbContext _context;

        public RoleRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(Role role)
        {
            await _context.Roles.AddAsync(role);
        }

        public void Delete(Role role)
        {
            _context.Roles.Remove(role);
        }

        public async Task<List<Role>> GetAllAsync()
        {
            return await _context.Roles.ToListAsync();
        }

        public async Task<Role?> GetByIdAsync(Guid id)
        {
            return await _context.Roles.FirstOrDefaultAsync(role => role.Id == id);
        }

        public async Task<Role?> GetByNameAsync(string name)
        {
            return await _context.Roles.FirstOrDefaultAsync(role => role.Name == name.Trim());
        }

        public async Task<List<Role>> GetRolesByNamesAsync(IEnumerable<string> roleNames)
        {
            var normalizedRoleNames = roleNames.Select(name => name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return await _context.Roles
                .Where(role => normalizedRoleNames.Contains(role.Name))
                .ToListAsync();
        }

        public async Task<bool> IsRoleAssignedAsync(Guid roleId)
        {
            return await _context.Users.AnyAsync(user => user.Roles.Any(role => role.Id == roleId));
        }

        public void Update(Role role)
        {
            _context.Roles.Update(role);
        }
    }
}
