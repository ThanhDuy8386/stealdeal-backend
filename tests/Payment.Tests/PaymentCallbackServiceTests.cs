using System.Text.Json;
using StealDeal.Services.Payment.Application.DTOs.Events;
using StealDeal.Services.Payment.Application.DTOs.Gateways;
using StealDeal.Services.Payment.Application.Gateways;
using StealDeal.Services.Payment.Application.Services;
using StealDeal.Services.Payment.Domain.Constants;
using StealDeal.Services.Payment.Domain.Interfaces;
using StealDeal.Services.Payment.Domain.Models;

namespace Payment.Tests;

public class PaymentCallbackServiceTests
{
    [Fact]
    public async Task ValidSuccess_ConfirmsPaymentAndQueuesEvent()
    {
        var paidAt = DateTime.UtcNow;
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            StoreId = Guid.NewGuid(),
            Amount = 138,
            PaymentMethod = PaymentMethods.VnPay,
            GatewayRef = "test-ref",
            Status = TransactionStatuses.Pending
        };

        var gateway = new GatewayFake(new PaymentCallbackResult
        {
            IsValidSignature = true,
            IsSuccess = true,
            GatewayRef = "test-ref",
            Amount = 138,
            GatewayTransactionNo = "payment-123",
            PaidAtUtc = paidAt
        });
        var factory = new GatewayFactoryFake(gateway);
        var transactions = new TransactionRepositoryFake(transaction);
        var refunds = new RefundRepositoryFake();
        var outbox = new OutboxRepositoryFake();
        var unitOfWork = new UnitOfWorkFake();
        var service = new PaymentCallbackService(
            factory, transactions, refunds, outbox, unitOfWork);

        var result = await service.HandleVnPayIpnAsync(
            new Dictionary<string, string>());

        Assert.Equal("00", result.RspCode);
        Assert.Equal(PaymentMethods.VnPay, factory.RequestedMethod);
        Assert.Equal(1, gateway.VerifyCalls);
        Assert.Equal(TransactionStatuses.Success, transaction.Status);
        Assert.Equal(paidAt, transaction.PaidAt);
        Assert.Equal("payment-123", transaction.GatewayTransactionNo);
        Assert.Equal(1, transactions.UpdateCalls);
        Assert.Equal(1, unitOfWork.SaveCalls);
        Assert.Equal(0, refunds.AddCalls);

        var message = Assert.Single(outbox.Added);
        Assert.Equal(PaymentEventTypes.PaymentCompleted, message.EventType);
        Assert.Equal(PaymentEventTypes.PaymentCompleted, message.RoutingKey);

        var completed = JsonSerializer.Deserialize<PaymentCompletedEvent>(
            message.Payload,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(completed);
        Assert.Equal(transaction.OrderId, completed!.OrderId);
        Assert.Equal(transaction.Id, completed.PaymentId);
        Assert.Equal(138m, completed.Amount);
    }

    [Fact]
    public async Task FailedPayment_QueuesInventoryRelease()
    {
        var orderId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var bagId = Guid.NewGuid();

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            UserId = Guid.NewGuid(),
            StoreId = storeId,
            Amount = 138,
            PaymentMethod = PaymentMethods.VnPay,
            GatewayRef = "test-ref",
            Status = TransactionStatuses.Pending,
            ReservedItemsJson = JsonSerializer.Serialize(
                new List<InventoryReservedItemDto>
                {
                new() { SurpriseBagId = bagId, Quantity = 2 }
                })
        };

        var gateway = new GatewayFake(new PaymentCallbackResult
        {
            IsValidSignature = true,
            IsSuccess = false,
            GatewayRef = "test-ref",
            Amount = 138,
            ReasonCode = "Declined",
            Reason = "Payment declined"
        });
        var factory = new GatewayFactoryFake(gateway);
        var transactions = new TransactionRepositoryFake(transaction);
        var refunds = new RefundRepositoryFake();
        var outbox = new OutboxRepositoryFake();
        var unitOfWork = new UnitOfWorkFake();
        var service = new PaymentCallbackService(
            factory, transactions, refunds, outbox, unitOfWork);

        var result = await service.HandleVnPayIpnAsync(
            new Dictionary<string, string>());

