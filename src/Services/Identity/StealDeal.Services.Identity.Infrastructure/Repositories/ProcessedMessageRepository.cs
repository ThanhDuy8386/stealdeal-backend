using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Identity.Domain.Interfaces.Repositories;
using StealDeal.Services.Identity.Domain.Models;
using StealDeal.Services.Identity.Infrastructure.Persistence;

namespace StealDeal.Services.Identity.Infrastructure.Repositories;

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

    public async Task<bool> ExistsAsync(
        Guid messageId,
        string consumerName)
    {
        return await _context.ProcessedMessages
            .AnyAsync(x =>
                x.MessageId == messageId &&
                x.ConsumerName == consumerName);
    }

    public async Task<ProcessedMessage?> GetAsync(
        Guid messageId,
        string consumerName)
    {
        return await _context.ProcessedMessages
            .FirstOrDefaultAsync(x =>
                x.MessageId == messageId &&
                x.ConsumerName == consumerName);
    }
}