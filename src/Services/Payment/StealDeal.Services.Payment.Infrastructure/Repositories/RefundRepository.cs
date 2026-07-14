using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Payment.Domain.Interfaces;
using StealDeal.Services.Payment.Domain.Models;
using StealDeal.Services.Payment.Infrastructure.Persistence;

namespace StealDeal.Services.Payment.Infrastructure.Repositories
{
    public class RefundRepository : IRefundRepository
    {
        private readonly ApplicationDbContext _context;

        public RefundRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(Refund refund)
        {
            await _context.Refunds.AddAsync(refund);
        }

        public async Task<Refund?> GetByIdAsync(Guid id)
        {
            return await _context.Refunds
                .Include(r => r.Transaction)
                .FirstOrDefaultAsync(r => r.Id == id);
        }

        public async Task<Refund?> GetByOrderIdAsync(Guid orderId)
        {
            return await _context.Refunds
                .Include(r => r.Transaction)
                .FirstOrDefaultAsync(r => r.OrderId == orderId);
        }

        public async Task<IEnumerable<Refund>> GetByTransactionIdAsync(Guid transactionId)
        {
            return await _context.Refunds
                .Include(r => r.Transaction)
                .Where(r => r.TransactionId == transactionId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<Refund>> GetAllAsync()
        {
            return await _context.Refunds
                .Include(r => r.Transaction)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
        }

        public void Update(Refund refund)
        {
            _context.Refunds.Update(refund);
        }
    }
}
