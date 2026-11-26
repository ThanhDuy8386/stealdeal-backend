using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using StealDeal.Services.Order.Application.DTOs.Events;
using StealDeal.Services.Order.Application.DTOs.Requests;
using StealDeal.Services.Order.Application.DTOs.Response;
using StealDeal.Services.Order.Application.Exceptions;
using StealDeal.Services.Order.Application.Mappings;
using StealDeal.Services.Order.Application.Services.Interfaces;
using StealDeal.Services.Order.Domain.Interfaces;
using StealDeal.Services.Order.Domain.Models;
using StealDeal.Services.Order.Domain.Constants;
using StealDeal.Services.Order.Domain.Policies;
using StealDeal.Services.Order.Application.Constants;

namespace StealDeal.Services.Order.Application.Services
{
    public class OrderService : IOrderService
    {
        private static readonly JsonSerializerOptions EventJsonOptions = new(JsonSerializerDefaults.Web);
        private readonly IOrderRepository _orderRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IOutboxMessageRepository _outboxMessageRepository;
        private readonly IStoreCatalogClient _storeCatalogClient;
        private readonly IIdentityClient _identityClient;

        public OrderService(
            IOrderRepository orderRepository,
            IUnitOfWork unitOfWork,
            IOutboxMessageRepository outboxMessageRepository,
            IStoreCatalogClient storeCatalogClient,
            IIdentityClient identityClient)
        {
            _orderRepository = orderRepository;
            _unitOfWork = unitOfWork;
            _outboxMessageRepository = outboxMessageRepository;
            _storeCatalogClient = storeCatalogClient;
            _identityClient = identityClient;
        }

        public async Task<OrderResponse> CreateOrderAsync(Guid userId, string accessToken, CreateOrderRequest request,
            CancellationToken cancellationToken = default)
        {
            if (userId == Guid.Empty)
                throw new UnauthorizedException("User is not authenticated.");
            var eligibility = await _identityClient.GetPurchaseEligibilityAsync(accessToken, cancellationToken);
            if (!eligibility.CanCreateOrder)
                throw new PurchaseRestrictionException(eligibility.Message!, OrderReasonCodes.TrustScorePurchaseSuspended, eligibility);

            var order = await BuildValidatedOrderAsync(userId, request, cancellationToken);

            var payload = JsonSerializer.Serialize(new CreateOrderEvent
            {
                OrderId = order.Id,
                UserId = order.UserId,
                StoreId = order.StoreId,
                TotalAmount = order.TotalAmount,
                CreatedAt = order.CreatedAt,
                Items = order.Items.Select(
                    item => new OrderItemDto
                    {
                        SurpriseBagId = item.BagId,
                        Quantity = item.Quantity
                    }
                ).ToList()
            });

            await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                var pendingCount = await _orderRepository.CountPendingForUpdateAsync(userId, cancellationToken);
                if (eligibility.MaxPendingOrders is int maximum && pendingCount >= maximum)
                    throw new PurchaseRestrictionException(
                        $"Bạn chỉ được có tối đa {maximum} đơn đang chờ thanh toán. Hãy thanh toán hoặc hủy đơn hiện tại trước khi đặt thêm.",
                        OrderReasonCodes.PendingOrderLimitReached, eligibility);

                await _outboxMessageRepository.AddAsync(new OutboxMessage
                {
                    ExchangeName = "stealdeal.events",
                    ExchangeType = "topic",
                    RoutingKey = OrderEventTypes.OrderCreated,
                    EventType = "OrderCreatedEvent",
                    Payload = payload,
                    Status = "Pending"
                });
                await _orderRepository.AddAsync(order);
            }, cancellationToken);

