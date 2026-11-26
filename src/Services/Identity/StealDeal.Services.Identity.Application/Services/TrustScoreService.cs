using StealDeal.Services.Identity.Application.Configuration;
using StealDeal.Services.Identity.Application.Constants;
using StealDeal.Services.Identity.Application.DTOs.Requests;
using StealDeal.Services.Identity.Application.DTOs.Responses;
using StealDeal.Services.Identity.Application.Exceptions;
using StealDeal.Services.Identity.Application.Messaging;
using StealDeal.Services.Identity.Application.Services.Interfaces;
using StealDeal.Services.Identity.Domain.Constants;
using StealDeal.Services.Identity.Domain.Interfaces.Repositories;
using StealDeal.Services.Identity.Domain.Models;

namespace StealDeal.Services.Identity.Application.Services;

public sealed class TrustScoreService : ITrustScoreService
{
    private readonly IUserTrustScoreRepository _scores;
    private readonly ITrustScoreEventRepository _events;
    private readonly IProcessedMessageRepository _messages;
    private readonly ITrustScoreUnitOfWork _unitOfWork;
    private readonly TrustScoreSettings _settings;
    private readonly TimeProvider _timeProvider;

    public TrustScoreService(
        IUserTrustScoreRepository scores,
        ITrustScoreEventRepository events,
        IProcessedMessageRepository messages,
        ITrustScoreUnitOfWork unitOfWork,
        TrustScoreSettings settings,
        TimeProvider timeProvider)
    {
        _scores = scores;
        _events = events;
        _messages = messages;
        _unitOfWork = unitOfWork;
        _settings = settings;
        _timeProvider = timeProvider;
    }

    public Task RecordPaymentSucceededAsync(
        RecordPaymentTrustScoreRequest request, IntegrationEventContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateEvent(request.UserId, request.OrderId, request.OccurredAtUtc, context,
            TrustScoreEventRoutingKeys.PaymentCompleted);

        return RecordAsync(request.UserId, request.OrderId, request.OccurredAtUtc,
            TrustScoreEventTypes.PaymentSucceeded, "Payment succeeded.", context, cancellationToken);
    }

    public Task RecordNoShowAsync(
        RecordNoShowTrustScoreRequest request, IntegrationEventContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateEvent(request.UserId, request.OrderId, request.OccurredAtUtc, context,
            TrustScoreEventRoutingKeys.OrderNoShow);
        if (request.MarkedByUserId == Guid.Empty)
            throw new BadRequestException("MarkedByUserId is required.");

        var reason = string.IsNullOrWhiteSpace(request.Reason)
            ? "Customer did not collect the order before the pickup deadline."
            : request.Reason.Trim();
        if (reason.Length > 800)
            throw new BadRequestException("NoShow reason cannot exceed 800 characters.");

        var note = $"Marked by {request.MarkedByUserId:D}. {reason}";
        return RecordAsync(request.UserId, request.OrderId, request.OccurredAtUtc,
            TrustScoreEventTypes.OrderNoShow, note, context, cancellationToken);
    }

