using System.Threading;
using System.Threading.Tasks;
using StealDeal.Services.Notification.Application.DTOs.Events;
using StealDeal.Services.Notification.Application.DTOs.Requests;
using StealDeal.Services.Notification.Application.Messaging;
using StealDeal.Services.Notification.Application.Services.Interfaces;

namespace StealDeal.Services.Notification.Application.EventHandlers
{
    public class SendEmailVerificationOtpEventHandler : IIntegrationEventHandler<SendEmailVerificationOtpEvent>
    {
        private readonly INotificationService _notificationService;

        public SendEmailVerificationOtpEventHandler(INotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        public async Task HandleAsync(SendEmailVerificationOtpEvent @event, CancellationToken cancellationToken = default)
        {
            var request = new CreateNotificationRequest
            {
                UserId = @event.UserId,
                Title = "Verify Email OTP",
                Body = $"Hello {@event.FullName}, your OTP is {@event.Otp}. It expires at {@event.ExpiresAt:g}.",
                Type = "EmailVerification",
                ActionUrl = null,
                ReferenceId = null,
                ReferenceType = null
            };

            await _notificationService.CreateNotificationAsync(request);
        }
    }
}
