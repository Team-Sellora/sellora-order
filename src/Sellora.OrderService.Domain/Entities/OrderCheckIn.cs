using Sellora.OrderService.Domain.Orders;
using Sellora.OrderService.Domain.Tenancy;

namespace Sellora.OrderService.Domain.Entities;

/// <summary>
/// One check-in attempt. Rejected attempts are kept too: they are evidence
/// of where a rep actually was, and cost nothing to store.
/// </summary>
public sealed class OrderCheckIn : ITenantScoped
{
    private OrderCheckIn()
    {
    }

    internal OrderCheckIn(
        Guid orderId,
        Guid companyId,
        Guid salesRepId,
        GeoPoint reported,
        double? accuracyMeters,
        GeoPoint shop,
        double distanceMeters,
        double radiusMeters,
        bool accepted,
        DateTimeOffset capturedAt,
        DateTimeOffset recordedAt,
        DateTimeOffset expiresAt)
    {
        OrderCheckInId = Guid.NewGuid();
        OrderId = orderId;
        CompanyId = companyId;
        SalesRepId = salesRepId;
        Latitude = reported.Latitude;
        Longitude = reported.Longitude;
        AccuracyMeters = accuracyMeters;
        ShopLatitude = shop.Latitude;
        ShopLongitude = shop.Longitude;
        DistanceMeters = distanceMeters;
        RadiusMeters = radiusMeters;
        Accepted = accepted;
        CapturedAt = capturedAt;
        RecordedAt = recordedAt;
        ExpiresAt = expiresAt;
    }

    public Guid OrderCheckInId { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid SalesRepId { get; private set; }

    public double Latitude { get; private set; }

    public double Longitude { get; private set; }

    /// <summary>The device's own accuracy estimate, when it gave one.</summary>
    public double? AccuracyMeters { get; private set; }

    /// <summary>The shop coordinates used, so the decision can be re-checked later.</summary>
    public double ShopLatitude { get; private set; }

    public double ShopLongitude { get; private set; }

    public double DistanceMeters { get; private set; }

    public double RadiusMeters { get; private set; }

    public bool Accepted { get; private set; }

    public DateTimeOffset CapturedAt { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public bool IsValidFor(Guid salesRepId, DateTimeOffset now) =>
        Accepted && SalesRepId == salesRepId && ExpiresAt > now;
}
