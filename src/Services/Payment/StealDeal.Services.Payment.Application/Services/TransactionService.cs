using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StealDeal.Services.Payment.Application.DTOs.Requests;
using StealDeal.Services.Payment.Application.DTOs.Response;
using StealDeal.Services.Payment.Application.Exceptions;
using StealDeal.Services.Payment.Application.Mappings;
using StealDeal.Services.Payment.Application.Services.Interfaces;
using StealDeal.Services.Payment.Domain.Constants;
using StealDeal.Services.Payment.Domain.Interfaces;
using StealDeal.Services.Payment.Domain.Models;

namespace StealDeal.Services.Payment.Application.Services
{
    public class TransactionService : ITransactionService
    {
        private readonly ITransactionRepository _transactionRepository;
        private readonly IUnitOfWork _unitOfWork;

        public TransactionService(
            ITransactionRepository transactionRepository,
            IUnitOfWork unitOfWork)
        {
            _transactionRepository = transactionRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<TransactionResponse> CreateTransactionAsync(Guid userId, CreateTransactionRequest request)
        {
            if (request == null)
                throw new BadRequestException("Request data is null.");

            // Check if there is already a successful or pending transaction for this order
            var existing = await _transactionRepository.GetByOrderIdAsync(request.OrderId);
            if (existing != null &&
                (existing.Status == TransactionStatuses.Success ||
                 existing.Status == TransactionStatuses.Pending))
            {
                throw new ConflictException("A transaction is already registered or succeeded for this order.");
            }

            var transaction = request.ToEntity(userId);

            await _transactionRepository.AddAsync(transaction);
            await _unitOfWork.SaveChangesAsync();

            return transaction.ToResponse();
        }

        public async Task<TransactionResponse> GetTransactionByIdAsync(Guid id, Guid userId, string role)
        {
            var transaction = await _transactionRepository.GetByIdAsync(id);
            if (transaction == null)
                throw new NotFoundException("Transaction not found.");

            bool isAdmin = role.Equals("Admin", StringComparison.OrdinalIgnoreCase);
            bool isOwner = transaction.UserId == userId;

            if (!isAdmin && !isOwner)
                throw new ForbiddenException("You do not have permission to view this transaction.");

            return transaction.ToResponse();
        }

        public async Task<TransactionResponse> GetTransactionByOrderIdAsync(Guid orderId, Guid userId, string role)
        {
            var transaction = await _transactionRepository.GetByOrderIdAsync(orderId);
            if (transaction == null)
                throw new NotFoundException("Transaction not found for this order.");

            bool isAdmin = role.Equals("Admin", StringComparison.OrdinalIgnoreCase);
            bool isOwner = transaction.UserId == userId;

            if (!isAdmin && !isOwner)
                throw new ForbiddenException("You do not have permission to view this transaction.");

            return transaction.ToResponse();
        }

        public async Task<IEnumerable<TransactionResponse>> GetMyTransactionsAsync(Guid userId)
        {
            var transactions = await _transactionRepository.GetByUserIdAsync(userId);
            return transactions.Select(t => t.ToResponse());
        }

        public async Task<TransactionResponse> UpdateTransactionStatusAsync(Guid id, UpdateTransactionStatusRequest request)
        {
            var transaction = await _transactionRepository.GetByIdAsync(id);
            if (transaction == null)
                throw new NotFoundException("Transaction not found.");

            transaction.Status = request.Status.Trim();

            if (request.GatewayRef != null)
            {
                transaction.GatewayRef = request.GatewayRef.Trim();
            }

            if (request.CheckoutUrl != null)
            {
                transaction.CheckoutUrl = request.CheckoutUrl.Trim();
            }

            if (request.GatewayTransactionNo != null)
            {
                transaction.GatewayTransactionNo = request.GatewayTransactionNo.Trim();
            }

            if (request.GatewayResponseCode != null)
            {
                transaction.GatewayResponseCode = request.GatewayResponseCode.Trim();
            }

            if (request.GatewayTransactionStatus != null)
            {
                transaction.GatewayTransactionStatus = request.GatewayTransactionStatus.Trim();
            }

            if (request.FailureReason != null)
            {
                transaction.FailureReason = request.FailureReason.Trim();
            }

            if (request.ExpiresAt.HasValue)
            {
                transaction.ExpiresAt = request.ExpiresAt;
            }

            transaction.UpdatedAt = DateTime.UtcNow;

            if (request.Status.Equals(TransactionStatuses.Success, StringComparison.OrdinalIgnoreCase))
            {
                transaction.PaidAt = request.PaidAt ?? DateTime.UtcNow;
            }
            else if (request.PaidAt.HasValue)
            {
                transaction.PaidAt = request.PaidAt;
            }

            _transactionRepository.Update(transaction);
            await _unitOfWork.SaveChangesAsync();

            return transaction.ToResponse();
        }
    }
}
