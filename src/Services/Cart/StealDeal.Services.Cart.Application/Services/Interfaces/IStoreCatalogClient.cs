using StealDeal.Services.Cart.Application.DTOs.Responses;

namespace StealDeal.Services.Cart.Application.Services.Interfaces
{
    public interface IStoreCatalogClient
    {
        Task<BagCartSnapshot?> GetBagCartSnapshotAsync(
            Guid bagId,
            CancellationToken cancellationToken = default);
    }
}
