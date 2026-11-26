namespace StealDeal.Services.Identity.Application.Configuration;

public sealed class TrustScoreSettings
{
    public const string SectionName = "TrustScore";

    public int PaymentSuccessReward { get; set; } = 2;
    // A positive amount to subtract, not a signed score delta.
    public int NoShowPenalty { get; set; } = 15;
    public int NormalPurchaseMinimumScore { get; set; } = 80;
    public int TemporaryBlockBelowScore { get; set; } = 50;
    public int PurchaseSuspensionHours { get; set; } = 24;
    public int LimitedMaxPendingOrders { get; set; } = 1;
}
