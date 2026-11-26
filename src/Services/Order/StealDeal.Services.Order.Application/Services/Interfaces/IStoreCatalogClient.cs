using StealDeal.Services.Order.Application.DTOs.Response;

namespace StealDeal.Services.Order.Application.Services.Interfaces
{
    public interface IStoreCatalogClient
    {
        Task<StoreClientResponse?> GetStoreAsync(
            Guid storeId,
            CancellationToken cancellationToken = default);

        Task<StoreBagClientResponse?> GetBagAsync(
            Guid bagId,
            CancellationToken cancellationToken = default);
    }
}
