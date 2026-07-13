using System;
using System.Collections.Generic;

namespace StealDeal.Services.Order.Application.DTOs.Response
{
    public class PickupDisputeResponse
    {
        public Guid Id { get; set; }
        public Guid OrderId { get; set; }
        public Guid ReporterId { get; set; }
        public string DisputeType { get; set; } = null!;
        public List<string> EvidenceUrls { get; set; } = new();
        public string Description { get; set; } = null!;
        public string Status { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
    }
}
