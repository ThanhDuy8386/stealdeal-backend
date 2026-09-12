using System.Text;
using System.Text.Json;
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
    public class InventoryReleaseRequestedConsumer : BackgroundService
    {
        private readonly RabbitMqSettings _rabbitSettings;
        private readonly InventoryReleaseRequestedConsumerSettings _consumerSettings;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<InventoryReleaseRequestedConsumer> _logger;
        private IConnection? _connection;
        private IChannel? _channel;

        public InventoryReleaseRequestedConsumer(
            IOptions<RabbitMqSettings> rabbitSettings,
            IOptions<InventoryReleaseRequestedConsumerSettings> consumerSettings,
            IServiceScopeFactory scopeFactory,
            ILogger<InventoryReleaseRequestedConsumer> logger)
        {
            _rabbitSettings = rabbitSettings.Value;
            _consumerSettings = consumerSettings.Value;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("InventoryReleaseRequestedConsumer background service is starting.");

            try
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
                    "InventoryReleaseRequestedConsumer successfully bound and listening to {QueueName}.",
                    _consumerSettings.QueueName);

                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Fatal error occurred while starting InventoryReleaseRequestedConsumer.");
            }
        }

        private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs args)
        {
            var body = args.Body.ToArray();
            var payload = Encoding.UTF8.GetString(body);

            _logger.LogInformation(
                "Received inventory release message with RoutingKey: {RoutingKey}, DeliveryTag: {DeliveryTag}",
                args.RoutingKey,
                args.DeliveryTag);

            try
            {
                var @event = JsonSerializer.Deserialize<InventoryReleaseRequestedEvent>(payload, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (@event == null)
                {
                    throw new InvalidOperationException(
                        "Message payload could not be deserialized into InventoryReleaseRequestedEvent.");
                }

                using var scope = _scopeFactory.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<InventoryReleaseRequestedEvent>>();
                var context = CreateEventContext(args);

                await handler.HandleAsync(@event, context, args.CancellationToken);

                _logger.LogInformation("Released inventory for order id: {OrderId}", @event.OrderId);

                if (_channel != null)
                {
                    await _channel.BasicAckAsync(args.DeliveryTag, multiple: false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process inventory release message with DeliveryTag: {DeliveryTag}", args.DeliveryTag);

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
                ConsumerName = nameof(InventoryReleaseRequestedConsumer),
                EventType = string.IsNullOrWhiteSpace(args.BasicProperties.Type)
                    ? nameof(InventoryReleaseRequestedEvent)
                    : args.BasicProperties.Type,
                RoutingKey = args.RoutingKey
            };
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("InventoryReleaseRequestedConsumer background service is stopping.");

            if (_channel != null)
            {
                await _channel.CloseAsync(cancellationToken);
            }

            if (_connection != null)
            {
                await _connection.CloseAsync(cancellationToken);
            }

            await base.StopAsync(cancellationToken);
        }
    }
}
