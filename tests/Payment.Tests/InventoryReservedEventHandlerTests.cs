using StealDeal.Services.Payment.Application.DTOs.Events;
using StealDeal.Services.Payment.Application.DTOs.Gateways;
using StealDeal.Services.Payment.Application.EventHandlers;
using StealDeal.Services.Payment.Application.Gateways;
using StealDeal.Services.Payment.Application.Messaging;
using StealDeal.Services.Payment.Domain.Constants;
using StealDeal.Services.Payment.Domain.Interfaces;
using StealDeal.Services.Payment.Domain.Models;
using System.Text.Json;

namespace Payment.Tests;

public class InventoryReservedEventHandlerTests
{
    [Fact]
    public async Task ValidEvent_CreatesPendingPayment()
    {
        var orderId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var bagId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        var gateway = new GatewayFake();
        var factory = new GatewayFactoryFake(gateway);
        var transactions = new TransactionRepositoryFake();
        var processed = new ProcessedMessageRepositoryFake();
        var unitOfWork = new UnitOfWorkFake(transactions, processed);
        var handler = new InventoryReservedEventHandler(
            transactions, processed, factory, unitOfWork);

        await handler.HandleAsync(
            new InventoryReservedEvent
            {
                MessageId = messageId,
                OrderId = orderId,
                UserId = userId,
                StoreId = storeId,
                TotalAmount = 138,
                Items =
                [
                    new InventoryReservedItemDto
                    {
                        SurpriseBagId = bagId,
                        Quantity = 2
                    }
                ]
            },
            new IntegrationEventContext
            {
                MessageId = messageId,
                ConsumerName = "payment-inventory-reserved-test",
                EventType = "inventory.reserved",
                RoutingKey = "inventory.reserved"
            });

        var transaction = Assert.IsType<Transaction>(transactions.Added);
        Assert.Equal(orderId, transaction.OrderId);
        Assert.Equal(userId, transaction.UserId);
        Assert.Equal(storeId, transaction.StoreId);
        Assert.Equal(138m, transaction.Amount);
        Assert.Equal(TransactionStatuses.Pending, transaction.Status);
        Assert.Equal("test-gateway-ref", transaction.GatewayRef);
        Assert.Equal("https://payment.test/checkout", transaction.CheckoutUrl);

        var items = JsonSerializer.Deserialize<List<InventoryReservedItemDto>>(
            transaction.ReservedItemsJson!);
        var item = Assert.Single(items!);
        Assert.Equal(bagId, item.SurpriseBagId);
        Assert.Equal(2, item.Quantity);

        Assert.Equal(PaymentMethods.VnPay, factory.RequestedMethod);
        Assert.Equal(1, gateway.CreateCalls);
        var gatewayRequest = Assert.IsType<CreatePaymentRequest>(
            gateway.LastRequest);
        Assert.Equal(transaction.Id, gatewayRequest.TransactionId);
        Assert.Equal(orderId, gatewayRequest.OrderId);
        Assert.Equal(138m, gatewayRequest.Amount);

        Assert.Equal(messageId, processed.Added?.MessageId);
        Assert.Equal(1, unitOfWork.SaveCalls);
        Assert.True(unitOfWork.HadTransactionAndProcessedMessageAtSave);
    }

