namespace StealDeal.Services.Store.Infrastructure.Configuration
{
    public class OrderCreatedConsummerSettings
    {
        public string ExchangeName { get; set; } = "stealdeal.events";
        public string ExchangeType { get; set; } = "topic";
        public string QueueName { get; set; } = "store.order.created";
        public string BindingKey { get; set; } = "order.created";
        public ushort PrefetchCount { get; set; } = 10;
    }
}



//Background service host consumer sẽ dùng cái này để khai báo queue và các thông tin liên quan.