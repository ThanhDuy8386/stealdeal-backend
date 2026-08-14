using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace StealDeal.Services.Order.Application.DTOs.Requests
{
    public class CreateOrderItemRequest
    {
        public Guid BagId { get; set; }
        public string BagNameSnapshot { get; set; } = null!;
        public decimal UnitPriceSnapshot { get; set; }
        public int Quantity { get; set; }
    }

    public class CreateOrderRequest
    {
        public Guid StoreId { get; set; }
        public string StoreNameSnapshot { get; set; } = null!;
        [Required, MaxLength(256)]
        public string ContactNameSnapshot { get; set; } = null!;
        [Required, MaxLength(20)]
        public string ContactPhoneSnapshot { get; set; } = null!;
        public decimal DeliveryFee { get; set; }
        public decimal VoucherDiscount { get; set; }
        public string DeliveryType { get; set; } = null!;
        public string DeliveryAddress { get; set; } = null!;
        public List<CreateOrderItemRequest> Items { get; set; } = new();
    }
}
