using System;
using System.Threading.Tasks;

namespace StealDeal.Services.Store.Application.Services.Interfaces
{
    public interface IOrderVerificationService
    {
        /// <summary>
        /// Verifies that the order exists, belongs to the specified buyer, contains the specified bag, and is in an eligible status (e.g. Completed).
        /// </summary>
        Task<bool> VerifyOwnershipAsync(Guid orderId, Guid buyerId, Guid bagId);
    }
}
