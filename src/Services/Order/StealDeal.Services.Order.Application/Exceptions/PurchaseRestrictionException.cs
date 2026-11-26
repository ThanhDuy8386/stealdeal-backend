using StealDeal.Services.Order.Application.DTOs.Response;

namespace StealDeal.Services.Order.Application.Exceptions;

public sealed class PurchaseRestrictionException : Exception
{
    public string ReasonCode { get; }
    public int Score { get; }
    public DateTime? RestrictedUntilUtc { get; }
    public int? MaxPendingOrders { get; }
    public bool IsSuspended { get; }

    public PurchaseRestrictionException(string message, string reasonCode, PurchaseEligibilityClientResponse eligibility)
        : base(message)
    {
        ReasonCode = reasonCode;
        Score = eligibility.Score;
        RestrictedUntilUtc = eligibility.RestrictedUntilUtc;
        MaxPendingOrders = eligibility.MaxPendingOrders;
        IsSuspended = !eligibility.CanCreateOrder;
    }
}
