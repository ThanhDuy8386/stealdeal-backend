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
using StealDeal.Services.Notification.Application.DTOs.Events;
using StealDeal.Services.Notification.Domain.Interfaces;
using StealDeal.Services.Notification.Domain.Models;
using StealDeal.Services.Notification.Infrastructure.Configuration;

namespace StealDeal.Services.Notification.Infrastructure.BackgroundService
{
    public class EmailVerificationConsumer : Microsoft.Extensions.Hosting.BackgroundService
    {
        private readonly RabbitMqSettings _rabbitSettings;
        private readonly EmailVerificationConsumerSettings _consumerSettings;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<EmailVerificationConsumer> _logger;
        private IConnection? _connection;
        private IChannel? _channel;

        public EmailVerificationConsumer(
            IOptions<RabbitMqSettings> rabbitSettings,
            IOptions<EmailVerificationConsumerSettings> consumerSettings,
            IServiceScopeFactory scopeFactory,
            ILogger<EmailVerificationConsumer> logger)
        {
            _rabbitSettings = rabbitSettings.Value;
            _consumerSettings = consumerSettings.Value;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("EmailVerificationConsumer background service is starting.");

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

                // Declare exchange just in case consumer starts first (idempotent)
                await _channel.ExchangeDeclareAsync(
                    exchange: _consumerSettings.ExchangeName,
                    type: _consumerSettings.ExchangeType,
                    durable: true,
                    autoDelete: false,
                    arguments: null,
                    cancellationToken: stoppingToken);

                // Declare queue
                await _channel.QueueDeclareAsync(
                    queue: _consumerSettings.QueueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null,
                    cancellationToken: stoppingToken);

                // Bind queue to exchange with binding key
                await _channel.QueueBindAsync(
                    queue: _consumerSettings.QueueName,
                    exchange: _consumerSettings.ExchangeName,
                    routingKey: _consumerSettings.BindingKey,
                    cancellationToken: stoppingToken);

                // Qos
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

                _logger.LogInformation("EmailVerificationConsumer successfully bound and listening to {QueueName}.", _consumerSettings.QueueName);

                // Keep service alive
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // App is stopping
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Fatal error occurred while starting EmailVerificationConsumer.");
            }
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
                var @event = JsonSerializer.Deserialize<SendEmailVerificationOtpEvent>(payload, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (@event != null)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var repo = scope.ServiceProvider.GetRequiredService<INotificationProfileRepository>();
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

                    var notification = new NotificationProfile
                    {
                        UserId = @event.UserId,
                        Title = "Verify Email OTP",
                        Body = $"Hello {@event.FullName}, your OTP is {@event.Otp}. It expires at {@event.ExpiresAt:g}.",
                        Type = "EmailVerification",
                        ActionUrl = null,
                        ReferenceId = null,
                        ReferenceType = null,
                        IsRead = false,
                        CreatedAt = DateTime.UtcNow
                    };

                    await repo.AddAsync(notification);
                    await unitOfWork.SaveChangesAsync();

                    _logger.LogInformation("Saved notification profile for user: {UserId}", @event.UserId);
                }

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

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("EmailVerificationConsumer background service is stopping.");

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
