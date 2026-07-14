using StealDeal.Services.Identity.Domain.Interfaces.Repositories;
using StealDeal.Services.Identity.Domain.Models;
using StealDeal.Services.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace StealDeal.Services.Identity.Infrastructure.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly ApplicationDbContext _context;

        public UserRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(User entity)
        {
            await _context.Users.AddAsync(entity);
        }

        public void Delete(User entity)
        {
            entity.IsDeleted = true;
            entity.IsActive = false;
            _context.Users.Update(entity);
        }

        public async Task<IEnumerable<User>> GetAllAsync()
        {
            return await _context.Users.ToListAsync();
        }

        public async Task<(IEnumerable<User>, int totalCount)> GetUsersAsync(string? searchTerm, string? role, bool? isActive, int page, int pageSize)
        {
            var query = _context.Users.AsQueryable();

            // filter deleted users
            query = query.Where(u => !u.IsDeleted);


            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.ToLower();
                query = query.Where(u => u.Email.ToLower().Contains(term) || u.FullName.ToLower().Contains(term));
            }


            if (!string.IsNullOrWhiteSpace(role))
            {
                query = query.Where(u => u.UserRoles.Any(r => r.Role == role));
            }

            if (isActive.HasValue)
            {
                query = query.Where(u => u.IsActive == isActive.Value);
            }

            // count all users after filtering for pagination
            var totalCount = await query.CountAsync();


            var users = await query
                .OrderByDescending(u => u.CreatedAt) // newest 
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Include(u => u.UserRoles) // load the roles too
                .ToListAsync();

            return (users, totalCount);

        }

        public async Task<User?> GetByEmailAsync(string email)
        {
            return await _context.Users
                .Include(u => u.UserRoles)
                .Include(u => u.UserTrustScore)
                .FirstOrDefaultAsync(u => u.Email == email);
        }

        public async Task<User?> GetByIdAsync(Guid id)
        {
            return await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
        }

        public async Task<bool> IsEmailUniqueAsync(string email)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            return user == null;
        }

        public void Update(User entity)
        {
            _context.Users.Update(entity);
        }
    }
}
