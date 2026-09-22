using System.Net;
using System.Net.Http.Json;
using StealDeal.Services.Order.Application.DTOs.Response;
using StealDeal.Services.Order.Application.Services.Interfaces;

namespace StealDeal.Services.Order.Infrastructure.Clients
{
    public class StoreCatalogHttpClient : IStoreCatalogClient
    {
        private readonly HttpClient _httpClient;

        public StoreCatalogHttpClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<StoreBagClientResponse?> GetBagAsync(
            Guid bagId,
            CancellationToken cancellationToken = default)
        {
            using var response = await _httpClient.GetAsync(
                $"api/bags/{bagId}",
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            await HttpClientErrorMapper.EnsureSuccessOrThrowAsync(
                response,
                "Could not read bag from Store service.",
                cancellationToken);

            return await response.Content.ReadFromJsonAsync<StoreBagClientResponse>(
                cancellationToken: cancellationToken);
        }
    }
}
