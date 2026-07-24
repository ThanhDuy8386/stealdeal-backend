using System;
using System.Threading.Tasks;
using StealDeal.Services.Store.Domain.Models;

namespace StealDeal.Services.Store.Domain.Interfaces
{
    public interface IProcessedMessageRepository
    {
        Task AddAsync(ProcessedMessage processedMessage);
        Task<bool> ExistsAsync(Guid messageId, string consumerName);
        Task<ProcessedMessage?> GetAsync(Guid messageId, string consumerName);
    }
}
