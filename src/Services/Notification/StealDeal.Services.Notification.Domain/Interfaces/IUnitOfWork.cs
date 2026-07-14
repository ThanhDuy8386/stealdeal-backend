using System;
using System.Threading.Tasks;

namespace StealDeal.Services.Notification.Domain.Interfaces
{
    public interface IUnitOfWork
    {
        Task<int> SaveChangesAsync();
    }
}
