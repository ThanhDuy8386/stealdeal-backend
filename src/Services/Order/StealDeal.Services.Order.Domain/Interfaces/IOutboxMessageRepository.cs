using StealDeal.Services.Order.Domain.Models;

namespace StealDeal.Services.Order.Domain.Interfaces
{
    public interface IOutboxMessageRepository
    {
        Task AddAsync(OutboxMessage entity);
        Task<List<OutboxMessage>> GetPendingBatchAsync(int batchSize);
        void Update(OutboxMessage entity);
    }
}
