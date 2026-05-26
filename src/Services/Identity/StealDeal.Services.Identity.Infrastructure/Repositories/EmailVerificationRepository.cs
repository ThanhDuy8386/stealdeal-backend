using StealDeal.Services.Identity.Domain.Interfaces.Repositories;
using StealDeal.Services.Identity.Domain.Models;
using StealDeal.Services.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace StealDeal.Services.Identity.Infrastructure.Repositories
{
    public class EmailVerificationRepository : IEmailVerificationRepository
    {
        private readonly ApplicationDbContext _context;

        public EmailVerificationRepository(ApplicationDbContext context)
        {
            _context = context;
        }
        public async Task AddAsync(EmailVerification entity)
        {
            await _context.EmailVerifications.AddAsync(entity);
        }

        public async Task<EmailVerification?> VerifyOtp(string email, string otpHash)
        { 
            //dùng để verify otp khi user nhập vào
            return await _context.EmailVerifications
                .Include(e => e.User)
                .Where(e => e.User.Email == email && 
                    e.OtpHash == otpHash && 
                    e.ConsumedAt == null && 
                    e.RevokedAt == null && 
                    e.ExpiresAt > DateTime.UtcNow)
                .FirstOrDefaultAsync();
        }

        public async Task<EmailVerification?> GetActiveOtpByUserIdAsync(Guid userId)
        {
            //dùng để lấy otp đang active của user hiện tại
            return await _context.EmailVerifications
                .Include(e => e.User)
                .Where(e => e.UserId == userId && 
                    e.ConsumedAt == null && 
                    e.RevokedAt == null && 
                    e.ExpiresAt > DateTime.UtcNow)
                .FirstOrDefaultAsync();
        }

        public void Update(EmailVerification entity)
        {
            _context.EmailVerifications.Update(entity);
        }
    }
}