using StealDeal.Services.Identity.Application.DTOs.Requests;
using StealDeal.Services.Identity.Application.DTOs.Responses;

namespace StealDeal.Services.Identity.Application.Services.Interfaces
{
    public interface IAdminService
    {
        Task<AdminDetailResponse> CreateAdmin(CreateAdminRequest request);
        Task<PagedResult<AdminResponse>> GetAdmins(GetAdminsQueryRequest request);
        Task<AdminDetailResponse> GetAdminDetail(Guid id);
        Task UpdateAdmin(Guid id, UpdateAdminRequest request);
        Task DeleteAdmin(Guid id);
    }
}
