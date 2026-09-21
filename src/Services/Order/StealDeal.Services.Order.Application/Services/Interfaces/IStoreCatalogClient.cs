using StealDeal.Services.Order.Application.DTOs.Response;

namespace StealDeal.Services.Order.Application.Services.Interfaces
{
    public interface IStoreCatalogClient
    {
        Task<StoreBagClientResponse?> GetBagAsync(
            Guid bagId,
            CancellationToken cancellationToken = default);
    }
}
