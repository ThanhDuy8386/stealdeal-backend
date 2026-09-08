namespace StealDeal.Services.Payment.Infrastructure.Configuration
{
    public class InventoryReservedConsumerSettings
    {
        public string ExchangeName { get; set; } = "stealdeal.events";
        public string ExchangeType { get; set; } = "topic";
        public string QueueName { get; set; } = "payment.inventory-reserved";
        public string BindingKey { get; set; } = "inventory.reserved";
        public ushort PrefetchCount { get; set; } = 10;
    }
}
