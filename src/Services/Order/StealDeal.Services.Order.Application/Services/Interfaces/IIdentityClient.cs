using StealDeal.Services.Order.Application.DTOs.Response;

namespace StealDeal.Services.Order.Application.Services.Interfaces;

public interface IIdentityClient
{
    Task<PurchaseEligibilityClientResponse> GetPurchaseEligibilityAsync(
        string accessToken, CancellationToken cancellationToken = default);
}
