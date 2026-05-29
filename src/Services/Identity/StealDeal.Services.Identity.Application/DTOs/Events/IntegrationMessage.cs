namespace StealDeal.Services.Identity.Application.DTOs.Events
{
    public class IntegrationMessage
    {
        public string ExchangeName { get; set; } = null!;
        public string ExchangeType { get; set; } = null!;
        public string RoutingKey { get; set; } = null!;
        public string EventType { get; set; } = null!;
        public string Payload { get; set; } = null!;
    }
}