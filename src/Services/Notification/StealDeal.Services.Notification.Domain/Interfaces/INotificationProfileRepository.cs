using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StealDeal.Services.Notification.Domain.Models;

namespace StealDeal.Services.Notification.Domain.Interfaces
{
    public interface INotificationProfileRepository
    {
        Task AddAsync(NotificationProfile notification);
        Task<NotificationProfile?> GetByIdAsync(Guid id);
        Task<IEnumerable<NotificationProfile>> GetByUserIdAsync(Guid userId);
        Task<int> GetUnreadCountByUserIdAsync(Guid userId);
        void Update(NotificationProfile notification);
        void Delete(NotificationProfile notification);
    }
}
