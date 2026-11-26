using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StealDeal.Services.Identity.Application.Constants;
using StealDeal.Services.Identity.Application.DTOs.Events;
using StealDeal.Services.Identity.Application.Exceptions;
using StealDeal.Services.Identity.Application.Messaging;
using StealDeal.Services.Identity.Infrastructure.Configuration;

namespace StealDeal.Services.Identity.Infrastructure.BackgroundServices;

public sealed class TrustScoreConsumer : BackgroundService
{
    private const string RetryCountHeader = "x-trust-score-retry-count";
    private const string OriginalRoutingKeyHeader = "x-trust-score-routing-key";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly RabbitMqSettings _rabbitSettings;
    private readonly TrustScoreConsumerSettings _consumerSettings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TrustScoreConsumer> _logger;

    public TrustScoreConsumer(
        IOptions<RabbitMqSettings> rabbitSettings,
        IOptions<TrustScoreConsumerSettings> consumerSettings,
        IServiceScopeFactory scopeFactory,
        ILogger<TrustScoreConsumer> logger)
    {
        _rabbitSettings = rabbitSettings.Value;
        _consumerSettings = consumerSettings.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StartConsumerAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TrustScoreConsumer disconnected. Reconnecting in {DelaySeconds}s.",
                    _consumerSettings.ReconnectDelaySeconds);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_consumerSettings.ReconnectDelaySeconds), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task StartConsumerAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _rabbitSettings.HostName,
            Port = _rabbitSettings.Port,
            UserName = _rabbitSettings.UserName,
            Password = _rabbitSettings.Password,
            ConsumerDispatchConcurrency = 1,
            // Recreate both connection and topology in the outer reconnect loop.
            AutomaticRecoveryEnabled = false
        };
        using var session = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var token = session.Token;
        await using var connection = await factory.CreateConnectionAsync(token);
        await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(
            publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), token);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task OnShutdownAsync(object sender, ShutdownEventArgs args)
        {
            disconnected.TrySetException(new IOException($"RabbitMQ consumer stopped: {args.ReplyText}"));
            return Task.CompletedTask;
        }
        connection.ConnectionShutdownAsync += OnShutdownAsync;
        channel.ChannelShutdownAsync += OnShutdownAsync;

        try
        {
            await DeclareTopologyAsync(channel, token);
            if (_consumerSettings.Enabled)
            {
                await channel.BasicQosAsync(0, _consumerSettings.PrefetchCount, global: false, cancellationToken: token);
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (_, args) =>
                {
                    try
                    {
                        await OnMessageReceivedAsync(channel, args, token);
                    }
                    catch (Exception ex)
                    {
                        // Close/reconnect if transferring or ACKing failed. The original stays unacked.
                        disconnected.TrySetException(ex);
                    }
                };
                await channel.BasicConsumeAsync(_consumerSettings.QueueName, autoAck: false,
                    consumer: consumer, cancellationToken: token);
                _logger.LogInformation("TrustScoreConsumer listening on {QueueName}.", _consumerSettings.QueueName);
            }
            else
            {
                _logger.LogInformation("Trust-score queues declared; processing disabled. Enable {Section}:Enabled after rollout checks.",
                    TrustScoreConsumerSettings.SectionName);
            }

            await disconnected.Task.WaitAsync(token);
        }
        finally
        {
            await session.CancelAsync();
        }
    }

    private async Task DeclareTopologyAsync(IChannel channel, CancellationToken token)
    {
        await channel.ExchangeDeclareAsync(_consumerSettings.ExchangeName, _consumerSettings.ExchangeType,
            durable: true, autoDelete: false, cancellationToken: token);
        await channel.QueueDeclareAsync(_consumerSettings.QueueName, durable: true, exclusive: false,
            autoDelete: false, cancellationToken: token);
        await channel.QueueDeclareAsync(_consumerSettings.DeadLetterQueueName, durable: true, exclusive: false,
            autoDelete: false, cancellationToken: token);
        await channel.QueueDeclareAsync(_consumerSettings.RetryQueueName, durable: true, exclusive: false,
            autoDelete: false, arguments: new Dictionary<string, object?>
            {
                ["x-message-ttl"] = _consumerSettings.RetryDelaySeconds * 1000,
                ["x-dead-letter-exchange"] = "",
                // Return only to Identity, without republishing payment.completed to Order.
                ["x-dead-letter-routing-key"] = _consumerSettings.QueueName
            }, cancellationToken: token);

        foreach (var routingKey in _consumerSettings.BindingKeys)
            await channel.QueueBindAsync(_consumerSettings.QueueName, _consumerSettings.ExchangeName, routingKey,
                cancellationToken: token);
    }

    private async Task OnMessageReceivedAsync(IChannel channel, BasicDeliverEventArgs args, CancellationToken token)
    {
        var routingKey = args.RoutingKey;
        var retryCount = 0;
        try
        {
            routingKey = GetOriginalRoutingKey(args);
            retryCount = GetRetryCount(args.BasicProperties.Headers);
            var context = CreateEventContext(args, routingKey);
            // A new DbContext for each delivery, including every delayed retry.
            await using var scope = _scopeFactory.CreateAsyncScope();
            await DispatchToHandlerAsync(scope.ServiceProvider, args.Body, context, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw; // Shutdown leaves the delivery unacked for another session.
        }
        catch (Exception ex)
        {
            var retry = IsRetryable(ex) && retryCount < _consumerSettings.MaxRetryCount;
            var destination = retry ? _consumerSettings.RetryQueueName : _consumerSettings.DeadLetterQueueName;
            _logger.LogWarning(ex,
                "Trust-score message {MessageId} ({RoutingKey}), retry {RetryCount}, moving to {Destination}.",
                args.BasicProperties.MessageId, routingKey, retryCount, destination);

            var properties = new BasicProperties(args.BasicProperties)
            {
                Persistent = true,
                Expiration = null,
                Headers = args.BasicProperties.Headers == null
                    ? new Dictionary<string, object?>()
                    : new Dictionary<string, object?>(args.BasicProperties.Headers)
            };
            properties.Headers[OriginalRoutingKeyHeader] = routingKey;
            properties.Headers[RetryCountHeader] = retry ? retryCount + 1 : retryCount;
            properties.Headers["x-trust-score-error"] = ex.GetType().Name;

            // Confirm the durable copy before ACK. A crash between these steps can duplicate,
            // which is safe because the trust-score transaction is idempotent.
            await channel.BasicPublishAsync("", destination, mandatory: true,
                basicProperties: properties, body: args.Body, cancellationToken: token);
        }

        // Successful handler returns only after its transaction commits.
        await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken: token);
    }

    private static Task DispatchToHandlerAsync(IServiceProvider serviceProvider, ReadOnlyMemory<byte> body,
        IntegrationEventContext context, CancellationToken cancellationToken)
    {
        switch (context.RoutingKey)
        {
            case TrustScoreEventRoutingKeys.PaymentCompleted:
                return serviceProvider.GetRequiredService<IIntegrationEventHandler<PaymentCompletedEvent>>()
                    .HandleAsync(DeserializeEvent<PaymentCompletedEvent>(body), context, cancellationToken);
            case TrustScoreEventRoutingKeys.OrderNoShow:
                return serviceProvider.GetRequiredService<IIntegrationEventHandler<OrderNoShowEvent>>()
                    .HandleAsync(DeserializeEvent<OrderNoShowEvent>(body), context, cancellationToken);
            default:
                throw new BadRequestException($"Unsupported trust-score routing key '{context.RoutingKey}'.");
        }
    }

    private static TEvent DeserializeEvent<TEvent>(ReadOnlyMemory<byte> body) =>
        JsonSerializer.Deserialize<TEvent>(body.Span, JsonOptions)
        ?? throw new BadRequestException("Event payload cannot be null.");

    private static IntegrationEventContext CreateEventContext(BasicDeliverEventArgs args, string routingKey)
    {
        if (!Guid.TryParse(args.BasicProperties.MessageId, out var messageId) || messageId == Guid.Empty)
            throw new BadRequestException("Broker MessageId must be a non-empty Guid.");
        if (!string.IsNullOrWhiteSpace(args.BasicProperties.Type) && args.BasicProperties.Type != routingKey)
            throw new BadRequestException("Broker Type must match the event routing key.");

        return new IntegrationEventContext
        {
            MessageId = messageId,
            ConsumerName = nameof(TrustScoreConsumer),
            EventType = routingKey,
            RoutingKey = routingKey
        };
    }

    private string GetOriginalRoutingKey(BasicDeliverEventArgs args)
    {
        if (args.RoutingKey != _consumerSettings.QueueName)
            return args.RoutingKey;
        if (args.BasicProperties.Headers?.TryGetValue(OriginalRoutingKeyHeader, out var value) == true)
        {
            if (value is byte[] bytes) return Encoding.UTF8.GetString(bytes);
            if (value is string text) return text;
        }
        throw new BadRequestException("Retried message is missing its original routing key.");
    }

    private static int GetRetryCount(IDictionary<string, object?>? headers)
    {
        if (headers?.TryGetValue(RetryCountHeader, out var value) != true)
            return 0;
        return value switch
        {
            int count when count >= 0 => count,
            long count when count >= 0 && count <= int.MaxValue => (int)count,
            _ => throw new BadRequestException("Invalid trust-score retry count.")
        };
    }

    private static bool IsRetryable(Exception exception)
    {
        if (exception is PaymentTrustScoreNotRecordedException)
            return true;
        if (exception is BadRequestException or ConflictException or NotFoundException or JsonException or ArgumentException)
            return false;

        // Atomic SQL throws SqlException directly; SaveChanges may wrap it in DbUpdateException.
        for (Exception? current = exception; current != null; current = current.InnerException)
            if (current is SqlException sql)
                return sql.Number is 2601 or 2627 or 1205 or -2 || sql.IsTransient;

        return exception is not DbUpdateException;
    }
}
