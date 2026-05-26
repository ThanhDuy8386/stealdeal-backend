using StealDeal.Services.Identity.Domain.Interfaces.Repositories;
using StealDeal.Services.Identity.Domain.Models;
using StealDeal.Services.Identity.Infrastructure.Persistence;

namespace StealDeal.Services.Identity.Infrastructure.Repositories
{
    public class OutboxRepository : IOutboxMessageRepository
    {
        private readonly ApplicationDbContext _context;
        public OutboxRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(OutboxMessage entity)
        {
            await _context.OutboxMessages.AddAsync(entity);
        }
    }
}