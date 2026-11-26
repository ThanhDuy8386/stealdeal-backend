using StealDeal.Services.Order.Application.DTOs.Events;
using StealDeal.Services.Order.Application.Messaging;
using StealDeal.Services.Order.Domain.Interfaces;
using StealDeal.Services.Order.Domain.Models;
using StealDeal.Services.Order.Domain.Constants;

namespace StealDeal.Services.Order.Application.EventHandlers
{
    public class OrderStatusEventHandler :
        IIntegrationEventHandler<InventoryReservationFailedEvent>,
        IIntegrationEventHandler<PaymentFailedEvent>,
        IIntegrationEventHandler<PaymentCompletedEvent>
    {
        private readonly IOrderRepository _orderRepository;
        private readonly IProcessedMessageRepository _processedMessageRepository;
        private readonly IUnitOfWork _unitOfWork;

        public OrderStatusEventHandler(
            IOrderRepository orderRepository,
            IProcessedMessageRepository processedMessageRepository,
            IUnitOfWork unitOfWork)
        {
            _orderRepository = orderRepository;
            _processedMessageRepository = processedMessageRepository;
            _unitOfWork = unitOfWork;
        }

        public Task HandleAsync(
            InventoryReservationFailedEvent @event,
            IntegrationEventContext context,
            CancellationToken cancellationToken = default)
        {
            return HandleStatusEventAsync(
                @event.OrderId,
                context,
                OrderStatuses.InventoryReservationFailed,
                cancellationToken);
        }

        public Task HandleAsync(
            PaymentFailedEvent @event,
            IntegrationEventContext context,
            CancellationToken cancellationToken = default)
        {
            return HandleStatusEventAsync(
                @event.OrderId,
                context,
                OrderStatuses.PaymentFailed,
                cancellationToken);
        }

        public Task HandleAsync(
            PaymentCompletedEvent @event,
            IntegrationEventContext context,
            CancellationToken cancellationToken = default)
        {
            return HandleStatusEventAsync(
                @event.OrderId,
                context,
                OrderStatuses.Confirmed,
                cancellationToken);
        }

        private async Task HandleStatusEventAsync(
            Guid orderId,
            IntegrationEventContext context,
            string targetStatus,
            CancellationToken cancellationToken)
        {
            await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                if (await _processedMessageRepository.ExistsAsync(context.MessageId, context.ConsumerName))
                    return;
                if (await _orderRepository.GetByIdAsync(orderId) == null)
                    throw new InvalidOperationException($"Order '{orderId}' was not found.");

                // Check Pending in the UPDATE itself. A stale read or late event cannot reset NoShow.
                await _orderRepository.TryUpdatePendingStatusAsync(orderId, targetStatus, DateTime.UtcNow, cancellationToken);
                await _processedMessageRepository.AddAsync(new ProcessedMessage
                {
                    MessageId = context.MessageId,
                    ConsumerName = context.ConsumerName,
                    EventType = context.EventType,
                    AggregateId = orderId,
                    ProcessedAt = DateTime.UtcNow
                });
            }, cancellationToken);
        }
    }
}
