using System;
using System.Threading.Tasks;
using StealDeal.Services.Notification.Domain.Interfaces;
using StealDeal.Services.Notification.Infrastructure.Persistence;

namespace StealDeal.Services.Notification.Infrastructure.Repositories
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly ApplicationDbContext _context;

        public UnitOfWork(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<int> SaveChangesAsync()
        {
            return await _context.SaveChangesAsync();
        }
    }
}
