using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Order.Domain.Interfaces;
using StealDeal.Services.Order.Domain.Models;
using StealDeal.Services.Order.Infrastructure.Persistency;

namespace StealDeal.Services.Order.Infrastructure.Repositories
{
    public class ProcessedMessageRepository : IProcessedMessageRepository
    {
        private readonly ApplicationDbContext _context;

        public ProcessedMessageRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(ProcessedMessage processedMessage)
        {
            await _context.ProcessedMessages.AddAsync(processedMessage);
        }

        public async Task<bool> ExistsAsync(Guid messageId, string consumerName)
        {
            return await _context.ProcessedMessages
                .AnyAsync(m => m.MessageId == messageId && m.ConsumerName == consumerName);
        }

        public async Task<ProcessedMessage?> GetAsync(Guid messageId, string consumerName)
        {
            return await _context.ProcessedMessages
                .FirstOrDefaultAsync(m => m.MessageId == messageId && m.ConsumerName == consumerName);
        }
    }
}
