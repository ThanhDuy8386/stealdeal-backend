using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Store.Domain.Models;
using StealDeal.Services.Store.Infrastructure.Persistence;
using StealDeal.Services.Store.Infrastructure.Repositories;

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
}
