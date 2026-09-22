using Microsoft.AspNetCore.Mvc;

namespace StealDeal.Services.Cart.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HealthController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            return Ok(new
            {
                Service = "Cart",
                Status = "Healthy",
                TimestampUtc = DateTime.UtcNow
            });
        }
    }
}
