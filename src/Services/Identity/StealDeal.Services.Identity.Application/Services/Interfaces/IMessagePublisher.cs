using StealDeal.Services.Identity.Application.DTOs.Events;

namespace StealDeal.Services.Identity.Application.Services.Interfaces
{
    public interface IMessagePublisher
    {
        Task PublishAsync(IntegrationMessage message, CancellationToken cancellationToken = default);
    }
}