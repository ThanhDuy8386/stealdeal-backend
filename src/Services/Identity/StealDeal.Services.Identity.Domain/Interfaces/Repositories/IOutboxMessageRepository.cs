using StealDeal.Services.Identity.Domain.Models;

namespace StealDeal.Services.Identity.Domain.Interfaces.Repositories
{
    public interface IOutboxMessageRepository
    {
        Task AddAsync(OutboxMessage entity);
    }
}
