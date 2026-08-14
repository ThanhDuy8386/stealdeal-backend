using StealDeal.Services.Payment.Domain.Models;

namespace StealDeal.Services.Payment.Domain.Interfaces
{
    public interface IOutboxMessageRepository
    {
        Task AddAsync(OutboxMessage entity);
        Task<List<OutboxMessage>> GetPendingBatchAsync(int batchSize);
        void Update(OutboxMessage entity);
    }
}
