using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StealDeal.Services.Identity.Application.DTOs.Requests;
using StealDeal.Services.Identity.Application.Services.Interfaces;

namespace StealDeal.Services.Identity.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class AdminController : ControllerBase
    {
        private readonly IAdminService _adminService;

        public AdminController(IAdminService adminService)
        {
            _adminService = adminService;
        }

        [HttpPost]
        public async Task<IActionResult> CreateAdmin([FromBody] CreateAdminRequest request)
        {
            var result = await _adminService.CreateAdmin(request);
            return CreatedAtAction(nameof(GetAdminDetail), new { id = result.Id }, result);
        }

        [HttpGet]
        public async Task<IActionResult> GetAdmins([FromQuery] GetAdminsQueryRequest request)
        {
            var result = await _adminService.GetAdmins(request);
            return Ok(result);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetAdminDetail(Guid id)
        {
            var result = await _adminService.GetAdminDetail(id);
            return Ok(result);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateAdmin(Guid id, [FromBody] UpdateAdminRequest request)
        {
            await _adminService.UpdateAdmin(id, request);
            return Ok();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAdmin(Guid id)
        {
            await _adminService.DeleteAdmin(id);
            return NoContent();
        }
    }
}
