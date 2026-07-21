using System.Threading;
using System.Threading.Tasks;

namespace StealDeal.Services.Notification.Application.Messaging
{
    public interface IIntegrationEventHandler<in TEvent>
    {
        Task HandleAsync(TEvent @event, IntegrationEventContext context, CancellationToken cancellationToken = default);
    }
}
