using System;

namespace StealDeal.Services.Notification.Application.DTOs.Events
{
    public class SendEmailVerificationOtpEvent
    {
        public Guid UserId { get; set; }
        public string Email { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string Otp { get; set; } = null!;
        public DateTime ExpiresAt { get; set; }
    }
}
