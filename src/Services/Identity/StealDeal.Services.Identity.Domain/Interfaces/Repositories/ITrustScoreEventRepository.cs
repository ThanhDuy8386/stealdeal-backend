using StealDeal.Services.Identity.Domain.Models;

namespace StealDeal.Services.Identity.Domain.Interfaces.Repositories;

public interface ITrustScoreEventRepository
{
    Task<TrustScoreEvent?> GetOrderEventAsync(
        Guid userId, Guid orderId, string eventType, CancellationToken cancellationToken = default);
    Task<DateTime?> GetLatestNoShowBelowScoreAtAsync(
        Guid userId, int scoreThreshold, CancellationToken cancellationToken = default);
    Task AddAsync(TrustScoreEvent scoreEvent, CancellationToken cancellationToken = default);
}
