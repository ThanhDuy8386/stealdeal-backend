using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Identity.Application.DTOs.Events
{
    public class SendPasswordResetOtpEvent
    {
        public Guid UserId {  get; set; }
        public string Email { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string Otp { get; set; } = null!;
        public DateTime ExpiresAt {  get; set; }
    }
}
