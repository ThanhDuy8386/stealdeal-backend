using StealDeal.Services.Cart.Application.DTOs.Requests;
using StealDeal.Services.Cart.Application.DTOs.Responses;

namespace StealDeal.Services.Cart.Application.Services.Interfaces
{
    public interface ICartService
    {
        Task<IReadOnlyList<CartResponse>> GetCartsAsync(
            Guid userId,
            CancellationToken cancellationToken = default);

        Task<CartResponse> GetCartAsync(
            Guid userId,
            Guid storeId,
            CancellationToken cancellationToken = default);

        Task<CartResponse> AddItemAsync(
            Guid userId,
            AddCartItemRequest request,
            CancellationToken cancellationToken = default);

        Task<CartResponse> UpdateQuantityAsync(
            Guid userId,
            Guid storeId,
            Guid bagId,
            UpdateCartItemRequest request,
            CancellationToken cancellationToken = default);

        Task<CartResponse> RemoveItemAsync(
            Guid userId,
            Guid storeId,
            Guid bagId,
            CancellationToken cancellationToken = default);

        Task ClearCartAsync(
            Guid userId,
            Guid? storeId = null,
            CancellationToken cancellationToken = default);

        Task<string> AcquireCheckoutLockAsync(
            Guid userId,
            Guid storeId,
            CancellationToken cancellationToken = default);

        Task ReleaseCheckoutLockAsync(
            Guid userId,
            Guid storeId,
            string lockToken,
            CancellationToken cancellationToken = default);
    }
}
