using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Store.Application.DTOs.Events;
using StealDeal.Services.Store.Application.EventHandlers;
using StealDeal.Services.Store.Application.Messaging;
using StealDeal.Services.Store.Domain.Models;
using StealDeal.Services.Store.Infrastructure.Persistence;
using StealDeal.Services.Store.Infrastructure.Repositories;

namespace Store.Tests;

public class InventoryReleaseRequestedEventHandlerTests
{
    [Fact]
    public async Task HandlingSameReleaseEventTwice_RestoresStockOnlyOnce()
    {
        var connection = Environment.GetEnvironmentVariable("STORE_TEST_DB_CONNECTION")
            ?? throw new InvalidOperationException("Set STORE_TEST_DB_CONNECTION.");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connection).Options;

        var storeId = Guid.NewGuid();
        var bagId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        const string consumerName = "store-inventory-release-test";

        var releaseEvent = new InventoryReleaseRequestedEvent
        {
            MessageId = messageId,
            OccurredAtUtc = DateTime.UtcNow,
            OrderId = orderId,
            StoreId = storeId,
            ReasonCode = "PaymentFailed",
            Reason = "Payment failed",
            Items =
            [
                new InventoryReleaseRequestedItemDto
                {
                    SurpriseBagId = bagId,
                    Quantity = 2
                }
            ]
        };
        var context = new IntegrationEventContext
        {
            MessageId = messageId,
            ConsumerName = consumerName,
            EventType = "inventory.release_requested",
            RoutingKey = "inventory.release_requested"
        };

        async Task HandleOnceAsync()
        {
            await using var db = new ApplicationDbContext(options);
            var handler = new InventoryReleaseRequestedEventHandler(
                new SurpriseBagRepository(db),
                new ProcessedMessageRepository(db),
                new UnitOfWork(db));

            await handler.HandleAsync(releaseEvent, context);
        }

        try
        {
            await using (var seed = new ApplicationDbContext(options))
            {
                seed.StoreProfiles.Add(new StoreProfile
                {
                    Id = storeId,
                    OwnerId = Guid.NewGuid(),
                    Name = "Release Test Store",
                    IsActive = true,
                    IsVerify = true
                });
                seed.SurpriseBags.Add(new SurpriseBag
                {
                    Id = bagId,
                    StoreId = storeId,
                    Name = "Release Test Bag",
                    Status = "Active",
                    OriginalPrice = 100,
                    SalePrice = 50,
                    QuantityTotal = 5,
                    QuantityRemaining = 3,
                    PickupStartTime = DateTime.UtcNow.AddHours(1),
                    PickupEndTime = DateTime.UtcNow.AddHours(3),
                    ExpiryDate = DateTime.UtcNow.AddDays(1)
                });
                await seed.SaveChangesAsync();
            }

            await HandleOnceAsync();
            await HandleOnceAsync(); // Redelivery of the same message.

            await using var check = new ApplicationDbContext(options);
            var remaining = await check.SurpriseBags
                .Where(b => b.Id == bagId)
                .Select(b => b.QuantityRemaining)
                .SingleAsync();
            var processedCount = await check.ProcessedMessages.CountAsync(
                m => m.MessageId == messageId &&
                     m.ConsumerName == consumerName);

            Assert.Equal(5, remaining);       // 3 + 2, not 3 + 2 + 2.
            Assert.Equal(1, processedCount);
        }
        finally
        {
            await using var cleanup = new ApplicationDbContext(options);
            await cleanup.ProcessedMessages
                .Where(m => m.MessageId == messageId &&
                            m.ConsumerName == consumerName)
                .ExecuteDeleteAsync();
            await cleanup.SurpriseBags
                .Where(b => b.Id == bagId).ExecuteDeleteAsync();
            await cleanup.StoreProfiles
                .Where(s => s.Id == storeId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task SameOrderDifferentReleaseMessages_RestoresStockOnlyOnce()
    {
        var connection = Environment.GetEnvironmentVariable("STORE_TEST_DB_CONNECTION")
            ?? throw new InvalidOperationException("Set STORE_TEST_DB_CONNECTION.");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connection).Options;

        var storeId = Guid.NewGuid();
        var bagId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var firstMessageId = Guid.NewGuid();
        var secondMessageId = Guid.NewGuid();
        var messageIds = new[] { firstMessageId, secondMessageId };
        const string consumerName = "store-logical-release-test";

        async Task HandleAsync(Guid messageId)
        {
            var releaseEvent = new InventoryReleaseRequestedEvent
            {
                MessageId = messageId,
                OccurredAtUtc = DateTime.UtcNow,
                OrderId = orderId,
                StoreId = storeId,
                ReasonCode = "PaymentFailed",
                Reason = "Payment failed",
                Items =
                [
                    new InventoryReleaseRequestedItemDto
                {
                    SurpriseBagId = bagId,
                    Quantity = 2
                }
                ]
            };

            var context = new IntegrationEventContext
            {
                MessageId = messageId,
                ConsumerName = consumerName,
                EventType = "inventory.release_requested",
                RoutingKey = "inventory.release_requested"
            };

            await using var db = new ApplicationDbContext(options);
            var handler = new InventoryReleaseRequestedEventHandler(
                new SurpriseBagRepository(db),
                new ProcessedMessageRepository(db),
                new UnitOfWork(db));

            await handler.HandleAsync(releaseEvent, context);
        }

        try
        {
            await using (var seed = new ApplicationDbContext(options))
            {
                seed.StoreProfiles.Add(new StoreProfile
                {
                    Id = storeId,
                    OwnerId = Guid.NewGuid(),
                    Name = "Logical Release Test Store",
                    IsActive = true,
                    IsVerify = true
                });
                seed.SurpriseBags.Add(new SurpriseBag
                {
                    Id = bagId,
                    StoreId = storeId,
                    Name = "Logical Release Test Bag",
                    Status = "Active",
                    OriginalPrice = 100,
                    SalePrice = 50,
                    QuantityTotal = 5,
                    QuantityRemaining = 3,
                    PickupStartTime = DateTime.UtcNow.AddHours(1),
                    PickupEndTime = DateTime.UtcNow.AddHours(3),
                    ExpiryDate = DateTime.UtcNow.AddDays(1)
                });
                await seed.SaveChangesAsync();
            }

            await HandleAsync(firstMessageId);
            await HandleAsync(secondMessageId);

            await using var check = new ApplicationDbContext(options);
            var remaining = await check.SurpriseBags
                .Where(b => b.Id == bagId)
                .Select(b => b.QuantityRemaining)
                .SingleAsync();

            Assert.Equal(5, remaining); // 3 + 2, not 3 + 2 + 2.
        }
        finally
        {
            await using var cleanup = new ApplicationDbContext(options);
            await cleanup.ProcessedMessages
                .Where(m => messageIds.Contains(m.MessageId) &&
                            m.ConsumerName == consumerName)
                .ExecuteDeleteAsync();
            await cleanup.SurpriseBags
                .Where(b => b.Id == bagId).ExecuteDeleteAsync();
            await cleanup.StoreProfiles
                .Where(s => s.Id == storeId).ExecuteDeleteAsync();
        }
    }
}
