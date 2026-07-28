namespace StealDeal.Services.Order.Application.Messaging
{
    public interface IIntegrationEventHandler<in TEvent>
    {
        Task HandleAsync(TEvent @event, IntegrationEventContext context, CancellationToken cancellationToken = default);
    }
}
