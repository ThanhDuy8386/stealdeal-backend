using System;
using StealDeal.Services.Store.Application.Exceptions;

namespace StealDeal.Services.Store.Application.DTOs.Requests
{
    public class NearbyQueryRequest
    {
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public decimal? Lat { get; set; }
        public decimal? Lng { get; set; }
        public double RadiusInKm { get; set; } = 10.0;
        public int Limit { get; set; } = 20;
        public string? Status { get; set; }

        public (decimal Latitude, decimal Longitude) ResolveCoordinates()
        {
            var lat = Latitude ?? Lat;
            var lng = Longitude ?? Lng;

            if (!lat.HasValue)
                throw new BadRequestException("Latitude is required.");

            if (!lng.HasValue)
                throw new BadRequestException("Longitude is required.");

            if (lat.Value < -90m || lat.Value > 90m)
                throw new BadRequestException("Latitude must be between -90 and 90 degrees.");

            if (lng.Value < -180m || lng.Value > 180m)
                throw new BadRequestException("Longitude must be between -180 and 180 degrees.");

            return (lat.Value, lng.Value);
        }

        public double GetClampedRadius() => Math.Clamp(RadiusInKm <= 0 ? 10.0 : RadiusInKm, 0.1, 100.0);

        public int GetClampedLimit() => Math.Clamp(Limit <= 0 ? 20 : Limit, 1, 100);
    }
}
