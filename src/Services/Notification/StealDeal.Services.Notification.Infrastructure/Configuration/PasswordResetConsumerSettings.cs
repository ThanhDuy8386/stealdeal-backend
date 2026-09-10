using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Notification.Infrastructure.Configuration
{
    public class PasswordResetConsumerSettings
    {
        public string ExchangeName { get; set; } = "stealdeal.events";
        public string ExchangeType { get; set; } = "topic";
        public string QueueName { get; set; } = "notification.password-reset";
        public string BindingKey { get; set; }
            = "identity.user.password-reset.#";
        public ushort PrefetchCount { get; set; } = 10;
    }
}
