using Sellora.OrderService.Domain.Orders;

namespace Sellora.OrderService.Application.Checkout;

/// <summary>Bound from the "CheckIn" configuration section.</summary>
public sealed class CheckInOptions
{
    public const string Section = "CheckIn";

    /// <summary>Open issue #3: the GPS tolerance is 300 m.</summary>
    public double RadiusMeters { get; set; } = 300;

    public int ValidityMinutes { get; set; } = 10;

    public int MaxClockSkewSeconds { get; set; } = 120;

    public int MaxCaptureAgeSeconds { get; set; } = 300;

    public CheckInPolicy ToPolicy() => new(
        RadiusMeters,
        TimeSpan.FromMinutes(ValidityMinutes),
        TimeSpan.FromSeconds(MaxClockSkewSeconds),
        TimeSpan.FromSeconds(MaxCaptureAgeSeconds));
}
