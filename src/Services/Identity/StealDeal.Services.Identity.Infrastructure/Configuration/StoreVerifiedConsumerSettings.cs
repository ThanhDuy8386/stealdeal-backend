using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Identity.Infrastructure.Configuration
{
    public class StoreVerifiedConsumerSettings
    {
        public string ExchangeName { get; set; } = "stealdeal.events";
        public string ExchangeType { get; set; } = "topic";
        public string QueueName { get; set; } = "identity.store-verified";
        public string BindingKey { get; set; } = "store.verified";
        public ushort PrefetchCount { get; set; } = 10;
    }
}
