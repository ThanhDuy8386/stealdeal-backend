using System;
using System.Threading;
using System.Threading.Tasks;
using StealDeal.Services.Order.Domain.Interfaces;
using StealDeal.Services.Order.Infrastructure.Persistency;

namespace StealDeal.Services.Order.Infrastructure.Repositories
{
    public class UnitOfWork : IUnitOfWork
    {
        private ApplicationDbContext _context;

        public UnitOfWork(ApplicationDbContext context)
        {

            _context = context;
        }
        public Task<int> SaveChangesAsync()
        {
            return _context.SaveChangesAsync();
        }

        public async Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await action();
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _context.ChangeTracker.Clear();
                throw;
            }
        }
    }
}
