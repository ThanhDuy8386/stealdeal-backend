namespace StealDeal.Services.Notification.Infrastructure.Configuration
{
    public class EmailVerificationConsumerSettings
    {
        public string ExchangeName { get; set; } = "stealdeal.events";
        public string ExchangeType { get; set; } = "topic";
        public string QueueName { get; set; } = "notification.email-verification";
        public string BindingKey { get; set; } = "identity.user.email-verification.#";
        public ushort PrefetchCount { get; set; } = 10;
    }
}
