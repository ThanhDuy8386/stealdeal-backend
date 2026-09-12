using StealDeal.Services.Identity.Domain.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Identity.Domain.Interfaces.Repositories
{
    public interface IPasswordResetRepository
    {
        Task AddAsync(PasswordReset reset);
        Task<PasswordReset?> GetActiveByUserIdAsync(Guid userId);
        void Update(PasswordReset reset);
    }
}
