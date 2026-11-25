using StealDeal.Services.Identity.Domain.Models;

namespace StealDeal.Services.Identity.Domain.Interfaces.Repositories;

public interface IUserTrustScoreRepository
{
    Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<UserTrustScore?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<TrustScoreUpdateResult> ApplyScoreChangeAsync(
        Guid userId, int scoreDelta, bool paymentSucceeded, DateTime calculatedAtUtc,
        CancellationToken cancellationToken = default);
}
