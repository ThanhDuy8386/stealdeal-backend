using System;

namespace StealDeal.Services.Notification.Application.DTOs.Requests
{
    public class CreateNotificationRequest
    {
        public Guid UserId { get; set; }
        public string Title { get; set; } = null!;
        public string Body { get; set; } = null!;
        public string Type { get; set; } = null!;
        public string? ActionUrl { get; set; }
        public Guid? ReferenceId { get; set; }
        public string? ReferenceType { get; set; }
    }
}
