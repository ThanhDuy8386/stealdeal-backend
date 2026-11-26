using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Identity.Domain.Constants;
using StealDeal.Services.Identity.Domain.Interfaces.Repositories;
using StealDeal.Services.Identity.Domain.Models;
using StealDeal.Services.Identity.Infrastructure.Persistence;

namespace StealDeal.Services.Identity.Infrastructure.Repositories;

public sealed class UserTrustScoreRepository : IUserTrustScoreRepository
{
    private readonly ApplicationDbContext _context;

    public UserTrustScoreRepository(ApplicationDbContext context) => _context = context;

    public Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _context.Users.AnyAsync(user => user.Id == userId, cancellationToken);

    public Task<UserTrustScore?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _context.UserTrustScores.AsNoTracking().SingleOrDefaultAsync(score => score.UserId == userId, cancellationToken);

    public async Task<TrustScoreUpdateResult> ApplyScoreChangeAsync(
        Guid userId, int scoreDelta, bool paymentSucceeded, DateTime calculatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (_context.Database.CurrentTransaction == null)
            throw new InvalidOperationException("Atomic trust-score updates must run inside a transaction.");

        var paymentCount = paymentSucceeded ? 1 : 0;
        var noShowCount = paymentSucceeded ? 0 : 1;

        // OUTPUT captures the actual before/after values, including clipping at 0/100.
        // Concurrent first inserts are protected by the existing unique UserId index;
        // a losing insert rolls back and can be retried by the consumer.
        var results = await _context.Database.SqlQuery<TrustScoreUpdateResult>($"""
            SET NOCOUNT ON;

            IF NOT EXISTS (SELECT 1 FROM [UserTrustScores] WHERE [UserId] = {userId})
                INSERT INTO [UserTrustScores]
                    ([Id], [UserId], [Score], [TotalOrders], [SuccessfulPickups],
                     [NoShowCount], [DisputeCount], [LastCalculatedAt])
                VALUES
                    ({Guid.NewGuid()}, {userId}, {TrustScoreLimits.InitialScore}, 0, 0, 0, 0, {calculatedAtUtc});

            UPDATE [UserTrustScores]
            SET [Score] = CASE
                    WHEN [Score] + {scoreDelta} > {TrustScoreLimits.MaximumScore} THEN {TrustScoreLimits.MaximumScore}
                    WHEN [Score] + {scoreDelta} < {TrustScoreLimits.MinimumScore} THEN {TrustScoreLimits.MinimumScore}
                    ELSE [Score] + {scoreDelta}
                END,
                [TotalOrders] = [TotalOrders] + {paymentCount},
                [NoShowCount] = [NoShowCount] + {noShowCount},
                [LastCalculatedAt] = {calculatedAtUtc}
            OUTPUT deleted.[Score] AS [ScoreBefore], inserted.[Score] AS [ScoreAfter]
            WHERE [UserId] = {userId};
            """).ToListAsync(cancellationToken);

        return results.Single();
    }
}
