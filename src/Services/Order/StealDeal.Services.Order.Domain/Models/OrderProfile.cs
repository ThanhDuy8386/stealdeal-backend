using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Order.Domain.Models
{
    public class OrderProfile
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public Guid StoreId { get; set; }

        public string StoreNameSnapshot { get; set; } = null!;

        public decimal DeliveryFee { get; set; }
        public decimal VoucherDiscount { get; set; }
        public decimal TotalAmount { get; set; }

        public string DeliveryType { get; set; } = null!;
        public string DeliveryAddress { get; set; } = null!;

        public string? PickupCode { get; set; } // Cho phép null nếu không phải đơn tự đến lấy
        public string Status { get; set; } = null!;

        public DateTime? PickupDeadline { get; set; } // Cho phép null nếu không áp dụng deadline lấy hàng

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
        public ICollection<PickupDispute> Disputes { get; set; } = new List<PickupDispute>();
    }
}
