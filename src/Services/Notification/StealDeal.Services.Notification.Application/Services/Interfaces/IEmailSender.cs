using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StealDeal.Services.Notification.Application.Services.Interfaces
{
    public interface IEmailSender
    {
        Task SendOtpAsync(
        string toEmail,
        string fullName,
        string otp,
        DateTime expiresAt,
        CancellationToken cancellationToken = default);
    }
}
