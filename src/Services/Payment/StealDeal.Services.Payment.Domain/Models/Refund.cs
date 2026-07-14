using System;
using System.Collections.Generic;

namespace StealDeal.Services.Payment.Domain.Models
{
    public class Refund
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TransactionId { get; set; }
        public Guid OrderId { get; set; }

        public decimal Amount { get; set; }
        public string Reason { get; set; } = null!;

        public string Status { get; set; } = null!; // Ví dụ: "Pending", "Processed", "Failed"

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ProcessedAt { get; set; } // Cho phép null khi yêu cầu hoàn tiền đang chờ xử lý

        // Navigation properties
        public Transaction Transaction { get; set; } = null!;
    }
}
