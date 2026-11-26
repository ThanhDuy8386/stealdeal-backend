using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Identity.Domain.Interfaces.Repositories;
using StealDeal.Services.Identity.Infrastructure.Persistence;

namespace StealDeal.Services.Identity.Infrastructure.Repositories;

public sealed class TrustScoreUnitOfWork : ITrustScoreUnitOfWork
{
    private readonly ApplicationDbContext _context;

    public TrustScoreUnitOfWork(ApplicationDbContext context) => _context = context;

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (_context.Database.CurrentTransaction != null || _context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Trust-score operations require a clean unit of work with no pending changes or transaction.");

        _context.ChangeTracker.Clear();
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await operation(cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            _context.ChangeTracker.Clear();
            throw; // Disposing the uncommitted transaction rolls back all changes.
        }
    }
}
