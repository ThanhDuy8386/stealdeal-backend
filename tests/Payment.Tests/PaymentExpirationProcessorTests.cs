using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StealDeal.Services.Payment.Application.DTOs.Events;
using StealDeal.Services.Payment.Domain.Constants;
using StealDeal.Services.Payment.Domain.Interfaces;
using StealDeal.Services.Payment.Domain.Models;
using StealDeal.Services.Payment.Infrastructure.BackgroundServices;
using StealDeal.Services.Payment.Infrastructure.Configuration;

namespace Payment.Tests;

public class PaymentExpirationProcessorTests
{
    [Fact]
    public async Task ExpiredPendingPayment_QueuesFailureAndInventoryRelease()
    {
        var bagId = Guid.NewGuid();
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            StoreId = Guid.NewGuid(),
            Amount = 138,
            PaymentMethod = PaymentMethods.VnPay,
            Status = TransactionStatuses.Pending,
            ExpiresAt = DateTime.UtcNow.AddMinutes(-5),
            ReservedItemsJson = JsonSerializer.Serialize(new[]
            {
                new InventoryReservedItemDto
                {
                    SurpriseBagId = bagId,
                    Quantity = 2
                }
            })
        };

        var transactions = new TransactionRepositoryFake(transaction);
        var outbox = new OutboxRepositoryFake();
        var unitOfWork = new UnitOfWorkFake();

        using var services = new ServiceCollection()
            .AddSingleton<ITransactionRepository>(transactions)
            .AddSingleton<IOutboxMessageRepository>(outbox)
            .AddSingleton<IUnitOfWork>(unitOfWork)
            .BuildServiceProvider();

