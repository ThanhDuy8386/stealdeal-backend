using StealDeal.Services.Identity.Application.DTOs.Requests;
using StealDeal.Services.Identity.Application.DTOs.Responses;
using StealDeal.Services.Identity.Application.Services.Interfaces;
using StealDeal.Services.Identity.Domain.Interfaces.Repositories;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Identity.Application.Services
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly IUnitOfWork _unitOfWork;

        public UserService(IUserRepository userRepository, IUnitOfWork unitOfWork)
        {
            _userRepository = userRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<PagedResult<UserResponse>> GetUsers(GetUsersQueryRequest request)
        {
            // these can be nulls
            bool? isActive = request.AccountStatus?.ToLower() switch
            {
                "active" => true,
                "inactive" => (bool?)false,
                _ => null
            };

            // this can't be null, so need default values
            int page = request.Page ?? 1;
            int pageSize = request.PageSize ?? 10;

            var (users, totalCount) = await _userRepository.GetUsersAsync(request.SearchTerm, request.Role, isActive, page, pageSize);

            var userResponses = users.Select(u => new UserResponse
            {
                Id = u.Id,
                Email = u.Email,
                Phone = u.Phone,
                FullName = u.FullName,
                AvatarUrl = u.AvatarUrl,
                IsEmailVerified = u.IsEmailVerified,
                IsActive = u.IsActive,
                CreatedAt = u.CreatedAt,
                Roles = u.UserRoles.Select(r => r.Role).ToList()
            }).ToList();

            return new PagedResult<UserResponse>
            {
                Items = userResponses,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount
            };
        }
    }
}
