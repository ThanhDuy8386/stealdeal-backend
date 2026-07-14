using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StealDeal.Services.Notification.Application.DTOs.Requests;
using StealDeal.Services.Notification.Application.DTOs.Response;
using StealDeal.Services.Notification.Application.Exceptions;
using StealDeal.Services.Notification.Application.Mappings;
using StealDeal.Services.Notification.Application.Services.Interfaces;
using StealDeal.Services.Notification.Domain.Interfaces;
using StealDeal.Services.Notification.Domain.Models;

namespace StealDeal.Services.Notification.Application.Services
{
    public class NotificationService : INotificationService
    {
        private readonly INotificationProfileRepository _notificationRepository;
        private readonly IUnitOfWork _unitOfWork;

        public NotificationService(
            INotificationProfileRepository notificationRepository,
            IUnitOfWork unitOfWork)
        {
            _notificationRepository = notificationRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<IEnumerable<NotificationResponse>> GetMyNotificationsAsync(Guid userId)
        {
            var notifications = await _notificationRepository.GetByUserIdAsync(userId);
            return notifications.Select(n => n.ToResponse());
        }

        public async Task<int> GetUnreadCountAsync(Guid userId)
        {
            return await _notificationRepository.GetUnreadCountByUserIdAsync(userId);
        }

        public async Task<NotificationResponse> MarkAsReadAsync(Guid notificationId, Guid userId)
        {
            var notification = await _notificationRepository.GetByIdAsync(notificationId);
            if (notification == null)
                throw new NotFoundException("Notification not found.");

            if (notification.UserId != userId)
                throw new ForbiddenException("You do not own this notification.");

            if (!notification.IsRead)
            {
                notification.IsRead = true;
                _notificationRepository.Update(notification);
                await _unitOfWork.SaveChangesAsync();
            }

            return notification.ToResponse();
        }

        public async Task MarkAllAsReadAsync(Guid userId)
        {
            var unreadNotifications = (await _notificationRepository.GetByUserIdAsync(userId))
                .Where(n => !n.IsRead);

            foreach (var notification in unreadNotifications)
            {
                notification.IsRead = true;
                _notificationRepository.Update(notification);
            }

            await _unitOfWork.SaveChangesAsync();
        }

        public async Task<NotificationResponse> CreateNotificationAsync(CreateNotificationRequest request)
        {
            if (request == null)
                throw new BadRequestException("Notification request cannot be null.");

            var notification = request.ToEntity();

            await _notificationRepository.AddAsync(notification);
            await _unitOfWork.SaveChangesAsync();

            return notification.ToResponse();
        }

        public async Task DeleteNotificationAsync(Guid notificationId, Guid userId)
        {
            var notification = await _notificationRepository.GetByIdAsync(notificationId);
            if (notification == null)
                throw new NotFoundException("Notification not found.");

            if (notification.UserId != userId)
                throw new ForbiddenException("You do not own this notification.");

            _notificationRepository.Delete(notification);
            await _unitOfWork.SaveChangesAsync();
        }
    }
}
