using Microsoft.EntityFrameworkCore;

using IdentityDbContext = StealDeal.Services.Identity.Infrastructure.Persistence.ApplicationDbContext;
using NotificationDbContext = StealDeal.Services.Notification.Infrastructure.Persistence.ApplicationDbContext;
using OrderDbContext = StealDeal.Services.Order.Infrastructure.Persistency.ApplicationDbContext;
using PaymentDbContext = StealDeal.Services.Payment.Infrastructure.Persistence.ApplicationDbContext;
using StoreDbContext = StealDeal.Services.Store.Infrastructure.Persistence.ApplicationDbContext;

var sqlPassword = GetRequiredEnvironmentVariable("MSSQL_SA_PASSWORD");
var sqlHost = Environment.GetEnvironmentVariable("MSSQL_HOST") ?? "sqlserver";
var sqlPort = Environment.GetEnvironmentVariable("MSSQL_PORT") ?? "1433";

var migrations = new (string Name, Func<Task> Run)[]
{
    ("Identity", () => MigrateAsync<IdentityDbContext>(CreateConnectionString("StealDealIdentityDb"))),
    ("Notification", () => MigrateAsync<NotificationDbContext>(CreateConnectionString("StealDealNotificationDb"))),
    ("Store", () => MigrateAsync<StoreDbContext>(CreateConnectionString("StealDealStoreDb"))),
    ("Order", () => MigrateAsync<OrderDbContext>(CreateConnectionString("StealDealOrderDb"))),
    ("Payment", () => MigrateAsync<PaymentDbContext>(CreateConnectionString("StealDealPaymentDb")))
};

Console.WriteLine("Starting database migrations...");

foreach (var migration in migrations)
{
    Console.WriteLine($"Migrating {migration.Name} database...");
    await RetryAsync(migration.Run);
    Console.WriteLine($"{migration.Name} database is up to date.");
}

Console.WriteLine("All database migrations completed.");

string CreateConnectionString(string databaseName)
{
    return $"Server={sqlHost},{sqlPort};Database={databaseName};User Id=sa;Password={sqlPassword};TrustServerCertificate=True;Encrypt=False";
}

static async Task MigrateAsync<TContext>(string connectionString)
    where TContext : DbContext
{
    var options = new DbContextOptionsBuilder<TContext>()
        .UseSqlServer(connectionString)
        .Options;

    await using var dbContext = (TContext)Activator.CreateInstance(
        typeof(TContext),
        options)!;

    await dbContext.Database.MigrateAsync();
}

static async Task RetryAsync(Func<Task> operation)
{
    const int maxAttempts = 30;

    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            await operation();
            return;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            Console.WriteLine(
                $"Migration attempt {attempt} failed: {ex.Message}. Retrying in 5 seconds...");

            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }

    await operation();
}

static string GetRequiredEnvironmentVariable(string name)
{
    var value = Environment.GetEnvironmentVariable(name);

    if (string.IsNullOrWhiteSpace(value))
        throw new InvalidOperationException($"{name} environment variable is required.");

    return value;
}
