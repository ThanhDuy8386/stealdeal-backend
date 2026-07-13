using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Application.DTOs.Requests
{
    public class CreateReviewRequest
    {
        public Guid OrderId { get; set; }
        public Guid BagId { get; set; }
        public int RatingScore { get; set; }
        public string? Comment { get; set; }
    }
}
