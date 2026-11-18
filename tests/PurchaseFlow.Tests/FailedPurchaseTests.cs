using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using O = StealDeal.Services.Order;
using S = StealDeal.Services.Store;
using P = StealDeal.Services.Payment;

namespace PurchaseFlow.Tests;

public class FailedPurchaseTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task FailedPayment_RestoresStockAndMarksOrderFailed()
    {
        var orderConnection = Environment.GetEnvironmentVariable(
            "ORDER_TEST_DB_CONNECTION")
            ?? throw new InvalidOperationException("Set ORDER_TEST_DB_CONNECTION.");
        var storeConnection = Environment.GetEnvironmentVariable(
            "STORE_TEST_DB_CONNECTION")
            ?? throw new InvalidOperationException("Set STORE_TEST_DB_CONNECTION.");
        var paymentConnection = Environment.GetEnvironmentVariable(
            "PAYMENT_TEST_DB_CONNECTION")
            ?? throw new InvalidOperationException("Set PAYMENT_TEST_DB_CONNECTION.");

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
        var orderMessageId = Guid.Empty;
        var storeMessageId = Guid.Empty;
        var failedMessageId = Guid.Empty;
        var releaseMessageId = Guid.Empty;

        await using (var seed = new S.Infrastructure.Persistence
            .ApplicationDbContext(storeOptions))
        {
            seed.StoreProfiles.Add(new S.Domain.Models.StoreProfile
            {
                Id = storeId,
                OwnerId = Guid.NewGuid(),
                Name = "Failed Purchase Test Store",
                IsActive = true,
                IsVerify = true
            });
            seed.SurpriseBags.Add(new S.Domain.Models.SurpriseBag
            {
                Id = bagId,
                StoreId = storeId,
                Name = "Failed Purchase Test Bag",
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
            await using var orderDb = new O.Infrastructure.Persistency
                .ApplicationDbContext(orderOptions);
            await using var storeDb = new S.Infrastructure.Persistence
                .ApplicationDbContext(storeOptions);
            await using var paymentDb = new P.Infrastructure.Persistence
                .ApplicationDbContext(paymentOptions);

            var orderService = new O.Application.Services.OrderService(
                new O.Infrastructure.Repositories.OrderRepository(orderDb),
                new O.Infrastructure.Repositories.UnitOfWork(orderDb),
                new O.Infrastructure.Repositories.OutboxMessageRepository(orderDb));

            var order = await orderService.CreateOrderAsync(
                buyerId,
                new O.Application.DTOs.Requests.CreateOrderRequest
                {
                    StoreId = storeId,
                    StoreNameSnapshot = "Failed Purchase Test Store",
                    ContactNameSnapshot = "Test Buyer",
                    ContactPhoneSnapshot = "0123456789",
                    DeliveryType = "Pickup",
                    DeliveryAddress = "",
                    Items =
                    [
                        new O.Application.DTOs.Requests.CreateOrderItemRequest
                        {
                            BagId = bagId,
                            BagNameSnapshot = "Failed Purchase Test Bag",
                            UnitPriceSnapshot = 50,
                            Quantity = 2
                        }
                    ]
                });
            orderId = order.Id;

            var orderMessage = orderDb.OutboxMessages.Local.Single(
                m => m.RoutingKey == "order.created");
            orderMessageId = orderMessage.Id;

            var storeHandler = new S.Application.EventHandlers
                .CreateOrderEventHandler(
                    new S.Infrastructure.Repositories.SurpriseBagRepository(storeDb),
                    new S.Infrastructure.Repositories.ProcessedMessageRepository(storeDb),
                    new S.Infrastructure.Repositories.OutboxMessageRepository(storeDb),
                    new S.Infrastructure.Repositories.UnitOfWork(storeDb));

            await storeHandler.HandleAsync(
                Read<S.Application.DTOs.Events.CreateOrderEvent>(
                    orderMessage.Payload),
                new S.Application.Messaging.IntegrationEventContext
                {
                    MessageId = orderMessage.Id,
                    ConsumerName = "failed-purchase-store",
                    EventType = orderMessage.EventType,
                    RoutingKey = orderMessage.RoutingKey
                });

            var storeMessage = storeDb.OutboxMessages.Local.Single(
                m => m.EventType == "inventory.reserved");
            storeMessageId = storeMessage.Id;

            await using (var reservedCheck = new S.Infrastructure.Persistence
                .ApplicationDbContext(storeOptions))
            {
                Assert.Equal(6, await reservedCheck.SurpriseBags
                    .Where(x => x.Id == bagId)
                    .Select(x => x.QuantityRemaining).SingleAsync());
            }

            var gateway = new FakeFailedGateway();
            var factory = new FakeGatewayFactory(gateway);
            var transactionRepository =
                new P.Infrastructure.Repositories.TransactionRepository(paymentDb);
            var paymentUnitOfWork =
                new P.Infrastructure.Repositories.UnitOfWork(paymentDb);

            var reservationHandler = new P.Application.EventHandlers
                .InventoryReservedEventHandler(
                    transactionRepository,
                    new P.Infrastructure.Repositories.ProcessedMessageRepository(paymentDb),
                    factory,
                    paymentUnitOfWork);

            await reservationHandler.HandleAsync(
                Read<P.Application.DTOs.Events.InventoryReservedEvent>(
                    storeMessage.Payload),
                new P.Application.Messaging.IntegrationEventContext
                {
                    MessageId = storeMessage.Id,
                    ConsumerName = "failed-purchase-payment",
                    EventType = storeMessage.EventType,
                    RoutingKey = storeMessage.RoutingKey
                });

            var callbackService = new P.Application.Services.PaymentCallbackService(
                factory,
                transactionRepository,
                new P.Infrastructure.Repositories.RefundRepository(paymentDb),
                new P.Infrastructure.Repositories.OutboxMessageRepository(paymentDb),
                paymentUnitOfWork);

            var callback = await callbackService.HandleVnPayIpnAsync(
                new Dictionary<string, string>());
            Assert.Equal("00", callback.RspCode);

            var failedMessage = paymentDb.OutboxMessages.Local.Single(
                m => m.EventType == "payment.failed");
            var releaseMessage = paymentDb.OutboxMessages.Local.Single(
                m => m.EventType == "inventory.release_requested");
            failedMessageId = failedMessage.Id;
            releaseMessageId = releaseMessage.Id;
            Assert.Equal(2, paymentDb.OutboxMessages.Local.Count);

            var statusHandler = new O.Application.EventHandlers
                .OrderStatusEventHandler(
                    new O.Infrastructure.Repositories.OrderRepository(orderDb),
                    new O.Infrastructure.Repositories.ProcessedMessageRepository(orderDb),
                    new O.Infrastructure.Repositories.UnitOfWork(orderDb));

            await statusHandler.HandleAsync(
                Read<O.Application.DTOs.Events.PaymentFailedEvent>(
                    failedMessage.Payload),
                new O.Application.Messaging.IntegrationEventContext
                {
                    MessageId = failedMessage.Id,
                    ConsumerName = "failed-purchase-order",
                    EventType = failedMessage.EventType,
                    RoutingKey = failedMessage.RoutingKey
                });

            var releaseHandler = new S.Application.EventHandlers
                .InventoryReleaseRequestedEventHandler(
                    new S.Infrastructure.Repositories.SurpriseBagRepository(storeDb),
                    new S.Infrastructure.Repositories.ProcessedMessageRepository(storeDb),
                    new S.Infrastructure.Repositories.UnitOfWork(storeDb));

            await releaseHandler.HandleAsync(
                Read<S.Application.DTOs.Events.InventoryReleaseRequestedEvent>(
                    releaseMessage.Payload),
                new S.Application.Messaging.IntegrationEventContext
                {
                    MessageId = releaseMessage.Id,
                    ConsumerName = "failed-purchase-store-release",
                    EventType = releaseMessage.EventType,
                    RoutingKey = releaseMessage.RoutingKey
                });

            await using var checkOrder = new O.Infrastructure.Persistency
                .ApplicationDbContext(orderOptions);
            await using var checkStore = new S.Infrastructure.Persistence
                .ApplicationDbContext(storeOptions);
            await using var checkPayment = new P.Infrastructure.Persistence
                .ApplicationDbContext(paymentOptions);

            Assert.Equal("PaymentFailed", await checkOrder.OrderProfiles
                .Where(x => x.Id == orderId)
                .Select(x => x.Status).SingleAsync());
            Assert.Equal(8, await checkStore.SurpriseBags
                .Where(x => x.Id == bagId)
                .Select(x => x.QuantityRemaining).SingleAsync());

            var payment = Assert.Single(await checkPayment.Transactions
                .Where(x => x.OrderId == orderId).ToListAsync());
            Assert.Equal(P.Domain.Constants.TransactionStatuses.Failed,
                payment.Status);
        }
        finally
        {
            if (orderId != Guid.Empty)
            {
                await using var paymentCleanup = new P.Infrastructure.Persistence
                    .ApplicationDbContext(paymentOptions);
                var paymentMessageIds = new[]
                {
                    failedMessageId, releaseMessageId
                }.Where(id => id != Guid.Empty).ToArray();

                if (paymentMessageIds.Length > 0)
                    await paymentCleanup.OutboxMessages
                        .Where(x => paymentMessageIds.Contains(x.Id))
                        .ExecuteDeleteAsync();
                await paymentCleanup.ProcessedMessages
                    .Where(x => x.AggregateId == orderId).ExecuteDeleteAsync();
                await paymentCleanup.Transactions
                    .Where(x => x.OrderId == orderId).ExecuteDeleteAsync();

                await using var orderCleanup = new O.Infrastructure.Persistency
                    .ApplicationDbContext(orderOptions);
                if (orderMessageId != Guid.Empty)
                    await orderCleanup.OutboxMessages
                        .Where(x => x.Id == orderMessageId).ExecuteDeleteAsync();
                await orderCleanup.ProcessedMessages
                    .Where(x => x.AggregateId == orderId).ExecuteDeleteAsync();
                await orderCleanup.OrderProfiles
                    .Where(x => x.Id == orderId).ExecuteDeleteAsync();
            }

            await using var storeCleanup = new S.Infrastructure.Persistence
                .ApplicationDbContext(storeOptions);
            if (storeMessageId != Guid.Empty)
                await storeCleanup.OutboxMessages
                    .Where(x => x.Id == storeMessageId).ExecuteDeleteAsync();
            if (orderId != Guid.Empty)
                await storeCleanup.ProcessedMessages
                    .Where(x => x.AggregateId == orderId).ExecuteDeleteAsync();
            await storeCleanup.SurpriseBags
                .Where(x => x.Id == bagId).ExecuteDeleteAsync();
            await storeCleanup.StoreProfiles
                .Where(x => x.Id == storeId).ExecuteDeleteAsync();
        }
    }

    private static T Read<T>(string payload) where T : class
        => JsonSerializer.Deserialize<T>(payload, JsonOptions)
           ?? throw new InvalidOperationException("Invalid outbox payload.");

    private sealed class FakeFailedGateway : P.Application.Gateways.IPaymentGateway
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
                    IsSuccess = false,
                    PaymentMethod = Method,
                    GatewayRef = GatewayRef,
                    Amount = _amount,
                    ReasonCode = "Declined",
                    Reason = "Payment declined"
                });
    }

    private sealed class FakeGatewayFactory(FakeFailedGateway gateway)
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
