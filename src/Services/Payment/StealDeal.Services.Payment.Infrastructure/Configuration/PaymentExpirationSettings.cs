namespace StealDeal.Services.Payment.Infrastructure.Configuration
{
    public class PaymentExpirationSettings
    {
        public bool Enabled { get; set; } = true;
        public int BatchSize { get; set; } = 20;
        public int PollingIntervalSeconds { get; set; } = 30;
    }
}
