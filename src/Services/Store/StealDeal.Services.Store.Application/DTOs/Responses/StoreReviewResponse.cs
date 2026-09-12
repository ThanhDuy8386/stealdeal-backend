using System;

namespace StealDeal.Services.Store.Application.DTOs.Responses
{
    public class StoreReviewResponse
    {
        public Guid Id { get; set; }
        public Guid OrderId { get; set; }
        public Guid BuyerId { get; set; }
        public string BuyerName { get; set; } = null!;
        public Guid StoreId { get; set; }
        public Guid BagId { get; set; }
        public string? BagName { get; set; }
        public int RatingScore { get; set; }
        public string? Comment { get; set; }
        public string? StoreReply { get; set; }
        public DateTime? RepliedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
