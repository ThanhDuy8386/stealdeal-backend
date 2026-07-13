using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StealDeal.Services.Order.Application.DTOs.Requests;
using StealDeal.Services.Order.Application.DTOs.Response;
using StealDeal.Services.Order.Application.Exceptions;
using StealDeal.Services.Order.Application.Mappings;
using StealDeal.Services.Order.Application.Services.Interfaces;
using StealDeal.Services.Order.Domain.Interfaces;
using StealDeal.Services.Order.Domain.Models;

namespace StealDeal.Services.Order.Application.Services
{
    public class PickupDisputeService : IPickupDisputeService
    {
        private readonly IPickupDisputeRepository _disputeRepository;
        private readonly IOrderRepository _orderRepository;
        private readonly IUnitOfWork _unitOfWork;

        public PickupDisputeService(
            IPickupDisputeRepository disputeRepository,
            IOrderRepository orderRepository,
            IUnitOfWork unitOfWork)
        {
            _disputeRepository = disputeRepository;
            _orderRepository = orderRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<PickupDisputeResponse> CreateDisputeAsync(Guid reporterId, CreateDisputeRequest request)
        {
            var order = await _orderRepository.GetByIdAsync(request.OrderId);
            if (order == null)
                throw new NotFoundException("Order not found.");

            // The reporter must be part of the order (buyer) or an admin.
            // In case of sellers reporting, it's also allowed.
            if (order.UserId != reporterId)
            {
                // In production, check if reporterId owns order.StoreId via cross-service
            }

            var dispute = request.ToEntity(reporterId);

            await _disputeRepository.AddAsync(dispute);
            await _unitOfWork.SaveChangesAsync();

            // Set relation reference for mapping mapping
            dispute.OrderProfile = order;

            return dispute.ToResponse();
        }

        public async Task<PickupDisputeResponse> GetDisputeByIdAsync(Guid disputeId, Guid userId, string role)
        {
            var dispute = await _disputeRepository.GetByIdAsync(disputeId);
            if (dispute == null)
                throw new NotFoundException("Pickup dispute not found.");

            bool isAdmin = role.Equals("Admin", StringComparison.OrdinalIgnoreCase);
            bool isReporter = dispute.ReporterId == userId;
            bool isOrderBuyer = dispute.OrderProfile?.UserId == userId;

            if (!isAdmin && !isReporter && !isOrderBuyer)
                throw new ForbiddenException("You do not have permission to view this dispute.");

            return dispute.ToResponse();
        }

        public async Task<IEnumerable<PickupDisputeResponse>> GetAllDisputesAsync()
        {
            var disputes = await _disputeRepository.GetAllAsync();
            return disputes.Select(d => d.ToResponse());
        }

        public async Task<PickupDisputeResponse> UpdateDisputeStatusAsync(Guid disputeId, UpdateDisputeStatusRequest request)
        {
            var dispute = await _disputeRepository.GetByIdAsync(disputeId);
            if (dispute == null)
                throw new NotFoundException("Pickup dispute not found.");

            dispute.Status = request.Status.Trim();
            
            _disputeRepository.Update(dispute);
            await _unitOfWork.SaveChangesAsync();

            return dispute.ToResponse();
        }
    }
}
