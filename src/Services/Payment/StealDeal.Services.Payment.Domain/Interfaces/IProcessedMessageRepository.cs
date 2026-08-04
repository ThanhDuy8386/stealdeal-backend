using System;
using System.Threading.Tasks;
using StealDeal.Services.Payment.Domain.Models;

namespace StealDeal.Services.Payment.Domain.Interfaces
{
    public interface IProcessedMessageRepository
    {
        Task AddAsync(ProcessedMessage processedMessage);
        Task<bool> ExistsAsync(Guid messageId, string consumerName);
        Task<ProcessedMessage?> GetAsync(Guid messageId, string consumerName);
    }
}
