namespace StealDeal.Services.Identity.Application.DTOs.Requests;

// Application input for the future payment consumer, not a public API request.
public sealed class RecordPaymentTrustScoreRequest
{
    public Guid UserId { get; set; }
    public Guid OrderId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}
