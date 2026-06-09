namespace StealDeal.Services.Identity.Infrastructure.Configuration
{
    public class OutboxSettings
    {
        public int BatchSize { get; set; } = 20;
        public int PollingIntervalSeconds { get; set; } = 10;
        public int MaxRetryCount { get; set; } = 5;
    }
}
