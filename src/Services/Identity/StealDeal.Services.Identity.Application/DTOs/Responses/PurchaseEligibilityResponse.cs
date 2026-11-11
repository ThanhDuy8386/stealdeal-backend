namespace StealDeal.Services.Identity.Application.DTOs.Responses;

/// <summary>Trust-score policy only; Order enforces the pending-order quota.</summary>
public sealed class PurchaseEligibilityResponse
{
    public int Score { get; set; }
    public string Tier { get; set; } = null!;
    public bool CanCreateOrder { get; set; }
    public DateTime? RestrictedUntilUtc { get; set; }
    public int? MaxPendingOrders { get; set; }
    public string? ReasonCode { get; set; }
    public string? Message { get; set; }
}
