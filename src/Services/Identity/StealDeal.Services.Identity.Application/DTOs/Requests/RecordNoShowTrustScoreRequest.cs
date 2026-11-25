namespace StealDeal.Services.Identity.Application.DTOs.Requests;

// Application input for the future Order consumer, not a seller-controlled score adjustment.
public sealed class RecordNoShowTrustScoreRequest
{
    public Guid UserId { get; set; }
    public Guid OrderId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public Guid MarkedByUserId { get; set; }
    public string? Reason { get; set; }
}