    private Task RecordAsync(
        Guid userId, Guid orderId, DateTime occurredAtUtc, string eventType, string note,
        IntegrationEventContext context, CancellationToken cancellationToken)
    {
        return _unitOfWork.ExecuteAsync(async token =>
        {
            var processed = await _messages.GetAsync(context.MessageId, context.ConsumerName);
            if (processed != null)
            {
                if (processed.AggregateId != orderId || processed.EventType != context.EventType ||
                    await _events.GetOrderEventAsync(userId, orderId, eventType, token) == null)
                    throw new ConflictException("MessageId was already used for a different event.");
                return false;
            }

            var previous = await _events.GetOrderEventAsync(userId, orderId, eventType, token);
            if (previous == null)
            {
                if (eventType == TrustScoreEventTypes.OrderNoShow &&
                    await _events.GetOrderEventAsync(userId, orderId, TrustScoreEventTypes.PaymentSucceeded, token) == null)
                    throw new PaymentTrustScoreNotRecordedException(orderId);

                if (!await _scores.UserExistsAsync(userId, token))
                    throw new NotFoundException("User not found.");

                var requestedDelta = eventType == TrustScoreEventTypes.PaymentSucceeded
                    ? _settings.PaymentSuccessReward : -_settings.NoShowPenalty;
                var change = await _scores.ApplyScoreChangeAsync(
                    userId, requestedDelta, eventType == TrustScoreEventTypes.PaymentSucceeded,
                    _timeProvider.GetUtcNow().UtcDateTime, token);
                await _events.AddAsync(new TrustScoreEvent
                {
                    UserId = userId,
                    EventType = eventType,
                    ScoreDelta = change.ScoreAfter - change.ScoreBefore,
                    ScoreAfter = change.ScoreAfter,
                    ReferenceType = TrustScoreReferenceTypes.Order,
                    ReferenceId = orderId.ToString("D"),
                    Note = note,
                    CreatedAt = occurredAtUtc
                }, token);
            }

            // A new message describing an already-recorded order outcome is also acknowledged once.
            await _messages.AddAsync(new ProcessedMessage
            {
                MessageId = context.MessageId,
                ConsumerName = context.ConsumerName,
                EventType = context.EventType,
                AggregateId = orderId,
                ProcessedAt = _timeProvider.GetUtcNow().UtcDateTime
            });
            return previous == null;
        }, cancellationToken);
    }

    public async Task<PurchaseEligibilityResponse> GetPurchaseEligibilityAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            throw new BadRequestException("UserId is required.");

        if (!await _scores.UserExistsAsync(userId, cancellationToken))
            throw new NotFoundException("User not found.");

        var score = await _scores.GetByUserIdAsync(userId, cancellationToken);
        var points = score?.Score ?? TrustScoreLimits.InitialScore;
        var result = new PurchaseEligibilityResponse
        {
            Score = points,
            Tier = TrustScoreTiers.Normal,
            CanCreateOrder = true
        };
        if (points >= _settings.NormalPurchaseMinimumScore)
            return result;

        result.Tier = TrustScoreTiers.Limited;
        result.MaxPendingOrders = _settings.LimitedMaxPendingOrders;
        if (points >= _settings.TemporaryBlockBelowScore)
            return result;

        var latestNoShow = await _events.GetLatestNoShowBelowScoreAtAsync(
            userId, _settings.TemporaryBlockBelowScore, cancellationToken);
        if (latestNoShow.HasValue)
        {
            // SQL Server datetime2 is UTC by convention but materializes with Kind=Unspecified.
            var restrictedUntil = DateTime.SpecifyKind(latestNoShow.Value, DateTimeKind.Utc)
                .AddHours(_settings.PurchaseSuspensionHours);
            if (restrictedUntil > _timeProvider.GetUtcNow().UtcDateTime)
            {
                result.Tier = TrustScoreTiers.TemporarilyBlocked;
                result.CanCreateOrder = false;
                result.RestrictedUntilUtc = restrictedUntil;
                result.ReasonCode = TrustScoreReasonCodes.PurchaseSuspended;
                result.Message = "Tạm hạn chế đặt đơn mới do điểm tín nhiệm thấp sau khi không đến nhận hàng.";
            }
        }
        return result;
    }

    private void ValidateEvent(
        Guid userId, Guid orderId, DateTime occurredAtUtc,
        IntegrationEventContext context, string expectedRoutingKey)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (userId == Guid.Empty || orderId == Guid.Empty)
            throw new BadRequestException("UserId and OrderId are required.");
        if (occurredAtUtc == default || occurredAtUtc.Kind != DateTimeKind.Utc ||
            occurredAtUtc > _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(5))
            throw new BadRequestException("OccurredAtUtc must be a valid UTC timestamp, at most five minutes in the future.");
        if (context.MessageId == Guid.Empty || string.IsNullOrWhiteSpace(context.ConsumerName) ||
            context.ConsumerName.Length > 100 || string.IsNullOrWhiteSpace(context.EventType) ||
            context.EventType.Length > 100 || context.RoutingKey != expectedRoutingKey)
            throw new BadRequestException("Invalid trust-score integration event context.");
    }
}
