using StealDeal.Services.Order.Application.DTOs.Response;

namespace StealDeal.Services.Order.Application.Services.Interfaces
{
    public interface ICartClient
    {
        Task<string> AcquireCheckoutLockAsync(
            string accessToken,
            Guid storeId,
            CancellationToken cancellationToken = default);

        Task ReleaseCheckoutLockAsync(
            string accessToken,
            Guid storeId,
            string lockToken,
            CancellationToken cancellationToken = default);

        Task<CartClientResponse> GetCartAsync(
            string accessToken,
            Guid storeId,
            CancellationToken cancellationToken = default);

        Task DeleteCartAsync(
            string accessToken,
            Guid storeId,
            CancellationToken cancellationToken = default);
    }
}
