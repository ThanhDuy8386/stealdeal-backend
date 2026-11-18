using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Store.Domain.Models;
using StealDeal.Services.Store.Infrastructure.Persistence;
using StealDeal.Services.Store.Infrastructure.Repositories;
using System.Text.Json;
using StealDeal.Services.Store.Application.DTOs.Events;
using StealDeal.Services.Store.Application.EventHandlers;
using StealDeal.Services.Store.Application.Messaging;

namespace Store.Tests;

public class InventoryConcurrencyTests
{
    [Fact]
    public async Task OnlyEightOfOneHundredReservationsSucceed()
    {
        var connection = Environment.GetEnvironmentVariable("STORE_TEST_DB_CONNECTION")
            ?? throw new InvalidOperationException("Set STORE_TEST_DB_CONNECTION.");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;

        var storeId = Guid.NewGuid();
        var bagId = Guid.NewGuid();

        await using (var seed = new ApplicationDbContext(options))
        {
            seed.StoreProfiles.Add(new StoreProfile
            {
                Id = storeId,
                OwnerId = Guid.NewGuid(),
                Name = "Concurrency Test Store",
                IsActive = true,
                IsVerify = true
            });
            seed.SurpriseBags.Add(new SurpriseBag
            {
                Id = bagId,
                StoreId = storeId,
                Name = "Test Bag",
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
            var start = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var attempts = Enumerable.Range(0, 100).Select(async _ =>
            {
                await start.Task;
                await using var db = new ApplicationDbContext(options);
                var repository = new SurpriseBagRepository(db);
                return await repository.TryReserveQuantityAsync(bagId, storeId, 1);
            }).ToArray();

            start.SetResult();
            var results = await Task.WhenAll(attempts);

            await using var check = new ApplicationDbContext(options);
            var remaining = await check.SurpriseBags
                .Where(b => b.Id == bagId)
                .Select(b => b.QuantityRemaining)
                .SingleAsync();

            Assert.Equal(8, results.Count(succeeded => succeeded));
            Assert.Equal(0, remaining);
        }
        finally
        {
            await using var cleanup = new ApplicationDbContext(options);
            await cleanup.SurpriseBags
                .Where(b => b.Id == bagId).ExecuteDeleteAsync();
            await cleanup.StoreProfiles
                .Where(s => s.Id == storeId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task ConcurrentMultiBagOrders_NeverPartiallyReserveOrOversell()
    {
        const int initialStock = 8;
        const int buyerCount = 20;
        const string consumerName = "store-multibag-concurrency-test";

        var connection = Environment.GetEnvironmentVariable("STORE_TEST_DB_CONNECTION")
            ?? throw new InvalidOperationException("Set STORE_TEST_DB_CONNECTION.");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connection).Options;

        var storeId = Guid.NewGuid();
        var firstBagId = Guid.NewGuid();
        var secondBagId = Guid.NewGuid();
        var bagIds = new[] { firstBagId, secondBagId };
        var orderIds = Enumerable.Range(0, buyerCount)
            .Select(_ => Guid.NewGuid()).ToArray();

        static Guid PayloadOrderId(string payload)
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.GetProperty("orderId").GetGuid();
        }

        try
        {
            await using (var seed = new ApplicationDbContext(options))
            {
                seed.StoreProfiles.Add(new StoreProfile
                {
                    Id = storeId,
                    OwnerId = Guid.NewGuid(),
                    Name = "Multi-bag Concurrency Store",
                    IsActive = true,
                    IsVerify = true
                });

                foreach (var bagId in bagIds)
                {
                    seed.SurpriseBags.Add(new SurpriseBag
                    {
                        Id = bagId,
                        StoreId = storeId,
                        Name = "Multi-bag Test Bag",
                        Status = "Active",
                        OriginalPrice = 100,
                        SalePrice = 50,
                        QuantityTotal = initialStock,
                        QuantityRemaining = initialStock,
                        PickupStartTime = DateTime.UtcNow.AddHours(1),
                        PickupEndTime = DateTime.UtcNow.AddHours(3),
                        ExpiryDate = DateTime.UtcNow.AddDays(1)
                    });
                }

                await seed.SaveChangesAsync();
            }

            var start = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var attempts = orderIds.Select(async orderId =>
            {
                await start.Task;

                await using var db = new ApplicationDbContext(options);
                var handler = new CreateOrderEventHandler(
                    new SurpriseBagRepository(db),
                    new ProcessedMessageRepository(db),
                    new OutboxMessageRepository(db),
                    new UnitOfWork(db));

                var messageId = Guid.NewGuid();
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

                await handler.HandleAsync(orderEvent, new IntegrationEventContext
                {
                    MessageId = messageId,
                    ConsumerName = consumerName,
                    EventType = "order.created",
                    RoutingKey = "order.created"
                });
            }).ToArray();

            start.SetResult();
            await Task.WhenAll(attempts);

            await using var check = new ApplicationDbContext(options);

            var remaining = await check.SurpriseBags.AsNoTracking()
                .Where(b => bagIds.Contains(b.Id))
                .Select(b => new { b.Id, b.QuantityRemaining })
                .ToListAsync();

            var candidates = await check.OutboxMessages.AsNoTracking()
                .Where(m => m.EventType == "inventory.reserved" ||
                            m.EventType == "inventory.reservation_failed")
                .Select(m => new { m.EventType, m.Payload })
                .ToListAsync();

            var messages = candidates
                .Where(m => orderIds.Contains(PayloadOrderId(m.Payload)))
                .ToList();

            var reserved = messages
                .Where(m => m.EventType == "inventory.reserved").ToList();
            var failed = messages
                .Where(m => m.EventType == "inventory.reservation_failed").ToList();

            var reservedOrders = reserved
                .Select(m => PayloadOrderId(m.Payload)).ToHashSet();
            var failedOrders = failed
                .Select(m => PayloadOrderId(m.Payload)).ToHashSet();

            Assert.Equal(initialStock, reserved.Count);
            Assert.Equal(buyerCount - initialStock, failed.Count);
            Assert.Empty(reservedOrders.Intersect(failedOrders));
            Assert.Equal(buyerCount, reservedOrders.Count + failedOrders.Count);
            Assert.All(remaining, bag => Assert.Equal(0, bag.QuantityRemaining));

            foreach (var message in reserved)
            {
                var reservation = JsonSerializer.Deserialize<InventoryReservedEvent>(
                    message.Payload,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                Assert.NotNull(reservation);
                Assert.Equal(2, reservation.Items.Count);
                Assert.Contains(reservation.Items, item =>
                    item.SurpriseBagId == firstBagId && item.Quantity == 1);
                Assert.Contains(reservation.Items, item =>
                    item.SurpriseBagId == secondBagId && item.Quantity == 1);
            }
        }
        finally
        {
            await using var cleanup = new ApplicationDbContext(options);

            var candidates = await cleanup.OutboxMessages
                .Where(m => m.EventType == "inventory.reserved" ||
                            m.EventType == "inventory.reservation_failed")
                .Select(m => new { m.Id, m.Payload })
                .ToListAsync();

            var messageIds = candidates
                .Where(m => orderIds.Contains(PayloadOrderId(m.Payload)))
                .Select(m => m.Id).ToArray();

            if (messageIds.Length > 0)
                await cleanup.OutboxMessages
                    .Where(m => messageIds.Contains(m.Id))
                    .ExecuteDeleteAsync();

            await cleanup.ProcessedMessages
                .Where(m => m.AggregateId.HasValue &&
                            orderIds.Contains(m.AggregateId.Value))
                .ExecuteDeleteAsync();
            await cleanup.SurpriseBags
                .Where(b => bagIds.Contains(b.Id)).ExecuteDeleteAsync();
            await cleanup.StoreProfiles
                .Where(s => s.Id == storeId).ExecuteDeleteAsync();
        }
    }
}
