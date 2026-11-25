using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using O = StealDeal.Services.Order;
using S = StealDeal.Services.Store;
using P = StealDeal.Services.Payment;

namespace PurchaseFlow.Tests;

public class ConcurrentPurchaseTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task ConcurrentBuyers_OnlyOneOrderConfirmedForLastBag()
    {
        var orderOptions = new DbContextOptionsBuilder<
            O.Infrastructure.Persistency.ApplicationDbContext>()
            .UseSqlServer(Connection("ORDER_TEST_DB_CONNECTION")).Options;
        var storeOptions = new DbContextOptionsBuilder<
            S.Infrastructure.Persistence.ApplicationDbContext>()
            .UseSqlServer(Connection("STORE_TEST_DB_CONNECTION")).Options;
        var paymentOptions = new DbContextOptionsBuilder<
            P.Infrastructure.Persistence.ApplicationDbContext>()
            .UseSqlServer(Connection("PAYMENT_TEST_DB_CONNECTION")).Options;

        var storeId = Guid.NewGuid();
        var bagId = Guid.NewGuid();
        var orderIds = new List<Guid>();

        await using (var seed = new S.Infrastructure.Persistence
            .ApplicationDbContext(storeOptions))
        {
            seed.StoreProfiles.Add(new S.Domain.Models.StoreProfile
            {
                Id = storeId,
                OwnerId = Guid.NewGuid(),
                Name = "Last Bag Test Store",
                IsActive = true,
                IsVerify = true
            });
            seed.SurpriseBags.Add(new S.Domain.Models.SurpriseBag
            {
                Id = bagId,
                StoreId = storeId,
                Name = "Last Bag",
                Status = "Active",
                OriginalPrice = 100,
                SalePrice = 50,
                QuantityTotal = 1,
                QuantityRemaining = 1,
                PickupStartTime = DateTime.UtcNow.AddHours(1),
                PickupEndTime = DateTime.UtcNow.AddHours(3),
                ExpiryDate = DateTime.UtcNow.AddDays(1)
            });
            await seed.SaveChangesAsync();
        }

        try
        {
            await using var orderDb = new O.Infrastructure.Persistency
                .ApplicationDbContext(orderOptions);
            var orderService = new O.Application.Services.OrderService(
                new O.Infrastructure.Repositories.OrderRepository(orderDb),
                new O.Infrastructure.Repositories.UnitOfWork(orderDb),
                new O.Infrastructure.Repositories.OutboxMessageRepository(orderDb));

            async Task<(Guid Id, O.Domain.Models.OutboxMessage Message)>
                CreateOrderAsync(Guid buyerId)
            {
                var order = await orderService.CreateOrderAsync(
                    buyerId,
                    new O.Application.DTOs.Requests.CreateOrderRequest
                    {
                        StoreId = storeId,
                        StoreNameSnapshot = "Last Bag Test Store",
                        ContactNameSnapshot = "Test Buyer",
                        ContactPhoneSnapshot = "0123456789",
                        DeliveryType = "Pickup",
                        DeliveryAddress = "",
                        Items =
                        [
                            new O.Application.DTOs.Requests.CreateOrderItemRequest
                            {
                                BagId = bagId,
                                BagNameSnapshot = "Last Bag",
                                UnitPriceSnapshot = 50,
                                Quantity = 1
                            }
                        ]
                    });

                orderIds.Add(order.Id);
                var message = orderDb.OutboxMessages.Local.Single(m =>
                    m.RoutingKey == "order.created" &&
                    Read<S.Application.DTOs.Events.CreateOrderEvent>(
                        m.Payload).OrderId == order.Id);
                return (order.Id, message);
            }

            var first = await CreateOrderAsync(Guid.NewGuid());
            var second = await CreateOrderAsync(Guid.NewGuid());

            var start = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

            async Task DeliverToStoreAsync(O.Domain.Models.OutboxMessage message)
            {
                await start.Task;

                await using var db = new S.Infrastructure.Persistence
                    .ApplicationDbContext(storeOptions);
                var handler = new S.Application.EventHandlers
                    .CreateOrderEventHandler(
                        new S.Infrastructure.Repositories.SurpriseBagRepository(db),
                        new S.Infrastructure.Repositories.ProcessedMessageRepository(db),
                        new S.Infrastructure.Repositories.OutboxMessageRepository(db),
                        new S.Infrastructure.Repositories.UnitOfWork(db));

                await handler.HandleAsync(
                    Read<S.Application.DTOs.Events.CreateOrderEvent>(
                        message.Payload),
                    new S.Application.Messaging.IntegrationEventContext
                    {
                        MessageId = message.Id,
                        ConsumerName = "concurrent-purchase-store",
                        EventType = message.EventType,
                        RoutingKey = message.RoutingKey
                    });
            }

            var firstDelivery = DeliverToStoreAsync(first.Message);
            var secondDelivery = DeliverToStoreAsync(second.Message);
            start.SetResult();
            await Task.WhenAll(firstDelivery, secondDelivery);

            await using var storeDb = new S.Infrastructure.Persistence
                .ApplicationDbContext(storeOptions);
            var allStoreMessages = await storeDb.OutboxMessages.AsNoTracking()
                .Where(m => m.EventType == "inventory.reserved" ||
                            m.EventType == "inventory.reservation_failed")
                .ToListAsync();
            var relevant = allStoreMessages
                .Where(m => orderIds.Contains(PayloadOrderId(m.Payload)))
                .ToList();

            var reservedMessage = Assert.Single(relevant.Where(m =>
                m.EventType == "inventory.reserved"));
            var failedMessage = Assert.Single(relevant.Where(m =>
                m.EventType == "inventory.reservation_failed"));

            var reservedEvent =
                Read<P.Application.DTOs.Events.InventoryReservedEvent>(
                    reservedMessage.Payload);
            var failedEvent =
                Read<O.Application.DTOs.Events.InventoryReservationFailedEvent>(
                    failedMessage.Payload);

            var winnerId = reservedEvent.OrderId;
            var loserId = failedEvent.OrderId;
            Assert.NotEqual(winnerId, loserId);
            Assert.Contains(winnerId, orderIds);
            Assert.Contains(loserId, orderIds);

            var statusHandler = new O.Application.EventHandlers
                .OrderStatusEventHandler(
                    new O.Infrastructure.Repositories.OrderRepository(orderDb),
                    new O.Infrastructure.Repositories.ProcessedMessageRepository(orderDb),
                    new O.Infrastructure.Repositories.UnitOfWork(orderDb));

            await statusHandler.HandleAsync(
                failedEvent,
                new O.Application.Messaging.IntegrationEventContext
                {
                    MessageId = failedMessage.Id,
                    ConsumerName = "concurrent-purchase-order",
                    EventType = failedMessage.EventType,
                    RoutingKey = failedMessage.RoutingKey
                });

            await using var paymentDb = new P.Infrastructure.Persistence
                .ApplicationDbContext(paymentOptions);
            var gateway = new FakeGateway();
            var factory = new FakeGatewayFactory(gateway);
            var transactions =
                new P.Infrastructure.Repositories.TransactionRepository(paymentDb);
            var paymentUnitOfWork =
                new P.Infrastructure.Repositories.UnitOfWork(paymentDb);

            var reservationHandler = new P.Application.EventHandlers
                .InventoryReservedEventHandler(
                    transactions,
                    new P.Infrastructure.Repositories.ProcessedMessageRepository(paymentDb),
                    factory,
                    paymentUnitOfWork);

            await reservationHandler.HandleAsync(
                reservedEvent,
                new P.Application.Messaging.IntegrationEventContext
                {
                    MessageId = reservedMessage.Id,
                    ConsumerName = "concurrent-purchase-payment",
                    EventType = reservedMessage.EventType,
                    RoutingKey = reservedMessage.RoutingKey
                });

            var callbackService = new P.Application.Services.PaymentCallbackService(
                factory,
                transactions,
                new P.Infrastructure.Repositories.RefundRepository(paymentDb),
                new P.Infrastructure.Repositories.OutboxMessageRepository(paymentDb),
                paymentUnitOfWork);

            var callback = await callbackService.HandleVnPayIpnAsync(
                new Dictionary<string, string>());
            Assert.Equal("00", callback.RspCode);

            var completedMessage = paymentDb.OutboxMessages.Local.Single(m =>
                m.EventType == "payment.completed");
            await statusHandler.HandleAsync(
                Read<O.Application.DTOs.Events.PaymentCompletedEvent>(
                    completedMessage.Payload),
                new O.Application.Messaging.IntegrationEventContext
                {
                    MessageId = completedMessage.Id,
                    ConsumerName = "concurrent-purchase-order",
                    EventType = completedMessage.EventType,
                    RoutingKey = completedMessage.RoutingKey
                });

            await using var checkOrder = new O.Infrastructure.Persistency
                .ApplicationDbContext(orderOptions);
            await using var checkStore = new S.Infrastructure.Persistence
                .ApplicationDbContext(storeOptions);
            await using var checkPayment = new P.Infrastructure.Persistence
                .ApplicationDbContext(paymentOptions);

            Assert.Equal("Confirmed", await checkOrder.OrderProfiles
                .Where(o => o.Id == winnerId)
                .Select(o => o.Status).SingleAsync());
            Assert.Equal("InventoryReservationFailed",
                await checkOrder.OrderProfiles
                    .Where(o => o.Id == loserId)
                    .Select(o => o.Status).SingleAsync());
            Assert.Equal(0, await checkStore.SurpriseBags
                .Where(b => b.Id == bagId)
                .Select(b => b.QuantityRemaining).SingleAsync());

            var payment = Assert.Single(await checkPayment.Transactions
                .Where(t => orderIds.Contains(t.OrderId)).ToListAsync());
            Assert.Equal(winnerId, payment.OrderId);
            Assert.Equal(
                P.Domain.Constants.TransactionStatuses.Success,
                payment.Status);
        }
        finally
        {
            if (orderIds.Count > 0)
            {
                await using var paymentCleanup = new P.Infrastructure.Persistence
                    .ApplicationDbContext(paymentOptions);
                var paymentMessages = await paymentCleanup.OutboxMessages
                    .Where(m => m.EventType == "payment.completed")
                    .Select(m => new { m.Id, m.Payload }).ToListAsync();
                var paymentMessageIds = paymentMessages
                    .Where(m => orderIds.Contains(PayloadOrderId(m.Payload)))
                    .Select(m => m.Id).ToArray();
                if (paymentMessageIds.Length > 0)
                    await paymentCleanup.OutboxMessages
                        .Where(m => paymentMessageIds.Contains(m.Id))
                        .ExecuteDeleteAsync();
                await paymentCleanup.ProcessedMessages
                    .Where(m => m.AggregateId.HasValue &&
                                orderIds.Contains(m.AggregateId.Value))
                    .ExecuteDeleteAsync();
                await paymentCleanup.Transactions
                    .Where(t => orderIds.Contains(t.OrderId))
                    .ExecuteDeleteAsync();

                await using var orderCleanup = new O.Infrastructure.Persistency
                    .ApplicationDbContext(orderOptions);
                var orderMessages = await orderCleanup.OutboxMessages
                    .Where(m => m.RoutingKey == "order.created")
                    .Select(m => new { m.Id, m.Payload }).ToListAsync();
                var orderMessageIds = orderMessages
                    .Where(m => orderIds.Contains(PayloadOrderId(m.Payload)))
                    .Select(m => m.Id).ToArray();
                if (orderMessageIds.Length > 0)
                    await orderCleanup.OutboxMessages
                        .Where(m => orderMessageIds.Contains(m.Id))
                        .ExecuteDeleteAsync();
                await orderCleanup.ProcessedMessages
                    .Where(m => m.AggregateId.HasValue &&
                                orderIds.Contains(m.AggregateId.Value))
                    .ExecuteDeleteAsync();
                await orderCleanup.OrderProfiles
                    .Where(o => orderIds.Contains(o.Id))
                    .ExecuteDeleteAsync();

                await using var storeCleanup = new S.Infrastructure.Persistence
                    .ApplicationDbContext(storeOptions);
                var storeMessages = await storeCleanup.OutboxMessages
                    .Where(m => m.EventType == "inventory.reserved" ||
                                m.EventType == "inventory.reservation_failed")
                    .Select(m => new { m.Id, m.Payload }).ToListAsync();
                var storeMessageIds = storeMessages
                    .Where(m => orderIds.Contains(PayloadOrderId(m.Payload)))
                    .Select(m => m.Id).ToArray();
                if (storeMessageIds.Length > 0)
                    await storeCleanup.OutboxMessages
                        .Where(m => storeMessageIds.Contains(m.Id))
                        .ExecuteDeleteAsync();
                await storeCleanup.ProcessedMessages
                    .Where(m => m.AggregateId.HasValue &&
                                orderIds.Contains(m.AggregateId.Value))
                    .ExecuteDeleteAsync();
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
