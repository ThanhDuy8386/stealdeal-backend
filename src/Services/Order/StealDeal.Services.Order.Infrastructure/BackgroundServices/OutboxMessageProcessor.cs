using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StealDeal.Services.Order.Application.DTOs.Events;
using StealDeal.Services.Order.Application.Services.Interfaces;
using StealDeal.Services.Order.Domain.Interfaces;
using StealDeal.Services.Order.Domain.Models;
using StealDeal.Services.Order.Infrastructure.Configuration;

namespace StealDeal.Services.Order.Infrastructure.BackgroundServices
{
    public class OutboxMessageProcessor : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<OutboxMessageProcessor> _logger;
        private readonly OutboxSettings _settings;
        public OutboxMessageProcessor(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<OutboxMessageProcessor> logger,
            IOptions<OutboxSettings> settings)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
            _settings = settings.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Outbox message processor started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessPendingMessagesAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // App is stopping.
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error while processing outbox messages.");
                }

                await Task.Delay(
                    TimeSpan.FromSeconds(_settings.PollingIntervalSeconds),
                    stoppingToken);
            }
        }

        private async Task ProcessPendingMessagesAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceScopeFactory.CreateScope();

            var outboxRepository = scope.ServiceProvider.GetRequiredService<IOutboxMessageRepository>();
            var messagePublisher = scope.ServiceProvider.GetRequiredService<IMessagePublisher>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var messages = await outboxRepository.GetPendingBatchAsync(_settings.BatchSize);

            if (messages.Count == 0)
            {
                return;
            }

            _logger.LogInformation("Processing {Count} outbox messages.", messages.Count);

            foreach (var outboxMessage in messages)
            {
                await ProcessMessageAsync(
                    outboxMessage,
                    messagePublisher,
                    outboxRepository,
                    cancellationToken);
            }

            await unitOfWork.SaveChangesAsync();
        }

        private async Task ProcessMessageAsync(
            OutboxMessage outboxMessage,
            IMessagePublisher messagePublisher,
            IOutboxMessageRepository outboxRepository,
            CancellationToken cancellationToken)
        {
            try
            {
                var integrationMessage = new IntegrationMessage
                {
                    MessageId = outboxMessage.Id,
                    ExchangeName = outboxMessage.ExchangeName,
                    ExchangeType = outboxMessage.ExchangeType,
                    RoutingKey = outboxMessage.RoutingKey,
                    EventType = outboxMessage.EventType,
                    Payload = outboxMessage.Payload,
                    OccurredAt = outboxMessage.CreatedAt
                };

                await messagePublisher.PublishAsync(integrationMessage, cancellationToken);

                outboxMessage.Status = "Processed";
                outboxMessage.ProcessedAt = DateTime.UtcNow;
                outboxMessage.Error = null;

                _logger.LogInformation(
                    "Published outbox message {MessageId} with routing key {RoutingKey}.",
                    outboxMessage.Id,
                    outboxMessage.RoutingKey);
            }
            catch (Exception ex)
            {
                outboxMessage.RetryCount += 1;
                outboxMessage.Error = ex.Message;

                if (outboxMessage.RetryCount >= _settings.MaxRetryCount)
                {
                    outboxMessage.Status = "Failed";
                }

                _logger.LogError(
                    ex,
                    "Failed to publish outbox message {MessageId}. Retry count: {RetryCount}.",
                    outboxMessage.Id,
                    outboxMessage.RetryCount);
            }

            outboxRepository.Update(outboxMessage);
        }
    }
}