namespace StealDeal.Services.Identity.Infrastructure.Configuration;

public sealed class TrustScoreConsumerSettings
{
    public const string SectionName = "TrustScoreConsumer";

    // Declare the queues first; enable processing after migration/producer rollout.
    public bool Enabled { get; set; }
    public string ExchangeName { get; set; } = "stealdeal.events";
    public string ExchangeType { get; set; } = "topic";
    public string QueueName { get; set; } = "identity.trust-score-events";
    public string[] BindingKeys { get; set; } = [];
    public ushort PrefetchCount { get; set; } = 1;
    public int RetryDelaySeconds { get; set; } = 30;
    public int MaxRetryCount { get; set; } = 5;
    public int ReconnectDelaySeconds { get; set; } = 10;

    public string RetryQueueName => $"{QueueName}.retry";
    public string DeadLetterQueueName => $"{QueueName}.dead-letter";
}
