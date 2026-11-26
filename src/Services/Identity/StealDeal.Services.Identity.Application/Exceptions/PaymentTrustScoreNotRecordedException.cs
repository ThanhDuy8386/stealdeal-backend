namespace StealDeal.Services.Identity.Application.Exceptions;

// A missing prerequisite, not a permanent rejection. The consumer must delay/retry it.
public sealed class PaymentTrustScoreNotRecordedException : ConflictException
{
    public Guid OrderId { get; }

    public PaymentTrustScoreNotRecordedException(Guid orderId)
        : base($"Payment trust score for order '{orderId}' has not been recorded yet.")
    {
        OrderId = orderId;
    }
}
