using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Store.Domain.Interfaces;
using StealDeal.Services.Store.Domain.Models;
using StealDeal.Services.Store.Infrastructure.Persistence;

namespace StealDeal.Services.Store.Infrastructure.Repositories
{
    public class OutboxMessageRepository : IOutboxMessageRepository
    {
        private readonly ApplicationDbContext _context;

        public OutboxMessageRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(OutboxMessage entity)
        {
            await _context.OutboxMessages.AddAsync(entity);
        }

        public async Task<List<OutboxMessage>> GetPendingBatchAsync(int batchSize)
        {
            return await _context.OutboxMessages
                .Where(message => message.Status == "Pending")
                .OrderBy(message => message.CreatedAt)
                .Take(batchSize)
                .ToListAsync();
        }

        public void Update(OutboxMessage entity)
        {
            _context.OutboxMessages.Update(entity);
        }
    }
}
