namespace Sellora.OrderService.Domain.Orders;

/// <summary>
/// Great-circle distance with the Haversine formula. Accurate to well under
/// a metre at the 300 m scale this is used for; PostGIS is not needed.
/// </summary>
public static class GeoDistance
{
    /// <summary>IUGG mean Earth radius in metres.</summary>
    public const double EarthRadiusMeters = 6_371_008.8;

    /// <summary>
    /// Distance in metres, rounded to the nearest centimetre. Rounding makes
    /// the radius comparison deterministic: a point constructed exactly
    /// 300 m away measures 300.00, not 300.0000000001.
    /// </summary>
    public static double Meters(GeoPoint from, GeoPoint to)
    {
        var phi1 = ToRadians(from.Latitude);
        var phi2 = ToRadians(to.Latitude);
        var deltaPhi = ToRadians(to.Latitude - from.Latitude);
        var deltaLambda = ToRadians(to.Longitude - from.Longitude);

        var a = Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2) +
                Math.Cos(phi1) * Math.Cos(phi2) *
                Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return Math.Round(EarthRadiusMeters * c, 2, MidpointRounding.AwayFromZero);
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
