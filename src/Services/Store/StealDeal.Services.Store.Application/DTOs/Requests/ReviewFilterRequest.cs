using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Application.DTOs.Requests
{
    public class ReviewFilterRequest
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public int? RatingScore { get; set; }   // 1..5
        public bool? HasReply { get; set; }     // true = replied, false = needs reply
        public string? Search { get; set; }     // keyword search: Comment, BuyerName, Bag.Name
        public bool? IsReported { get; set; }   // only used by seller dashboard endpoint
    }
}
