using StealDeal.Services.Notification.Application.DTOs.Events;
using StealDeal.Services.Notification.Application.Messaging;
using StealDeal.Services.Notification.Application.Services.Interfaces;
using StealDeal.Services.Notification.Domain.Interfaces;
using StealDeal.Services.Notification.Domain.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Notification.Application.EventHandlers
{
    public class SendPasswordResetOtpEventHandler : IIntegrationEventHandler<SendPasswordResetOtpEvent>
    {
        private const string ConsumerName = "PasswordResetConsumer";

        private readonly IProcessedMessageRepository _processedMessageRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IEmailSender _emailSender;

        public SendPasswordResetOtpEventHandler(IProcessedMessageRepository processedMessageRepository, IUnitOfWork unitOfWork, IEmailSender emailSender)
        {
            _processedMessageRepository = processedMessageRepository;
            _unitOfWork = unitOfWork;
            _emailSender = emailSender;
        }

        public async Task HandleAsync(SendPasswordResetOtpEvent @event, IntegrationEventContext context, CancellationToken cancellationToken = default)
        {
            if(await _processedMessageRepository.ExistsAsync(context.MessageId, ConsumerName))
            {
                return;
            }

            await _emailSender.SendPasswordResetOtpAsync(
                @event.Email,
                @event.FullName,
                @event.Otp,
                @event.ExpiresAt,
                cancellationToken);

            await _processedMessageRepository.AddAsync(new ProcessedMessage
            {
                MessageId = context.MessageId,
                ConsumerName = ConsumerName,
                EventType = context.EventType,
                AggregateId = @event.UserId,
                ProcessedAt = DateTime.UtcNow,
            });

            await _unitOfWork.SaveChangesAsync();
        }
    }
}
