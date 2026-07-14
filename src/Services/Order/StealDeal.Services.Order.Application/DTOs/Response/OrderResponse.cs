using System;
using System.Collections.Generic;

namespace StealDeal.Services.Order.Application.DTOs.Response
{
    public class OrderItemResponse
    {
        public Guid Id { get; set; }
        public Guid BagId { get; set; }
        public string BagNameSnapshot { get; set; } = null!;
        public decimal UnitPriceSnapshot { get; set; }
        public int Quantity { get; set; }
        public decimal Subtotal { get; set; }
    }

    public class OrderResponse
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid StoreId { get; set; }
        public string StoreNameSnapshot { get; set; } = null!;
        public decimal DeliveryFee { get; set; }
        public decimal VoucherDiscount { get; set; }
        public decimal TotalAmount { get; set; }
        public string DeliveryType { get; set; } = null!;
        public string DeliveryAddress { get; set; } = null!;
        public string? PickupCode { get; set; }
        public string Status { get; set; } = null!;
        public DateTime? PickupDeadline { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<OrderItemResponse> Items { get; set; } = new();
    }
}
