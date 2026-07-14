using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StealDeal.Services.Payment.Application.DTOs.Requests;
using StealDeal.Services.Payment.Application.DTOs.Response;
using StealDeal.Services.Payment.Application.Exceptions;
using StealDeal.Services.Payment.Application.Mappings;
using StealDeal.Services.Payment.Application.Services.Interfaces;
using StealDeal.Services.Payment.Domain.Interfaces;
using StealDeal.Services.Payment.Domain.Models;

namespace StealDeal.Services.Payment.Application.Services
{
    public class RefundService : IRefundService
    {
        private readonly IRefundRepository _refundRepository;
        private readonly ITransactionRepository _transactionRepository;
        private readonly IUnitOfWork _unitOfWork;

        public RefundService(
            IRefundRepository refundRepository,
            ITransactionRepository transactionRepository,
            IUnitOfWork unitOfWork)
        {
            _refundRepository = refundRepository;
            _transactionRepository = transactionRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<RefundResponse> CreateRefundAsync(CreateRefundRequest request)
        {
            if (request == null)
                throw new BadRequestException("Request data is null.");

            var transaction = await _transactionRepository.GetByIdAsync(request.TransactionId);
            if (transaction == null)
                throw new NotFoundException("Transaction not found.");

            if (!transaction.Status.Equals("Success", StringComparison.OrdinalIgnoreCase))
                throw new BadRequestException("Only successful transactions can be refunded.");

            // Check if amount to refund exceeds the transaction amount
            var existingRefunds = await _refundRepository.GetByTransactionIdAsync(request.TransactionId);
            var alreadyRefundedAmount = existingRefunds
                .Where(r => r.Status.Equals("Processed", StringComparison.OrdinalIgnoreCase) || r.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
                .Sum(r => r.Amount);

            if (alreadyRefundedAmount + request.Amount > transaction.Amount)
                throw new BadRequestException("Total refund amount exceeds original transaction amount.");

            var refund = request.ToEntity(orderId: transaction.OrderId);

            await _refundRepository.AddAsync(refund);
            await _unitOfWork.SaveChangesAsync();

            return refund.ToResponse();
        }

        public async Task<RefundResponse> GetRefundByIdAsync(Guid id, Guid userId, string role)
        {
            var refund = await _refundRepository.GetByIdAsync(id);
            if (refund == null)
                throw new NotFoundException("Refund not found.");

            bool isAdmin = role.Equals("Admin", StringComparison.OrdinalIgnoreCase);
            // Must be admin or the owner of the original transaction
            bool isTransactionOwner = refund.Transaction?.UserId == userId;

            if (!isAdmin && !isTransactionOwner)
                throw new ForbiddenException("You do not have permission to view this refund.");

            return refund.ToResponse();
        }

        public async Task<IEnumerable<RefundResponse>> GetRefundsByTransactionIdAsync(Guid transactionId, Guid userId, string role)
        {
            var transaction = await _transactionRepository.GetByIdAsync(transactionId);
            if (transaction == null)
                throw new NotFoundException("Transaction not found.");

            bool isAdmin = role.Equals("Admin", StringComparison.OrdinalIgnoreCase);
            bool isOwner = transaction.UserId == userId;

            if (!isAdmin && !isOwner)
                throw new ForbiddenException("You do not have permission to view these refunds.");

            var refunds = await _refundRepository.GetByTransactionIdAsync(transactionId);
            return refunds.Select(r => r.ToResponse());
        }

        public async Task<IEnumerable<RefundResponse>> GetAllRefundsAsync()
        {
            var refunds = await _refundRepository.GetAllAsync();
            return refunds.Select(r => r.ToResponse());
        }

        public async Task<RefundResponse> UpdateRefundStatusAsync(Guid id, UpdateRefundStatusRequest request)
        {
            var refund = await _refundRepository.GetByIdAsync(id);
            if (refund == null)
                throw new NotFoundException("Refund not found.");

            refund.Status = request.Status.Trim();

            if (request.Status.Equals("Processed", StringComparison.OrdinalIgnoreCase))
            {
                refund.ProcessedAt = DateTime.UtcNow;
            }

            _refundRepository.Update(refund);
            await _unitOfWork.SaveChangesAsync();

            return refund.ToResponse();
        }
    }
}
