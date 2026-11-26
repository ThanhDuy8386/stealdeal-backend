using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Identity.Domain.Constants;
using StealDeal.Services.Identity.Domain.Interfaces.Repositories;
using StealDeal.Services.Identity.Domain.Models;
using StealDeal.Services.Identity.Infrastructure.Persistence;

namespace StealDeal.Services.Identity.Infrastructure.Repositories;

public sealed class TrustScoreEventRepository : ITrustScoreEventRepository
{
    private readonly ApplicationDbContext _context;

    public TrustScoreEventRepository(ApplicationDbContext context) => _context = context;

    public Task<TrustScoreEvent?> GetOrderEventAsync(
        Guid userId, Guid orderId, string eventType, CancellationToken cancellationToken = default)
    {
        var referenceId = orderId.ToString("D");
        return _context.TrustScoreEvents.AsNoTracking().SingleOrDefaultAsync(scoreEvent =>
            scoreEvent.UserId == userId && scoreEvent.ReferenceType == TrustScoreReferenceTypes.Order &&
            scoreEvent.ReferenceId == referenceId && scoreEvent.EventType == eventType, cancellationToken);
    }

    public Task<DateTime?> GetLatestNoShowBelowScoreAtAsync(
        Guid userId, int scoreThreshold, CancellationToken cancellationToken = default) =>
        _context.TrustScoreEvents.AsNoTracking()
            .Where(scoreEvent => scoreEvent.UserId == userId &&
                scoreEvent.ReferenceType == TrustScoreReferenceTypes.Order &&
                scoreEvent.EventType == TrustScoreEventTypes.OrderNoShow && scoreEvent.ScoreAfter < scoreThreshold)
            .OrderByDescending(scoreEvent => scoreEvent.CreatedAt)
            .Select(scoreEvent => (DateTime?)scoreEvent.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddAsync(TrustScoreEvent scoreEvent, CancellationToken cancellationToken = default) =>
        await _context.TrustScoreEvents.AddAsync(scoreEvent, cancellationToken);
}
