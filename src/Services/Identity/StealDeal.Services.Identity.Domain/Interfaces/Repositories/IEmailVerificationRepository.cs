using StealDeal.Services.Identity.Domain.Models;

namespace StealDeal.Services.Identity.Domain.Interfaces.Repositories
{
    public interface IEmailVerificationRepository
    {
        Task AddAsync(EmailVerification entity);
        Task<EmailVerification?> GetActiveOtpByUserIdAsync(Guid userId);
        Task<EmailVerification?> VerifyOtp(string email, string otpHash);
        void Update(EmailVerification entity);
    }
}
