using System.ComponentModel.DataAnnotations;

namespace StealDeal.Services.Order.Application.DTOs.Requests
{
    public class CheckoutFromCartRequest
    {
        public Guid StoreId { get; set; }

        [Required, MaxLength(256)]
        public string ContactNameSnapshot { get; set; } = null!;

        [Required, MaxLength(20)]
        public string ContactPhoneSnapshot { get; set; } = null!;

        [Required]
        public string DeliveryType { get; set; } = "Pickup";

        public string DeliveryAddress { get; set; } = string.Empty;
    }
}
