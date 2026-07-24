using System.Threading;
using System.Threading.Tasks;

namespace StealDeal.Services.Store.Application.Messaging
{
    public interface IIntegrationEventHandler<in TEvent>
    {
        Task HandleAsync(TEvent @event, IntegrationEventContext context, CancellationToken cancellationToken = default);
    }
}

// interface để implement business logic khi consume 1 event nào đó