        var processor = new PaymentExpirationProcessor(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PaymentExpirationSettings
            {
                Enabled = true,
                BatchSize = 20,
                PollingIntervalSeconds = 60
            }),
            NullLogger<PaymentExpirationProcessor>.Instance);

        await processor.StartAsync(CancellationToken.None);
        try
        {
            await unitOfWork.Saved.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await processor.StopAsync(CancellationToken.None);
        }

        Assert.Equal(TransactionStatuses.Expired, transaction.Status);
        Assert.Equal(
            "Payment expired before gateway confirmation.",
            transaction.FailureReason);
        Assert.Equal(1, transactions.UpdateCalls);
        Assert.Equal(1, unitOfWork.SaveCalls);

        Assert.Equal(2, outbox.Added.Count);
        var failed = Assert.Single(outbox.Added, message => message.EventType == PaymentEventTypes.PaymentFailed);
        var release = Assert.Single(outbox.Added, message => message.EventType ==
                       PaymentEventTypes.InventoryReleaseRequested);

        Assert.Equal("Pending", failed.Status);
        Assert.Equal("Pending", release.Status);

        using var payload = JsonDocument.Parse(release.Payload);
        Assert.Equal(
            "PaymentExpired",
            payload.RootElement.GetProperty("reasonCode").GetString());

        var item = payload.RootElement.GetProperty("items")[0];
        Assert.Equal(
            bagId,
            item.GetProperty("surpriseBagId").GetGuid());
        Assert.Equal(2, item.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task SuccessfulPaymentInExpirationBatch_IsNotExpiredOrReleased()
    {
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            StoreId = Guid.NewGuid(),
            Amount = 138,
            PaymentMethod = PaymentMethods.VnPay,
            Status = TransactionStatuses.Success,
            ExpiresAt = DateTime.UtcNow.AddMinutes(-5),
            ReservedItemsJson = JsonSerializer.Serialize(new[]
            {
            new InventoryReservedItemDto
            {
                SurpriseBagId = Guid.NewGuid(),
                Quantity = 1
            }
        })
        };

        // Deliberately return a successful payment in the batch to test
        // the processor's own status check.
        var transactions = new TransactionRepositoryFake(
            transaction, includeNonPendingInBatch: true);
        var outbox = new OutboxRepositoryFake();
        var unitOfWork = new UnitOfWorkFake();

        using var services = new ServiceCollection()
            .AddSingleton<ITransactionRepository>(transactions)
            .AddSingleton<IOutboxMessageRepository>(outbox)
            .AddSingleton<IUnitOfWork>(unitOfWork)
            .BuildServiceProvider();

        var processor = new PaymentExpirationProcessor(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PaymentExpirationSettings
            {
                Enabled = true,
                BatchSize = 20,
                PollingIntervalSeconds = 60
            }),
            NullLogger<PaymentExpirationProcessor>.Instance);

        await processor.StartAsync(CancellationToken.None);
        try
        {
            await unitOfWork.Saved.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await processor.StopAsync(CancellationToken.None);
        }

        Assert.Equal(TransactionStatuses.Success, transaction.Status);
        Assert.Equal(0, transactions.UpdateCalls);
        Assert.Empty(outbox.Added);
    }

    [Fact]
    public async Task ExpiredPendingPayment_WithoutReservedItems_QueuesOnlyFailure()
    {
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            StoreId = Guid.NewGuid(),
            Amount = 138,
            PaymentMethod = PaymentMethods.VnPay,
            Status = TransactionStatuses.Pending,
            ExpiresAt = DateTime.UtcNow.AddMinutes(-5),
            ReservedItemsJson = null
        };

        var transactions = new TransactionRepositoryFake(transaction);
        var outbox = new OutboxRepositoryFake();
        var unitOfWork = new UnitOfWorkFake();

        using var services = new ServiceCollection()
            .AddSingleton<ITransactionRepository>(transactions)
            .AddSingleton<IOutboxMessageRepository>(outbox)
            .AddSingleton<IUnitOfWork>(unitOfWork)
            .BuildServiceProvider();

        var processor = new PaymentExpirationProcessor(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PaymentExpirationSettings
            {
                Enabled = true,
                BatchSize = 20,
                PollingIntervalSeconds = 60
            }),
            NullLogger<PaymentExpirationProcessor>.Instance);

        await processor.StartAsync(CancellationToken.None);
        try
        {
            await unitOfWork.Saved.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await processor.StopAsync(CancellationToken.None);
        }

        Assert.Equal(TransactionStatuses.Expired, transaction.Status);
        Assert.Equal(1, transactions.UpdateCalls);
        Assert.Equal(1, unitOfWork.SaveCalls);

        var message = Assert.Single(outbox.Added);
        Assert.Equal(PaymentEventTypes.PaymentFailed, message.EventType);
    }

    [Fact]
    public async Task ExpiredPaymentBatch_ProcessesAllTransactionsAndSavesOnce()
    {
        Transaction CreateExpiredPayment() => new()
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            StoreId = Guid.NewGuid(),
            Amount = 138,
            PaymentMethod = PaymentMethods.VnPay,
            Status = TransactionStatuses.Pending,
            ExpiresAt = DateTime.UtcNow.AddMinutes(-5),
            ReservedItemsJson = JsonSerializer.Serialize(new[]
            {
            new InventoryReservedItemDto
            {
                SurpriseBagId = Guid.NewGuid(),
                Quantity = 1
            }
        })
        };

        var first = CreateExpiredPayment();
        var second = CreateExpiredPayment();
        var transactions = new TransactionRepositoryFake(
            new[] { first, second });
        var outbox = new OutboxRepositoryFake();
        var unitOfWork = new UnitOfWorkFake();

        using var services = new ServiceCollection()
            .AddSingleton<ITransactionRepository>(transactions)
            .AddSingleton<IOutboxMessageRepository>(outbox)
            .AddSingleton<IUnitOfWork>(unitOfWork)
            .BuildServiceProvider();

        var processor = new PaymentExpirationProcessor(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PaymentExpirationSettings
            {
                Enabled = true,
                BatchSize = 20,
                PollingIntervalSeconds = 60
            }),
            NullLogger<PaymentExpirationProcessor>.Instance);

        await processor.StartAsync(CancellationToken.None);
        try
        {
            await unitOfWork.Saved.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await processor.StopAsync(CancellationToken.None);
        }

        Assert.Equal(TransactionStatuses.Expired, first.Status);
        Assert.Equal(TransactionStatuses.Expired, second.Status);
        Assert.Equal(2, transactions.UpdateCalls);
        Assert.Equal(1, unitOfWork.SaveCalls);
        Assert.Equal(4, outbox.Added.Count);
        Assert.Equal(2, outbox.Added.Count(message =>
            message.EventType == PaymentEventTypes.PaymentFailed));
        Assert.Equal(2, outbox.Added.Count(message =>
            message.EventType == PaymentEventTypes.InventoryReleaseRequested));
    }

    private sealed class TransactionRepositoryFake(
    IReadOnlyList<Transaction> transactions,
    bool includeNonPendingInBatch = false) : ITransactionRepository
    {
        public TransactionRepositoryFake(
       Transaction transaction,
       bool includeNonPendingInBatch = false)
       : this(new[] { transaction }, includeNonPendingInBatch)
        {
        }

        public void Update(Transaction updated)
        {
            Assert.Contains(updated, transactions);
            UpdateCalls++;
        }

        public Task AddAsync(Transaction value)
            => throw new NotSupportedException();

        public Task<Transaction?> GetByIdAsync(Guid id)
            => throw new NotSupportedException();

        public Task<Transaction?> GetByOrderIdAsync(Guid orderId)
            => throw new NotSupportedException();

        public Task<Transaction?> GetByGatewayRefAsync(string gatewayRef)
            => throw new NotSupportedException();

        public Task<IEnumerable<Transaction>> GetByUserIdAsync(Guid userId)
            => throw new NotSupportedException();
        public int UpdateCalls { get; private set; }

        public Task<List<Transaction>> GetExpiredPendingBatchAsync(
    DateTime nowUtc,
    int batchSize,
    CancellationToken cancellationToken = default)
        {
            var batch = transactions
                .Where(transaction =>
                    transaction.ExpiresAt is DateTime expiresAt &&
                    expiresAt <= nowUtc &&
                    (transaction.Status == TransactionStatuses.Pending ||
                     includeNonPendingInBatch))
                .Take(batchSize)
                .ToList();

            return Task.FromResult(batch);
        }
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

    private sealed class UnitOfWorkFake : IUnitOfWork
    {
        private readonly TaskCompletionSource<bool> _saved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Saved => _saved.Task;
        public int SaveCalls { get; private set; }

        public Task<int> SaveChangesAsync()
        {
            SaveCalls++;
            _saved.TrySetResult(true);
            return Task.FromResult(1);
        }
    }
}
