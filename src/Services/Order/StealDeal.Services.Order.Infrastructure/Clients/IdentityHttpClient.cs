using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using StealDeal.Services.Order.Application.Constants;
using StealDeal.Services.Order.Application.DTOs.Response;
using StealDeal.Services.Order.Application.Exceptions;
using StealDeal.Services.Order.Application.Services.Interfaces;

namespace StealDeal.Services.Order.Infrastructure.Clients;

public sealed class IdentityHttpClient : IIdentityClient
{
    private readonly HttpClient _httpClient;

    public IdentityHttpClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<PurchaseEligibilityClientResponse> GetPurchaseEligibilityAsync(
        string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new UnauthorizedException("Access token is required.");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/identity/account/purchase-eligibility");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new UnauthorizedException("Your session is no longer valid. Please sign in again.");
            if (!response.IsSuccessStatusCode)
                throw new DependencyUnavailableException("Identity service is unavailable. Please try again later.");

            var eligibility = await response.Content.ReadFromJsonAsync<PurchaseEligibilityClientResponse>(
                cancellationToken: cancellationToken);
            if (eligibility == null || !IsValid(eligibility))
                throw new DependencyUnavailableException("Identity service returned invalid purchase eligibility.");
            return eligibility;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DependencyUnavailableException("Identity service timed out. Please try again later.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
        {
            throw new DependencyUnavailableException("Could not read purchase eligibility. Please try again later.", ex);
        }
    }

    private static bool IsValid(PurchaseEligibilityClientResponse value)
    {
        if (value.Score is < 0 or > 100 || value.MaxPendingOrders is <= 0)
            return false;
        return value.Tier switch
        {
            "Normal" => value.CanCreateOrder && value.MaxPendingOrders == null && value.RestrictedUntilUtc == null,
            "Limited" => value.CanCreateOrder && value.MaxPendingOrders > 0 && value.RestrictedUntilUtc == null,
            "TemporarilyBlocked" => !value.CanCreateOrder && value.MaxPendingOrders > 0 &&
                value.RestrictedUntilUtc is { Kind: DateTimeKind.Utc } until && until != default &&
                value.ReasonCode == OrderReasonCodes.TrustScorePurchaseSuspended && !string.IsNullOrWhiteSpace(value.Message),
            _ => false
        };
    }
}
