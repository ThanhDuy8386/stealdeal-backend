using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Store.Domain.Interfaces;
using StealDeal.Services.Store.Domain.Models;
using StealDeal.Services.Store.Infrastructure.Persistence;

namespace StealDeal.Services.Store.Infrastructure.Repositories
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
