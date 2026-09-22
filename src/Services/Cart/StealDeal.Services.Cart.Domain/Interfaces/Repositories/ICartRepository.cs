using StealDeal.Services.Cart.Domain.Models;

namespace StealDeal.Services.Cart.Domain.Interfaces.Repositories
{
    public interface ICartRepository
    {
        Task<IReadOnlyList<UserCart>> GetCartsAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<UserCart?> GetAsync(Guid userId, Guid storeId, CancellationToken cancellationToken = default);
        Task SetAsync(UserCart cart, TimeSpan ttl, CancellationToken cancellationToken = default);
        Task DeleteAsync(Guid userId, Guid storeId, CancellationToken cancellationToken = default);
        Task DeleteAllAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<long?> AddOrIncrementItemAsync(Guid userId, CartItem item, int maxQuantity, TimeSpan ttl, CancellationToken cancellationToken = default);
        Task<bool> SetItemQuantityAsync(Guid userId, Guid storeId, Guid bagId, int quantity, TimeSpan ttl, CancellationToken cancellationToken = default);
        Task<bool> RemoveItemAsync(Guid userId, Guid storeId, Guid bagId, TimeSpan ttl, CancellationToken cancellationToken = default);
        Task<string?> AcquireCheckoutLockAsync(Guid userId, Guid storeId, TimeSpan ttl, CancellationToken cancellationToken = default);
        Task ReleaseCheckoutLockAsync(Guid userId, Guid storeId, string lockToken, CancellationToken cancellationToken = default);
    }
}
