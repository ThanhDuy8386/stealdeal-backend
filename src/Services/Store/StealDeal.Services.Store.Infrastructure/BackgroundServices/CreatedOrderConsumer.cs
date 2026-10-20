using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StealDeal.Services.Store.Application.DTOs.Events;
using StealDeal.Services.Store.Application.Messaging;
using StealDeal.Services.Store.Infrastructure.Configuration;

namespace StealDeal.Services.Store.Infrastructure.BackgroundServices
{
    public class CreatedOrderConsumer : BackgroundService
    {
        private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(60);

        private readonly RabbitMqSettings _rabbitSettings;
        private readonly OrderCreatedConsummerSettings _consumerSettings;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CreatedOrderConsumer> _logger;
        private IConnection? _connection;
        private IChannel? _channel;

        public CreatedOrderConsumer(
            IOptions<RabbitMqSettings> rabbitSettings,
            IOptions<OrderCreatedConsummerSettings> consumerSettings,
            IServiceScopeFactory scopeFactory,
            ILogger<CreatedOrderConsumer> logger)
        {
            _rabbitSettings = rabbitSettings.Value;
            _consumerSettings = consumerSettings.Value;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("CreatedOrderConsumer background service is starting.");

            var retryDelay = InitialRetryDelay;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await StartConsumerAsync(stoppingToken);
                    retryDelay = InitialRetryDelay;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "CreatedOrderConsumer could not connect to RabbitMQ. Retrying in {RetryDelaySeconds} seconds.",
                        retryDelay.TotalSeconds);

                    await DisposeRabbitResourcesAsync();
                    await Task.Delay(retryDelay, stoppingToken);

                    retryDelay = TimeSpan.FromSeconds(
                        Math.Min(retryDelay.TotalSeconds * 2, MaxRetryDelay.TotalSeconds));
                }
            }

            await DisposeRabbitResourcesAsync();
        }

        private async Task StartConsumerAsync(CancellationToken stoppingToken)
        {
            var factory = new ConnectionFactory
            {
                HostName = _rabbitSettings.HostName,
                Port = _rabbitSettings.Port,
                UserName = _rabbitSettings.UserName,
                Password = _rabbitSettings.Password,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true
            };

            _connection = await factory.CreateConnectionAsync(stoppingToken);
            _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

            await _channel.ExchangeDeclareAsync(
                exchange: _consumerSettings.ExchangeName,
                type: _consumerSettings.ExchangeType,
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: stoppingToken);

            await _channel.QueueDeclareAsync(
                queue: _consumerSettings.QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: stoppingToken);

            await _channel.QueueBindAsync(
                queue: _consumerSettings.QueueName,
                exchange: _consumerSettings.ExchangeName,
                routingKey: _consumerSettings.BindingKey,
                cancellationToken: stoppingToken);

            await _channel.BasicQosAsync(
                prefetchSize: 0,
                prefetchCount: _consumerSettings.PrefetchCount,
                global: false,
                cancellationToken: stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += OnMessageReceivedAsync;

            await _channel.BasicConsumeAsync(
                queue: _consumerSettings.QueueName,
                autoAck: false,
                consumer: consumer,
                cancellationToken: stoppingToken);

            _logger.LogInformation(
                "CreatedOrderConsumer successfully bound and listening to {QueueName}.",
                _consumerSettings.QueueName);

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }

        private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs args)
        {
            var body = args.Body.ToArray();
            var payload = Encoding.UTF8.GetString(body);

            _logger.LogInformation(
                "Received message with RoutingKey: {RoutingKey}, DeliveryTag: {DeliveryTag}",
                args.RoutingKey, args.DeliveryTag);

            try
            {
                //Deserialize payload của message thành objecct
                var @event = JsonSerializer.Deserialize<CreateOrderEvent>(payload, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (@event == null)
                {
                    throw new InvalidOperationException("Message payload could not be deserialized into CreateOrderEvent.");
                }

                using var scope = _scopeFactory.CreateScope();
                //gọi handler ra để xử lý
                var handler = scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<CreateOrderEvent>>();
                var context = CreateEventContext(args);

                //handler thực hiện business logic
                await handler.HandleAsync(@event, context, args.CancellationToken);

                _logger.LogInformation("Processed trừ hàng cho đơn id: {OrderId}", @event.OrderId);

                // Acknowledge the message
                if (_channel != null)
                {
                    await _channel.BasicAckAsync(args.DeliveryTag, multiple: false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process message with DeliveryTag: {DeliveryTag}", args.DeliveryTag);

                // Nack without requeuing to prevent infinite loops on malformed messages.
                // In production, you would route to a DLQ instead.
                if (_channel != null)
                {
                    await _channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false);
                }
            }
        }

        private IntegrationEventContext CreateEventContext(BasicDeliverEventArgs args)
        {
            var messageId = args.BasicProperties.MessageId;

            if (!Guid.TryParse(messageId, out var parsedMessageId))
            {
                throw new InvalidOperationException("MessageId is missing or is not a valid Guid.");
            }

            return new IntegrationEventContext
            {
                MessageId = parsedMessageId,
                ConsumerName = nameof(CreatedOrderConsumer),
                EventType = string.IsNullOrWhiteSpace(args.BasicProperties.Type)
                    ? nameof(CreateOrderEvent)
                    : args.BasicProperties.Type,
                RoutingKey = args.RoutingKey
            };
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("CreatedOrderConsumer background service is stopping.");

            await DisposeRabbitResourcesAsync();

            await base.StopAsync(cancellationToken);
        }

        private async Task DisposeRabbitResourcesAsync()
        {
            if (_channel != null)
            {
                try
                {
                    await _channel.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error while disposing CreatedOrderConsumer channel.");
                }

                _channel = null;
            }

            if (_connection != null)
            {
                try
                {
                    await _connection.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error while disposing CreatedOrderConsumer connection.");
                }

                _connection = null;
            }
        }
    }
}
