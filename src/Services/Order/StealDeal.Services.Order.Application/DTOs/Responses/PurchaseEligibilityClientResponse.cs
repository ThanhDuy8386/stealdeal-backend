using System.Text.Json.Serialization;

namespace StealDeal.Services.Order.Application.DTOs.Response;

/// <summary>Wire contract of Identity's purchase-eligibility endpoint.</summary>
public sealed class PurchaseEligibilityClientResponse
{
    [JsonRequired] public int Score { get; set; }
    [JsonRequired] public string Tier { get; set; } = null!;
    [JsonRequired] public bool CanCreateOrder { get; set; }
    public DateTime? RestrictedUntilUtc { get; set; }
    public int? MaxPendingOrders { get; set; }
    public string? ReasonCode { get; set; }
    public string? Message { get; set; }
}
