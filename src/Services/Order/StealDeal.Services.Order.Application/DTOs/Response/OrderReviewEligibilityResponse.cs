using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Order.Application.DTOs.Response
{
    public class OrderReviewEligibilityResponse
    {
        public bool IsEligible { get; set; }
        public Guid OrderId { get; set; }
        public Guid BuyerId { get; set; }
        public Guid BagId { get; set; }
        public string OrderStatus { get; set; } = null!;
    }
}
