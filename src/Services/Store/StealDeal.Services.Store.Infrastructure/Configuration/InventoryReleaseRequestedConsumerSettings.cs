namespace StealDeal.Services.Store.Infrastructure.Configuration
{
    public class InventoryReleaseRequestedConsumerSettings
    {
        public string ExchangeName { get; set; } = "stealdeal.events";
        public string ExchangeType { get; set; } = "topic";
        public string QueueName { get; set; } = "store.inventory-release-requested";
        public string BindingKey { get; set; } = "inventory.release_requested";
        public ushort PrefetchCount { get; set; } = 10;
    }
}
