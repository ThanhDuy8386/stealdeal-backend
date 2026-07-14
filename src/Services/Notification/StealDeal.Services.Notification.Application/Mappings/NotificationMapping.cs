using System;
using StealDeal.Services.Notification.Application.DTOs.Requests;
using StealDeal.Services.Notification.Application.DTOs.Response;
using StealDeal.Services.Notification.Domain.Models;

namespace StealDeal.Services.Notification.Application.Mappings
{
    public static class NotificationMapping
    {
        public static NotificationProfile ToEntity(this CreateNotificationRequest request)
        {
            return new NotificationProfile
            {
                Id = Guid.NewGuid(),
                UserId = request.UserId,
                Title = request.Title.Trim(),
                Body = request.Body.Trim(),
                Type = request.Type.Trim(),
                ActionUrl = request.ActionUrl?.Trim(),
                ReferenceId = request.ReferenceId,
                ReferenceType = request.ReferenceType?.Trim(),
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };
        }

        public static NotificationResponse ToResponse(this NotificationProfile notification)
        {
            if (notification == null) return null!;

            return new NotificationResponse
            {
                Id = notification.Id,
                UserId = notification.UserId,
                Title = notification.Title,
                Body = notification.Body,
                Type = notification.Type,
                ActionUrl = notification.ActionUrl,
                ReferenceId = notification.ReferenceId,
                ReferenceType = notification.ReferenceType,
                IsRead = notification.IsRead,
                CreatedAt = notification.CreatedAt
            };
        }
    }
}
