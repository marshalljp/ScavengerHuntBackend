using System;

namespace ScavengerHuntBackend.Utils
{
    public static class GPSValidator
    {
        private const double EarthRadiusKm = 6371.0;

        public static bool IsWithinRange(double targetLat, double targetLon, double userLat, double userLon, double allowedRadiusMeters)
        {
            double dLat = DegreesToRadians(userLat - targetLat);
            double dLon = DegreesToRadians(userLon - targetLon);
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(DegreesToRadians(targetLat)) * Math.Cos(DegreesToRadians(userLat)) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            double distance = EarthRadiusKm * c * 1000; // Convert to meters

            return distance <= allowedRadiusMeters;
        }

        private static double DegreesToRadians(double degrees)
        {
            return degrees * (Math.PI / 180.0);
        }
    }
}