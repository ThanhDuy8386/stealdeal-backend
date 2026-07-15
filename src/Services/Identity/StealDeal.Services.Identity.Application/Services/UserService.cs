using StealDeal.Services.Identity.Application.DTOs.Requests;
using StealDeal.Services.Identity.Application.DTOs.Responses;
using StealDeal.Services.Identity.Application.Exceptions;
using StealDeal.Services.Identity.Application.Mappings;
using StealDeal.Services.Identity.Application.Services.Interfaces;
using StealDeal.Services.Identity.Domain.Interfaces.Repositories;
using StealDeal.Services.Identity.Domain.Models;
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

            var userResponses = users.Select(u => u.ToUserResponse()).ToList();

            return new PagedResult<UserResponse>
            {
                Items = userResponses,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount
            };
        }

        public async Task<UserDetailResponse> GetUserDetail(Guid id)
        {
            var user = await _userRepository.GetByIdAsync(id);
            if (user == null)
            {
                throw new NotFoundException($"User with ID {id} not found.");
            }

            var userDetailResponse = user.ToUserDetailResponse();

            return userDetailResponse;
        }

        public async Task UpdateUser(Guid id, AdminUpdateUserRequest request)
        {
            var user = await _userRepository.GetByIdAsync(id);
            if (user == null)
            {
                throw new NotFoundException($"User with ID {id} not found.");
            }

            if (!String.IsNullOrWhiteSpace(request.FullName))
            {
                user.FullName = request.FullName;
            }

            if (!String.IsNullOrWhiteSpace(request.Email))
            {
                user.Email = request.Email;
            }

            if (!String.IsNullOrWhiteSpace(request.Phone))
            {
                user.Phone = request.Phone;
            }

            if (request.IsActive.HasValue)
            {
                user.IsActive = request.IsActive.Value;
            }

            if (request.Roles != null && request.Roles.Count > 0)
            {
                var allowedRoles = new HashSet<string> { "Customer", "Seller", "Admin" };
                foreach (var role in request.Roles)
                {
                    if (!allowedRoles.Contains(role))
                    {
                        throw new BadRequestException($"Invalid role: {role}");
                    }
                }

                user.UserRoles.Clear();
                foreach (var role in request.Roles)
                {
                    user.UserRoles.Add(new UserRole { UserId = user.Id, Role = role });
                }
            }

            _userRepository.Update(user);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task DeleteUser(Guid id)
        {
            var user = await _userRepository.GetByIdAsync(id);
            if (user == null)
            {
                throw new NotFoundException($"User with ID {id} not found.");
            }
            _userRepository.Delete(user);
            await _unitOfWork.SaveChangesAsync();
        }
    }
}