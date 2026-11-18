using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using O = StealDeal.Services.Order;
using S = StealDeal.Services.Store;
using P = StealDeal.Services.Payment;

namespace PurchaseFlow.Tests;

public class ExpiredPaymentLateSuccessTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task ExpiredPayment_LateSuccess_RestoresStockAndQueuesRefund()
    {
        var orderConnection = Connection("ORDER_TEST_DB_CONNECTION");
        var storeConnection = Connection("STORE_TEST_DB_CONNECTION");
        var paymentConnection = Connection(
            "PAYMENT_EXPIRY_TEST_DB_CONNECTION");

        var orderOptions = new DbContextOptionsBuilder<
            O.Infrastructure.Persistency.ApplicationDbContext>()
            .UseSqlServer(orderConnection).Options;
        var storeOptions = new DbContextOptionsBuilder<
            S.Infrastructure.Persistence.ApplicationDbContext>()
            .UseSqlServer(storeConnection).Options;
        var paymentOptions = new DbContextOptionsBuilder<
            P.Infrastructure.Persistence.ApplicationDbContext>()
            .UseSqlServer(paymentConnection).Options;

        var storeId = Guid.NewGuid();
        var bagId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var orderId = Guid.Empty;

        await using (var seed = new S.Infrastructure.Persistence
            .ApplicationDbContext(storeOptions))
        {
            seed.StoreProfiles.Add(new S.Domain.Models.StoreProfile
            {
                Id = storeId,
                OwnerId = Guid.NewGuid(),
                Name = "Expiry Test Store",
                IsActive = true,
                IsVerify = true
            });
            seed.SurpriseBags.Add(new S.Domain.Models.SurpriseBag
            {
                Id = bagId,
                StoreId = storeId,
                Name = "Expiry Test Bag",
                Status = "Active",
                OriginalPrice = 100,
                SalePrice = 50,
                QuantityTotal = 8,
                QuantityRemaining = 8,
                PickupStartTime = DateTime.UtcNow.AddHours(1),
                PickupEndTime = DateTime.UtcNow.AddHours(3),
                ExpiryDate = DateTime.UtcNow.AddDays(1)
            });
            await seed.SaveChangesAsync();
        }

        try
        {
            O.Domain.Models.OutboxMessage orderMessage;

            await using (var orderDb = new O.Infrastructure.Persistency
                .ApplicationDbContext(orderOptions))
            {
                var orderService = new O.Application.Services.OrderService(
                    new O.Infrastructure.Repositories.OrderRepository(orderDb),
                    new O.Infrastructure.Repositories.UnitOfWork(orderDb),
                    new O.Infrastructure.Repositories.OutboxMessageRepository(orderDb));

                var order = await orderService.CreateOrderAsync(
                    buyerId,
                    new O.Application.DTOs.Requests.CreateOrderRequest
                    {
                        StoreId = storeId,
                        StoreNameSnapshot = "Expiry Test Store",
                        ContactNameSnapshot = "Test Buyer",
                        ContactPhoneSnapshot = "0123456789",
                        DeliveryType = "Pickup",
                        DeliveryAddress = "",
                        Items =
                        [
                            new O.Application.DTOs.Requests.CreateOrderItemRequest
                            {
                                BagId = bagId,
                                BagNameSnapshot = "Expiry Test Bag",
                                UnitPriceSnapshot = 50,
                                Quantity = 2
                            }
                        ]
                    });

                orderId = order.Id;
                orderMessage = orderDb.OutboxMessages.Local.Single(
                    m => m.RoutingKey == "order.created");
            }

            S.Domain.Models.OutboxMessage reservedMessage;

            await using (var storeDb = new S.Infrastructure.Persistence
                .ApplicationDbContext(storeOptions))
            {
                var handler = new S.Application.EventHandlers
                    .CreateOrderEventHandler(
                        new S.Infrastructure.Repositories.SurpriseBagRepository(storeDb),
                        new S.Infrastructure.Repositories.ProcessedMessageRepository(storeDb),
                        new S.Infrastructure.Repositories.OutboxMessageRepository(storeDb),
                        new S.Infrastructure.Repositories.UnitOfWork(storeDb));

                await handler.HandleAsync(
                    Read<S.Application.DTOs.Events.CreateOrderEvent>(
                        orderMessage.Payload),
                    new S.Application.Messaging.IntegrationEventContext
                    {
                        MessageId = orderMessage.Id,
                        ConsumerName = "expiry-flow-store",
                        EventType = orderMessage.EventType,
                        RoutingKey = orderMessage.RoutingKey
                    });

                reservedMessage = storeDb.OutboxMessages.Local.Single(
                    m => m.EventType == "inventory.reserved");
            }

            await using (var check = new S.Infrastructure.Persistence
                .ApplicationDbContext(storeOptions))
            {
                Assert.Equal(6, await check.SurpriseBags
                    .Where(b => b.Id == bagId)
                    .Select(b => b.QuantityRemaining).SingleAsync());
            }

            var gateway = new FakeGateway();
            var factory = new FakeGatewayFactory(gateway);

            await using (var paymentDb = new P.Infrastructure.Persistence
                .ApplicationDbContext(paymentOptions))
            {
                var handler = new P.Application.EventHandlers
                    .InventoryReservedEventHandler(
                        new P.Infrastructure.Repositories.TransactionRepository(paymentDb),
                        new P.Infrastructure.Repositories.ProcessedMessageRepository(paymentDb),
                        factory,
                        new P.Infrastructure.Repositories.UnitOfWork(paymentDb));

                await handler.HandleAsync(
                    Read<P.Application.DTOs.Events.InventoryReservedEvent>(
                        reservedMessage.Payload),
                    new P.Application.Messaging.IntegrationEventContext
                    {
                        MessageId = reservedMessage.Id,
                        ConsumerName = "expiry-flow-payment",
                        EventType = reservedMessage.EventType,
                        RoutingKey = reservedMessage.RoutingKey
                    });
            }

            // Move this test's payment past its deadline.
            await using (var paymentDb = new P.Infrastructure.Persistence
                .ApplicationDbContext(paymentOptions))
            {
                var expiredAt = DateTime.UtcNow.AddMinutes(-5);
                var changed = await paymentDb.Transactions
                    .Where(t => t.OrderId == orderId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(t => t.ExpiresAt, (DateTime?)expiredAt));
                Assert.Equal(1, changed);
            }

            // Run one expiration pass and wait until it has saved.
            var saved = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var services = new ServiceCollection();
            services.AddDbContext<
                P.Infrastructure.Persistence.ApplicationDbContext>(
                options => options.UseSqlServer(paymentConnection));
            services.AddScoped<
                P.Domain.Interfaces.ITransactionRepository,
                P.Infrastructure.Repositories.TransactionRepository>();
            services.AddScoped<
                P.Domain.Interfaces.IOutboxMessageRepository,
                P.Infrastructure.Repositories.OutboxMessageRepository>();
            services.AddScoped<P.Domain.Interfaces.IUnitOfWork>(provider =>
                new SignalingUnitOfWork(
                    new P.Infrastructure.Repositories.UnitOfWork(
                        provider.GetRequiredService<
                            P.Infrastructure.Persistence.ApplicationDbContext>()),
                    saved));

            using (var provider = services.BuildServiceProvider())
            {
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
                    await saved.Task.WaitAsync(TimeSpan.FromSeconds(15));
                }
                finally
                {
                    await processor.StopAsync(CancellationToken.None);
                }
            }

            P.Domain.Models.OutboxMessage failedMessage;
            P.Domain.Models.OutboxMessage releaseMessage;

            await using (var paymentDb = new P.Infrastructure.Persistence
                .ApplicationDbContext(paymentOptions))
            {
                var messages = await paymentDb.OutboxMessages.AsNoTracking()
                    .Where(m => m.EventType == "payment.failed" ||
                                m.EventType == "inventory.release_requested")
                    .ToListAsync();
                var forOrder = messages
                    .Where(m => PayloadOrderId(m.Payload) == orderId)
                    .ToList();

                failedMessage = Assert.Single(forOrder.Where(m =>
                    m.EventType == "payment.failed"));
                releaseMessage = Assert.Single(forOrder.Where(m =>
                    m.EventType == "inventory.release_requested"));

                Assert.Equal(
                    P.Domain.Constants.TransactionStatuses.Expired,
                    await paymentDb.Transactions
                        .Where(t => t.OrderId == orderId)
                        .Select(t => t.Status).SingleAsync());
            }

            await using (var orderDb = new O.Infrastructure.Persistency
                .ApplicationDbContext(orderOptions))
            {
                var handler = new O.Application.EventHandlers
                    .OrderStatusEventHandler(
                        new O.Infrastructure.Repositories.OrderRepository(orderDb),
                        new O.Infrastructure.Repositories.ProcessedMessageRepository(orderDb),
                        new O.Infrastructure.Repositories.UnitOfWork(orderDb));

                await handler.HandleAsync(
                    Read<O.Application.DTOs.Events.PaymentFailedEvent>(
                        failedMessage.Payload),
                    new O.Application.Messaging.IntegrationEventContext
                    {
                        MessageId = failedMessage.Id,
                        ConsumerName = "expiry-flow-order",
                        EventType = failedMessage.EventType,
                        RoutingKey = failedMessage.RoutingKey
                    });
            }

            await using (var storeDb = new S.Infrastructure.Persistence
                .ApplicationDbContext(storeOptions))
            {
                var handler = new S.Application.EventHandlers
                    .InventoryReleaseRequestedEventHandler(
                        new S.Infrastructure.Repositories.SurpriseBagRepository(storeDb),
                        new S.Infrastructure.Repositories.ProcessedMessageRepository(storeDb),
                        new S.Infrastructure.Repositories.UnitOfWork(storeDb));

                await handler.HandleAsync(
                    Read<S.Application.DTOs.Events.InventoryReleaseRequestedEvent>(
                        releaseMessage.Payload),
                    new S.Application.Messaging.IntegrationEventContext
                    {
                        MessageId = releaseMessage.Id,
                        ConsumerName = "expiry-flow-store-release",
                        EventType = releaseMessage.EventType,
                        RoutingKey = releaseMessage.RoutingKey
                    });
            }

            // Use a fresh context: the callback must read Expired from SQL,
            // not an earlier tracked Pending transaction.
            await using (var paymentDb = new P.Infrastructure.Persistence
                .ApplicationDbContext(paymentOptions))
            {
                var callbackService =
                    new P.Application.Services.PaymentCallbackService(
                        factory,
                        new P.Infrastructure.Repositories.TransactionRepository(paymentDb),
                        new P.Infrastructure.Repositories.RefundRepository(paymentDb),
                        new P.Infrastructure.Repositories.OutboxMessageRepository(paymentDb),
                        new P.Infrastructure.Repositories.UnitOfWork(paymentDb));

                var result = await callbackService.HandleVnPayIpnAsync(
                    new Dictionary<string, string>());
                Assert.Equal("00", result.RspCode);
            }

            await using var checkOrder = new O.Infrastructure.Persistency
                .ApplicationDbContext(orderOptions);
            await using var checkStore = new S.Infrastructure.Persistence
                .ApplicationDbContext(storeOptions);
            await using var checkPayment = new P.Infrastructure.Persistence
                .ApplicationDbContext(paymentOptions);

            Assert.Equal("PaymentFailed", await checkOrder.OrderProfiles
                .Where(o => o.Id == orderId)
                .Select(o => o.Status).SingleAsync());
            Assert.Equal(8, await checkStore.SurpriseBags
                .Where(b => b.Id == bagId)
                .Select(b => b.QuantityRemaining).SingleAsync());

            var transaction = Assert.Single(await checkPayment.Transactions
                .Where(t => t.OrderId == orderId).ToListAsync());
            Assert.Equal(
                P.Domain.Constants.TransactionStatuses.RefundPending,
                transaction.Status);

            var refund = Assert.Single(await checkPayment.Refunds
                .Where(r => r.OrderId == orderId).ToListAsync());
            Assert.Equal(
                P.Domain.Constants.RefundStatuses.Pending,
                refund.Status);
            Assert.Equal(transaction.Id, refund.TransactionId);

            var completedPayloads = await checkPayment.OutboxMessages
                .Where(m => m.EventType == "payment.completed")
                .Select(m => m.Payload).ToListAsync();
            Assert.Empty(completedPayloads.Where(payload =>
                PayloadOrderId(payload) == orderId));
        }
        finally
        {
            if (orderId != Guid.Empty)
            {
                await using var paymentCleanup = new P.Infrastructure.Persistence
                    .ApplicationDbContext(paymentOptions);
                var paymentMessages = await paymentCleanup.OutboxMessages
                    .Where(m => m.EventType == "payment.failed" ||
                                m.EventType == "inventory.release_requested" ||
                                m.EventType == "payment.completed")
                    .Select(m => new { m.Id, m.Payload }).ToListAsync();
                var paymentIds = paymentMessages
                    .Where(m => PayloadOrderId(m.Payload) == orderId)
                    .Select(m => m.Id).ToArray();
                if (paymentIds.Length > 0)
                    await paymentCleanup.OutboxMessages
                        .Where(m => paymentIds.Contains(m.Id))
                        .ExecuteDeleteAsync();

                await paymentCleanup.Refunds
                    .Where(r => r.OrderId == orderId).ExecuteDeleteAsync();
                await paymentCleanup.ProcessedMessages
                    .Where(m => m.AggregateId == orderId).ExecuteDeleteAsync();
                await paymentCleanup.Transactions
                    .Where(t => t.OrderId == orderId).ExecuteDeleteAsync();

                await using var orderCleanup = new O.Infrastructure.Persistency
                    .ApplicationDbContext(orderOptions);
                var orderMessages = await orderCleanup.OutboxMessages
                    .Where(m => m.RoutingKey == "order.created")
                    .Select(m => new { m.Id, m.Payload }).ToListAsync();
                var orderIds = orderMessages
                    .Where(m => PayloadOrderId(m.Payload) == orderId)
                    .Select(m => m.Id).ToArray();
                if (orderIds.Length > 0)
                    await orderCleanup.OutboxMessages
                        .Where(m => orderIds.Contains(m.Id))
                        .ExecuteDeleteAsync();
                await orderCleanup.ProcessedMessages
                    .Where(m => m.AggregateId == orderId).ExecuteDeleteAsync();
                await orderCleanup.OrderProfiles
                    .Where(o => o.Id == orderId).ExecuteDeleteAsync();

                await using var storeCleanup = new S.Infrastructure.Persistence
                    .ApplicationDbContext(storeOptions);
                var storeMessages = await storeCleanup.OutboxMessages
                    .Where(m => m.EventType == "inventory.reserved" ||
                                m.EventType == "inventory.reservation_failed")
                    .Select(m => new { m.Id, m.Payload }).ToListAsync();
                var storeIds = storeMessages
                    .Where(m => PayloadOrderId(m.Payload) == orderId)
                    .Select(m => m.Id).ToArray();
                if (storeIds.Length > 0)
                    await storeCleanup.OutboxMessages
                        .Where(m => storeIds.Contains(m.Id))
                        .ExecuteDeleteAsync();
                await storeCleanup.ProcessedMessages
                    .Where(m => m.AggregateId == orderId).ExecuteDeleteAsync();
            }

            await using var seedCleanup = new S.Infrastructure.Persistence
                .ApplicationDbContext(storeOptions);
            await seedCleanup.SurpriseBags
                .Where(b => b.Id == bagId).ExecuteDeleteAsync();
            await seedCleanup.StoreProfiles
                .Where(s => s.Id == storeId).ExecuteDeleteAsync();
        }
    }

    private static string Connection(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"Set {name}.");

    private static T Read<T>(string payload) where T : class =>
        JsonSerializer.Deserialize<T>(payload, JsonOptions)
        ?? throw new InvalidOperationException("Invalid outbox payload.");

    private static Guid PayloadOrderId(string payload)
    {
        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;
        return root.TryGetProperty("orderId", out var id)
            ? id.GetGuid()
            : root.GetProperty("OrderId").GetGuid();
    }

    private sealed class SignalingUnitOfWork(
        P.Domain.Interfaces.IUnitOfWork inner,
        TaskCompletionSource<bool> saved)
        : P.Domain.Interfaces.IUnitOfWork
    {
        public async Task<int> SaveChangesAsync()
        {
            var result = await inner.SaveChangesAsync();
            saved.TrySetResult(true);
            return result;
        }
    }

    private sealed class FakeGateway : P.Application.Gateways.IPaymentGateway
    {
        private decimal _amount;
        public string Method => P.Domain.Constants.PaymentMethods.VnPay;
        public string GatewayRef { get; } = Guid.NewGuid().ToString("N");

        public Task<P.Application.DTOs.Gateways.CreatePaymentResult>
            CreatePaymentAsync(
                P.Application.DTOs.Gateways.CreatePaymentRequest request,
                CancellationToken cancellationToken = default)
        {
            _amount = request.Amount;
            return Task.FromResult(
                new P.Application.DTOs.Gateways.CreatePaymentResult
                {
                    PaymentMethod = Method,
                    GatewayRef = GatewayRef,
                    CheckoutUrl = "https://example.test/payment",
                    ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15)
                });
        }

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
                    GatewayRef = GatewayRef,
                    Amount = _amount,
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
