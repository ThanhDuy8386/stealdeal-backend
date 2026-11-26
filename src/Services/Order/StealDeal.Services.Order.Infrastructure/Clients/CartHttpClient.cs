using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using StealDeal.Services.Order.Application.DTOs.Response;
using StealDeal.Services.Order.Application.Exceptions;
using StealDeal.Services.Order.Application.Services.Interfaces;

namespace StealDeal.Services.Order.Infrastructure.Clients
{
    public class CartHttpClient : ICartClient
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<CartHttpClient> _logger;

        public CartHttpClient(HttpClient httpClient, ILogger<CartHttpClient> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<string> AcquireCheckoutLockAsync(
            string accessToken,
            Guid storeId,
            CancellationToken cancellationToken = default)
        {
            using var request = CreateRequest(
                HttpMethod.Post,
                $"api/cart/stores/{storeId}/checkout-lock",
                accessToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            await HttpClientErrorMapper.EnsureSuccessOrThrowAsync(
                response,
                "Could not acquire checkout lock.",
                cancellationToken);

            var body = await response.Content.ReadFromJsonAsync<CheckoutLockClientResponse>(
                cancellationToken: cancellationToken);

            if (body is null || string.IsNullOrWhiteSpace(body.LockToken))
            {
                throw new ConflictException("Checkout lock response is invalid.");
            }

            return body.LockToken;
        }

        public async Task ReleaseCheckoutLockAsync(
            string accessToken,
            Guid storeId,
            string lockToken,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(lockToken))
            {
                return;
            }

            try
            {
                using var request = CreateRequest(
                    HttpMethod.Delete,
                    $"api/cart/stores/{storeId}/checkout-lock",
                    accessToken);
                request.Content = JsonContent.Create(new { lockToken });

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                await HttpClientErrorMapper.EnsureSuccessOrThrowAsync(
                    response, "Could not release checkout lock.", cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // Preserve the checkout result/policy error. The existing Redis lock also has a TTL.
                _logger.LogWarning(ex, "Could not release checkout lock for store {StoreId}.", storeId);
            }
        }

        public async Task<CartClientResponse> GetCartAsync(
            string accessToken,
            Guid storeId,
            CancellationToken cancellationToken = default)
        {
            using var request = CreateRequest(
                HttpMethod.Get,
                $"api/cart/stores/{storeId}",
                accessToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            await HttpClientErrorMapper.EnsureSuccessOrThrowAsync(
                response,
                "Could not read cart.",
                cancellationToken);

            var cart = await response.Content.ReadFromJsonAsync<CartClientResponse>(
                cancellationToken: cancellationToken);

            return cart ?? throw new NotFoundException("Cart was not found.");
        }

        public async Task DeleteCartAsync(
            string accessToken,
            Guid storeId,
            CancellationToken cancellationToken = default)
        {
            using var request = CreateRequest(
                HttpMethod.Delete,
                $"api/cart/stores/{storeId}",
                accessToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            await HttpClientErrorMapper.EnsureSuccessOrThrowAsync(
                response,
                "Could not clear cart after order creation.",
                cancellationToken);
        }

        private static HttpRequestMessage CreateRequest(
            HttpMethod method,
            string requestUri,
            string accessToken)
        {
            var request = new HttpRequestMessage(method, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                accessToken);

            return request;
        }
    }
}
