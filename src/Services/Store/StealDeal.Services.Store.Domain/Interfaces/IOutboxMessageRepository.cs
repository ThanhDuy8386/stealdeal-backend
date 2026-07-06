using StealDeal.Services.Store.Domain.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Domain.Interfaces
{
    public interface IOutboxMessageRepository
    {
        Task AddAsync(OutboxMessage entity);
        Task<List<OutboxMessage>> GetPendingBatchAsync(int batchSize);
        void Update(OutboxMessage entity);
    }
}
