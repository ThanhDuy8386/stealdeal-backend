using StealDeal.Services.Identity.Application.DTOs.Requests;
using StealDeal.Services.Identity.Application.DTOs.Responses;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Identity.Application.Services.Interfaces
{
    public interface IUserService
    {
        Task<PagedResult<UserResponse>> GetUsers(GetUsersQueryRequest request);
        Task<UserDetailResponse> GetUserDetail(Guid id);
        Task UpdateUser(Guid id, AdminUpdateUserRequest request);
        Task DeleteUser(Guid id);

    }
}
