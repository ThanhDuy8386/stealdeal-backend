using StealDeal.Services.Payment.Application.DTOs.Events;
using StealDeal.Services.Payment.Application.DTOs.Gateways;
using StealDeal.Services.Payment.Application.Gateways;
using StealDeal.Services.Payment.Application.Messaging;
using StealDeal.Services.Payment.Domain.Constants;
using StealDeal.Services.Payment.Domain.Interfaces;
using StealDeal.Services.Payment.Domain.Models;

namespace StealDeal.Services.Payment.Application.EventHandlers
{
    public class InventoryReservedEventHandler : IIntegrationEventHandler<InventoryReservedEvent>
    {
        private readonly ITransactionRepository _transactionRepository;
        private readonly IProcessedMessageRepository _processedMessageRepository;
        private readonly IPaymentGatewayFactory _paymentGatewayFactory;
        private readonly IUnitOfWork _unitOfWork;

        public InventoryReservedEventHandler(
            ITransactionRepository transactionRepository,
            IProcessedMessageRepository processedMessageRepository,
            IPaymentGatewayFactory paymentGatewayFactory,
            IUnitOfWork unitOfWork)
        {
            _transactionRepository = transactionRepository;
            _processedMessageRepository = processedMessageRepository;
            _paymentGatewayFactory = paymentGatewayFactory;
            _unitOfWork = unitOfWork;
        }

        public async Task HandleAsync(
            InventoryReservedEvent @event,
            IntegrationEventContext context,
            CancellationToken cancellationToken = default)
        {
            if (await _processedMessageRepository.ExistsAsync(context.MessageId, context.ConsumerName))
            {
                return;
            }

            var existingTransaction = await _transactionRepository.GetByOrderIdAsync(@event.OrderId);

            if (existingTransaction != null &&
                IsActiveOrCompleted(existingTransaction.Status))
            {
                await AddProcessedMessageAsync(@event, context);
                await _unitOfWork.SaveChangesAsync();
                return;
            }

            var transaction = new Transaction
            {
                Id = Guid.NewGuid(),
                OrderId = @event.OrderId,
                UserId = @event.UserId,
                Amount = @event.TotalAmount,
                PaymentMethod = PaymentMethods.VnPay,
                Status = TransactionStatuses.Pending,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var gateway = _paymentGatewayFactory.GetGateway(PaymentMethods.VnPay);
            var paymentResult = await gateway.CreatePaymentAsync(
                new CreatePaymentRequest
                {
                    TransactionId = transaction.Id,
                    OrderId = transaction.OrderId,
                    UserId = transaction.UserId,
                    Amount = transaction.Amount,
                    OrderInfo = $"Thanh toan don hang {transaction.OrderId}",
                    CreatedAtUtc = transaction.CreatedAt
                },
                cancellationToken);

            transaction.PaymentMethod = paymentResult.PaymentMethod;
            transaction.GatewayRef = paymentResult.GatewayRef;
            transaction.CheckoutUrl = paymentResult.CheckoutUrl;
            transaction.ExpiresAt = paymentResult.ExpiresAtUtc;
            transaction.UpdatedAt = DateTime.UtcNow;

            await _transactionRepository.AddAsync(transaction);
            await AddProcessedMessageAsync(@event, context);
            await _unitOfWork.SaveChangesAsync();
        }

        private async Task AddProcessedMessageAsync(
            InventoryReservedEvent @event,
            IntegrationEventContext context)
        {
            await _processedMessageRepository.AddAsync(new ProcessedMessage
            {
                MessageId = context.MessageId,
                ConsumerName = context.ConsumerName,
                EventType = context.EventType,
                AggregateId = @event.OrderId,
                ProcessedAt = DateTime.UtcNow
            });
        }

        private static bool IsActiveOrCompleted(string status)
        {
            return status.Equals(TransactionStatuses.Pending, StringComparison.OrdinalIgnoreCase) ||
                   status.Equals(TransactionStatuses.Success, StringComparison.OrdinalIgnoreCase);
        }
    }
}
