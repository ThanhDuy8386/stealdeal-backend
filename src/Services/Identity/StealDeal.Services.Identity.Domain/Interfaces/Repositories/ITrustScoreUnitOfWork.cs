namespace StealDeal.Services.Identity.Domain.Interfaces.Repositories;

public interface ITrustScoreUnitOfWork
{
    // Saves score, history and processed message atomically.
    // The repository updates points/counters atomically in the database.
    // The callback only performs DB work. Message retries belong to the consumer.
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);
}
