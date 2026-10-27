namespace Sellora.OrderService.Domain.Orders;

/// <summary>A WGS-84 coordinate. Construction rejects out-of-range values.</summary>
public readonly record struct GeoPoint
{
    public GeoPoint(double latitude, double longitude)
    {
        if (double.IsNaN(latitude) || latitude is < -90 or > 90)
        {
            throw new CheckoutRuleViolationException(
                CheckoutFailure.InvalidCoordinates,
                $"Latitude {latitude} is out of range; it must be between -90 and 90.");
        }

        if (double.IsNaN(longitude) || longitude is < -180 or > 180)
        {
            throw new CheckoutRuleViolationException(
                CheckoutFailure.InvalidCoordinates,
                $"Longitude {longitude} is out of range; it must be between -180 and 180.");
        }

        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude { get; }

    public double Longitude { get; }
}
