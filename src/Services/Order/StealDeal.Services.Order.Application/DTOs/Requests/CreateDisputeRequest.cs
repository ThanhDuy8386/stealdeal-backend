using System;
using System.Collections.Generic;

namespace StealDeal.Services.Order.Application.DTOs.Requests
{
    public class CreateDisputeRequest
    {
        public Guid OrderId { get; set; }
        public string DisputeType { get; set; } = null!;
        public string Description { get; set; } = null!;
        public List<string> EvidenceUrls { get; set; } = new();
    }

    public class UpdateDisputeStatusRequest
    {
        public string Status { get; set; } = null!;
    }
}
