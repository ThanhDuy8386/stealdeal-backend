using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Store.Application.DTOs.Events;
using StealDeal.Services.Store.Application.EventHandlers;
using StealDeal.Services.Store.Application.Messaging;
using StealDeal.Services.Store.Domain.Models;
using StealDeal.Services.Store.Infrastructure.Persistence;
using StealDeal.Services.Store.Infrastructure.Repositories;
using StealDeal.Services.Store.Domain.Interfaces;

namespace Store.Tests;

public class CreateOrderEventHandlerTests
{
    [Fact]
    public async Task HandlingSameOrderEventTwice_ReservesStockOnlyOnce()
    {
        var connection = Environment.GetEnvironmentVariable("STORE_TEST_DB_CONNECTION")
            ?? throw new InvalidOperationException("Set STORE_TEST_DB_CONNECTION.");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connection).Options;

        var storeId = Guid.NewGuid();
        var bagId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        const string consumerName = "store-create-order-test";

        var orderEvent = new CreateOrderEvent
        {
            OrderId = orderId,
            UserId = Guid.NewGuid(),
            StoreId = storeId,
            TotalAmount = 100,
            CreatedAt = DateTime.UtcNow,
            Items = [new OrderItemDto { SurpriseBagId = bagId, Quantity = 2 }]
        };
        var context = new IntegrationEventContext
        {
            MessageId = messageId,
            ConsumerName = consumerName,
            EventType = "order.created",
            RoutingKey = "order.created"
        };

        async Task HandleOnceAsync()
        {
            await using var db = new ApplicationDbContext(options);
            var handler = new CreateOrderEventHandler(
                new SurpriseBagRepository(db),
                new ProcessedMessageRepository(db),
                new OutboxMessageRepository(db),
                new UnitOfWork(db));
            await handler.HandleAsync(orderEvent, context);
        }

        async Task<List<Guid>> FindOutboxIdsAsync(ApplicationDbContext db)
        {
            var messages = await db.OutboxMessages.AsNoTracking()
                .Where(m => m.EventType == "inventory.reserved")
                .Select(m => new { m.Id, m.Payload })
                .ToListAsync();

            return messages
                .Where(m => JsonSerializer.Deserialize<InventoryReservedEvent>(
                    m.Payload,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                )?.OrderId == orderId)
                .Select(m => m.Id)
                .ToList();
        }

