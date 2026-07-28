using StealDeal.Services.Order.Application.DTOs.Events;
using StealDeal.Services.Order.Application.Messaging;
using StealDeal.Services.Order.Domain.Interfaces;
using StealDeal.Services.Order.Domain.Models;

namespace StealDeal.Services.Order.Application.EventHandlers
{
    public class OrderStatusEventHandler :
        IIntegrationEventHandler<InventoryReservationFailedEvent>,
        IIntegrationEventHandler<PaymentFailedEvent>,
        IIntegrationEventHandler<PaymentCompletedEvent>
    {
        private const string PendingStatus = "Pending";
        private const string InventoryReservationFailedStatus = "InventoryReservationFailed";
        private const string PaymentFailedStatus = "PaymentFailed";
        private const string ConfirmedStatus = "Confirmed";

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
                InventoryReservationFailedStatus,
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
                PaymentFailedStatus,
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
                ConfirmedStatus,
                cancellationToken);
        }

        private async Task HandleStatusEventAsync(
            Guid orderId,
            IntegrationEventContext context,
            string targetStatus,
            CancellationToken cancellationToken)
        {
            if (await _processedMessageRepository.ExistsAsync(context.MessageId, context.ConsumerName))
            {
                return;
            }

            var order = await _orderRepository.GetByIdAsync(orderId);
            if (order == null)
            {
                throw new InvalidOperationException($"Order '{orderId}' was not found.");
            }

            if (CanMoveToStatus(order.Status, targetStatus))
            {
                order.Status = targetStatus;
                order.UpdatedAt = DateTime.UtcNow;
                _orderRepository.Update(order);
            }

            await _processedMessageRepository.AddAsync(new ProcessedMessage
            {
                MessageId = context.MessageId,
                ConsumerName = context.ConsumerName,
                EventType = context.EventType,
                AggregateId = orderId,
                ProcessedAt = DateTime.UtcNow
            });

            await _unitOfWork.SaveChangesAsync();
        }

        private static bool CanMoveToStatus(string currentStatus, string targetStatus)
        {
            if (currentStatus.Equals(targetStatus, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return currentStatus.Equals(PendingStatus, StringComparison.OrdinalIgnoreCase);
        }
    }
}
