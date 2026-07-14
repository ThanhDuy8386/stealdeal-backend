using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StealDeal.Services.Notification.Application.DTOs.Requests;
using StealDeal.Services.Notification.Application.DTOs.Response;

namespace StealDeal.Services.Notification.Application.Services.Interfaces
{
    public interface INotificationService
    {
        Task<IEnumerable<NotificationResponse>> GetMyNotificationsAsync(Guid userId);
        Task<int> GetUnreadCountAsync(Guid userId);
        Task<NotificationResponse> MarkAsReadAsync(Guid notificationId, Guid userId);
        Task MarkAllAsReadAsync(Guid userId);
        Task<NotificationResponse> CreateNotificationAsync(CreateNotificationRequest request);
        Task DeleteNotificationAsync(Guid notificationId, Guid userId);
    }
}
