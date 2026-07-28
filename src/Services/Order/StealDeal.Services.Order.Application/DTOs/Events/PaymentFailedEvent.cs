namespace StealDeal.Services.Order.Application.DTOs.Events
{
    public class PaymentFailedEvent
    {
        public Guid MessageId { get; set; }
        public DateTime OccurredAtUtc { get; set; }
        public Guid OrderId { get; set; }
        public Guid PaymentId { get; set; }
        public string ReasonCode { get; set; } = null!;
        public string Reason { get; set; } = null!;
    }
}
