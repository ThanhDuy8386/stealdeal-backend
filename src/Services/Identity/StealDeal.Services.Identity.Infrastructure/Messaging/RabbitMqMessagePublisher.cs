using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using StealDeal.Services.Identity.Application.DTOs.Events;
using StealDeal.Services.Identity.Application.Services.Interfaces;
using StealDeal.Services.Identity.Infrastructure.Configuration;

namespace StealDeal.Services.Identity.Infrastructure.Messaging
{
    public class RabbitMqMessagePublisher : IMessagePublisher, IAsyncDisposable
    {
        private readonly RabbitMqSettings _settings;
        private readonly ConnectionFactory _factory;
        private readonly ILogger<RabbitMqMessagePublisher> _logger;
        private readonly SemaphoreSlim _connectionLock = new(1, 1);
        private IConnection? _connection;
        public RabbitMqMessagePublisher(
            IOptions<RabbitMqSettings> settings,
            ILogger<RabbitMqMessagePublisher> logger)
        {
            _settings = settings.Value;
            _logger = logger;

            _factory = new ConnectionFactory
            {
                HostName = _settings.HostName,
                Port = _settings.Port,
                UserName = _settings.UserName,
                Password = _settings.Password,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true
            };
        }
        public async Task PublishAsync(IntegrationMessage message, CancellationToken cancellationToken = default)
        {
            ValidateMessage(message);

            var connection = await GetOrCreateConnectionAsync(cancellationToken);

            await using var channel = await CreateConfirmedChannelAsync(
                connection, cancellationToken);

            // Subscribe BasicReturnAsync BEFORE publishing.
            // In AMQP protocol, basic.return arrives BEFORE basic.ack,
            // so by the time BasicPublishAsync completes (after ack),
            // the return has already been processed if it occurred.
            bool messageReturned = false;
            string? returnReason = null;

            channel.BasicReturnAsync += (sender, args) =>
            {
                messageReturned = true;
                returnReason = $"ReplyCode={args.ReplyCode}, ReplyText={args.ReplyText}";
                _logger.LogWarning(
                    "Message {MessageId} with routing key '{RoutingKey}' was returned by broker. {Reason}",
                    message.MessageId, message.RoutingKey, returnReason);
                return Task.CompletedTask;
            };

            await DeclareExchangeAsync(channel, message, cancellationToken);

            // With publisher confirms enabled, BasicPublishAsync only completes
            // after the broker sends basic.ack (exchange-level confirmation).
            // With mandatory: true, broker returns the message if it can't
            // be routed to at least one queue.
            await PublishOnChannelAsync(channel, message, cancellationToken);

            if (messageReturned)
            {
                throw new InvalidOperationException(
                    $"Message '{message.MessageId}' with routing key '{message.RoutingKey}' " +
                    $"was not routed to any queue. {returnReason}");
            }
        }

        public async Task PublishBatchAsync(IReadOnlyCollection<IntegrationMessage> messages, CancellationToken cancellationToken = default)
        {
            if (messages.Count == 0)
            {
                return;
            }

            foreach (var message in messages)
            {
                ValidateMessage(message);
            }

            var connection = await GetOrCreateConnectionAsync(cancellationToken);

            await using var channel = await CreateConfirmedChannelAsync(
                connection, cancellationToken);

            bool messageReturned = false;
            string? returnReason = null;
            Guid returnedMessageId = Guid.Empty;

            channel.BasicReturnAsync += (sender, args) =>
            {
                messageReturned = true;
                returnReason = $"ReplyCode={args.ReplyCode}, ReplyText={args.ReplyText}";
                _logger.LogWarning(
                    "A message with routing key '{RoutingKey}' was returned by broker. {Reason}",
                    args.RoutingKey, returnReason);
                return Task.CompletedTask;
            };

            var declaredExchanges = new HashSet<string>(StringComparer.Ordinal);

            foreach (var message in messages)
            {
                // Reset return flag for each message
                messageReturned = false;
                returnReason = null;

                var exchangeKey = $"{message.ExchangeName}:{message.ExchangeType}";

                if (declaredExchanges.Add(exchangeKey))
                {
                    await DeclareExchangeAsync(channel, message, cancellationToken);
                }

                await PublishOnChannelAsync(channel, message, cancellationToken);

                if (messageReturned)
                {
                    throw new InvalidOperationException(
                        $"Message '{message.MessageId}' with routing key '{message.RoutingKey}' " +
                        $"was not routed to any queue. {returnReason}");
                }
            }
        }

        private static async Task DeclareExchangeAsync(IChannel channel, IntegrationMessage message, CancellationToken cancellationToken)
        {
            await channel.ExchangeDeclareAsync(
                exchange: message.ExchangeName,
                type: message.ExchangeType,
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken);
        }

        private static async Task PublishOnChannelAsync(IChannel channel, IntegrationMessage message, CancellationToken cancellationToken)
        {
            var body = Encoding.UTF8.GetBytes(message.Payload);
            var properties = CreateBasicProperties(message);

            await channel.BasicPublishAsync(
                exchange: message.ExchangeName,
                routingKey: message.RoutingKey,
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);
        }

        private static async Task<IChannel> CreateConfirmedChannelAsync(
            IConnection connection, CancellationToken cancellationToken)
        {
            return await connection.CreateChannelAsync(
                new CreateChannelOptions(
                    publisherConfirmationsEnabled: true,
                    publisherConfirmationTrackingEnabled: true),
                cancellationToken: cancellationToken);
        }

        private static BasicProperties CreateBasicProperties(IntegrationMessage message)
        {
            return new BasicProperties
            {
                Persistent = true,
                Type = message.EventType,
                ContentType = "application/json",
                MessageId = message.MessageId.ToString(),
                Timestamp = new AmqpTimestamp(
                    new DateTimeOffset(message.OccurredAt).ToUnixTimeSeconds())
            };
        }

        private async Task<IConnection> GetOrCreateConnectionAsync(CancellationToken cancellationToken)
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            await _connectionLock.WaitAsync(cancellationToken);

            try
            {
                if (_connection is { IsOpen: true })
                {
                    return _connection;
                }

                if (_connection is not null)
                {
                    await _connection.DisposeAsync();
                }

                _connection = await _factory.CreateConnectionAsync(cancellationToken);
                return _connection;
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
            }

            _connectionLock.Dispose();
        }

        private static void ValidateMessage(IntegrationMessage message)
        {
            if (message.MessageId == Guid.Empty)
            {
                throw new InvalidOperationException("Message id is required.");
            }

            if (string.IsNullOrWhiteSpace(message.ExchangeName))
            {
                throw new InvalidOperationException("Exchange name is required.");
            }

            if (string.IsNullOrWhiteSpace(message.ExchangeType))
            {
                throw new InvalidOperationException("Exchange type is required.");
            }

            if (string.IsNullOrWhiteSpace(message.RoutingKey))
            {
                throw new InvalidOperationException("Routing key is required.");
            }

            if (string.IsNullOrWhiteSpace(message.EventType))
            {
                throw new InvalidOperationException("Event type is required.");
            }

            if (string.IsNullOrWhiteSpace(message.Payload))
            {
                throw new InvalidOperationException("Payload is required.");
            }
        }
    }
}
