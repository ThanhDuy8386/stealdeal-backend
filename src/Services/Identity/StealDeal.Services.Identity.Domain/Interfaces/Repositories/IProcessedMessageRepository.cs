using StealDeal.Services.Identity.Domain.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Identity.Domain.Interfaces.Repositories
{
    public interface IProcessedMessageRepository
    {
        Task AddAsync(ProcessedMessage processedMessage);
        Task<bool> ExistsAsync(Guid messageId, string consumerName);
        Task<ProcessedMessage?> GetAsync(Guid messageId, string consumerName);
    }
}
