using System;
using System.Threading;
using System.Threading.Tasks;
using StealDeal.Services.Notification.Application.DTOs.Events;
using StealDeal.Services.Notification.Application.Messaging;
using StealDeal.Services.Notification.Application.Services.Interfaces;
using StealDeal.Services.Notification.Domain.Interfaces;
using StealDeal.Services.Notification.Domain.Models;

namespace StealDeal.Services.Notification.Application.EventHandlers
{
    public class SendEmailVerificationOtpEventHandler : IIntegrationEventHandler<SendEmailVerificationOtpEvent>
    {
        private const string ConsumerName = "EmailVerificationConsumer";

        private readonly IProcessedMessageRepository _processedMessageRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IEmailSender _emailSender;

        public SendEmailVerificationOtpEventHandler(
            IProcessedMessageRepository processedMessageRepository,
            IUnitOfWork unitOfWork,
            IEmailSender emailSender)
        {
            _processedMessageRepository = processedMessageRepository;
            _unitOfWork = unitOfWork;
            _emailSender = emailSender;
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

            // await _notificationRepository.AddAsync(notification);
            await _emailSender.SendOtpAsync(
                @event.Email,
                @event.FullName,
                @event.Otp,
                @event.ExpiresAt,
                cancellationToken);


            await _processedMessageRepository.AddAsync(processedMessage);
            await _unitOfWork.SaveChangesAsync();
        }
    }
}
