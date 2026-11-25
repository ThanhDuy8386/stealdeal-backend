using StealDeal.Services.Identity.Application.DTOs.Requests;
using StealDeal.Services.Identity.Application.DTOs.Responses;
using StealDeal.Services.Identity.Application.Messaging;

namespace StealDeal.Services.Identity.Application.Services.Interfaces;

public interface ITrustScoreService
{
    Task RecordPaymentSucceededAsync(
        RecordPaymentTrustScoreRequest request, IntegrationEventContext context,
        CancellationToken cancellationToken = default);
    Task RecordNoShowAsync(
        RecordNoShowTrustScoreRequest request, IntegrationEventContext context,
        CancellationToken cancellationToken = default);
    Task<PurchaseEligibilityResponse> GetPurchaseEligibilityAsync(
        Guid userId, CancellationToken cancellationToken = default);
}
