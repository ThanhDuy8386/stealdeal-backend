using System;

namespace StealDeal.Services.Store.Application.Messaging
{
    public class IntegrationEventContext
    {
        public Guid MessageId { get; set; }
        public string ConsumerName { get; set; } = null!;
        public string EventType { get; set; } = null!;
        public string RoutingKey { get; set; } = null!;
        public DateTime? OccurredAt { get; set; }
    }
}
//class này chứa context của event được truyền vào handler

//dùng để bổ sung các thông tin cần thiết khi xử lý message, đồng thời có ích cho bảng processed_message