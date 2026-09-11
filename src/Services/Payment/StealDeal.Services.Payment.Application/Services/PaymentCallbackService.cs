using System.Text.Json;
using StealDeal.Services.Payment.Application.DTOs.Events;
using StealDeal.Services.Payment.Application.DTOs.Gateways;
using StealDeal.Services.Payment.Application.Events;
using StealDeal.Services.Payment.Application.Gateways;
using StealDeal.Services.Payment.Application.Services.Interfaces;
using StealDeal.Services.Payment.Domain.Constants;
using StealDeal.Services.Payment.Domain.Interfaces;
using StealDeal.Services.Payment.Domain.Models;

namespace StealDeal.Services.Payment.Application.Services
{
    public class PaymentCallbackService : IPaymentCallbackService
    {
        private readonly IPaymentGatewayFactory _paymentGatewayFactory;
        private readonly ITransactionRepository _transactionRepository;
        private readonly IRefundRepository _refundRepository;
        private readonly IOutboxMessageRepository _outboxMessageRepository;
        private readonly IUnitOfWork _unitOfWork;

        public PaymentCallbackService(
            IPaymentGatewayFactory paymentGatewayFactory,
            ITransactionRepository transactionRepository,
            IRefundRepository refundRepository,
            IOutboxMessageRepository outboxMessageRepository,
            IUnitOfWork unitOfWork)
        {
            _paymentGatewayFactory = paymentGatewayFactory;
            _transactionRepository = transactionRepository;
            _refundRepository = refundRepository;
            _outboxMessageRepository = outboxMessageRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<VnPayIpnHandleResult> HandleVnPayIpnAsync(
            IReadOnlyDictionary<string, string> parameters,
            CancellationToken cancellationToken = default)
        {
            var gateway = _paymentGatewayFactory.GetGateway(PaymentMethods.VnPay);
            var callbackResult = await gateway.VerifyIpnAsync(parameters, cancellationToken);

            if (!callbackResult.IsValidSignature)
            {
                return new VnPayIpnHandleResult("97", "Invalid signature");
            }

            if (string.IsNullOrWhiteSpace(callbackResult.GatewayRef))
            {
                return new VnPayIpnHandleResult("01", "Order not found");
            }

            var transaction = await _transactionRepository.GetByGatewayRefAsync(callbackResult.GatewayRef);

            if (transaction == null)
            {
                return new VnPayIpnHandleResult("01", "Order not found");
            }

            if (!AmountsMatch(transaction.Amount, callbackResult.Amount))
            {
                return new VnPayIpnHandleResult("04", "Invalid amount");
            }

            if (transaction.Status.Equals(TransactionStatuses.Success, StringComparison.OrdinalIgnoreCase))
            {
                return new VnPayIpnHandleResult("02", "Order already confirmed");
            }

            if (callbackResult.IsSuccess)
            {
                //Payment success but transaction status is refund_pending or refunded (refund was done)
                if (IsRefundInProgressOrCompleted(transaction.Status))
                {
                    return new VnPayIpnHandleResult("00", "Confirm success");
                }

                //Payment success but transaction was cancel -> refund step
                //money was take but transaction was cancel -> need to refund
                if (IsFailedOrExpired(transaction.Status))
                {
                    await MarkLateSuccessForRefundAsync(transaction, callbackResult);
                    await _unitOfWork.SaveChangesAsync();
                    return new VnPayIpnHandleResult("00", "Confirm success");
                }

                //Payment success with happy path
                await MarkPaymentSuccessAsync(transaction, callbackResult);
                await _unitOfWork.SaveChangesAsync();
                return new VnPayIpnHandleResult("00", "Confirm success");
            }

            //transaction failed already, so mark confirm to prevent retry
            if (IsFailedOrExpired(transaction.Status) ||
                IsRefundInProgressOrCompleted(transaction.Status))
            {
                return new VnPayIpnHandleResult("00", "Confirm success");
            }

            await MarkPaymentFailedAsync(transaction, callbackResult);
            await _unitOfWork.SaveChangesAsync();

            return new VnPayIpnHandleResult("00", "Confirm success");
        }

        private async Task MarkPaymentSuccessAsync(
            Transaction transaction,
            PaymentCallbackResult callbackResult)
        {
            transaction.Status = TransactionStatuses.Success;
            transaction.GatewayTransactionNo = callbackResult.GatewayTransactionNo;
            transaction.GatewayResponseCode = callbackResult.GatewayResponseCode;
            transaction.GatewayTransactionStatus = callbackResult.GatewayTransactionStatus;
            transaction.FailureReason = null;
            transaction.PaidAt = callbackResult.PaidAtUtc ?? DateTime.UtcNow;
            transaction.UpdatedAt = DateTime.UtcNow;

            _transactionRepository.Update(transaction);
            await _outboxMessageRepository.AddAsync(
                PaymentOutboxMessageFactory.CreatePaymentCompleted(transaction));
        }

        private async Task MarkPaymentFailedAsync(
            Transaction transaction,
            PaymentCallbackResult callbackResult)
        {
            transaction.Status = TransactionStatuses.Failed;
            transaction.GatewayTransactionNo = callbackResult.GatewayTransactionNo;
            transaction.GatewayResponseCode = callbackResult.GatewayResponseCode;
            transaction.GatewayTransactionStatus = callbackResult.GatewayTransactionStatus;
            transaction.FailureReason = callbackResult.Reason;
            transaction.UpdatedAt = DateTime.UtcNow;

            _transactionRepository.Update(transaction);
            await _outboxMessageRepository.AddAsync(
                PaymentOutboxMessageFactory.CreatePaymentFailed(
                    transaction,
                    callbackResult.ReasonCode ?? "PaymentFailed",
                    callbackResult.Reason ?? "Payment failed."));

            if (CanCreateInventoryReleaseEvent(transaction))
            {
                await _outboxMessageRepository.AddAsync(
                    PaymentOutboxMessageFactory.CreateInventoryReleaseRequested(
                        transaction,
                        CreateInventoryReleaseItems(transaction.ReservedItemsJson),
                        "PaymentFailed",
                        callbackResult.Reason ?? "Payment failed, release reserved inventory."));
            }
        }

        private async Task MarkLateSuccessForRefundAsync(
            Transaction transaction,
            PaymentCallbackResult callbackResult)
        {
            transaction.Status = TransactionStatuses.RefundPending;
            transaction.GatewayTransactionNo = callbackResult.GatewayTransactionNo;
            transaction.GatewayResponseCode = callbackResult.GatewayResponseCode;
            transaction.GatewayTransactionStatus = callbackResult.GatewayTransactionStatus;
            transaction.FailureReason = "Payment succeeded after the order was already failed or expired.";
            transaction.PaidAt = callbackResult.PaidAtUtc ?? DateTime.UtcNow;
            transaction.UpdatedAt = DateTime.UtcNow;

            _transactionRepository.Update(transaction);

            await _refundRepository.AddAsync(new Refund
            {
                Id = Guid.NewGuid(),
                TransactionId = transaction.Id,
                OrderId = transaction.OrderId,
                Amount = transaction.Amount,
                Reason = "Payment succeeded after inventory was released.",
                Status = RefundStatuses.Pending,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        private static bool AmountsMatch(decimal expectedAmount, decimal? actualAmount)
        {
            return actualAmount.HasValue && expectedAmount == actualAmount.Value;
        }

        private static bool IsFailedOrExpired(string status)
        {
            return status.Equals(TransactionStatuses.Failed, StringComparison.OrdinalIgnoreCase) ||
                   status.Equals(TransactionStatuses.Expired, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRefundInProgressOrCompleted(string status)
        {
            return status.Equals(TransactionStatuses.RefundPending, StringComparison.OrdinalIgnoreCase) ||
                   status.Equals(TransactionStatuses.Refunded, StringComparison.OrdinalIgnoreCase);
        }

        private static bool CanCreateInventoryReleaseEvent(Transaction transaction)
        {
            return transaction.StoreId.HasValue &&
                   !string.IsNullOrWhiteSpace(transaction.ReservedItemsJson);
        }

        private static List<InventoryReleaseRequestedItemDto> CreateInventoryReleaseItems(string? reservedItemsJson)
        {
            return DeserializeReservedItems(reservedItemsJson)
                .Select(item => new InventoryReleaseRequestedItemDto
                {
                    SurpriseBagId = item.SurpriseBagId,
                    Quantity = item.Quantity
                })
                .ToList();
        }

        private static List<InventoryReservedItemDto> DeserializeReservedItems(string? reservedItemsJson)
        {
            if (string.IsNullOrWhiteSpace(reservedItemsJson))
            {
                return new List<InventoryReservedItemDto>();
            }

            return JsonSerializer.Deserialize<List<InventoryReservedItemDto>>(reservedItemsJson) ??
                   new List<InventoryReservedItemDto>();
        }
    }
}
