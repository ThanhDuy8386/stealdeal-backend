namespace StealDeal.Services.Order.Infrastructure.Configuration
{
    public class OrderStatusConsumerSettings
    {
        public string ExchangeName { get; set; } = "stealdeal.events";
        public string ExchangeType { get; set; } = "topic";
        public string QueueName { get; set; } = "order.saga-events";
        public string[] BindingKeys { get; set; } =
        [
            "inventory.reservation_failed",
            "payment.failed",
            "payment.completed"
        ];
        public ushort PrefetchCount { get; set; } = 10;
    }
}
