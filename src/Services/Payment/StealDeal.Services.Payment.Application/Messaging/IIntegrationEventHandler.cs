namespace StealDeal.Services.Payment.Application.Messaging
{
    public interface IIntegrationEventHandler<in TEvent>
    {
        Task HandleAsync(TEvent @event, IntegrationEventContext context, CancellationToken cancellationToken = default);
    }
}
