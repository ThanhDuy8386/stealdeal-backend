namespace StealDeal.Services.Store.Infrastructure.Configuration
{
    public class RedisSettings
    {
        public string ConnectionString { get; set; } = "localhost:6379";
        public int SessionTtlMinutes { get; set; } = 30;
        public int MaxRecentTurns { get; set; } = 10;
    }
}
