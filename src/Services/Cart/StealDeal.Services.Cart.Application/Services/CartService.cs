using StealDeal.Services.Cart.Application.Configuration;
using StealDeal.Services.Cart.Application.DTOs.Requests;
using StealDeal.Services.Cart.Application.DTOs.Responses;
using StealDeal.Services.Cart.Application.Exceptions;
using StealDeal.Services.Cart.Application.Mappings;
using StealDeal.Services.Cart.Application.Services.Interfaces;
using StealDeal.Services.Cart.Domain.Interfaces.Repositories;
using StealDeal.Services.Cart.Domain.Models;

namespace StealDeal.Services.Cart.Application.Services
{
    public class CartService : ICartService
    {
        private readonly ICartRepository _cartRepository;
        private readonly IStoreCatalogClient _storeCatalogClient;
        private readonly TimeSpan _cartTtl;

        public CartService(
            ICartRepository cartRepository,
            IStoreCatalogClient storeCatalogClient,
            CartSettings cartSettings)
        {
            _cartRepository = cartRepository;
            _storeCatalogClient = storeCatalogClient;

            var ttlHours = cartSettings.CartTtlHours <= 0
                ? 24
                : cartSettings.CartTtlHours;

            _cartTtl = TimeSpan.FromHours(ttlHours);
        }

        public async Task<IReadOnlyList<CartResponse>> GetCartsAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            ValidateUserId(userId);

            var carts = await _cartRepository.GetCartsAsync(userId, cancellationToken);
            return carts
                .Select(cart => cart.ToResponse())
                .ToList();
        }

        public async Task<CartResponse> GetCartAsync(
            Guid userId,
            Guid storeId,
            CancellationToken cancellationToken = default)
        {
            ValidateUserId(userId);
            ValidateStoreId(storeId);

            var cart = await _cartRepository.GetAsync(userId, storeId, cancellationToken);
            if (cart is null)
            {
                return CreateEmptyCart(userId, storeId).ToResponse();
            }

            return cart.ToResponse();
        }

        public async Task<CartResponse> AddItemAsync(
            Guid userId,
            AddCartItemRequest request,
            CancellationToken cancellationToken = default)
        {
            ValidateUserId(userId);

            if (request.BagId == Guid.Empty)
            {
                throw new BadRequestException("Bag id is required.");
            }

            if (request.Quantity <= 0)
            {
                throw new BadRequestException("Quantity must be greater than zero.");
            }

            var snapshot = await _storeCatalogClient.GetBagCartSnapshotAsync(
                request.BagId,
                cancellationToken);

            if (snapshot is null)
            {
                throw new NotFoundException($"Bag with ID {request.BagId} not found.");
            }

            ValidatePurchasable(snapshot, request.Quantity);

            var cartItem = new CartItem
            {
                BagId = snapshot.BagId,
                StoreId = snapshot.StoreId,
                BagNameSnapshot = snapshot.BagNameSnapshot,
                UnitPriceSnapshot = snapshot.UnitPriceSnapshot,
                Quantity = request.Quantity,
                ImageUrlSnapshot = snapshot.ImageUrlSnapshot,
                PickupStartUtc = snapshot.PickupStartUtc,
                PickupEndUtc = snapshot.PickupEndUtc,
                AddedAtUtc = DateTime.UtcNow
            };

            var newQuantity = await _cartRepository.AddOrIncrementItemAsync(
                userId,
                cartItem,
                snapshot.QuantityRemaining,
                _cartTtl,
                cancellationToken);

            if (!newQuantity.HasValue)
            {
                throw new BadRequestException("Requested quantity exceeds available quantity.");
            }

            var cart = await _cartRepository.GetAsync(userId, snapshot.StoreId, cancellationToken);
            return (cart ?? CreateEmptyCart(userId, snapshot.StoreId)).ToResponse();
        }

        public async Task<CartResponse> UpdateQuantityAsync(
            Guid userId,
            Guid storeId,
            Guid bagId,
            UpdateCartItemRequest request,
            CancellationToken cancellationToken = default)
        {
            ValidateUserId(userId);
            ValidateStoreId(storeId);
            ValidateBagId(bagId);

            if (request.Quantity < 0)
            {
                throw new BadRequestException("Quantity cannot be negative.");
            }

            var updated = await _cartRepository.SetItemQuantityAsync(
                userId,
                storeId,
                bagId,
                request.Quantity,
                _cartTtl,
                cancellationToken);

            if (!updated)
            {
                throw new NotFoundException($"Bag with ID {bagId} was not found in cart.");
            }

            var cart = await _cartRepository.GetAsync(userId, storeId, cancellationToken);
            return (cart ?? CreateEmptyCart(userId, storeId)).ToResponse();
        }

        public async Task<CartResponse> RemoveItemAsync(
            Guid userId,
            Guid storeId,
            Guid bagId,
            CancellationToken cancellationToken = default)
        {
            ValidateUserId(userId);
            ValidateStoreId(storeId);
            ValidateBagId(bagId);

            var removed = await _cartRepository.RemoveItemAsync(
                userId,
                storeId,
                bagId,
                _cartTtl,
                cancellationToken);

            if (!removed)
            {
                throw new NotFoundException($"Bag with ID {bagId} was not found in cart.");
            }

            var cart = await _cartRepository.GetAsync(userId, storeId, cancellationToken);
            return (cart ?? CreateEmptyCart(userId, storeId)).ToResponse();
        }

        public async Task ClearCartAsync(
            Guid userId,
            Guid? storeId = null,
            CancellationToken cancellationToken = default)
        {
            ValidateUserId(userId);

            if (storeId.HasValue)
            {
                ValidateStoreId(storeId.Value);
                await _cartRepository.DeleteAsync(userId, storeId.Value, cancellationToken);
                return;
            }

            await _cartRepository.DeleteAllAsync(userId, cancellationToken);
        }

        private static UserCart CreateEmptyCart(Guid userId, Guid storeId)
        {
            var now = DateTime.UtcNow;
            return new UserCart
            {
                UserId = userId,
                StoreId = storeId,
                Currency = "VND",
                Items = new List<CartItem>(),
                Version = 0,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                ExpiresAtUtc = now
            };
        }

        private static void ValidatePurchasable(BagCartSnapshot snapshot, int requestedQuantity)
        {
            if (!snapshot.IsPurchasable)
            {
                throw new BadRequestException("Bag is not available for cart.");
            }

            if (snapshot.QuantityRemaining <= 0)
            {
                throw new BadRequestException("Bag is sold out.");
            }

            if (requestedQuantity > snapshot.QuantityRemaining)
            {
                throw new BadRequestException("Requested quantity exceeds available quantity.");
            }

            if (snapshot.PickupEndUtc <= DateTime.UtcNow)
            {
                throw new BadRequestException("Bag pickup window has already ended.");
            }
        }

        private static void ValidateUserId(Guid userId)
        {
            if (userId == Guid.Empty)
            {
                throw new UnauthorizedException("User id is missing.");
            }
        }

        private static void ValidateStoreId(Guid storeId)
        {
            if (storeId == Guid.Empty)
            {
                throw new BadRequestException("Store id is required.");
            }
        }

        private static void ValidateBagId(Guid bagId)
        {
            if (bagId == Guid.Empty)
            {
                throw new BadRequestException("Bag id is required.");
            }
        }
    }
}
