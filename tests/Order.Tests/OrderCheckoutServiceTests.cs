using StealDeal.Services.Order.Application.DTOs.Requests;
using StealDeal.Services.Order.Application.DTOs.Response;
using StealDeal.Services.Order.Application.Services;
using StealDeal.Services.Order.Application.Services.Interfaces;
using StealDeal.Services.Order.Application.Exceptions;

namespace Order.Tests;

public class OrderCheckoutServiceTests
{
    [Fact]
    public async Task CheckoutFromCartAsync_ValidCart_CreatesOrderAndReleasesLock()
    {
        var userId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var bagId = Guid.NewGuid();
        var calls = new List<string>();

        var cart = new CartClientResponse
        {
            UserId = userId,
            StoreId = storeId,
            Items =
            [
                new CartItemClientResponse
                {
                    BagId = bagId,
                    StoreId = storeId,
                    BagNameSnapshot = "Old bag name",
                    UnitPriceSnapshot = 99,
                    Quantity = 2
                }
            ]
        };
        var bag = new StoreBagClientResponse
        {
            Id = bagId,
            StoreId = storeId,
            StoreName = "Sample Store",
            Name = "Fresh Bag",
            SalePrice = 69,
            QuantityRemaining = 3,
            Status = "Active",
            PickupEndTime = DateTime.UtcNow.AddHours(3),
            ExpiryDate = DateTime.UtcNow.AddDays(1)
        };
        var expectedOrder = new OrderResponse { Id = Guid.NewGuid() };

        var cartClient = new CartClientFake(cart, calls);
        var catalogClient = new StoreCatalogClientFake(bag, calls);
        var orderService = new OrderServiceFake(expectedOrder, calls);
        var checkout = new OrderCheckoutService(
            cartClient, catalogClient, orderService);

        var result = await checkout.CheckoutFromCartAsync(
            userId,
            "access-token",
            new CheckoutFromCartRequest
            {
                StoreId = storeId,
                ContactNameSnapshot = "Test Buyer",
                ContactPhoneSnapshot = "0123456789",
                DeliveryType = "Pickup"
            });

        Assert.Same(expectedOrder, result);
        Assert.Equal(
            "lock,cart,bag,order,delete,release",
            string.Join(",", calls));
        Assert.Equal("test-lock", cartClient.ReleasedToken);
        Assert.Equal(bagId, catalogClient.RequestedBagId);
        Assert.Equal(userId, orderService.RequestedUserId);

        var created = Assert.IsType<CreateOrderRequest>(
            orderService.CreatedRequest);
        Assert.Equal(storeId, created.StoreId);
        Assert.Equal("Sample Store", created.StoreNameSnapshot);
        Assert.Equal("Test Buyer", created.ContactNameSnapshot);
        Assert.Equal("0123456789", created.ContactPhoneSnapshot);

        var item = Assert.Single(created.Items);
        Assert.Equal(bagId, item.BagId);
        Assert.Equal(2, item.Quantity);
        Assert.Equal("Fresh Bag", item.BagNameSnapshot);
        Assert.Equal(69m, item.UnitPriceSnapshot);
    }

    [Fact]
    public async Task CheckoutFromCartAsync_OutOfStock_KeepsCartAndReleasesLock()
    {
        var userId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var bagId = Guid.NewGuid();
        var calls = new List<string>();

        var cart = new CartClientResponse
        {
            UserId = userId,
            StoreId = storeId,
            Items =
            [
                new CartItemClientResponse
            {
                BagId = bagId,
                StoreId = storeId,
                BagNameSnapshot = "Test Bag",
                Quantity = 1
            }
            ]
        };
        var bag = new StoreBagClientResponse
        {
            Id = bagId,
            StoreId = storeId,
            StoreName = "Sample Store",
            Name = "Test Bag",
            SalePrice = 69,
            QuantityRemaining = 0,
            Status = "Active",
            PickupEndTime = DateTime.UtcNow.AddHours(3),
            ExpiryDate = DateTime.UtcNow.AddDays(1)
        };

        var cartClient = new CartClientFake(cart, calls);
        var catalogClient = new StoreCatalogClientFake(bag, calls);
        var orderService = new OrderServiceFake(new OrderResponse(), calls);
        var checkout = new OrderCheckoutService(
            cartClient, catalogClient, orderService);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            checkout.CheckoutFromCartAsync(
                userId,
                "access-token",
                new CheckoutFromCartRequest
                {
                    StoreId = storeId,
                    ContactNameSnapshot = "Test Buyer",
                    ContactPhoneSnapshot = "0123456789",
                    DeliveryType = "Pickup"
                }));

        Assert.Equal("lock,cart,bag,release", string.Join(",", calls));
        Assert.Equal("test-lock", cartClient.ReleasedToken);
        Assert.Null(orderService.CreatedRequest);
    }

    private sealed class CartClientFake(
        CartClientResponse cart, List<string> calls) : ICartClient
    {
        public string? ReleasedToken { get; private set; }

        public Task<string> AcquireCheckoutLockAsync(
            string token, Guid storeId, CancellationToken ct = default)
        {
            calls.Add("lock");
            return Task.FromResult("test-lock");
        }

        public Task<CartClientResponse> GetCartAsync(
            string token, Guid storeId, CancellationToken ct = default)
        {
            calls.Add("cart");
            return Task.FromResult(cart);
        }

        public Task DeleteCartAsync(
            string token, Guid storeId, CancellationToken ct = default)
        {
            calls.Add("delete");
            return Task.CompletedTask;
        }

        public Task ReleaseCheckoutLockAsync(
            string token, Guid storeId, string lockToken,
            CancellationToken ct = default)
        {
            calls.Add("release");
            ReleasedToken = lockToken;
            return Task.CompletedTask;
        }
    }

    private sealed class StoreCatalogClientFake(
        StoreBagClientResponse bag, List<string> calls) : IStoreCatalogClient
    {
        public Guid? RequestedBagId { get; private set; }

        public Task<StoreBagClientResponse?> GetBagAsync(
            Guid bagId, CancellationToken ct = default)
        {
            calls.Add("bag");
            RequestedBagId = bagId;
            return Task.FromResult<StoreBagClientResponse?>(bag);
        }
    }

    private sealed class OrderServiceFake(
        OrderResponse response, List<string> calls) : IOrderService
    {
        public Guid? RequestedUserId { get; private set; }
        public CreateOrderRequest? CreatedRequest { get; private set; }

        public Task<OrderResponse> CreateOrderAsync(
            Guid userId, CreateOrderRequest request)
        {
            calls.Add("order");
            RequestedUserId = userId;
            CreatedRequest = request;
            return Task.FromResult(response);
        }

        public Task<OrderResponse> GetOrderByIdAsync(
            Guid orderId, Guid userId, IEnumerable<string> roles)
            => throw new NotSupportedException();

        public Task<IEnumerable<OrderResponse>> GetMyOrdersAsync(Guid userId)
            => throw new NotSupportedException();

        public Task<IEnumerable<OrderResponse>> GetStoreOrdersAsync(
            Guid storeId, Guid ownerId)
            => throw new NotSupportedException();

        public Task<OrderResponse> UpdateOrderStatusAsync(
            Guid orderId, Guid userId, IEnumerable<string> roles,
            UpdateOrderStatusRequest request)
            => throw new NotSupportedException();

        public Task<OrderReviewEligibilityResponse> CheckReviewEligibilityAsync(
            Guid orderId, Guid buyerId, Guid bagId)
            => throw new NotSupportedException();
    }
}