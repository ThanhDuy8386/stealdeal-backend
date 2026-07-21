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
            return await _context.Roles.FirstOrDefaultAsync(role => role.Name == name);
        }

        public async Task<List<Role>> GetOrCreateRolesByNamesAsync(IEnumerable<string> roleNames)
        {
            var normalizedRoleNames = roleNames.Distinct().ToList();
            var existingRoles = await _context.Roles
                .Where(role => normalizedRoleNames.Contains(role.Name))
                .ToListAsync();

            var missingRoles = normalizedRoleNames
                .Except(existingRoles.Select(role => role.Name))
                .Select(roleName => new Role { Name = roleName })
                .ToList();

            if (missingRoles.Count > 0)
            {
                await _context.Roles.AddRangeAsync(missingRoles);
                existingRoles.AddRange(missingRoles);
            }

            return existingRoles;
        }

        public void Update(Role role)
        {
            _context.Roles.Update(role);
        }
    }
}
