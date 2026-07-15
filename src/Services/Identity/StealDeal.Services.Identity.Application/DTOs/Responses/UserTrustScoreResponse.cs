using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Identity.Application.DTOs.Responses
{
    public class UserTrustScoreResponse
    {
        public Guid Id { get; set; }
        public int Score { get; set; }
        public int TotalOrders { get; set; }
        public int SuccessfulPickups { get; set; }
        public int NoShowCount { get; set; }
        public int DisputeCount { get; set; }
        public DateTime? LastCalculatedAt { get; set; }
    }
}
