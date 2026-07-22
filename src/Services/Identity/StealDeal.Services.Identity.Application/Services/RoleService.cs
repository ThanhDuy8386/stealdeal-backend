using StealDeal.Services.Identity.Application.DTOs.Responses;
using StealDeal.Services.Identity.Application.Exceptions;
using StealDeal.Services.Identity.Application.Services.Interfaces;
using StealDeal.Services.Identity.Domain.Interfaces.Repositories;
using StealDeal.Services.Identity.Application.Mappings;
using StealDeal.Services.Identity.Domain.Models;

namespace StealDeal.Services.Identity.Application.Services
{
    public class RoleService : IRoleService
    {
        private const int MaxRoleNameLength = 50;
        private readonly IRoleRepository _roleRepository;
        private readonly IUnitOfWork _unitOfWork;
        public RoleService(IRoleRepository roleRepository, IUnitOfWork unitOfWork)
        {
            _roleRepository = roleRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<RoleResponse> CreateRole(string roleName)
        {
            roleName = ValidateRoleName(roleName);

            var existingRole = await _roleRepository.GetByNameAsync(roleName);
            if (existingRole != null)
            {
                throw new ConflictException($"Role with name '{roleName}' already exists.");
            }

            var role = new Role
            {
                Name = roleName
            };
            await _roleRepository.AddAsync(role);
            await _unitOfWork.SaveChangesAsync();

            return role.ToRoleResponse();
        }

        public async Task<RoleResponse> UpdateRole(Guid id, string roleName)
        {
            var role = await _roleRepository.GetByIdAsync(id);
            if (role == null)
            {
                throw new NotFoundException($"Role with ID {id} not found.");
            }

            if (IsSystemRole(role.Name))
            {
                throw new ConflictException($"Cannot update system role {role.Name}.");
            }

            roleName = ValidateRoleName(roleName);

            var roleWithSameName = await _roleRepository.GetByNameAsync(roleName);

            if (roleWithSameName != null && roleWithSameName.Id != id)
            {
                throw new ConflictException($"Role with name '{roleName}' already exists.");
            }

            role.Name = roleName;

            // GetByIdAsync already tracks the entity, so we don't need to call Update explicitly.

            await _unitOfWork.SaveChangesAsync();
            return role.ToRoleResponse();
        }

        public async Task DeleteRole(Guid id)
        {
            var role = await _roleRepository.GetByIdAsync(id);
            if (role == null)
            {
                throw new NotFoundException($"Role with ID {id} not found.");
            }
            
            if (IsSystemRole(role.Name))
            {
                throw new ConflictException($"Cannot delete system role {role.Name}.");
            }

            var isAssigned = await _roleRepository.IsRoleAssignedAsync(role.Id);
            if (isAssigned)
            {
                throw new ConflictException($"Cannot delete role '{role.Name}' because it is assigned to one or more users.");
            }

            _roleRepository.Delete(role);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task<IEnumerable<RoleResponse>> GetAllRolesAsync()
        {
            var roles = await _roleRepository.GetAllAsync();
            return roles.Select(r => r.ToRoleResponse()).ToList();
        }

        public async Task<RoleResponse> GetRoleByIdAsync(Guid id)
        {
            var role = await _roleRepository.GetByIdAsync(id);
            if (role == null)
            {
                throw new NotFoundException($"Role with ID {id} not found.");
            }
            return role.ToRoleResponse();
        }

        public async Task<RoleResponse> GetRoleByNameAsync(string roleName)
        {
            roleName = ValidateRoleName(roleName);

            var role = await _roleRepository.GetByNameAsync(roleName);
            if (role == null)
            {
                throw new NotFoundException($"Role with name '{roleName}' not found.");
            }
            return role.ToRoleResponse();
        }

        private static string ValidateRoleName(string roleName)
        {

            if (string.IsNullOrWhiteSpace(roleName))
            {
                throw new BadRequestException("Role name cannot be null or empty.");
            }
            roleName = roleName.Trim();
            if (roleName.Length > MaxRoleNameLength)
            {
                throw new BadRequestException($"Role name cannot exceed {MaxRoleNameLength} characters.");
            }
            return roleName;
        }

        private static bool IsSystemRole(string roleName)
        {
            return string.Equals(roleName, "Admin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(roleName, "Customer", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(roleName, "Seller", StringComparison.OrdinalIgnoreCase);
        }
    }
}
