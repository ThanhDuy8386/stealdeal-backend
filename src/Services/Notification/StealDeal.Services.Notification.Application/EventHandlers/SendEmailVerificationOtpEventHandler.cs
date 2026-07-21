using System;
using System.Threading;
using System.Threading.Tasks;
using StealDeal.Services.Notification.Application.DTOs.Events;
using StealDeal.Services.Notification.Application.Messaging;
using StealDeal.Services.Notification.Domain.Interfaces;
using StealDeal.Services.Notification.Domain.Models;

namespace StealDeal.Services.Notification.Application.EventHandlers
{
    public class SendEmailVerificationOtpEventHandler : IIntegrationEventHandler<SendEmailVerificationOtpEvent>
    {
        private const string ConsumerName = "EmailVerificationConsumer";

        private readonly INotificationProfileRepository _notificationRepository;
        private readonly IProcessedMessageRepository _processedMessageRepository;
        private readonly IUnitOfWork _unitOfWork;

        public SendEmailVerificationOtpEventHandler(
            INotificationProfileRepository notificationRepository,
            IProcessedMessageRepository processedMessageRepository,
            IUnitOfWork unitOfWork)
        {
            _notificationRepository = notificationRepository;
            _processedMessageRepository = processedMessageRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task HandleAsync(
            SendEmailVerificationOtpEvent @event,
            IntegrationEventContext context,
            CancellationToken cancellationToken = default)
        {
            var alreadyProcessed = await _processedMessageRepository.ExistsAsync(
                context.MessageId,
                ConsumerName);

            if (alreadyProcessed)
            {
                return;
            }

            var notification = new NotificationProfile
            {
                UserId = @event.UserId,
                Title = "Verify Email OTP",
                Body = $"Hello {@event.FullName}, your OTP is {@event.Otp}. It expires at {@event.ExpiresAt:g}.",
                Type = "EmailVerification",
                ActionUrl = null,
                ReferenceId = null,
                ReferenceType = null
            };

            var processedMessage = new ProcessedMessage
            {
                MessageId = context.MessageId,
                ConsumerName = ConsumerName,
                EventType = context.EventType,
                AggregateId = @event.UserId,
                ProcessedAt = DateTime.UtcNow
            };

            await _notificationRepository.AddAsync(notification);
            await _processedMessageRepository.AddAsync(processedMessage);

            await _unitOfWork.SaveChangesAsync();
        }
    }
}
