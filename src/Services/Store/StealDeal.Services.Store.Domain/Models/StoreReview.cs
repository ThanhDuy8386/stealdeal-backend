using System;

namespace StealDeal.Services.Store.Domain.Models
{
    public class StoreReview
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid OrderId { get; set; }
        public Guid BuyerId { get; set; }
        public Guid StoreId { get; set; }
        public Guid BagId { get; set; }
        public int RatingScore { get; set; }
        public string? Comment { get; set; }
        public string? StoreReply { get; set; }
        public bool IsReported { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public StoreProfile Store { get; set; } = null!;
        public SurpriseBag Bag { get; set; } = null!;
    }
}
