namespace StealDeal.Services.Order.Application.DTOs.Response;

/// <summary>Wire contract of Identity's purchase-eligibility endpoint.</summary>
public sealed class PurchaseEligibilityClientResponse
{
    public int Score { get; set; }
    public string Tier { get; set; } = null!;
    public bool CanCreateOrder { get; set; }
    public DateTime? RestrictedUntilUtc { get; set; }
    public int? MaxPendingOrders { get; set; }
    public string? ReasonCode { get; set; }
    public string? Message { get; set; }
}
