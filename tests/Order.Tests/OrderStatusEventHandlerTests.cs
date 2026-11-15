using StealDeal.Services.Order.Application.DTOs.Events;
using StealDeal.Services.Order.Application.EventHandlers;
using StealDeal.Services.Order.Application.Messaging;
using StealDeal.Services.Order.Domain.Interfaces;
using StealDeal.Services.Order.Domain.Models;

namespace Order.Tests;

public class OrderStatusEventHandlerTests
{
    [Fact]
    public async Task PaymentCompleted_ChangesPendingOrderToConfirmed()
    {
        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var order = new OrderProfile
        {
            Id = orderId,
            UserId = Guid.NewGuid(),
            StoreId = Guid.NewGuid(),
            Status = "Pending"
        };

        var orders = new OrderRepositoryFake(order);
        var processed = new ProcessedMessageRepositoryFake();
        var unitOfWork = new UnitOfWorkFake();
        var handler = new OrderStatusEventHandler(
            orders, processed, unitOfWork);

        await handler.HandleAsync(
            new PaymentCompletedEvent
            {
                MessageId = messageId,
                OrderId = orderId,
                PaymentId = Guid.NewGuid(),
                Amount = 138,
                PaymentMethod = "VNPAY"
            },
            new IntegrationEventContext
            {
                MessageId = messageId,
                ConsumerName = "order-payment-status-test",
                EventType = "payment.completed",
                RoutingKey = "payment.completed"
            });

        Assert.Equal("Confirmed", order.Status);
        Assert.Equal(1, orders.UpdateCalls);
        Assert.Equal(messageId, processed.Added?.MessageId);
        Assert.Equal(orderId, processed.Added?.AggregateId);
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task PaymentFailed_ChangesPendingOrderToPaymentFailed()
    {
        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var order = new OrderProfile
        {
            Id = orderId,
            UserId = Guid.NewGuid(),
            StoreId = Guid.NewGuid(),
            Status = "Pending"
        };

        var orders = new OrderRepositoryFake(order);
        var processed = new ProcessedMessageRepositoryFake();
        var unitOfWork = new UnitOfWorkFake();
        var handler = new OrderStatusEventHandler(
            orders, processed, unitOfWork);

        await handler.HandleAsync(
            new PaymentFailedEvent
            {
                MessageId = messageId,
                OrderId = orderId,
                PaymentId = Guid.NewGuid(),
                ReasonCode = "Declined",
                Reason = "Payment declined"
            },
            new IntegrationEventContext
            {
                MessageId = messageId,
                ConsumerName = "order-payment-status-test",
                EventType = "payment.failed",
                RoutingKey = "payment.failed"
            });

        Assert.Equal("PaymentFailed", order.Status);
        Assert.Equal(1, orders.UpdateCalls);
        Assert.Equal(messageId, processed.Added?.MessageId);
        Assert.Equal("payment.failed", processed.Added?.EventType);
        Assert.Equal(orderId, processed.Added?.AggregateId);
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task LatePaymentCompleted_DoesNotConfirmFailedOrder()
    {
        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var order = new OrderProfile
        {
            Id = orderId,
            UserId = Guid.NewGuid(),
            StoreId = Guid.NewGuid(),
            Status = "PaymentFailed"
        };

        var orders = new OrderRepositoryFake(order);
        var processed = new ProcessedMessageRepositoryFake();
        var unitOfWork = new UnitOfWorkFake();
        var handler = new OrderStatusEventHandler(
            orders, processed, unitOfWork);

        await handler.HandleAsync(
            new PaymentCompletedEvent
            {
                MessageId = messageId,
                OrderId = orderId,
                PaymentId = Guid.NewGuid(),
                Amount = 138,
                PaymentMethod = "VNPAY"
            },
            new IntegrationEventContext
            {
                MessageId = messageId,
                ConsumerName = "order-payment-status-test",
                EventType = "payment.completed",
                RoutingKey = "payment.completed"
            });

        Assert.Equal("PaymentFailed", order.Status);
        Assert.Equal(0, orders.UpdateCalls);
        Assert.Equal(messageId, processed.Added?.MessageId);
        Assert.Equal("payment.completed", processed.Added?.EventType);
        Assert.Equal(orderId, processed.Added?.AggregateId);
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    private sealed class OrderRepositoryFake(OrderProfile order)
        : IOrderRepository
    {
        public int UpdateCalls { get; private set; }

        public Task<OrderProfile?> GetByIdAsync(Guid id)
            => Task.FromResult<OrderProfile?>(id == order.Id ? order : null);

        public void Update(OrderProfile updated)
        {
            Assert.Same(order, updated);
            UpdateCalls++;
        }

        public Task AddAsync(OrderProfile value)
            => throw new NotSupportedException();

        public Task<IEnumerable<OrderProfile>> GetByUserIdAsync(Guid userId)
            => throw new NotSupportedException();

        public Task<IEnumerable<OrderProfile>> GetByStoreIdAsync(Guid storeId)
            => throw new NotSupportedException();
    }

    private sealed class ProcessedMessageRepositoryFake
        : IProcessedMessageRepository
    {
        public ProcessedMessage? Added { get; private set; }

        public Task<bool> ExistsAsync(Guid messageId, string consumerName)
            => Task.FromResult(false);

        public Task AddAsync(ProcessedMessage message)
        {
            Added = message;
            return Task.CompletedTask;
        }

        public Task<ProcessedMessage?> GetAsync(
            Guid messageId, string consumerName)
            => throw new NotSupportedException();
    }

    private sealed class UnitOfWorkFake : IUnitOfWork
    {
        public int SaveCalls { get; private set; }

        public Task<int> SaveChangesAsync()
        {
            SaveCalls++;
            return Task.FromResult(1);
        }
    }
}
