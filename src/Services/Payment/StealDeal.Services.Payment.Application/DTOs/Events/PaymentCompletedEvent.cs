namespace StealDeal.Services.Payment.Application.DTOs.Events
{
    public class PaymentCompletedEvent
    {
        public Guid MessageId { get; set; }
        public DateTime OccurredAtUtc { get; set; }
        public Guid OrderId { get; set; }
        public Guid PaymentId { get; set; }
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = null!;
        public string? GatewayRef { get; set; }
    }
}
