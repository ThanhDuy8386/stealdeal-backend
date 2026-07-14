using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Order.Domain.Models
{
    public class OrderItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid OrderId { get; set; }
        public Guid BagId { get; set; }

        public string BagNameSnapshot { get; set; } = null!;
        public decimal UnitPriceSnapshot { get; set; }

        public int Quantity { get; set; }
        public decimal Subtotal { get; set; }

        // Navigation properties
        public OrderProfile OrderProfile { get; set; } = null!;
    }
}
