using System;
using System.Collections.Generic;

namespace StealDeal.Services.Store.Domain.Models
{
    public class SurpriseBag
    {
        //đã sửa. 1 bag có nhiều categories, 1 category có nhiều bags
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid StoreId { get; set; }
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
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Navigation properties
        public StoreProfile Store { get; set; } = null!;
        public ICollection<Category> Categories { get; set; } = new List<Category>();
        public ICollection<StoreReview> StoreReviews { get; set; } = new List<StoreReview>();
    }
}
