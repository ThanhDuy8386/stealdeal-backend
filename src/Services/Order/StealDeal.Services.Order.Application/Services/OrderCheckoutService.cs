using StealDeal.Services.Order.Application.DTOs.Requests;
using StealDeal.Services.Order.Application.DTOs.Response;
using StealDeal.Services.Order.Application.Exceptions;
using StealDeal.Services.Order.Application.Services.Interfaces;

namespace StealDeal.Services.Order.Application.Services
{
    public class OrderCheckoutService : IOrderCheckoutService
    {
        private readonly ICartClient _cartClient;
        private readonly IStoreCatalogClient _storeCatalogClient;
        private readonly IOrderService _orderService;

        public OrderCheckoutService(
            ICartClient cartClient,
            IStoreCatalogClient storeCatalogClient,
            IOrderService orderService)
        {
            _cartClient = cartClient;
            _storeCatalogClient = storeCatalogClient;
            _orderService = orderService;
        }

        public async Task<OrderResponse> CheckoutFromCartAsync(
            Guid userId,
            string accessToken,
            CheckoutFromCartRequest request,
            CancellationToken cancellationToken = default)
        {
            ValidateRequest(userId, accessToken, request);

            string? lockToken = null;
            try
            {
                lockToken = await _cartClient.AcquireCheckoutLockAsync(
                    accessToken,
                    request.StoreId,
                    cancellationToken);

                var cart = await _cartClient.GetCartAsync(
                    accessToken,
                    request.StoreId,
                    cancellationToken);

                ValidateCart(userId, request.StoreId, cart);

                var createOrderRequest = await BuildCreateOrderRequestAsync(
                    request,
                    cart,
                    cancellationToken);

                var order = await _orderService.CreateOrderAsync(userId, createOrderRequest);

                await _cartClient.DeleteCartAsync(
                    accessToken,
                    request.StoreId,
                    cancellationToken);

                return order;
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(lockToken))
                {
                    await _cartClient.ReleaseCheckoutLockAsync(
                        accessToken,
                        request.StoreId,
                        lockToken,
                        CancellationToken.None);
                }
            }
        }

        private async Task<CreateOrderRequest> BuildCreateOrderRequestAsync(
            CheckoutFromCartRequest request,
            CartClientResponse cart,
            CancellationToken cancellationToken)
        {
            var orderItems = new List<CreateOrderItemRequest>();
            string? storeNameSnapshot = null;

            foreach (var cartItem in cart.Items)
            {
                var bag = await _storeCatalogClient.GetBagAsync(
                    cartItem.BagId,
                    cancellationToken);

                if (bag is null)
                {
                    throw new NotFoundException($"Bag with ID {cartItem.BagId} not found.");
                }

                ValidateBagForCheckout(bag, request.StoreId, cartItem.Quantity);

                storeNameSnapshot ??= bag.StoreName;

                orderItems.Add(new CreateOrderItemRequest
                {
                    BagId = bag.Id,
                    BagNameSnapshot = bag.Name,
                    UnitPriceSnapshot = bag.SalePrice,
                    Quantity = cartItem.Quantity
                });
            }

            return new CreateOrderRequest
            {
                StoreId = request.StoreId,
                StoreNameSnapshot = storeNameSnapshot ?? string.Empty,
                ContactNameSnapshot = request.ContactNameSnapshot,
                ContactPhoneSnapshot = request.ContactPhoneSnapshot,
                DeliveryFee = 0,
                VoucherDiscount = 0,
                DeliveryType = request.DeliveryType,
                DeliveryAddress = request.DeliveryAddress ?? string.Empty,
                Items = orderItems
            };
        }

        private static void ValidateRequest(
            Guid userId,
            string accessToken,
            CheckoutFromCartRequest request)
        {
            if (userId == Guid.Empty)
            {
                throw new UnauthorizedException("User is not authenticated.");
            }

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new UnauthorizedException("Access token is required.");
            }

            if (request.StoreId == Guid.Empty)
            {
                throw new BadRequestException("Store id is required.");
            }

            if (string.IsNullOrWhiteSpace(request.ContactNameSnapshot))
            {
                throw new BadRequestException("Contact name is required.");
            }

            if (string.IsNullOrWhiteSpace(request.ContactPhoneSnapshot))
            {
                throw new BadRequestException("Contact phone is required.");
            }

            if (string.IsNullOrWhiteSpace(request.DeliveryType))
            {
                throw new BadRequestException("Delivery type is required.");
            }
        }

        private static void ValidateCart(Guid userId, Guid storeId, CartClientResponse cart)
        {
            if (cart.UserId != userId)
            {
                throw new ForbiddenException("Cart does not belong to the current user.");
            }

            if (cart.StoreId != storeId)
            {
                throw new BadRequestException("Cart store does not match checkout store.");
            }

            if (cart.Items.Count == 0)
            {
                throw new BadRequestException("Cart is empty.");
            }

            if (cart.Items.Any(item => item.StoreId != storeId))
            {
                throw new BadRequestException("Cart contains items from a different store.");
            }

            if (cart.Items.Any(item => item.Quantity <= 0))
            {
                throw new BadRequestException("Cart contains invalid item quantity.");
            }
        }

        private static void ValidateBagForCheckout(
            StoreBagClientResponse bag,
            Guid storeId,
            int requestedQuantity)
        {
            if (bag.StoreId != storeId)
            {
                throw new BadRequestException($"Bag with ID {bag.Id} does not belong to the selected store.");
            }

            if (requestedQuantity <= 0)
            {
                throw new BadRequestException("Quantity must be greater than zero.");
            }

            if (bag.QuantityRemaining < requestedQuantity)
            {
                throw new BadRequestException($"Bag '{bag.Name}' does not have enough stock.");
            }

            var pickupEndUtc = ToUtc(bag.PickupEndTime);
            var expiryUtc = ToUtc(bag.ExpiryDate);
            var now = DateTime.UtcNow;

            if (pickupEndUtc <= now || expiryUtc <= now)
            {
                throw new BadRequestException($"Bag '{bag.Name}' is no longer available for checkout.");
            }

            if (IsKnownUnavailableStatus(bag.Status))
            {
                throw new BadRequestException($"Bag '{bag.Name}' is not available for checkout.");
            }
        }

        private static bool IsKnownUnavailableStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return false;
            }

            return status.Trim().ToLowerInvariant() switch
            {
                "inactive" => true,
                "deleted" => true,
                "soldout" => true,
                "sold_out" => true,
                "sold out" => true,
                "expired" => true,
                "cancelled" => true,
                "canceled" => true,
                "draft" => true,
                _ => false
            };
        }

        private static DateTime ToUtc(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
        }
    }
}
