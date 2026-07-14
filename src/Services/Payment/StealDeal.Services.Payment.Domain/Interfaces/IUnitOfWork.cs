using System;
using System.Threading.Tasks;

namespace StealDeal.Services.Payment.Domain.Interfaces
{
    public interface IUnitOfWork
    {
        Task<int> SaveChangesAsync();
    }
}
