using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Application.DTOs.Responses
{
    public class SurpriseBagResponse
    {
        public Guid Id { get; set; }
        public Guid StoreId { get; set; }
        public string StoreName { get; set; } = null!;         // từ navigation
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public decimal OriginalPrice { get; set; }
        public decimal SalePrice { get; set; }
        public int QuantityTotal { get; set; }
        public int QuantityRemaining { get; set; }
        public DateTime PickupStartTime { get; set; }
        public DateTime PickupEndTime { get; set; }
        public DateTime ExpiryDate { get; set; }
        public string Status { get; set; } = null!;
        public List<CategoryResponse> Categories { get; set; } = new(); // từ N:N
        public DateTime CreatedAt { get; set; }
    }
}