    [Fact]
    public async Task DuplicateEvent_DoesNotCreateSecondPayment()
    {
        var messageId = Guid.NewGuid();
        var gateway = new GatewayFake();
        var transactions = new TransactionRepositoryFake();
        var processed = new ProcessedMessageRepositoryFake();
        var unitOfWork = new UnitOfWorkFake(transactions, processed);
        var factory = new GatewayFactoryFake(gateway);

        var reservedEvent = new InventoryReservedEvent
        {
            MessageId = messageId,
            OrderId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            StoreId = Guid.NewGuid(),
            TotalAmount = 138
        };
        var context = new IntegrationEventContext
        {
            MessageId = messageId,
            ConsumerName = "payment-inventory-reserved-test",
            EventType = "inventory.reserved",
            RoutingKey = "inventory.reserved"
        };

        var firstHandler = new InventoryReservedEventHandler(
            transactions, processed, factory, unitOfWork);
        await firstHandler.HandleAsync(reservedEvent, context);

        // A new handler receives the same message again.
        var secondHandler = new InventoryReservedEventHandler(
            transactions, processed, factory, unitOfWork);
        await secondHandler.HandleAsync(reservedEvent, context);

        Assert.Equal(1, gateway.CreateCalls);
        Assert.Equal(1, transactions.AddCalls);
        Assert.Equal(1, processed.AddCalls);
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task SameOrderDifferentMessageIds_DoesNotCreateSecondPayment()
    {
        var orderId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var firstMessageId = Guid.NewGuid();
        var secondMessageId = Guid.NewGuid();

        var gateway = new GatewayFake();
        var factory = new GatewayFactoryFake(gateway);
        var transactions = new TransactionRepositoryFake();
        var processed = new ProcessedMessageRepositoryFake();
        var unitOfWork = new UnitOfWorkFake(transactions, processed);
        var handler = new InventoryReservedEventHandler(
            transactions, processed, factory, unitOfWork);

        InventoryReservedEvent MakeEvent(Guid messageId) => new()
        {
            MessageId = messageId,
            OrderId = orderId,
            UserId = userId,
            StoreId = storeId,
            TotalAmount = 138
        };

        IntegrationEventContext MakeContext(Guid messageId) => new()
        {
            MessageId = messageId,
            ConsumerName = "payment-inventory-reserved-test",
            EventType = "inventory.reserved",
            RoutingKey = "inventory.reserved"
        };

        await handler.HandleAsync(
            MakeEvent(firstMessageId), MakeContext(firstMessageId));
        await handler.HandleAsync(
            MakeEvent(secondMessageId), MakeContext(secondMessageId));

        Assert.Equal(1, gateway.CreateCalls);
        Assert.Equal(1, transactions.AddCalls);
        Assert.Equal(TransactionStatuses.Pending, transactions.Added?.Status);
        Assert.Equal(2, processed.AddCalls);
        Assert.Equal(2, unitOfWork.SaveCalls);
        Assert.Contains(processed.Messages, m => m.MessageId == firstMessageId);
        Assert.Contains(processed.Messages, m => m.MessageId == secondMessageId);
    }

    private sealed class GatewayFake : IPaymentGateway
    {
        public string Method => PaymentMethods.VnPay;
        public int CreateCalls { get; private set; }
        public CreatePaymentRequest? LastRequest { get; private set; }

        public Task<CreatePaymentResult> CreatePaymentAsync(
            CreatePaymentRequest request,
            CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            LastRequest = request;
            return Task.FromResult(new CreatePaymentResult
            {
                PaymentMethod = PaymentMethods.VnPay,
                GatewayRef = "test-gateway-ref",
                CheckoutUrl = "https://payment.test/checkout",
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15)
            });
        }

        public Task<PaymentCallbackResult> VerifyIpnAsync(
            IReadOnlyDictionary<string, string> parameters,
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

    private sealed class TransactionRepositoryFake : ITransactionRepository
    {
        public Transaction? Added { get; private set; }
        public int AddCalls { get; private set; }

        public Task AddAsync(Transaction transaction)
        {
            AddCalls++;
            Added = transaction;
            return Task.CompletedTask;
        }

        public Task<Transaction?> GetByOrderIdAsync(Guid orderId)
    => Task.FromResult<Transaction?>(
        Added?.OrderId == orderId ? Added : null);

        public Task<Transaction?> GetByIdAsync(Guid id)
            => throw new NotSupportedException();

        public Task<Transaction?> GetByGatewayRefAsync(string gatewayRef)
            => throw new NotSupportedException();

        public Task<IEnumerable<Transaction>> GetByUserIdAsync(Guid userId)
            => throw new NotSupportedException();

        public Task<List<Transaction>> GetExpiredPendingBatchAsync(
            DateTime nowUtc, int batchSize,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Update(Transaction transaction)
            => throw new NotSupportedException();
    }

    private sealed class ProcessedMessageRepositoryFake
        : IProcessedMessageRepository
    {
        public List<ProcessedMessage> Messages { get; } = new();
        public ProcessedMessage? Added => Messages.LastOrDefault();
        public int AddCalls => Messages.Count;
        public Task<ProcessedMessage?> GetAsync(
            Guid messageId, string consumerName)
            => Task.FromResult(Messages.FirstOrDefault(m =>
                m.MessageId == messageId && m.ConsumerName == consumerName));   

        public Task<bool> ExistsAsync(Guid messageId, string consumerName)
            => Task.FromResult(Messages.Any(m =>
                m.MessageId == messageId && m.ConsumerName == consumerName));

        public Task AddAsync(ProcessedMessage message)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class UnitOfWorkFake(
        TransactionRepositoryFake transactions,
        ProcessedMessageRepositoryFake processed) : IUnitOfWork
    {
        public int SaveCalls { get; private set; }
        public bool HadTransactionAndProcessedMessageAtSave { get; private set; }

        public Task<int> SaveChangesAsync()
        {
            SaveCalls++;
            HadTransactionAndProcessedMessageAtSave =
                transactions.Added is not null && processed.Added is not null;
            return Task.FromResult(2);
        }
    }
}