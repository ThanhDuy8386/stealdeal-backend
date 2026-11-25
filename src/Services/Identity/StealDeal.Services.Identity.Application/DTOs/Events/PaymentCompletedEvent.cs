namespace StealDeal.Services.Identity.Application.DTOs.Events;

public sealed class PaymentCompletedEvent
{
    public Guid MessageId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public Guid OrderId { get; set; }
    public Guid UserId { get; set; }
    public Guid PaymentId { get; set; }
    public decimal Amount { get; set; }
    public string? PaymentMethod { get; set; }
    public string? GatewayRef { get; set; }
}
