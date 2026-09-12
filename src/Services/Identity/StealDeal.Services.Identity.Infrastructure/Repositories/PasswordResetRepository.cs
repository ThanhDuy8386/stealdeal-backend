using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Identity.Domain.Interfaces.Repositories;
using StealDeal.Services.Identity.Domain.Models;
using StealDeal.Services.Identity.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Identity.Infrastructure.Repositories
{
    public class PasswordResetRepository : IPasswordResetRepository
    {
        private readonly ApplicationDbContext _context;

        public PasswordResetRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(PasswordReset reset)
        {
            await _context.PasswordResets.AddAsync(reset);
        }

        public Task<PasswordReset?> GetActiveByUserIdAsync(Guid userId)
        {
            return _context.PasswordResets
                .Where(x =>
                    x.UserId == userId &&
                    x.ConsumedAt == null &&
                    x.RevokedAt == null &&
                    x.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();
        }

        public void Update(PasswordReset reset)
        {
            _context.PasswordResets.Update(reset);
        }
    }
}
