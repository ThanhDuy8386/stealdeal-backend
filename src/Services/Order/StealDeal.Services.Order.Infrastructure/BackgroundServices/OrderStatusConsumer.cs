using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StealDeal.Services.Order.Application.DTOs.Events;
using StealDeal.Services.Order.Application.Messaging;
using StealDeal.Services.Order.Infrastructure.Configuration;

namespace StealDeal.Services.Order.Infrastructure.BackgroundServices
{
    public class OrderStatusConsumer : BackgroundService
    {
        private const string InventoryReservationFailedRoutingKey = "inventory.reservation_failed";
        private const string PaymentFailedRoutingKey = "payment.failed";
        private const string PaymentCompletedRoutingKey = "payment.completed";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly RabbitMqSettings _rabbitSettings;
        private readonly OrderStatusConsumerSettings _consumerSettings;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OrderStatusConsumer> _logger;
        private IConnection? _connection;
        private IChannel? _channel;

        public OrderStatusConsumer(
            IOptions<RabbitMqSettings> rabbitSettings,
            IOptions<OrderStatusConsumerSettings> consumerSettings,
            IServiceScopeFactory scopeFactory,
            ILogger<OrderStatusConsumer> logger)
        {
            _rabbitSettings = rabbitSettings.Value;
            _consumerSettings = consumerSettings.Value;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("OrderStatusConsumer background service is starting.");

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

                foreach (var bindingKey in _consumerSettings.BindingKeys)
                {
                    await _channel.QueueBindAsync(
                        queue: _consumerSettings.QueueName,
                        exchange: _consumerSettings.ExchangeName,
                        routingKey: bindingKey,
                        cancellationToken: stoppingToken);
                }

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
                    "OrderStatusConsumer successfully bound and listening to {QueueName}.",
                    _consumerSettings.QueueName);

                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // App is stopping.
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Fatal error occurred while starting OrderStatusConsumer.");
            }
        }

        private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs args)
        {
            var payload = Encoding.UTF8.GetString(args.Body.ToArray());

            _logger.LogInformation(
                "Received order status message with RoutingKey: {RoutingKey}, DeliveryTag: {DeliveryTag}",
                args.RoutingKey,
                args.DeliveryTag);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = CreateEventContext(args);

                await DispatchToHandlerAsync(scope.ServiceProvider, args.RoutingKey, payload, context, args.CancellationToken);

                if (_channel != null)
                {
                    await _channel.BasicAckAsync(args.DeliveryTag, multiple: false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to process order status message with RoutingKey: {RoutingKey}, DeliveryTag: {DeliveryTag}",
                    args.RoutingKey,
                    args.DeliveryTag);

                if (_channel != null)
                {
                    await _channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false);
                }
            }
        }

        private static async Task DispatchToHandlerAsync(
            IServiceProvider serviceProvider,
            string routingKey,
            string payload,
            IntegrationEventContext context,
            CancellationToken cancellationToken)
        {
            switch (routingKey)
            {
                case InventoryReservationFailedRoutingKey:
                    var inventoryFailedEvent = DeserializeEvent<InventoryReservationFailedEvent>(payload);
                    await serviceProvider
                        .GetRequiredService<IIntegrationEventHandler<InventoryReservationFailedEvent>>()
                        .HandleAsync(inventoryFailedEvent, context, cancellationToken);
                    break;

                case PaymentFailedRoutingKey:
                    var paymentFailedEvent = DeserializeEvent<PaymentFailedEvent>(payload);
                    await serviceProvider
                        .GetRequiredService<IIntegrationEventHandler<PaymentFailedEvent>>()
                        .HandleAsync(paymentFailedEvent, context, cancellationToken);
                    break;

                case PaymentCompletedRoutingKey:
                    var paymentCompletedEvent = DeserializeEvent<PaymentCompletedEvent>(payload);
                    await serviceProvider
                        .GetRequiredService<IIntegrationEventHandler<PaymentCompletedEvent>>()
                        .HandleAsync(paymentCompletedEvent, context, cancellationToken);
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported routing key '{routingKey}'.");
            }
        }

        private static TEvent DeserializeEvent<TEvent>(string payload)
        {
            var @event = JsonSerializer.Deserialize<TEvent>(payload, JsonOptions);

            if (@event == null)
            {
                throw new InvalidOperationException($"Message payload could not be deserialized into {typeof(TEvent).Name}.");
            }

            return @event;
        }

        private static IntegrationEventContext CreateEventContext(BasicDeliverEventArgs args)
        {
            var messageId = args.BasicProperties.MessageId;

            if (!Guid.TryParse(messageId, out var parsedMessageId))
            {
                throw new InvalidOperationException("MessageId is missing or is not a valid Guid.");
            }

            return new IntegrationEventContext
            {
                MessageId = parsedMessageId,
                ConsumerName = nameof(OrderStatusConsumer),
                EventType = string.IsNullOrWhiteSpace(args.BasicProperties.Type)
                    ? args.RoutingKey
                    : args.BasicProperties.Type,
                RoutingKey = args.RoutingKey
            };
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("OrderStatusConsumer background service is stopping.");

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
