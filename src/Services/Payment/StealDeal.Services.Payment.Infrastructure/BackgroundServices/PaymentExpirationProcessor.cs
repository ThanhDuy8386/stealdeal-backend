using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StealDeal.Services.Payment.Application.DTOs.Events;
using StealDeal.Services.Payment.Application.Events;
using StealDeal.Services.Payment.Domain.Constants;
using StealDeal.Services.Payment.Domain.Interfaces;
using StealDeal.Services.Payment.Domain.Models;
using StealDeal.Services.Payment.Infrastructure.Configuration;

namespace StealDeal.Services.Payment.Infrastructure.BackgroundServices
{
    public class PaymentExpirationProcessor : BackgroundService
    {
        private const string ExpiredReasonCode = "PaymentExpired";
        private const string ExpiredReason = "Payment expired before gateway confirmation.";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly PaymentExpirationSettings _settings;
        private readonly ILogger<PaymentExpirationProcessor> _logger;

        public PaymentExpirationProcessor(
            IServiceScopeFactory scopeFactory,
            IOptions<PaymentExpirationSettings> settings,
            ILogger<PaymentExpirationProcessor> logger)
        {
            _scopeFactory = scopeFactory;
            _settings = settings.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_settings.Enabled)
            {
                _logger.LogInformation("PaymentExpirationProcessor is disabled.");
                return;
            }

            _logger.LogInformation("PaymentExpirationProcessor background service is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessExpiredPaymentsAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process expired payment transactions.");
                }

                await Task.Delay(TimeSpan.FromSeconds(_settings.PollingIntervalSeconds), stoppingToken);
            }
        }

        private async Task ProcessExpiredPaymentsAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var transactionRepository = scope.ServiceProvider.GetRequiredService<ITransactionRepository>();
            var outboxMessageRepository = scope.ServiceProvider.GetRequiredService<IOutboxMessageRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var nowUtc = DateTime.UtcNow;
            var expiredTransactions = await transactionRepository.GetExpiredPendingBatchAsync(
                nowUtc,
                _settings.BatchSize,
                cancellationToken);

            if (expiredTransactions.Count == 0)
            {
                return;
            }

            foreach (var transaction in expiredTransactions)
            {
                if (!transaction.Status.Equals(TransactionStatuses.Pending, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                MarkExpired(transaction);
                transactionRepository.Update(transaction);

                await outboxMessageRepository.AddAsync(
                    PaymentOutboxMessageFactory.CreatePaymentFailed(
                        transaction,
                        ExpiredReasonCode,
                        ExpiredReason));

                if (CanCreateInventoryReleaseEvent(transaction))
                {
                    await outboxMessageRepository.AddAsync(
                        PaymentOutboxMessageFactory.CreateInventoryReleaseRequested(
                            transaction,
                            CreateInventoryReleaseItems(transaction.ReservedItemsJson),
                            ExpiredReasonCode,
                            ExpiredReason));
                }
            }

            await unitOfWork.SaveChangesAsync();

            _logger.LogInformation(
                "Expired {Count} pending payment transaction(s).",
                expiredTransactions.Count);
        }

        private static void MarkExpired(Transaction transaction)
        {
            transaction.Status = TransactionStatuses.Expired;
            transaction.FailureReason = ExpiredReason;
            transaction.UpdatedAt = DateTime.UtcNow;
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
