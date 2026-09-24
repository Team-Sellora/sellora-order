using Sellora.OrderService.Domain.Orders;

namespace Sellora.OrderService.Tests;

/// <summary>
/// Builds points a deliberate distance from a shop, instead of estimating.
/// Moving due north changes only latitude, so the Haversine distance is
/// exactly R × Δφ — which makes the 299/300/301 m tests precise.
/// </summary>
internal static class GeoTestPoints
{
    public static readonly GeoPoint Shop = new(6.8960, 79.8556);

    public static GeoPoint NorthOf(GeoPoint origin, double meters) =>
        new(origin.Latitude + meters / GeoDistance.EarthRadiusMeters * 180 / Math.PI, origin.Longitude);
}

internal sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