            return order.ToResponse();
        }

        public async Task<OrderResponse> GetOrderByIdAsync(Guid orderId, Guid userId, IEnumerable<string> roles,
            CancellationToken cancellationToken = default)
        {
            var order = await _orderRepository.GetByIdAsync(orderId);
            if (order == null)
                throw new NotFoundException("Order not found.");

            if (order.UserId != userId)
                await EnsureStoreOwnerAsync(order.StoreId, userId, roles, cancellationToken);

            return order.ToResponse();
        }

        public async Task<IEnumerable<OrderResponse>> GetMyOrdersAsync(Guid userId)
        {
            var orders = await _orderRepository.GetByUserIdAsync(userId);
            return orders.Select(o => o.ToResponse());
        }

        public async Task<IEnumerable<OrderResponse>> GetStoreOrdersAsync(Guid storeId, Guid userId,
            IEnumerable<string> roles, CancellationToken cancellationToken = default)
        {
            await EnsureStoreOwnerAsync(storeId, userId, roles, cancellationToken);
            var orders = await _orderRepository.GetByStoreIdAsync(storeId);
            return orders.Select(o => o.ToResponse());
        }

        public async Task<OrderResponse> UpdateOrderStatusAsync(Guid orderId, Guid userId, IEnumerable<string> roles,
            UpdateOrderStatusRequest request, CancellationToken cancellationToken = default)
        {
            var order = await _orderRepository.GetByIdAsync(orderId);
            if (order == null)
                throw new NotFoundException("Order not found.");

            // Authorize even an idempotent request before returning order details.
            if (order.UserId != userId)
                await EnsureStoreOwnerAsync(order.StoreId, userId, roles, cancellationToken);

            if (request == null || string.IsNullOrWhiteSpace(request.Status))
                throw new BadRequestException("Status is required.");
            if (!request.Status.Trim().Equals(OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
                throw new ConflictException("This endpoint only supports cancelling pending orders. Payment and NoShow use their dedicated flows.");
            if (order.Status.Equals(OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
                return order.ToResponse();
            if (!order.Status.Equals(OrderStatuses.Pending, StringComparison.OrdinalIgnoreCase))
                throw new ConflictException("Only pending orders can be cancelled.");

            var updatedAtUtc = DateTime.UtcNow;
            if (!await _orderRepository.TryUpdatePendingStatusAsync(orderId, OrderStatuses.Cancelled, updatedAtUtc, cancellationToken))
                throw new ConflictException("Order status changed. Reload the order before trying again.");
            order.Status = OrderStatuses.Cancelled;
            order.UpdatedAt = updatedAtUtc;

            return order.ToResponse();
        }

        public async Task<OrderResponse> MarkNoShowAsync(Guid orderId, Guid userId, IEnumerable<string> roles,
            MarkOrderNoShowRequest? request, CancellationToken cancellationToken = default)
        {
            if (userId == Guid.Empty)
                throw new UnauthorizedException("User is not authenticated.");
            var order = await _orderRepository.GetByIdAsync(orderId)
                ?? throw new NotFoundException("Order not found.");

            // Unlike viewing/cancelling, being the buyer does not authorize this command.
            // Keep the Store HTTP call outside the database transaction.
            await EnsureStoreOwnerAsync(order.StoreId, userId, roles, cancellationToken);
            var reason = string.IsNullOrWhiteSpace(request?.Reason)
                ? MarkOrderNoShowRequest.DefaultReason : request.Reason.Trim();
            if (reason.Length > MarkOrderNoShowRequest.MaximumReasonLength)
                throw new BadRequestException("NoShow reason cannot exceed 800 characters.");
            if (order.Status.Equals(OrderStatuses.NoShow, StringComparison.OrdinalIgnoreCase))
                return order.ToResponse();

            var occurredAtUtc = DateTime.UtcNow;
            var ineligibleReason = OrderNoShowPolicy.GetIneligibleReason(order, occurredAtUtc);
            if (ineligibleReason != null)
                throw new ConflictException(ineligibleReason, OrderReasonCodes.NoShowNotEligible);

            var pickupDeadlineUtc = DateTime.SpecifyKind(order.PickupDeadline!.Value, DateTimeKind.Utc);
            await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                if (!await _orderRepository.TryMarkNoShowAsync(orderId, pickupDeadlineUtc, occurredAtUtc, cancellationToken))
                {
                    var current = await _orderRepository.GetByIdAsync(orderId);
                    if (current?.Status.Equals(OrderStatuses.NoShow, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        order = current;
                        return;
                    }
                    throw new ConflictException("Order changed. Reload it before marking NoShow.", OrderReasonCodes.NoShowNotEligible);
                }

                var messageId = Guid.NewGuid();
                await _outboxMessageRepository.AddAsync(new OutboxMessage
                {
                    Id = messageId,
                    ExchangeName = "stealdeal.events",
                    ExchangeType = "topic",
                    RoutingKey = OrderEventTypes.OrderNoShow,
                    EventType = OrderEventTypes.OrderNoShow,
                    CreatedAt = occurredAtUtc,
                    Payload = JsonSerializer.Serialize(new OrderNoShowEvent
                    {
                        MessageId = messageId, OccurredAtUtc = occurredAtUtc,
                        OrderId = order.Id, UserId = order.UserId, StoreId = order.StoreId,
                        MarkedByUserId = userId, PickupDeadlineUtc = pickupDeadlineUtc, Reason = reason
                    }, EventJsonOptions)
                });
                order.Status = OrderStatuses.NoShow;
                order.UpdatedAt = occurredAtUtc;
            }, cancellationToken);

            return order.ToResponse();
        }

        private async Task EnsureStoreOwnerAsync(Guid storeId, Guid userId, IEnumerable<string> roles,
            CancellationToken cancellationToken)
        {
            if (storeId == Guid.Empty)
                throw new BadRequestException("Store id is required.");
            if (OrderRoles.IsAdministrator(roles))
                return;
            if (!roles.Contains(OrderRoles.Seller, StringComparer.OrdinalIgnoreCase))
                throw new ForbiddenException("Only the store owner or an administrator can access this order.", OrderReasonCodes.StoreOwnershipRequired);

            var store = await _storeCatalogClient.GetStoreAsync(storeId, cancellationToken)
                ?? throw new NotFoundException("Store not found.");
            if (store.OwnerId != userId)
                throw new ForbiddenException("You do not own this store.", OrderReasonCodes.StoreOwnershipRequired);
        }

        private async Task<OrderProfile> BuildValidatedOrderAsync(Guid userId, CreateOrderRequest request,
            CancellationToken cancellationToken)
        {
            if (userId == Guid.Empty)
                throw new UnauthorizedException("User is not authenticated.");
            if (request == null || request.StoreId == Guid.Empty || request.Items == null || request.Items.Count == 0)
                throw new BadRequestException("Store and at least one order item are required.");
            if (request.Items.Any(item => item == null || item.BagId == Guid.Empty || item.Quantity <= 0))
                throw new BadRequestException("Every order item needs a bag id and a positive quantity.");
            if (string.IsNullOrWhiteSpace(request.ContactNameSnapshot) || string.IsNullOrWhiteSpace(request.ContactPhoneSnapshot) ||
                string.IsNullOrWhiteSpace(request.DeliveryType))
                throw new BadRequestException("Contact name, contact phone and delivery type are required.");
            if (request.DeliveryFee < 0 || request.VoucherDiscount < 0)
                throw new BadRequestException("Delivery fee and voucher discount cannot be negative.");

            var store = await _storeCatalogClient.GetStoreAsync(request.StoreId, cancellationToken)
                ?? throw new NotFoundException("Store not found.");
            if (!store.IsActive || !store.IsVerify)
                throw new BadRequestException("The store is not available for orders.");

            var now = DateTime.UtcNow;
            var pickupStart = DateTime.MinValue;
            var pickupEnd = DateTime.MaxValue;
            var items = new List<CreateOrderItemRequest>();
            foreach (var group in request.Items.GroupBy(item => item.BagId))
            {
                var quantity = group.Sum(item => (long)item.Quantity);
                if (quantity > int.MaxValue)
                    throw new BadRequestException("Requested quantity is too large.");
                var bag = await _storeCatalogClient.GetBagAsync(group.Key, cancellationToken)
                    ?? throw new NotFoundException($"Bag with ID {group.Key} not found.");
                ValidateBag(bag, request.StoreId, (int)quantity, now);
                var bagStart = ToUtc(bag.PickupStartTime);
                var bagEnd = ToUtc(bag.PickupEndTime);
                if (bagStart > pickupStart) pickupStart = bagStart;
                if (bagEnd < pickupEnd) pickupEnd = bagEnd;
                items.Add(new CreateOrderItemRequest
                {
                    BagId = bag.Id, BagNameSnapshot = bag.Name,
                    UnitPriceSnapshot = bag.SalePrice, Quantity = (int)quantity
                });
            }

            var isPickup = request.DeliveryType.Trim().Equals("Pickup", StringComparison.OrdinalIgnoreCase);
            if (isPickup && pickupStart >= pickupEnd)
                throw new BadRequestException("The bags do not have an overlapping pickup window. Place separate orders.");

            // Both entry points use server snapshots; never take the deadline from the caller/cart.
            request.StoreNameSnapshot = store.Name;
            request.Items = items;
            request.DeliveryType = isPickup ? "Pickup" : request.DeliveryType.Trim();
            request.DeliveryAddress ??= string.Empty;
            return request.ToEntity(userId, isPickup ? pickupEnd : null);
        }

        private static void ValidateBag(StoreBagClientResponse bag, Guid storeId, int quantity, DateTime nowUtc)
        {
            if (bag.StoreId != storeId)
                throw new BadRequestException($"Bag '{bag.Name}' does not belong to the selected store.");
            if (bag.QuantityRemaining < quantity)
                throw new BadRequestException($"Bag '{bag.Name}' does not have enough stock.");
            var start = ToUtc(bag.PickupStartTime);
            var end = ToUtc(bag.PickupEndTime);
            if (start == default || start >= end || end <= nowUtc || ToUtc(bag.ExpiryDate) <= nowUtc)
                throw new BadRequestException($"Bag '{bag.Name}' has an invalid or expired pickup window.");
            if (bag.SalePrice < 0 || bag.Status?.Trim().ToLowerInvariant() is
                "inactive" or "deleted" or "soldout" or "sold_out" or "sold out" or "expired" or "cancelled" or "canceled" or "draft")
                throw new BadRequestException($"Bag '{bag.Name}' is not available for checkout.");
        }

        private static DateTime ToUtc(DateTime value) => value.Kind == DateTimeKind.Local
            ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);

        public async Task<OrderReviewEligibilityResponse> CheckReviewEligibilityAsync(Guid orderId, Guid buyerId, Guid bagId)
        {
            // find order
            var order = await _orderRepository.GetByIdAsync(orderId);
            if (order == null)
                throw new NotFoundException("Order not found.");

            // check if user own the order
            if (order.UserId != buyerId)
                throw new ForbiddenException("You do not own this order.");

            // check if order contain the bag
            if (!order.Items.Any(items => items.BagId == bagId))
                throw new BadRequestException("This order does not contain the specified bag.");

            if (!order.Status.Equals(OrderStatuses.Confirmed, StringComparison.OrdinalIgnoreCase))
                throw new BadRequestException("Only confirmed, paid orders are eligible for review.");

            return new OrderReviewEligibilityResponse
            {
                // Payment confirmation makes the order eligible; it does not prove collection.
                IsEligible = true,
                OrderId = order.Id,
                BuyerId = order.UserId,
                BagId = bagId,
                OrderStatus = order.Status
            };

        }
    }
}
