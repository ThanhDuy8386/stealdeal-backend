using StealDeal.Services.Order.Application.DTOs.Requests;
using StealDeal.Services.Order.Application.DTOs.Response;
using StealDeal.Services.Order.Application.Exceptions;
using StealDeal.Services.Order.Application.Services.Interfaces;

namespace StealDeal.Services.Order.Application.Services
{
    public class OrderCheckoutService : IOrderCheckoutService
    {
        private readonly ICartClient _cartClient;
        private readonly IOrderService _orderService;

        public OrderCheckoutService(
            ICartClient cartClient,
            IOrderService orderService)
        {
            _cartClient = cartClient;
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

                var createOrderRequest = BuildCreateOrderRequest(request, cart);

                var order = await _orderService.CreateOrderAsync(userId, accessToken, createOrderRequest, cancellationToken);

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

        private static CreateOrderRequest BuildCreateOrderRequest(
            CheckoutFromCartRequest request, CartClientResponse cart)
        {
            return new CreateOrderRequest
            {
                StoreId = request.StoreId,
                StoreNameSnapshot = string.Empty,
                ContactNameSnapshot = request.ContactNameSnapshot,
                ContactPhoneSnapshot = request.ContactPhoneSnapshot,
                DeliveryFee = 0,
                VoucherDiscount = 0,
                DeliveryType = request.DeliveryType,
                DeliveryAddress = request.DeliveryAddress ?? string.Empty,
                Items = cart.Items.Select(item => new CreateOrderItemRequest
                {
                    BagId = item.BagId,
                    BagNameSnapshot = item.BagNameSnapshot,
                    UnitPriceSnapshot = item.UnitPriceSnapshot,
                    Quantity = item.Quantity
                }).ToList()
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
    }
}
