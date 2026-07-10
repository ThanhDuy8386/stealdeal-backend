using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Application.DTOs.Requests
{
    public class CreateBagRequest
    {
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public decimal OriginalPrice { get; set; }
        public decimal SalePrice { get; set; }
        public int QuantityTotal { get; set; }
        public string Status { get; set; }
        public DateTime PickupStartTime { get; set; }
        public DateTime PickupEndTime { get; set; }
        public DateTime ExpiryDate { get; set; }
        public List<Guid> CategoryIds { get; set; } = new();
    }
}