        Assert.Equal("00", result.RspCode);
        Assert.Equal(TransactionStatuses.Failed, transaction.Status);
        Assert.Equal("Payment declined", transaction.FailureReason);
        Assert.Equal(1, transactions.UpdateCalls);
        Assert.Equal(1, unitOfWork.SaveCalls);
        Assert.Equal(0, refunds.AddCalls);
        Assert.Equal(2, outbox.Added.Count);
        Assert.DoesNotContain(outbox.Added,
            m => m.EventType == PaymentEventTypes.PaymentCompleted);

        var failedMessage = Assert.Single(outbox.Added,
            m => m.EventType == PaymentEventTypes.PaymentFailed);
        var failed = JsonSerializer.Deserialize<PaymentFailedEvent>(
            failedMessage.Payload,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(failed);
        Assert.Equal(orderId, failed!.OrderId);
        Assert.Equal(transaction.Id, failed.PaymentId);
        Assert.Equal("Declined", failed.ReasonCode);

        var releaseMessage = Assert.Single(outbox.Added,
            m => m.EventType == PaymentEventTypes.InventoryReleaseRequested);
        var release = JsonSerializer.Deserialize<InventoryReleaseRequestedEvent>(
            releaseMessage.Payload,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(release);
        Assert.Equal(orderId, release!.OrderId);
        Assert.Equal(storeId, release.StoreId);
        Assert.Equal("PaymentFailed", release.ReasonCode);

        var item = Assert.Single(release.Items);
        Assert.Equal(bagId, item.SurpriseBagId);
        Assert.Equal(2, item.Quantity);
    }

    private sealed class GatewayFake(PaymentCallbackResult callback)
        : IPaymentGateway
    {
        public string Method => PaymentMethods.VnPay;
        public int VerifyCalls { get; private set; }

        public Task<PaymentCallbackResult> VerifyIpnAsync(
            IReadOnlyDictionary<string, string> parameters,
            CancellationToken cancellationToken = default)
        {
            VerifyCalls++;
            return Task.FromResult(callback);
        }

        public Task<CreatePaymentResult> CreatePaymentAsync(
            CreatePaymentRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class GatewayFactoryFake(IPaymentGateway gateway)
        : IPaymentGatewayFactory
    {
        public string? RequestedMethod { get; private set; }

        public IPaymentGateway GetGateway(string paymentMethod)
        {
            RequestedMethod = paymentMethod;
            return gateway;
        }
    }

    private sealed class TransactionRepositoryFake(Transaction transaction)
        : ITransactionRepository
    {
        public int UpdateCalls { get; private set; }

        public Task<Transaction?> GetByGatewayRefAsync(string gatewayRef)
            => Task.FromResult<Transaction?>(
                gatewayRef == transaction.GatewayRef ? transaction : null);

        public void Update(Transaction updated)
        {
            Assert.Same(transaction, updated);
            UpdateCalls++;
        }

        public Task AddAsync(Transaction value)
            => throw new NotSupportedException();
        public Task<Transaction?> GetByIdAsync(Guid id)
            => throw new NotSupportedException();
        public Task<Transaction?> GetByOrderIdAsync(Guid orderId)
            => throw new NotSupportedException();
        public Task<IEnumerable<Transaction>> GetByUserIdAsync(Guid userId)
            => throw new NotSupportedException();
        public Task<List<Transaction>> GetExpiredPendingBatchAsync(
            DateTime nowUtc, int batchSize,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class OutboxRepositoryFake : IOutboxMessageRepository
    {
        public List<OutboxMessage> Added { get; } = new();

        public Task AddAsync(OutboxMessage message)
        {
            Added.Add(message);
            return Task.CompletedTask;
        }

        public Task<List<OutboxMessage>> GetPendingBatchAsync(int batchSize)
            => throw new NotSupportedException();
        public void Update(OutboxMessage message)
            => throw new NotSupportedException();
    }

    private sealed class RefundRepositoryFake : IRefundRepository
    {
        public int AddCalls { get; private set; }

        public Task AddAsync(Refund refund)
        {
            AddCalls++;
            return Task.CompletedTask;
        }

        public Task<Refund?> GetByIdAsync(Guid id)
            => throw new NotSupportedException();
        public Task<Refund?> GetByOrderIdAsync(Guid orderId)
            => throw new NotSupportedException();
        public Task<IEnumerable<Refund>> GetByTransactionIdAsync(Guid id)
            => throw new NotSupportedException();
        public Task<IEnumerable<Refund>> GetAllAsync()
            => throw new NotSupportedException();
        public void Update(Refund refund)
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