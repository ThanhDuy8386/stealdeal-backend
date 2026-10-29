using System;

namespace StealDeal.Services.Store.Application.Common
{
    public static class GeoCalculator
    {
        private const double EarthRadiusKm = 6371.0;

        public static (decimal MinLat, decimal MaxLat, decimal MinLng, decimal MaxLng) CalculateBoundingBox(
            decimal latitude, decimal longitude, double radiusInKm)
        {
            var lat = (double)latitude;
            var lng = (double)longitude;

            var dLat = radiusInKm / 111.0;
            var cosLat = Math.Cos(lat * Math.PI / 180.0);
            var dLng = Math.Abs(cosLat) > 0.0001 ? radiusInKm / (111.0 * cosLat) : radiusInKm / 111.0;

            return (
                (decimal)Math.Max(-90.0, lat - dLat),
                (decimal)Math.Min(90.0, lat + dLat),
                (decimal)Math.Max(-180.0, lng - dLng),
                (decimal)Math.Min(180.0, lng + dLng)
            );
        }

        public static double CalculateDistanceKm(decimal lat1, decimal lon1, decimal lat2, decimal lon2)
        {
            var rLat1 = (double)lat1 * Math.PI / 180.0;
            var rLat2 = (double)lat2 * Math.PI / 180.0;
            var dLat = ((double)lat2 - (double)lat1) * Math.PI / 180.0;
            var dLon = ((double)lon2 - (double)lon1) * Math.PI / 180.0;

            var a = Math.Sin(dLat / 2.0) * Math.Sin(dLat / 2.0) +
                    Math.Cos(rLat1) * Math.Cos(rLat2) *
                    Math.Sin(dLon / 2.0) * Math.Sin(dLon / 2.0);

            var c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
            return Math.Round(EarthRadiusKm * c, 2);
        }
    }
}
