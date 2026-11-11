using System.Text.Json;
using StealDeal.Services.Order.Application.DTOs.Events;
using StealDeal.Services.Order.Application.DTOs.Requests;
using StealDeal.Services.Order.Application.Services;
using StealDeal.Services.Order.Domain.Interfaces;
using StealDeal.Services.Order.Domain.Models;

namespace Order.Tests;

public class OrderServiceTests
{
    [Fact]
    public async Task CreateOrderAsync_ValidRequest_QueuesEventAndSavesOnce()
    {
        var buyerId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var bagId = Guid.NewGuid();

        var orders = new OrderRepositoryFake();
        var outbox = new OutboxRepositoryFake();
        var unitOfWork = new UnitOfWorkFake(orders, outbox);
        var service = new OrderService(orders, unitOfWork, outbox);

        var result = await service.CreateOrderAsync(
            buyerId,
            new CreateOrderRequest
            {
                StoreId = storeId,
                StoreNameSnapshot = "Sample Store",
                ContactNameSnapshot = "Test Buyer",
                ContactPhoneSnapshot = "0123456789",
                DeliveryType = "Pickup",
                DeliveryAddress = "",
                Items =
                [
                    new CreateOrderItemRequest
                    {
                        BagId = bagId,
                        BagNameSnapshot = "Fresh Bag",
                        UnitPriceSnapshot = 69,
                        Quantity = 2
                    }
                ]
            });

        var order = Assert.IsType<OrderProfile>(orders.Added);
        Assert.Equal(order.Id, result.Id);
        Assert.Equal(buyerId, order.UserId);
        Assert.Equal(storeId, order.StoreId);
        Assert.Equal("Pending", order.Status);
        Assert.Equal(138m, order.TotalAmount);

        var message = Assert.IsType<OutboxMessage>(outbox.Added);
        Assert.Equal("order.created", message.RoutingKey);
        Assert.Equal("OrderCreatedEvent", message.EventType);
        Assert.Equal("Pending", message.Status);

        var payload = JsonSerializer.Deserialize<CreateOrderEvent>(message.Payload);
        Assert.NotNull(payload);
        Assert.Equal(order.Id, payload!.OrderId);
        Assert.Equal(buyerId, payload.UserId);
        Assert.Equal(storeId, payload.StoreId);
        Assert.Equal(138m, payload.TotalAmount);

        var item = Assert.Single(payload.Items);
        Assert.Equal(bagId, item.SurpriseBagId);
        Assert.Equal(2, item.Quantity);

        Assert.Equal(1, unitOfWork.SaveCalls);
        Assert.True(unitOfWork.HadOrderAndMessageAtSave);
    }

    private sealed class OrderRepositoryFake : IOrderRepository
    {
        public OrderProfile? Added { get; private set; }

        public Task AddAsync(OrderProfile order)
        {
            Added = order;
            return Task.CompletedTask;
        }

        public Task<OrderProfile?> GetByIdAsync(Guid id)
            => throw new NotSupportedException();

        public Task<IEnumerable<OrderProfile>> GetByUserIdAsync(Guid userId)
            => throw new NotSupportedException();

        public Task<IEnumerable<OrderProfile>> GetByStoreIdAsync(Guid storeId)
            => throw new NotSupportedException();

        public void Update(OrderProfile order)
            => throw new NotSupportedException();
    }

    private sealed class OutboxRepositoryFake : IOutboxMessageRepository
    {
        public OutboxMessage? Added { get; private set; }

        public Task AddAsync(OutboxMessage message)
        {
            Added = message;
            return Task.CompletedTask;
        }

        public Task<List<OutboxMessage>> GetPendingBatchAsync(int batchSize)
            => throw new NotSupportedException();

        public void Update(OutboxMessage message)
            => throw new NotSupportedException();
    }

    private sealed class UnitOfWorkFake(
        OrderRepositoryFake orders,
        OutboxRepositoryFake outbox) : IUnitOfWork
    {
        public int SaveCalls { get; private set; }
        public bool HadOrderAndMessageAtSave { get; private set; }

        public Task<int> SaveChangesAsync()
        {
            SaveCalls++;
            HadOrderAndMessageAtSave =
                orders.Added is not null && outbox.Added is not null;
            return Task.FromResult(2);
        }
    }
}