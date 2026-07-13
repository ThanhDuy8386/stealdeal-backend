using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Application.DTOs.Responses
{
    public class StoreReviewResponse
    {
        public Guid Id { get; set; }
        public Guid OrderId { get; set; }
        public Guid BuyerId { get; set; }
        public int RatingScore { get; set; }
        public string? Comment { get; set; }
        public string? StoreReply { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
