using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StealDeal.Services.Identity.Application.DTOs.Events;
using StealDeal.Services.Identity.Application.Messaging;
using StealDeal.Services.Identity.Infrastructure.Configuration;

namespace StealDeal.Services.Identity.Infrastructure.BackgroundServices;

public class StoreVerifiedConsumer : BackgroundService
{
    private readonly RabbitMqSettings _rabbitSettings;
    private readonly StoreVerifiedConsumerSettings _consumerSettings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StoreVerifiedConsumer> _logger;

    private IConnection? _connection;
    private IChannel? _channel;

    public StoreVerifiedConsumer(
        IOptions<RabbitMqSettings> rabbitSettings,
        IOptions<StoreVerifiedConsumerSettings> consumerSettings,
        IServiceScopeFactory scopeFactory,
        ILogger<StoreVerifiedConsumer> logger)
    {
        _rabbitSettings = rabbitSettings.Value;
        _consumerSettings = consumerSettings.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
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
        _channel = await _connection.CreateChannelAsync(
            cancellationToken: stoppingToken);

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
        consumer.ReceivedAsync += HandleMessageAsync;

        await _channel.BasicConsumeAsync(
            queue: _consumerSettings.QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation(
            "StoreVerifiedConsumer listening on {QueueName}.",
            _consumerSettings.QueueName);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleMessageAsync(
        object sender,
        BasicDeliverEventArgs args)
    {
        try
        {
            var payload = Encoding.UTF8.GetString(args.Body.ToArray());

            var @event = JsonSerializer.Deserialize<StoreVerifiedEvent>(
                payload,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (@event is null)
                throw new InvalidOperationException(
                    "Invalid StoreVerifiedEvent payload.");

            using var scope = _scopeFactory.CreateScope();

            var handler =
                scope.ServiceProvider
                    .GetRequiredService<
                        IIntegrationEventHandler<StoreVerifiedEvent>>();

            var messageId = args.BasicProperties.MessageId;

            if (!Guid.TryParse(messageId, out var parsedMessageId))
                throw new InvalidOperationException(
                    "MessageId is missing or invalid.");

            await handler.HandleAsync(
                @event,
                new IntegrationEventContext
                {
                    MessageId = parsedMessageId,
                    ConsumerName = nameof(StoreVerifiedConsumer),
                    EventType = nameof(StoreVerifiedEvent),
                    RoutingKey = args.RoutingKey
                },
                args.CancellationToken);

            await _channel!.BasicAckAsync(
                args.DeliveryTag,
                multiple: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to process StoreVerifiedEvent.");

            await _channel!.BasicNackAsync(
                args.DeliveryTag,
                multiple: false,
                requeue: false);
        }
    }

    public override async Task StopAsync(
        CancellationToken cancellationToken)
    {
        if (_channel is not null)
            await _channel.CloseAsync(cancellationToken);

        if (_connection is not null)
            await _connection.CloseAsync(cancellationToken);

        await base.StopAsync(cancellationToken);
    }
}