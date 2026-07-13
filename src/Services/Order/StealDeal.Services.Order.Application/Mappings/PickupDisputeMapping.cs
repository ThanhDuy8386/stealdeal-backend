using System;
using System.Linq;
using StealDeal.Services.Order.Application.DTOs.Requests;
using StealDeal.Services.Order.Application.DTOs.Response;
using StealDeal.Services.Order.Domain.Models;

namespace StealDeal.Services.Order.Application.Mappings
{
    public static class PickupDisputeMapping
    {
        public static PickupDispute ToEntity(this CreateDisputeRequest request, Guid reporterId)
        {
            return new PickupDispute
            {
                Id = Guid.NewGuid(),
                OrderId = request.OrderId,
                ReporterId = reporterId,
                DisputeType = request.DisputeType.Trim(),
                EvidenceUrls = request.EvidenceUrls ?? new(),
                Description = request.Description.Trim(),
                Status = "Pending", // Default initial status
                CreatedAt = DateTime.UtcNow
            };
        }

        public static PickupDisputeResponse ToResponse(this PickupDispute dispute)
        {
            if (dispute == null) return null!;

            return new PickupDisputeResponse
            {
                Id = dispute.Id,
                OrderId = dispute.OrderId,
                ReporterId = dispute.ReporterId,
                DisputeType = dispute.DisputeType,
                EvidenceUrls = dispute.EvidenceUrls,
                Description = dispute.Description,
                Status = dispute.Status,
                CreatedAt = dispute.CreatedAt
            };
        }
    }
}
