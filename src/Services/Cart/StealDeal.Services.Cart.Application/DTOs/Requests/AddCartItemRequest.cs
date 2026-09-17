namespace StealDeal.Services.Cart.Application.DTOs.Requests
{
    public class AddCartItemRequest
    {
        public Guid BagId { get; set; }
        public int Quantity { get; set; }
    }
}
