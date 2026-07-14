using System;
using System.Collections.Generic;

namespace StealDeal.Services.Payment.Domain.Models
{
    public class Transaction
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid OrderId { get; set; }
        public Guid UserId { get; set; }

        public decimal Amount { get; set; }

        public string PaymentMethod { get; set; } = null!; // Ví dụ: "VNPAY", "MOMO", "COD", "STRIPE"
        public string? GatewayRef { get; set; } // Mã tham chiếu từ cổng thanh toán, cho phép null nếu chưa thanh toán hoặc thanh toán COD

        public string Status { get; set; } = null!; // Ví dụ: "Pending", "Success", "Failed"
        public string? FailureReason { get; set; } // Lý do thất bại (nếu có), để nullable vì giao dịch thành công sẽ không có trường này

        public DateTime? PaidAt { get; set; } // Thời điểm thanh toán thành công, để nullable vì lúc mới tạo đơn chưa thanh toán ngay

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public ICollection<Refund> Refunds { get; set; } = new List<Refund>();
    }
}
