using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StealDeal.Services.Store.Application.Services.Interfaces;

namespace StealDeal.Services.Store.API.Controllers
{
    [ApiController]
    [Route("api/locations")]
    [Authorize]
    [EnableRateLimiting("LocationPolicy")]
    public class LocationController : ControllerBase
    {
        private readonly ILocationService _locationService;

        public LocationController(ILocationService locationService)
        {
            _locationService = locationService;
        }

        // GET api/locations/autocomplete?input=...&sessionToken=...
        [HttpGet("autocomplete")]
        public async Task<IActionResult> Autocomplete(
            [FromQuery] string input,
            [FromQuery] string? sessionToken = null,
            [FromQuery] decimal? latitude = null,
            [FromQuery] decimal? longitude = null,
            CancellationToken cancellationToken = default)
        {
            var result = await _locationService.AutocompleteAsync(
                input,
                sessionToken,
                latitude,
                longitude,
                cancellationToken);

            return Ok(result);
        }

        // GET api/locations/place-detail?placeId=...&sessionToken=...
        [HttpGet("place-detail")]
        public async Task<IActionResult> GetPlaceDetail(
            [FromQuery] string placeId,
            [FromQuery] string? sessionToken = null,
            CancellationToken cancellationToken = default)
        {
            var result = await _locationService.GetPlaceDetailAsync(
                placeId,
                sessionToken,
                cancellationToken);

            return Ok(result);
        }
    }
}
