namespace StealDeal.Services.Identity.Application.DTOs.Events;

public sealed class OrderNoShowEvent
{
    public Guid MessageId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public Guid OrderId { get; set; }
    public Guid UserId { get; set; }
    public Guid StoreId { get; set; }
    public Guid MarkedByUserId { get; set; }
    public DateTime PickupDeadlineUtc { get; set; }
    public string? Reason { get; set; }
}
