namespace StealDeal.Services.Cart.Infrastructure.Configuration
{
    public class RedisSettings
    {
        public string ConnectionString { get; set; } = "localhost:6379";
        public int CartTtlHours { get; set; } = 24;
        public int CheckoutLockSeconds { get; set; } = 5;
    }
}
