using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using StealDeal.Services.Order.Application.DTOs.Response;
using StealDeal.Services.Order.Application.Exceptions;
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
            var bag = await GetAsync<StoreBagClientResponse>($"api/store/bags/{bagId}", cancellationToken);
            if (bag != null && (bag.Id != bagId || bag.StoreId == Guid.Empty || string.IsNullOrWhiteSpace(bag.Name)))
                throw new DependencyUnavailableException("Store service returned invalid bag data.");
            return bag;
        }

        public async Task<StoreClientResponse?> GetStoreAsync(
            Guid storeId, CancellationToken cancellationToken = default)
        {
            var store = await GetAsync<StoreClientResponse>($"api/store/stores/{storeId}", cancellationToken);
            if (store != null && (store.Id != storeId || store.OwnerId == Guid.Empty || string.IsNullOrWhiteSpace(store.Name)))
                throw new DependencyUnavailableException("Store service returned invalid store data.");
            return store;
        }

        private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken) where T : class
        {
            try
            {
                using var response = await _httpClient.GetAsync(path, cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return null;
                if (!response.IsSuccessStatusCode)
                    throw new DependencyUnavailableException("Store service is unavailable. Please try again later.");

                return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
                    ?? throw new DependencyUnavailableException("Store service returned an empty response.");
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new DependencyUnavailableException("Store service timed out. Please try again later.", ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
            {
                throw new DependencyUnavailableException("Could not read data from Store service. Please try again later.", ex);
            }
        }
    }
}
