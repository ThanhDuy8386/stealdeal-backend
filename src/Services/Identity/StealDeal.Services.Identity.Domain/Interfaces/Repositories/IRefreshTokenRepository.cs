using StealDeal.Services.Identity.Domain.Models;

namespace StealDeal.Services.Identity.Domain.Interfaces.Repositories
{
    public interface IRefreshTokenRepository
    {
        Task<RefreshToken?> GetByTokenHashAsync(string tokenHash);
        Task<List<RefreshToken>> GetActiveRefreshTokensByUserIdAsync(Guid userId);
        Task AddAsync(RefreshToken refreshToken);
        void Update(RefreshToken refreshToken);
    }
}
