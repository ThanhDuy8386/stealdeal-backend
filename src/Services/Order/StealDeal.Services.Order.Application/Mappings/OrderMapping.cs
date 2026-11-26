using System;
using System.Collections.Generic;
using System.Linq;
using StealDeal.Services.Order.Application.DTOs.Requests;
using StealDeal.Services.Order.Application.DTOs.Response;
using StealDeal.Services.Order.Domain.Models;
using StealDeal.Services.Order.Domain.Constants;
using StealDeal.Services.Order.Domain.Policies;

namespace StealDeal.Services.Order.Application.Mappings
{
    public static class OrderMapping
    {
        public static OrderProfile ToEntity(this CreateOrderRequest request, Guid userId, DateTime? pickupDeadlineUtc)
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

            // Combine the order ID prefix with a fresh GUID prefix for pickup orders.
            string? pickupCode = null;
            if (request.DeliveryType.Equals("Pickup", StringComparison.OrdinalIgnoreCase))
            {
                pickupCode = (orderId.ToString("N").Substring(0, 5)
                    + Guid.NewGuid().ToString("N").Substring(0, 5)).ToUpperInvariant();
            }

            return new OrderProfile
            {
                Id = orderId,
                UserId = userId,
                StoreId = request.StoreId,
                StoreNameSnapshot = request.StoreNameSnapshot.Trim(),
                ContactNameSnapshot = request.ContactNameSnapshot.Trim(),
                ContactPhoneSnapshot = request.ContactPhoneSnapshot.Trim(),
                DeliveryFee = request.DeliveryFee,
                VoucherDiscount = request.VoucherDiscount,
                TotalAmount = totalAmount > 0 ? totalAmount : 0,
                DeliveryType = request.DeliveryType.Trim(),
                DeliveryAddress = request.DeliveryAddress.Trim(),
                PickupCode = pickupCode,
                Status = OrderStatuses.Pending,
                PickupDeadline = pickupDeadlineUtc,
                PickupDeadlineVerified = pickupDeadlineUtc.HasValue,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Items = items
            };
        }

        public static OrderResponse ToResponse(this OrderProfile order)
        {
            if (order == null) return null!;

            var noShowIneligibleReason = OrderNoShowPolicy.GetIneligibleReason(order, DateTime.UtcNow);

            return new OrderResponse
            {
                Id = order.Id,
                UserId = order.UserId,
                StoreId = order.StoreId,
                StoreNameSnapshot = order.StoreNameSnapshot,
                ContactNameSnapshot = order.ContactNameSnapshot,
                ContactPhoneSnapshot = order.ContactPhoneSnapshot,
                DeliveryFee = order.DeliveryFee,
                VoucherDiscount = order.VoucherDiscount,
                TotalAmount = order.TotalAmount,
                DeliveryType = order.DeliveryType,
                DeliveryAddress = order.DeliveryAddress,
                PickupCode = order.PickupCode,
                Status = order.Status,
                PickupDeadline = order.PickupDeadline.HasValue
                    ? DateTime.SpecifyKind(order.PickupDeadline.Value, DateTimeKind.Utc) : null,
                CanMarkNoShow = noShowIneligibleReason == null,
                NoShowIneligibleReason = noShowIneligibleReason,
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
