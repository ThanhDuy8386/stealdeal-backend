using System;
using System.Threading.Tasks;
using StealDeal.Services.Payment.Domain.Interfaces;
using StealDeal.Services.Payment.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Payment.Application.Exceptions;

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
            try
            {
                return await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _context.ChangeTracker.Clear();
                throw new PaymentWriteConflictException(ex);
            }
        }
    }
}
