using System;
using System.Collections.Generic;
using System.Linq;
using StealDeal.Services.Order.Application.DTOs.Requests;
using StealDeal.Services.Order.Application.DTOs.Response;
using StealDeal.Services.Order.Domain.Models;

namespace StealDeal.Services.Order.Application.Mappings
{
    public static class OrderMapping
    {
        public static OrderProfile ToEntity(this CreateOrderRequest request, Guid userId)
        {
            var orderId = Guid.NewGuid();
            var items = request.Items.Select(itemRequest => new OrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                BagId = itemRequest.BagId,
                BagNameSnapshot = itemRequest.BagNameSnapshot.Trim(),
                UnitPriceSnapshot = itemRequest.UnitPriceSnapshot,
                Quantity = itemRequest.Quantity,
                Subtotal = itemRequest.UnitPriceSnapshot * itemRequest.Quantity
            }).ToList();

            var subtotalSum = items.Sum(i => i.Subtotal);
            var totalAmount = subtotalSum + request.DeliveryFee - request.VoucherDiscount;

            // Generate a simple pickup code if it is a Pickup delivery type
            string? pickupCode = null;
            if (request.DeliveryType.Equals("Pickup", StringComparison.OrdinalIgnoreCase))
            {
                pickupCode = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
            }

            return new OrderProfile
            {
                Id = orderId,
                UserId = userId,
                StoreId = request.StoreId,
                StoreNameSnapshot = request.StoreNameSnapshot.Trim(),
                DeliveryFee = request.DeliveryFee,
                VoucherDiscount = request.VoucherDiscount,
                TotalAmount = totalAmount > 0 ? totalAmount : 0,
                DeliveryType = request.DeliveryType.Trim(),
                DeliveryAddress = request.DeliveryAddress.Trim(),
                PickupCode = pickupCode,
                Status = "Pending", // Default initial status
                PickupDeadline = request.DeliveryType.Equals("Pickup", StringComparison.OrdinalIgnoreCase) 
                    ? DateTime.UtcNow.AddDays(1) // Example default: 24h deadline to pickup
                    : null,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Items = items
            };
        }

        public static OrderResponse ToResponse(this OrderProfile order)
        {
            if (order == null) return null!;

            return new OrderResponse
            {
                Id = order.Id,
                UserId = order.UserId,
                StoreId = order.StoreId,
                StoreNameSnapshot = order.StoreNameSnapshot,
                DeliveryFee = order.DeliveryFee,
                VoucherDiscount = order.VoucherDiscount,
                TotalAmount = order.TotalAmount,
                DeliveryType = order.DeliveryType,
                DeliveryAddress = order.DeliveryAddress,
                PickupCode = order.PickupCode,
                Status = order.Status,
                PickupDeadline = order.PickupDeadline,
                CreatedAt = order.CreatedAt,
                UpdatedAt = order.UpdatedAt,
                Items = order.Items.Select(i => i.ToResponse()).ToList()
            };
        }

        public static OrderItemResponse ToResponse(this OrderItem item)
        {
            if (item == null) return null!;

            return new OrderItemResponse
            {
                Id = item.Id,
                BagId = item.BagId,
                BagNameSnapshot = item.BagNameSnapshot,
                UnitPriceSnapshot = item.UnitPriceSnapshot,
                Quantity = item.Quantity,
                Subtotal = item.Subtotal
            };
        }
    }
}
