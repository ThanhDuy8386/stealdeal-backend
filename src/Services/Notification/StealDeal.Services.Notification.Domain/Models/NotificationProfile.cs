using System;
using System.Collections.Generic;

namespace StealDeal.Services.Notification.Domain.Models
{
    public class NotificationProfile
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }

        public string Title { get; set; } = null!;
        public string Body { get; set; } = null!;
        public string Type { get; set; } = null!;

        public string? ActionUrl { get; set; } // Cho phép null nếu thông báo chỉ để đọc, không có link điều hướng
        public Guid? ReferenceId { get; set; } // Đổi từ action_url (uuid) thành tên này cho hợp logic, để null nếu không tham chiếu entity nào
        public string? ReferenceType { get; set; } // Ví dụ: "Order", "Dispute"...

        public bool IsRead { get; set; } = false; // Mặc định thông báo mới tạo là chưa đọc
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
