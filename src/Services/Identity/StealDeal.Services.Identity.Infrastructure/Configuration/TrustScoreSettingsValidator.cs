using Microsoft.Extensions.Options;
using StealDeal.Services.Identity.Application.Configuration;
using StealDeal.Services.Identity.Domain.Constants;

namespace StealDeal.Services.Identity.Infrastructure.Configuration;

public sealed class TrustScoreSettingsValidator : IValidateOptions<TrustScoreSettings>
{
    public ValidateOptionsResult Validate(string? name, TrustScoreSettings options)
    {
        var failures = new List<string>();

        if (options.PaymentSuccessReward is < 1 or > TrustScoreLimits.MaximumScore)
            failures.Add("TrustScore:PaymentSuccessReward must be between 1 and 100.");

        if (options.NoShowPenalty is < 1 or > TrustScoreLimits.MaximumScore)
            failures.Add("TrustScore:NoShowPenalty must be a positive amount between 1 and 100.");

        if (options.TemporaryBlockBelowScore <= TrustScoreLimits.MinimumScore ||
            options.TemporaryBlockBelowScore >= options.NormalPurchaseMinimumScore ||
            options.NormalPurchaseMinimumScore > TrustScoreLimits.MaximumScore)
            failures.Add("TrustScore thresholds must satisfy 0 < TemporaryBlockBelowScore < NormalPurchaseMinimumScore <= 100.");

        if (options.PurchaseSuspensionHours is < 1 or > 8760)
            failures.Add("TrustScore:PurchaseSuspensionHours must be between 1 and 8760 (one year).");

        if (options.LimitedMaxPendingOrders < 1)
            failures.Add("TrustScore:LimitedMaxPendingOrders must be greater than zero.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
