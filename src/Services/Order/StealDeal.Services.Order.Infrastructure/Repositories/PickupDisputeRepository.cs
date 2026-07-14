using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Order.Domain.Interfaces;
using StealDeal.Services.Order.Domain.Models;
using StealDeal.Services.Order.Infrastructure.Persistency;

namespace StealDeal.Services.Order.Infrastructure.Repositories
{
    public class PickupDisputeRepository : IPickupDisputeRepository
    {
        private readonly ApplicationDbContext _context;

        public PickupDisputeRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(PickupDispute dispute)
        {
            await _context.PickupDisputes.AddAsync(dispute);
        }

        public async Task<PickupDispute?> GetByIdAsync(Guid id)
        {
            return await _context.PickupDisputes
                .Include(d => d.OrderProfile)
                .FirstOrDefaultAsync(d => d.Id == id);
        }

        public async Task<PickupDispute?> GetByOrderIdAsync(Guid orderId)
        {
            return await _context.PickupDisputes
                .Include(d => d.OrderProfile)
                .FirstOrDefaultAsync(d => d.OrderId == orderId);
        }

        public async Task<IEnumerable<PickupDispute>> GetAllAsync()
        {
            return await _context.PickupDisputes
                .Include(d => d.OrderProfile)
                .ToListAsync();
        }

        public void Update(PickupDispute dispute)
        {
            _context.PickupDisputes.Update(dispute);
        }
    }
}
