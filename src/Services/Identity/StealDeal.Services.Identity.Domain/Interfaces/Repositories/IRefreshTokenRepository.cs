using StealDeal.Services.Identity.Domain.Models;

namespace StealDeal.Services.Identity.Domain.Interfaces.Repositories
{
    public interface IRefreshTokenRepository
    {
        Task<RefreshToken?> GetByTokenHashAsync(string tokenHash);
        Task AddAsync(RefreshToken refreshToken);
        void Update(RefreshToken refreshToken);
    }
}