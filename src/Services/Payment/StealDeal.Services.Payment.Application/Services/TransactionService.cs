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
            if (existing != null && (existing.Status == "Success" || existing.Status == "Pending"))
                throw new ConflictException("A transaction is already registered or succeeded for this order.");

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
            transaction.GatewayRef = request.GatewayRef?.Trim();
            transaction.FailureReason = request.FailureReason?.Trim();
            transaction.UpdatedAt = DateTime.UtcNow;

            if (request.Status.Equals("Success", StringComparison.OrdinalIgnoreCase))
            {
                transaction.PaidAt = DateTime.UtcNow;
            }

            _transactionRepository.Update(transaction);
            await _unitOfWork.SaveChangesAsync();

            return transaction.ToResponse();
        }
    }
}
