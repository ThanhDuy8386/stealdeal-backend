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

        public Task DeleteAsync(User entity)
        {
            entity.IsDeleted = true;
            entity.IsActive = false;
            _context.Users.Update(entity);
            return Task.CompletedTask;
        }

        public async Task<IEnumerable<User>> GetAllAsync()
        {
            return await _context.Users.ToListAsync();
        }

        public async Task<User> GetByEmailAsync(string email)
        {
            return await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        }

        public async Task<User> GetByIdAsync(Guid id)
        {
            return await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
        }

        public async Task<bool> IsEmailUniqueAsync(string email)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            return user == null;
        }

        public async Task UpdateAsync(User entity)
        {
            _context.Users.Update(entity);
        }
    }
}