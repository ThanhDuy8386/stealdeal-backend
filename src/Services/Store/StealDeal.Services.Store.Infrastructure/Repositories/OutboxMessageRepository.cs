using StealDeal.Services.Store.Domain.Interfaces;
using StealDeal.Services.Store.Domain.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Infrastructure.Repositories
{
    public class OutboxMessageRepository : IOutboxMessageRepository
    {
        public Task AddAsync(OutboxMessage entity)
        {
            throw new NotImplementedException();
        }

        public Task<List<OutboxMessage>> GetPendingBatchAsync(int batchSize)
        {
            throw new NotImplementedException();
        }

        public void Update(OutboxMessage entity)
        {
            throw new NotImplementedException();
        }
    }
}
