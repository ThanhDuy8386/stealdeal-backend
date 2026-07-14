using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Notification.Domain.Interfaces;
using StealDeal.Services.Notification.Domain.Models;
using StealDeal.Services.Notification.Infrastructure.Persistence;

namespace StealDeal.Services.Notification.Infrastructure.Repositories
{
    public class NotificationProfileRepository : INotificationProfileRepository
    {
        private readonly ApplicationDbContext _context;

        public NotificationProfileRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(NotificationProfile notification)
        {
            await _context.NotificationProfiles.AddAsync(notification);
        }

        public async Task<NotificationProfile?> GetByIdAsync(Guid id)
        {
            return await _context.NotificationProfiles
                .FirstOrDefaultAsync(n => n.Id == id);
        }

        public async Task<IEnumerable<NotificationProfile>> GetByUserIdAsync(Guid userId)
        {
            return await _context.NotificationProfiles
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();
        }

        public async Task<int> GetUnreadCountByUserIdAsync(Guid userId)
        {
            return await _context.NotificationProfiles
                .CountAsync(n => n.UserId == userId && !n.IsRead);
        }

        public void Update(NotificationProfile notification)
        {
            _context.NotificationProfiles.Update(notification);
        }

        public void Delete(NotificationProfile notification)
        {
            _context.NotificationProfiles.Remove(notification);
        }
    }
}
