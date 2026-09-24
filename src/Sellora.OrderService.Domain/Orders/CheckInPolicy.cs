namespace Sellora.OrderService.Domain.Orders;

/// <summary>
/// Tunable limits for the GPS gate, read from configuration so the radius
/// can change without a redeploy.
/// </summary>
/// <param name="RadiusMeters">Accepted when the distance is ≤ this (the exact radius is inside).</param>
/// <param name="Validity">How long an accepted check-in permits payment.</param>
/// <param name="MaxClockSkew">How far in the future a device timestamp may be.</param>
/// <param name="MaxCaptureAge">How old a GPS fix may be, so an old fix cannot be replayed.</param>
public sealed record CheckInPolicy(
    double RadiusMeters,
    TimeSpan Validity,
    TimeSpan MaxClockSkew,
    TimeSpan MaxCaptureAge)
{
    public static CheckInPolicy Default { get; } = new(
        300,
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5));
}

public enum PaymentMethod
{
    /// <summary>The only method in scope (FR-4.4a): no gateway, no cards.</summary>
    Cash = 1
}
