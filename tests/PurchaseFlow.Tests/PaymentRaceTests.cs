using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using P = StealDeal.Services.Payment;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PurchaseFlow.Tests;

public class PaymentRaceTests
{
    [Fact]
    public async Task ConcurrentCallbackAndExpiration_ProducesConsistentOutcome()
    {
        var connection = Environment.GetEnvironmentVariable(
            "PAYMENT_EXPIRY_TEST_DB_CONNECTION")
            ?? throw new InvalidOperationException(
                "Set PAYMENT_EXPIRY_TEST_DB_CONNECTION.");

        var options = new DbContextOptionsBuilder<
            P.Infrastructure.Persistence.ApplicationDbContext>()
            .UseSqlServer(connection).Options;

        var orderId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var gatewayRef = Guid.NewGuid().ToString("N");

        await using (var seed = new P.Infrastructure.Persistence
            .ApplicationDbContext(options))
        {
            seed.Transactions.Add(new P.Domain.Models.Transaction
            {
                Id = transactionId,
                OrderId = orderId,
                UserId = Guid.NewGuid(),
                StoreId = Guid.NewGuid(),
                Amount = 100,
                PaymentMethod = P.Domain.Constants.PaymentMethods.VnPay,
                GatewayRef = gatewayRef,
                Status = P.Domain.Constants.TransactionStatuses.Pending,
                ExpiresAt = DateTime.UtcNow.AddMinutes(-5),
                ReservedItemsJson = JsonSerializer.Serialize(new[]
                {
                    new P.Application.DTOs.Events.InventoryReservedItemDto
                    {
                        SurpriseBagId = Guid.NewGuid(),
                        Quantity = 1
                    }
                })
            });
            await seed.SaveChangesAsync();
        }

        try
        {
            var barrier = new ReadBarrier();
            var expirationPassFinished = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            // The callback and processor must use different DbContexts.
            await using var callbackDb = new P.Infrastructure.Persistence
                .ApplicationDbContext(options);
            var callbackRepository = new GatedTransactionRepository(
                new P.Infrastructure.Repositories.TransactionRepository(callbackDb),
                barrier,
                transactionId,
                gateCallbackRead: true,
                gateExpirationRead: false);

            var gateway = new FakeGateway(gatewayRef, 100);
            var callbackService = new P.Application.Services
                .PaymentCallbackService(
                    new FakeGatewayFactory(gateway),
                    callbackRepository,
                    new P.Infrastructure.Repositories.RefundRepository(callbackDb),
                    new P.Infrastructure.Repositories.OutboxMessageRepository(callbackDb),
                    new P.Infrastructure.Repositories.UnitOfWork(callbackDb));

            var services = new ServiceCollection();
            services.AddDbContext<
                P.Infrastructure.Persistence.ApplicationDbContext>(
                builder => builder.UseSqlServer(connection));
            services.AddScoped<P.Domain.Interfaces.ITransactionRepository>(
                provider => new GatedTransactionRepository(
                    new P.Infrastructure.Repositories.TransactionRepository(
                        provider.GetRequiredService<
                            P.Infrastructure.Persistence.ApplicationDbContext>()),
                    barrier,
                    transactionId,
                    gateCallbackRead: false,
                    gateExpirationRead: true));
            services.AddScoped<
                P.Domain.Interfaces.IOutboxMessageRepository,
                P.Infrastructure.Repositories.OutboxMessageRepository>();
            services.AddScoped<P.Domain.Interfaces.IUnitOfWork>(
                provider => new SignalingUnitOfWork(
                    new P.Infrastructure.Repositories.UnitOfWork(
                        provider.GetRequiredService<
                            P.Infrastructure.Persistence.ApplicationDbContext>()),
                    expirationPassFinished));

            using var provider = services.BuildServiceProvider();
            var processor = new P.Infrastructure.BackgroundServices
                .PaymentExpirationProcessor(
                    provider.GetRequiredService<IServiceScopeFactory>(),
                    Options.Create(new P.Infrastructure.Configuration
                        .PaymentExpirationSettings
                    {
                        Enabled = true,
                        BatchSize = 20,
                        PollingIntervalSeconds = 60
                    }),
                    NullLogger<P.Infrastructure.BackgroundServices
                        .PaymentExpirationProcessor>.Instance);

            await processor.StartAsync(CancellationToken.None);
            try
            {
                var callbackTask = callbackService.HandleVnPayIpnAsync(
                    new Dictionary<string, string>());

                // Both queries have loaded the same Pending transaction.
                await barrier.Ready.WaitAsync(TimeSpan.FromSeconds(20));
                barrier.Release();

                var callbackResult = await callbackTask;
                Assert.Equal("00", callbackResult.RspCode);
                await expirationPassFinished.Task.WaitAsync(
                    TimeSpan.FromSeconds(20));
            }
            finally
            {
                barrier.Release();
                await processor.StopAsync(CancellationToken.None);
            }

            await using var check = new P.Infrastructure.Persistence
                .ApplicationDbContext(options);
            var transaction = await check.Transactions.AsNoTracking()
                .SingleAsync(t => t.Id == transactionId);
            var candidateMessages = await check.OutboxMessages.AsNoTracking()
                .Where(m => m.EventType == "payment.completed" ||
                            m.EventType == "payment.failed" ||
                            m.EventType == "inventory.release_requested")
                .ToListAsync();
            var messages = candidateMessages
                .Where(m => PayloadOrderId(m.Payload) == orderId)
                .ToList();
            var refunds = await check.Refunds.AsNoTracking()
                .Where(r => r.OrderId == orderId).ToListAsync();

            var completed = messages.Count(m =>
                m.EventType == "payment.completed");
            var failed = messages.Count(m =>
                m.EventType == "payment.failed");
            var released = messages.Count(m =>
                m.EventType == "inventory.release_requested");

            var callbackWon =
                transaction.Status ==
                    P.Domain.Constants.TransactionStatuses.Success &&
                completed == 1 && failed == 0 && released == 0 &&
                refunds.Count == 0;

            var expirationWon =
                transaction.Status ==
                    P.Domain.Constants.TransactionStatuses.RefundPending &&
                completed == 0 && failed == 1 && released == 1 &&
                refunds.Count == 1 &&
                refunds[0].Status ==
                    P.Domain.Constants.RefundStatuses.Pending;

            Assert.True(
                callbackWon || expirationWon,
                $"Inconsistent outcome: status={transaction.Status}, " +
                $"completed={completed}, failed={failed}, " +
                $"released={released}, refunds={refunds.Count}");
        }
        finally
        {
            await using var cleanup = new P.Infrastructure.Persistence
                .ApplicationDbContext(options);
            var candidates = await cleanup.OutboxMessages
                .Where(m => m.EventType == "payment.completed" ||
                            m.EventType == "payment.failed" ||
                            m.EventType == "inventory.release_requested")
                .Select(m => new { m.Id, m.Payload }).ToListAsync();
            var messageIds = candidates
                .Where(m => PayloadOrderId(m.Payload) == orderId)
                .Select(m => m.Id).ToArray();

            if (messageIds.Length > 0)
                await cleanup.OutboxMessages
                    .Where(m => messageIds.Contains(m.Id))
                    .ExecuteDeleteAsync();

            await cleanup.Refunds
                .Where(r => r.OrderId == orderId).ExecuteDeleteAsync();
            await cleanup.Transactions
                .Where(t => t.Id == transactionId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task ConcurrentSuccessfulCallbacks_QueueCompletionOnlyOnce()
    {
        var connection = Environment.GetEnvironmentVariable(
            "PAYMENT_EXPIRY_TEST_DB_CONNECTION")
            ?? throw new InvalidOperationException(
                "Set PAYMENT_EXPIRY_TEST_DB_CONNECTION.");

        var options = new DbContextOptionsBuilder<
            P.Infrastructure.Persistence.ApplicationDbContext>()
            .UseSqlServer(connection).Options;

        var orderId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var gatewayRef = Guid.NewGuid().ToString("N");

        await using (var seed = new P.Infrastructure.Persistence
            .ApplicationDbContext(options))
        {
            seed.Transactions.Add(new P.Domain.Models.Transaction
            {
                Id = transactionId,
                OrderId = orderId,
                UserId = Guid.NewGuid(),
                StoreId = Guid.NewGuid(),
                Amount = 100,
                PaymentMethod = P.Domain.Constants.PaymentMethods.VnPay,
                GatewayRef = gatewayRef,
                Status = P.Domain.Constants.TransactionStatuses.Pending,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                ReservedItemsJson = JsonSerializer.Serialize(new[]
                {
                new P.Application.DTOs.Events.InventoryReservedItemDto
                {
                    SurpriseBagId = Guid.NewGuid(),
                    Quantity = 1
                }
            })
            });
            await seed.SaveChangesAsync();
        }

        try
        {
            var barrier = new ReadBarrier();
            var gateway = new FakeGateway(gatewayRef, 100);

            await using var firstDb = new P.Infrastructure.Persistence
                .ApplicationDbContext(options);
            await using var secondDb = new P.Infrastructure.Persistence
                .ApplicationDbContext(options);

            P.Application.Services.PaymentCallbackService CreateCallback(
                P.Infrastructure.Persistence.ApplicationDbContext db)
            {
                var repository = new GatedTransactionRepository(
                    new P.Infrastructure.Repositories.TransactionRepository(db),
                    barrier,
                    transactionId,
                    gateCallbackRead: true,
                    gateExpirationRead: false);

                return new P.Application.Services.PaymentCallbackService(
                    new FakeGatewayFactory(gateway),
                    repository,
                    new P.Infrastructure.Repositories.RefundRepository(db),
                    new P.Infrastructure.Repositories.OutboxMessageRepository(db),
                    new P.Infrastructure.Repositories.UnitOfWork(db));
            }

            var firstCallback = CreateCallback(firstDb);
            var secondCallback = CreateCallback(secondDb);

            var firstTask = firstCallback.HandleVnPayIpnAsync(
                new Dictionary<string, string>());
            var secondTask = secondCallback.HandleVnPayIpnAsync(
                new Dictionary<string, string>());

            P.Application.DTOs.Gateways.VnPayIpnHandleResult[] results;
            try
            {
                // Both callbacks must have read Pending before either saves.
                await barrier.Ready.WaitAsync(TimeSpan.FromSeconds(20));
                barrier.Release();

                results = await Task.WhenAll(firstTask, secondTask)
                    .WaitAsync(TimeSpan.FromSeconds(20));
            }
            finally
            {
                barrier.Release();
            }

            Assert.Equal(1, results.Count(r => r.RspCode == "00"));
            Assert.Equal(1, results.Count(r => r.RspCode == "02"));

            await using var check = new P.Infrastructure.Persistence
                .ApplicationDbContext(options);

            var transaction = await check.Transactions.AsNoTracking()
                .SingleAsync(t => t.Id == transactionId);

            var candidates = await check.OutboxMessages.AsNoTracking()
                .Where(m => m.EventType == "payment.completed" ||
                            m.EventType == "payment.failed" ||
                            m.EventType == "inventory.release_requested")
                .ToListAsync();

            var messages = candidates
                .Where(m => PayloadOrderId(m.Payload) == orderId)
                .ToList();

            Assert.Equal(P.Domain.Constants.TransactionStatuses.Success,
                transaction.Status);
            Assert.Single(messages, m => m.EventType == "payment.completed");
            Assert.DoesNotContain(messages, m =>
                m.EventType == "payment.failed" ||
                m.EventType == "inventory.release_requested");
            Assert.False(await check.Refunds.AsNoTracking()
                .AnyAsync(r => r.OrderId == orderId));
        }
        finally
        {
            await using var cleanup = new P.Infrastructure.Persistence
                .ApplicationDbContext(options);

            var candidates = await cleanup.OutboxMessages
                .Where(m => m.EventType == "payment.completed" ||
                            m.EventType == "payment.failed" ||
                            m.EventType == "inventory.release_requested")
                .Select(m => new { m.Id, m.Payload })
                .ToListAsync();

            var messageIds = candidates
                .Where(m => PayloadOrderId(m.Payload) == orderId)
                .Select(m => m.Id)
                .ToArray();

            if (messageIds.Length > 0)
                await cleanup.OutboxMessages
                    .Where(m => messageIds.Contains(m.Id))
                    .ExecuteDeleteAsync();

            await cleanup.Refunds
                .Where(r => r.OrderId == orderId).ExecuteDeleteAsync();
            await cleanup.Transactions
                .Where(t => t.Id == transactionId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task ConcurrentExpirationProcessors_ReleaseInventoryOnlyOnce()
    {
        var connection = Environment.GetEnvironmentVariable(
            "PAYMENT_EXPIRY_TEST_DB_CONNECTION")
            ?? throw new InvalidOperationException(
                "Set PAYMENT_EXPIRY_TEST_DB_CONNECTION.");

        var options = new DbContextOptionsBuilder<
            P.Infrastructure.Persistence.ApplicationDbContext>()
            .UseSqlServer(connection).Options;

        var orderId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();

        await using (var seed = new P.Infrastructure.Persistence
            .ApplicationDbContext(options))
        {
            seed.Transactions.Add(new P.Domain.Models.Transaction
            {
                Id = transactionId,
                OrderId = orderId,
                UserId = Guid.NewGuid(),
                StoreId = Guid.NewGuid(),
                Amount = 100,
                PaymentMethod = P.Domain.Constants.PaymentMethods.VnPay,
                Status = P.Domain.Constants.TransactionStatuses.Pending,
                ExpiresAt = DateTime.UtcNow.AddMinutes(-5),
                ReservedItemsJson = JsonSerializer.Serialize(new[]
                {
                new P.Application.DTOs.Events.InventoryReservedItemDto
                {
                    SurpriseBagId = Guid.NewGuid(),
                    Quantity = 1
                }
            })
            });
            await seed.SaveChangesAsync();
        }

        try
        {
            var barrier = new ReadBarrier();
            var firstFinished = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var secondFinished = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            (ServiceProvider Provider,
             P.Infrastructure.BackgroundServices.PaymentExpirationProcessor Processor)
                CreateProcessor(TaskCompletionSource<bool> finished)
            {
                var services = new ServiceCollection();

                services.AddDbContext<
                    P.Infrastructure.Persistence.ApplicationDbContext>(
                    builder => builder.UseSqlServer(connection));

                services.AddScoped<P.Domain.Interfaces.ITransactionRepository>(
                    provider => new GatedTransactionRepository(
                        new P.Infrastructure.Repositories.TransactionRepository(
                            provider.GetRequiredService<
                                P.Infrastructure.Persistence.ApplicationDbContext>()),
                        barrier,
                        transactionId,
                        gateCallbackRead: false,
                        gateExpirationRead: true));

                services.AddScoped<
                    P.Domain.Interfaces.IOutboxMessageRepository,
                    P.Infrastructure.Repositories.OutboxMessageRepository>();

                services.AddScoped<P.Domain.Interfaces.IUnitOfWork>(
                    provider => new SignalingUnitOfWork(
                        new P.Infrastructure.Repositories.UnitOfWork(
                            provider.GetRequiredService<
                                P.Infrastructure.Persistence.ApplicationDbContext>()),
                        finished));

                var provider = services.BuildServiceProvider();
                var processor = new P.Infrastructure.BackgroundServices
                    .PaymentExpirationProcessor(
                        provider.GetRequiredService<IServiceScopeFactory>(),
                        Options.Create(new P.Infrastructure.Configuration
                            .PaymentExpirationSettings
                        {
                            Enabled = true,
                            BatchSize = 20,
                            PollingIntervalSeconds = 60
                        }),
                        NullLogger<P.Infrastructure.BackgroundServices
                            .PaymentExpirationProcessor>.Instance);

                return (provider, processor);
            }

            var first = CreateProcessor(firstFinished);
            using var firstProvider = first.Provider;
            using var firstProcessor = first.Processor;

            var second = CreateProcessor(secondFinished);
            using var secondProvider = second.Provider;
            using var secondProcessor = second.Processor;

            try
            {
                await firstProcessor.StartAsync(CancellationToken.None);
                await secondProcessor.StartAsync(CancellationToken.None);

                // Both workers have loaded the same Pending payment.
                await barrier.Ready.WaitAsync(TimeSpan.FromSeconds(20));
                barrier.Release();

                // Both have attempted their first save, including the losing worker.
                await Task.WhenAll(firstFinished.Task, secondFinished.Task)
                    .WaitAsync(TimeSpan.FromSeconds(20));
            }
            finally
            {
                barrier.Release();
                await firstProcessor.StopAsync(CancellationToken.None);
                await secondProcessor.StopAsync(CancellationToken.None);
            }

            await using var check = new P.Infrastructure.Persistence
                .ApplicationDbContext(options);

            var transaction = await check.Transactions.AsNoTracking()
                .SingleAsync(t => t.Id == transactionId);

            var candidates = await check.OutboxMessages.AsNoTracking()
                .Where(m => m.EventType == "payment.completed" ||
                            m.EventType == "payment.failed" ||
                            m.EventType == "inventory.release_requested")
                .ToListAsync();

            var messages = candidates
                .Where(m => PayloadOrderId(m.Payload) == orderId)
                .ToList();

            Assert.Equal(P.Domain.Constants.TransactionStatuses.Expired,
                transaction.Status);
            Assert.Single(messages, m => m.EventType == "payment.failed");
            Assert.Single(messages, m =>
                m.EventType == "inventory.release_requested");
            Assert.DoesNotContain(messages, m =>
                m.EventType == "payment.completed");
            Assert.False(await check.Refunds.AsNoTracking()
                .AnyAsync(r => r.OrderId == orderId));
        }
        finally
        {
            await using var cleanup = new P.Infrastructure.Persistence
                .ApplicationDbContext(options);

            var candidates = await cleanup.OutboxMessages
                .Where(m => m.EventType == "payment.completed" ||
                            m.EventType == "payment.failed" ||
                            m.EventType == "inventory.release_requested")
                .Select(m => new { m.Id, m.Payload })
                .ToListAsync();

            var messageIds = candidates
                .Where(m => PayloadOrderId(m.Payload) == orderId)
                .Select(m => m.Id)
                .ToArray();

            if (messageIds.Length > 0)
                await cleanup.OutboxMessages
                    .Where(m => messageIds.Contains(m.Id))
                    .ExecuteDeleteAsync();

            await cleanup.Refunds
                .Where(r => r.OrderId == orderId).ExecuteDeleteAsync();
            await cleanup.Transactions
                .Where(t => t.Id == transactionId).ExecuteDeleteAsync();
        }
    }

    private static Guid PayloadOrderId(string payload)
    {
        using var json = JsonDocument.Parse(payload);
        return json.RootElement.GetProperty("orderId").GetGuid();
    }

    private sealed class ReadBarrier
    {
        private int _arrivals;
        private readonly TaskCompletionSource<bool> _ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Ready => _ready.Task;

        public async Task ArriveAsync()
        {
            if (Interlocked.Increment(ref _arrivals) == 2)
                _ready.TrySetResult(true);

            await _release.Task;
        }

        public void Release() => _release.TrySetResult(true);
    }

    private sealed class GatedTransactionRepository(
        P.Domain.Interfaces.ITransactionRepository inner,
        ReadBarrier barrier,
        Guid targetTransactionId,
        bool gateCallbackRead,
        bool gateExpirationRead)
        : P.Domain.Interfaces.ITransactionRepository
    {
        public Task AddAsync(P.Domain.Models.Transaction transaction)
            => inner.AddAsync(transaction);

        public Task<P.Domain.Models.Transaction?> GetByIdAsync(Guid id)
            => inner.GetByIdAsync(id);

        public Task<P.Domain.Models.Transaction?> GetByOrderIdAsync(Guid orderId)
            => inner.GetByOrderIdAsync(orderId);

        public async Task<P.Domain.Models.Transaction?> GetByGatewayRefAsync(
            string gatewayRef)
        {
            var transaction = await inner.GetByGatewayRefAsync(gatewayRef);
            if (gateCallbackRead && transaction?.Id == targetTransactionId)
                await barrier.ArriveAsync();
            return transaction;
        }

        public Task<IEnumerable<P.Domain.Models.Transaction>>
            GetByUserIdAsync(Guid userId)
            => inner.GetByUserIdAsync(userId);

        public async Task<List<P.Domain.Models.Transaction>>
            GetExpiredPendingBatchAsync(
                DateTime nowUtc,
                int batchSize,
                CancellationToken cancellationToken = default)
        {
            var batch = await inner.GetExpiredPendingBatchAsync(
                nowUtc, batchSize, cancellationToken);
            if (gateExpirationRead &&
                batch.Any(t => t.Id == targetTransactionId))
                await barrier.ArriveAsync();
            return batch;
        }

        public void Update(P.Domain.Models.Transaction transaction)
            => inner.Update(transaction);
    }

    private sealed class SignalingUnitOfWork(
        P.Domain.Interfaces.IUnitOfWork inner,
        TaskCompletionSource<bool> finished)
        : P.Domain.Interfaces.IUnitOfWork
    {
        public async Task<int> SaveChangesAsync()
        {
            try
            {
                return await inner.SaveChangesAsync();
            }
            finally
            {
                finished.TrySetResult(true);
            }
        }
    }

    private sealed class FakeGateway(string gatewayRef, decimal amount)
        : P.Application.Gateways.IPaymentGateway
    {
        public string Method => P.Domain.Constants.PaymentMethods.VnPay;

        public Task<P.Application.DTOs.Gateways.CreatePaymentResult>
            CreatePaymentAsync(
                P.Application.DTOs.Gateways.CreatePaymentRequest request,
                CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<P.Application.DTOs.Gateways.PaymentCallbackResult>
            VerifyIpnAsync(
                IReadOnlyDictionary<string, string> parameters,
                CancellationToken cancellationToken = default)
            => Task.FromResult(
                new P.Application.DTOs.Gateways.PaymentCallbackResult
                {
                    IsValidSignature = true,
                    IsSuccess = true,
                    PaymentMethod = Method,
                    GatewayRef = gatewayRef,
                    Amount = amount,
                    PaidAtUtc = DateTime.UtcNow
                });
    }

    private sealed class FakeGatewayFactory(FakeGateway gateway)
        : P.Application.Gateways.IPaymentGatewayFactory
    {
        public P.Application.Gateways.IPaymentGateway GetGateway(
            string paymentMethod)
        {
            Assert.Equal(gateway.Method, paymentMethod);
            return gateway;
        }
    }
}
