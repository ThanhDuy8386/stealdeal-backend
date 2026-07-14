using System;
using System.Threading.Tasks;
using StealDeal.Services.Payment.Domain.Interfaces;
using StealDeal.Services.Payment.Infrastructure.Persistence;

namespace StealDeal.Services.Payment.Infrastructure.Repositories
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
