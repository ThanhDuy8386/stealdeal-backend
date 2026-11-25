using StealDeal.Services.Identity.Application.Constants;
using StealDeal.Services.Identity.Application.DTOs.Events;
using StealDeal.Services.Identity.Application.DTOs.Requests;
using StealDeal.Services.Identity.Application.Exceptions;
using StealDeal.Services.Identity.Application.Messaging;
using StealDeal.Services.Identity.Application.Services.Interfaces;

namespace StealDeal.Services.Identity.Application.EventHandlers;

public sealed class TrustScoreEventHandler
    : IIntegrationEventHandler<PaymentCompletedEvent>, IIntegrationEventHandler<OrderNoShowEvent>
{
    private readonly ITrustScoreService _trustScoreService;

    public TrustScoreEventHandler(ITrustScoreService trustScoreService)
    {
        _trustScoreService = trustScoreService;
    }

    public Task HandleAsync(PaymentCompletedEvent @event, IntegrationEventContext context,
        CancellationToken cancellationToken = default)
    {
        ValidateEnvelope(@event.MessageId, context, TrustScoreEventRoutingKeys.PaymentCompleted);
        if (@event.UserId == Guid.Empty)
            throw new BadRequestException("payment.completed requires UserId. Legacy messages must be enriched before replay.");
        if (@event.PaymentId == Guid.Empty)
            throw new BadRequestException("PaymentId is required.");

        return _trustScoreService.RecordPaymentSucceededAsync(new RecordPaymentTrustScoreRequest
        {
            UserId = @event.UserId,
            OrderId = @event.OrderId,
            OccurredAtUtc = @event.OccurredAtUtc
        }, context, cancellationToken);
    }

    public Task HandleAsync(OrderNoShowEvent @event, IntegrationEventContext context,
        CancellationToken cancellationToken = default)
    {
        ValidateEnvelope(@event.MessageId, context, TrustScoreEventRoutingKeys.OrderNoShow);
        if (@event.StoreId == Guid.Empty || @event.PickupDeadlineUtc == default ||
            @event.PickupDeadlineUtc.Kind != DateTimeKind.Utc ||
            @event.OccurredAtUtc <= @event.PickupDeadlineUtc)
            throw new BadRequestException("NoShow requires StoreId and an elapsed UTC pickup deadline.");

        return _trustScoreService.RecordNoShowAsync(new RecordNoShowTrustScoreRequest
        {
            UserId = @event.UserId,
            OrderId = @event.OrderId,
            OccurredAtUtc = @event.OccurredAtUtc,
            MarkedByUserId = @event.MarkedByUserId,
            Reason = @event.Reason
        }, context, cancellationToken);
    }

    private static void ValidateEnvelope(Guid messageId, IntegrationEventContext context, string routingKey)
    {
        if (messageId == Guid.Empty || messageId != context.MessageId || context.RoutingKey != routingKey)
            throw new BadRequestException("Payload MessageId must match the broker MessageId and event routing key.");
    }
}
