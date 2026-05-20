namespace StealDeal.Services.Identity.Domain.Interfaces.Repositories;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync();
}