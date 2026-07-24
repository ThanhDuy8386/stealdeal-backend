namespace StealDeal.Services.Order.Application.DTOs.Events
{
    public class CreateOrderEvent
    {
        public Guid OrderId { get; set; }
        public Guid UserId { get; set; }
        public Guid StoreId { get; set; }
        public List<OrderItemDto> Items { get; set; } = new();
        public decimal TotalAmount { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class OrderItemDto
    {
        public Guid SurpriseBagId { get; set; }
        public int Quantity { get; set; }
    }
}
