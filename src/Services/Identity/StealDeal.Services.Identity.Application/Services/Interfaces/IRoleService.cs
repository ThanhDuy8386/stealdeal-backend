using StealDeal.Services.Identity.Application.DTOs.Responses;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Identity.Application.Services.Interfaces
{
    public interface IRoleService
    {
        Task<IEnumerable<RoleResponse>> GetAllRolesAsync();
        Task<RoleResponse> GetRoleByIdAsync(Guid id);
        Task<RoleResponse> GetRoleByNameAsync(string roleName);
        Task<RoleResponse> CreateRole(string roleName);
        Task<RoleResponse> UpdateRole(Guid id, string roleName);
        Task DeleteRole(Guid id);
    }
}
