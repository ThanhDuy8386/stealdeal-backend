using StealDeal.Services.Order.Application.DTOs.Events;

namespace StealDeal.Services.Order.Application.Services.Interfaces
{
    public interface IMessagePublisher
    {
        Task PublishAsync(IntegrationMessage message, CancellationToken cancellationToken = default);
        Task PublishBatchAsync(IReadOnlyCollection<IntegrationMessage> messages, CancellationToken cancellationToken = default);
    }
}
