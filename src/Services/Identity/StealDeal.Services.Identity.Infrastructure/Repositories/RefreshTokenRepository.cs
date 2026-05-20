using StealDeal.Services.Identity.Domain.Interfaces.Repositories;
using StealDeal.Services.Identity.Domain.Models;
using StealDeal.Services.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace StealDeal.Services.Identity.Infrastructure.Repositories
{
    public class RefreshTokenRepository : IRefreshTokenRepository
    {
        private readonly ApplicationDbContext _context;
        public RefreshTokenRepository(ApplicationDbContext context)
        {
            _context = context;
        }
        public async Task AddAsync(RefreshToken refreshToken)
        {
            await _context.RefreshTokens.AddAsync(refreshToken);
        }

        public Task<RefreshToken?> GetByTokenHashAsync(string tokenHash)
        {
            return _context.RefreshTokens.FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash);
        }

        public void Update(RefreshToken refreshToken)
        {
            _context.RefreshTokens.Update(refreshToken);
        }
    }
} 