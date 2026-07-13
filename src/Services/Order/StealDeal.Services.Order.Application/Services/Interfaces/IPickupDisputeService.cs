using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StealDeal.Services.Order.Application.DTOs.Requests;
using StealDeal.Services.Order.Application.DTOs.Response;

namespace StealDeal.Services.Order.Application.Services.Interfaces
{
    public interface IPickupDisputeService
    {
        Task<PickupDisputeResponse> CreateDisputeAsync(Guid reporterId, CreateDisputeRequest request);
        Task<PickupDisputeResponse> GetDisputeByIdAsync(Guid disputeId, Guid userId, string role);
        Task<IEnumerable<PickupDisputeResponse>> GetAllDisputesAsync();
        Task<PickupDisputeResponse> UpdateDisputeStatusAsync(Guid disputeId, UpdateDisputeStatusRequest request);
    }
}