        try
        {
            await using (var db = new ApplicationDbContext(options))
            {
                db.StoreProfiles.Add(new StoreProfile
                {
                    Id = storeId,
                    OwnerId = Guid.NewGuid(),
                    Name = "Idempotency Test Store",
                    IsActive = true,
                    IsVerify = true
                });
                db.SurpriseBags.Add(new SurpriseBag
                {
                    Id = bagId,
                    StoreId = storeId,
                    Name = "Idempotency Test Bag",
                    Status = "Active",
                    OriginalPrice = 100,
                    SalePrice = 50,
                    QuantityTotal = 8,
                    QuantityRemaining = 8,
                    PickupStartTime = DateTime.UtcNow.AddHours(1),
                    PickupEndTime = DateTime.UtcNow.AddHours(3),
                    ExpiryDate = DateTime.UtcNow.AddDays(1)
                });
                await db.SaveChangesAsync();
            }

            await HandleOnceAsync();
            await HandleOnceAsync(); // Same order message is delivered twice.

            await using var check = new ApplicationDbContext(options);
            var remaining = await check.SurpriseBags
                .Where(b => b.Id == bagId)
                .Select(b => b.QuantityRemaining)
                .SingleAsync();
            var processedCount = await check.ProcessedMessages.CountAsync(
                m => m.MessageId == messageId && m.ConsumerName == consumerName);
            var outboxIds = await FindOutboxIdsAsync(check);

            Assert.Equal(6, remaining);
            Assert.Equal(1, processedCount);
            Assert.Single(outboxIds);
        }
        finally
        {
            await using var cleanup = new ApplicationDbContext(options);
            var outboxIds = await FindOutboxIdsAsync(cleanup);
            await cleanup.OutboxMessages
                .Where(m => outboxIds.Contains(m.Id)).ExecuteDeleteAsync();
            await cleanup.ProcessedMessages
                .Where(m => m.MessageId == messageId && m.ConsumerName == consumerName)
                .ExecuteDeleteAsync();
            await cleanup.SurpriseBags
                .Where(b => b.Id == bagId).ExecuteDeleteAsync();
            await cleanup.StoreProfiles
                .Where(s => s.Id == storeId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task InsufficientStockInOneBag_DoesNotReserveAnyBag()
    {
        var connection = Environment.GetEnvironmentVariable("STORE_TEST_DB_CONNECTION")
            ?? throw new InvalidOperationException("Set STORE_TEST_DB_CONNECTION.");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connection).Options;

        var storeId = Guid.NewGuid();
        var firstBagId = Guid.NewGuid();
        var emptyBagId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        const string consumerName = "store-create-order-test";

        var orderEvent = new CreateOrderEvent
        {
            OrderId = orderId,
            UserId = Guid.NewGuid(),
            StoreId = storeId,
            CreatedAt = DateTime.UtcNow,
            TotalAmount = 100,
            Items =
            [
                new OrderItemDto { SurpriseBagId = firstBagId, Quantity = 1 },
            new OrderItemDto { SurpriseBagId = emptyBagId, Quantity = 1 }
            ]
        };
        var context = new IntegrationEventContext
        {
            MessageId = messageId,
            ConsumerName = consumerName,
            EventType = "order.created",
            RoutingKey = "order.created"
        };

        SurpriseBag CreateBag(Guid id, string name, int remaining) => new()
        {
            Id = id,
            StoreId = storeId,
            Name = name,
            Status = "Active",
            OriginalPrice = 100,
            SalePrice = 50,
            QuantityTotal = 5,
            QuantityRemaining = remaining,
            PickupStartTime = DateTime.UtcNow.AddHours(1),
            PickupEndTime = DateTime.UtcNow.AddHours(3),
            ExpiryDate = DateTime.UtcNow.AddDays(1)
        };

        async Task<List<OutboxMessage>> FindOrderMessagesAsync(ApplicationDbContext db)
        {
            var candidates = await db.OutboxMessages.AsNoTracking()
                .Where(m => m.EventType == "inventory.reserved"
                         || m.EventType == "inventory.reservation_failed")
                .ToListAsync();

            return candidates.Where(m =>
            {
                using var payload = JsonDocument.Parse(m.Payload);
                return payload.RootElement.GetProperty("orderId").GetGuid() == orderId;
            }).ToList();
        }

        try
        {
            await using (var seed = new ApplicationDbContext(options))
            {
                seed.StoreProfiles.Add(new StoreProfile
                {
                    Id = storeId,
                    OwnerId = Guid.NewGuid(),
                    Name = "Insufficient Stock Test Store",
                    IsActive = true,
                    IsVerify = true
                });
                //Create 2 bags with with one has 5 in stock and the other has 0. When order one 
                //from each of them, the order should be failed.
                seed.SurpriseBags.Add(CreateBag(firstBagId, "Available Bag", 5));
                seed.SurpriseBags.Add(CreateBag(emptyBagId, "Empty Bag", 0));
                await seed.SaveChangesAsync();
            }

            await using (var db = new ApplicationDbContext(options))
            {
                var handler = new CreateOrderEventHandler(
                    new SurpriseBagRepository(db),
                    new ProcessedMessageRepository(db),
                    new OutboxMessageRepository(db),
                    new UnitOfWork(db));

                await handler.HandleAsync(orderEvent, context);
            }

            await using var check = new ApplicationDbContext(options);
            var stock = await check.SurpriseBags
                .Where(b => b.StoreId == storeId)
                .ToDictionaryAsync(b => b.Id, b => b.QuantityRemaining);
            var processedCount = await check.ProcessedMessages.CountAsync(
                m => m.MessageId == messageId && m.ConsumerName == consumerName);
            var messages = await FindOrderMessagesAsync(check);

            Assert.Equal(5, stock[firstBagId]);
            Assert.Equal(0, stock[emptyBagId]);
            Assert.Equal(1, processedCount);
            Assert.DoesNotContain(messages, m => m.EventType == "inventory.reserved");

            var failedMessage = Assert.Single(messages, m => m.EventType == "inventory.reservation_failed");
            var failure = JsonSerializer.Deserialize<InventoryReservationFailedEvent>(
                failedMessage.Payload,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.Equal("InsufficientStock", failure?.ReasonCode);
        }
        finally
        {
            await using var cleanup = new ApplicationDbContext(options);
            var messageIds = (await FindOrderMessagesAsync(cleanup))
                .Select(m => m.Id).ToList();

            await cleanup.OutboxMessages
                .Where(m => messageIds.Contains(m.Id)).ExecuteDeleteAsync();
            await cleanup.ProcessedMessages
                .Where(m => m.MessageId == messageId && m.ConsumerName == consumerName)
                .ExecuteDeleteAsync();
            await cleanup.SurpriseBags
                .Where(b => b.StoreId == storeId).ExecuteDeleteAsync();
            await cleanup.StoreProfiles
                .Where(s => s.Id == storeId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task SecondReservationFails_RollsBackFirstReservation()
    {
        var connection = Environment.GetEnvironmentVariable("STORE_TEST_DB_CONNECTION")
            ?? throw new InvalidOperationException("Set STORE_TEST_DB_CONNECTION.");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connection).Options;

        var storeId = Guid.NewGuid();
        var firstBagId = Guid.NewGuid();
        var secondBagId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        const string consumerName = "store-create-order-test";

        var orderEvent = new CreateOrderEvent
        {
            OrderId = orderId,
            UserId = Guid.NewGuid(),
            StoreId = storeId,
            TotalAmount = 100,
            CreatedAt = DateTime.UtcNow,
            Items =
            [
                new OrderItemDto { SurpriseBagId = firstBagId, Quantity = 1 },
            new OrderItemDto { SurpriseBagId = secondBagId, Quantity = 1 }
            ]
        };
        var context = new IntegrationEventContext
        {
            MessageId = messageId,
            ConsumerName = consumerName,
            EventType = "order.created",
            RoutingKey = "order.created"
        };

        SurpriseBag CreateBag(Guid id, string name) => new()
        {
            Id = id,
            StoreId = storeId,
            Name = name,
            Status = "Active",
            OriginalPrice = 100,
            SalePrice = 50,
            QuantityTotal = 5,
            QuantityRemaining = 5,
            PickupStartTime = DateTime.UtcNow.AddHours(1),
            PickupEndTime = DateTime.UtcNow.AddHours(3),
            ExpiryDate = DateTime.UtcNow.AddDays(1)
        };

        async Task<List<OutboxMessage>> FindOrderMessagesAsync(ApplicationDbContext db)
        {
            var candidates = await db.OutboxMessages.AsNoTracking()
                .Where(m => m.EventType == "inventory.reserved"
                         || m.EventType == "inventory.reservation_failed")
                .ToListAsync();

            return candidates.Where(m =>
            {
                using var payload = JsonDocument.Parse(m.Payload);
                return payload.RootElement.GetProperty("orderId").GetGuid() == orderId;
            }).ToList();
        }

        try
        {
            await using (var seed = new ApplicationDbContext(options))
            {
                seed.StoreProfiles.Add(new StoreProfile
                {
                    Id = storeId,
                    OwnerId = Guid.NewGuid(),
                    Name = "Rollback Test Store",
                    IsActive = true,
                    IsVerify = true
                });
                seed.SurpriseBags.Add(CreateBag(firstBagId, "First Bag"));
                seed.SurpriseBags.Add(CreateBag(secondBagId, "Second Bag"));
                await seed.SaveChangesAsync();
            }

            await using (var db = new ApplicationDbContext(options))
            {
                var repository = new FailSecondReservationRepository(db);
                var handler = new CreateOrderEventHandler(
                    repository,
                    new ProcessedMessageRepository(db),
                    new OutboxMessageRepository(db),
                    new UnitOfWork(db));

                await handler.HandleAsync(orderEvent, context);

                Assert.Equal(2, repository.ReserveCalls);
                Assert.True(repository.FirstReservationSucceeded);
            }

            await using var check = new ApplicationDbContext(options);
            var stock = await check.SurpriseBags
                .Where(b => b.StoreId == storeId)
                .ToDictionaryAsync(b => b.Id, b => b.QuantityRemaining);
            var processedCount = await check.ProcessedMessages.CountAsync(
                m => m.MessageId == messageId && m.ConsumerName == consumerName);
            var messages = await FindOrderMessagesAsync(check);

            Assert.Equal(5, stock[firstBagId]);  // First update was rolled back.
            Assert.Equal(5, stock[secondBagId]);
            Assert.Equal(1, processedCount);
            Assert.DoesNotContain(messages, m => m.EventType == "inventory.reserved");

            var failed = Assert.Single(
                messages, m => m.EventType == "inventory.reservation_failed");
            using var failurePayload = JsonDocument.Parse(failed.Payload);
            Assert.Equal(
                "InsufficientStock",
                failurePayload.RootElement.GetProperty("reasonCode").GetString());
        }
        finally
        {
            await using var cleanup = new ApplicationDbContext(options);
            var outboxIds = (await FindOrderMessagesAsync(cleanup))
                .Select(m => m.Id).ToList();

            await cleanup.OutboxMessages
                .Where(m => outboxIds.Contains(m.Id)).ExecuteDeleteAsync();
            await cleanup.ProcessedMessages
                .Where(m => m.MessageId == messageId && m.ConsumerName == consumerName)
                .ExecuteDeleteAsync();
            await cleanup.SurpriseBags
                .Where(b => b.StoreId == storeId).ExecuteDeleteAsync();
            await cleanup.StoreProfiles
                .Where(s => s.Id == storeId).ExecuteDeleteAsync();
        }
    }

    private sealed class FailSecondReservationRepository
        : SurpriseBagRepository, ISurpriseBagRepository
    {
        public FailSecondReservationRepository(ApplicationDbContext db) : base(db) { }

        public int ReserveCalls { get; private set; }
        public bool FirstReservationSucceeded { get; private set; }

        async Task<bool> ISurpriseBagRepository.TryReserveQuantityAsync(
            Guid surpriseBagId,
            Guid storeId,
            int quantity,
            CancellationToken cancellationToken)
        {
            ReserveCalls++;

            if (ReserveCalls == 2)
                return false;

            FirstReservationSucceeded = await base.TryReserveQuantityAsync(
                surpriseBagId, storeId, quantity, cancellationToken);
            return FirstReservationSucceeded;
        }
    }
}
