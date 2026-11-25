using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Store.Application.DTOs.Events;
using StealDeal.Services.Store.Application.EventHandlers;
using StealDeal.Services.Store.Application.Messaging;
using StealDeal.Services.Store.Domain.Interfaces;
using StealDeal.Services.Store.Domain.Models;
using StealDeal.Services.Store.Infrastructure.Persistence;
using StealDeal.Services.Store.Infrastructure.Repositories;

namespace Store.Tests;

public class InventoryDeadlockTests
{
    [Fact]
    public async Task OppositeBagLockOrder_DeadlockDoesNotLoseOrder()
    {
        var connection = Environment.GetEnvironmentVariable("STORE_TEST_DB_CONNECTION")
            ?? throw new InvalidOperationException("Set STORE_TEST_DB_CONNECTION.");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connection).Options;

        var storeId = Guid.NewGuid();
        var bagA = Guid.NewGuid();
        var bagB = Guid.NewGuid();
        var orderA = Guid.NewGuid();
        var orderB = Guid.NewGuid();
        var orderIds = new[] { orderA, orderB };
        var bagIds = new[] { bagA, bagB };
        const string consumerName = "store-deadlock-test";

        var beforeFirstReserve = new TwoPartyGate();
        var afterFirstReserve = new TwoPartyGate();

        static Guid PayloadOrderId(string payload)
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.GetProperty("orderId").GetGuid();
        }

        async Task<Exception?> DeliverAsync(Guid orderId, Guid[] bagOrder)
        {
            try
            {
                await using var db = new ApplicationDbContext(options);
                var repository = new GatedReservationRepository(
                    db, beforeFirstReserve, afterFirstReserve);
                var handler = new CreateOrderEventHandler(
                    repository,
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
                    Items = bagOrder.Select(id => new OrderItemDto
                    {
                        SurpriseBagId = id,
                        Quantity = 1
                    }).ToList()
                };

                await handler.HandleAsync(orderEvent, new IntegrationEventContext
                {
                    MessageId = messageId,
                    ConsumerName = consumerName,
                    EventType = "order.created",
                    RoutingKey = "order.created"
                });

                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        try
        {
            await using (var seed = new ApplicationDbContext(options))
            {
                seed.StoreProfiles.Add(new StoreProfile
                {
                    Id = storeId,
                    OwnerId = Guid.NewGuid(),
                    Name = "Deadlock Test Store",
                    IsActive = true,
                    IsVerify = true
                });

                foreach (var bagId in bagIds)
                {
                    seed.SurpriseBags.Add(new SurpriseBag
                    {
                        Id = bagId,
                        StoreId = storeId,
                        Name = "Deadlock Test Bag",
                        Status = "Active",
                        OriginalPrice = 100,
                        SalePrice = 50,
                        QuantityTotal = 1,
                        QuantityRemaining = 1,
                        PickupStartTime = DateTime.UtcNow.AddHours(1),
                        PickupEndTime = DateTime.UtcNow.AddHours(3),
                        ExpiryDate = DateTime.UtcNow.AddDays(1)
                    });
                }

                await seed.SaveChangesAsync();
            }

            // Both handlers pass prechecks, then reserve opposite first bags.
            var results = await Task.WhenAll(
                DeliverAsync(orderA, [bagA, bagB]),
                DeliverAsync(orderB, [bagB, bagA]));

            Assert.All(results, error => Assert.Null(error));

            await using var check = new ApplicationDbContext(options);
            var stock = await check.SurpriseBags.AsNoTracking()
                .Where(b => bagIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.QuantityRemaining);

            var candidates = await check.OutboxMessages.AsNoTracking()
                .Where(m => m.EventType == "inventory.reserved" ||
                            m.EventType == "inventory.reservation_failed")
                .Select(m => new { m.EventType, m.Payload })
                .ToListAsync();

            var messages = candidates
                .Where(m => orderIds.Contains(PayloadOrderId(m.Payload)))
                .ToList();

            var reserved = Assert.Single(messages,
                m => m.EventType == "inventory.reserved");
            var failed = Assert.Single(messages,
                m => m.EventType == "inventory.reservation_failed");

            Assert.NotEqual(
                PayloadOrderId(reserved.Payload),
                PayloadOrderId(failed.Payload));
            Assert.Equal(0, stock[bagA]);
            Assert.Equal(0, stock[bagB]);
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

    private sealed class TwoPartyGate
    {
        private int _arrivals;
        private readonly TaskCompletionSource _ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task MeetAsync()
        {
            if (Interlocked.Increment(ref _arrivals) == 2)
                _ready.TrySetResult();

            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
    }

    private sealed class GatedReservationRepository(
        ApplicationDbContext db,
        TwoPartyGate beforeFirstReserve,
        TwoPartyGate afterFirstReserve)
        : SurpriseBagRepository(db), ISurpriseBagRepository
    {
        private int _reserveCalls;

        async Task<bool> ISurpriseBagRepository.TryReserveQuantityAsync(
            Guid bagId,
            Guid storeId,
            int quantity,
            CancellationToken cancellationToken)
        {
            var firstCall = Interlocked.Increment(ref _reserveCalls) == 1;

            if (firstCall)
                await beforeFirstReserve.MeetAsync();

            var reserved = await base.TryReserveQuantityAsync(
                bagId, storeId, quantity, cancellationToken);

            if (firstCall && reserved)
                await afterFirstReserve.MeetAsync();

            return reserved;
        }
    }
}