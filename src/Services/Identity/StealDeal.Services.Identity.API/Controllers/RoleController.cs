using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StealDeal.Services.Identity.Application.DTOs.Requests;
using StealDeal.Services.Identity.Application.Services.Interfaces;

namespace StealDeal.Services.Identity.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin")]
    public class RoleController : ControllerBase
    {
        private readonly IRoleService _roleService;

        public RoleController(IRoleService roleService)
        {
            _roleService = roleService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAllRoles()
        {
            var roles = await _roleService.GetAllRolesAsync();
            return Ok(roles);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetRoleById(Guid id)
        {
            var role = await _roleService.GetRoleByIdAsync(id);
            return Ok(role);
        }

        [HttpGet("name/{name}")]
        public async Task<IActionResult> GetRoleByName(string name)
        {
            var role = await _roleService.GetRoleByNameAsync(name);
            return Ok(role);
        }

        [HttpPost]
        public async Task<IActionResult> CreateRole([FromBody] RoleRequest request)
        {
            var role = await _roleService.CreateRole(request.Name);
            return CreatedAtAction(nameof(GetRoleById), new { id = role.Id }, role);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateRole(Guid id, [FromBody] RoleRequest request)
        {

            var role = await _roleService.UpdateRole(id, request.Name);
            return Ok(role);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteRole(Guid id)
        {
            await _roleService.DeleteRole(id);
            return NoContent();
        }

    }
}
