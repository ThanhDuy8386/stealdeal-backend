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

namespace StealDeal.Services.Order.Application.Services
{
    public class OrderService : IOrderService
    {
        private readonly IOrderRepository _orderRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IOutboxMessageRepository _outboxMessageRepository;

        public OrderService(
            IOrderRepository orderRepository,
            IUnitOfWork unitOfWork,
            IOutboxMessageRepository outboxMessageRepository)
        {
            _orderRepository = orderRepository;
            _unitOfWork = unitOfWork;
            _outboxMessageRepository = outboxMessageRepository;
        }

        public async Task<OrderResponse> CreateOrderAsync(Guid userId, CreateOrderRequest request)
        {
            if (request == null)
                throw new BadRequestException("Request data is null.");

            if (request.Items == null || !request.Items.Any())
                throw new BadRequestException("Order must have at least one item.");

            var order = request.ToEntity(userId);

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

            await _outboxMessageRepository.AddAsync(new OutboxMessage
            {
                ExchangeName = "stealdeal.events",
                ExchangeType = "topic",
                RoutingKey = "order.created",
                EventType = "OrderCreatedEvent",
                Payload = payload,
                Status = "Pending"
            });
            await _orderRepository.AddAsync(order);
            await _unitOfWork.SaveChangesAsync();

            return order.ToResponse();
        }

        public async Task<OrderResponse> GetOrderByIdAsync(Guid orderId, Guid userId, string role)
        {
            var order = await _orderRepository.GetByIdAsync(orderId);
            if (order == null)
                throw new NotFoundException("Order not found.");

            // Verification check
            bool isAdmin = role.Equals("Admin", StringComparison.OrdinalIgnoreCase);
            bool isBuyer = order.UserId == userId;
            // In a real microservice, Seller ownership of order.StoreId should be verified.
            // For now, we allow access if the user is the Admin, the Buyer, or any Seller.
            bool isSeller = role.Equals("Seller", StringComparison.OrdinalIgnoreCase);

            if (!isAdmin && !isBuyer && !isSeller)
                throw new ForbiddenException("You do not have permission to view this order.");

            return order.ToResponse();
        }

        public async Task<IEnumerable<OrderResponse>> GetMyOrdersAsync(Guid userId)
        {
            var orders = await _orderRepository.GetByUserIdAsync(userId);
            return orders.Select(o => o.ToResponse());
        }

        public async Task<IEnumerable<OrderResponse>> GetStoreOrdersAsync(Guid storeId, Guid ownerId)
        {
            // Note: In production, verify that ownerId actually owns storeId via cross-service call or JWT claims.
            var orders = await _orderRepository.GetByStoreIdAsync(storeId);
            return orders.Select(o => o.ToResponse());
        }

        public async Task<OrderResponse> UpdateOrderStatusAsync(Guid orderId, Guid userId, string role, UpdateOrderStatusRequest request)
        {
            var order = await _orderRepository.GetByIdAsync(orderId);
            if (order == null)
                throw new NotFoundException("Order not found.");

            bool isAdmin = role.Equals("Admin", StringComparison.OrdinalIgnoreCase);
            bool isBuyer = order.UserId == userId;
            bool isSeller = role.Equals("Seller", StringComparison.OrdinalIgnoreCase);

            // Validate status transitions & permissions
            string currentStatus = order.Status;
            string newStatus = request.Status.Trim();

            if (currentStatus.Equals(newStatus, StringComparison.OrdinalIgnoreCase))
                return order.ToResponse();

            if (newStatus.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                // Both buyer and seller can cancel, but only if it is still "Pending"
                if (!isBuyer && !isSeller && !isAdmin)
                    throw new ForbiddenException("You do not have permission to cancel this order.");

                if (!currentStatus.Equals("Pending", StringComparison.OrdinalIgnoreCase))
                    throw new BadRequestException("Only pending orders can be cancelled.");
            }
            else
            {
                // Status progression (e.g., Confirmed, Preparing, ReadyForPickup, Completed) is managed by Seller or Admin
                if (!isSeller && !isAdmin)
                    throw new ForbiddenException("Only sellers or admins can update order progression status.");
            }

            order.Status = newStatus;
            order.UpdatedAt = DateTime.UtcNow;

            _orderRepository.Update(order);
            await _unitOfWork.SaveChangesAsync();

            return order.ToResponse();
        }
    }
}
