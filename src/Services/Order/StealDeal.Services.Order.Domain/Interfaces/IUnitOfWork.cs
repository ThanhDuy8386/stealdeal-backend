using System;
using System.Threading;
using System.Threading.Tasks;

namespace StealDeal.Services.Order.Domain.Interfaces
{
    public interface IUnitOfWork
    {
        Task<int> SaveChangesAsync();
        Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken = default);
    }
}
