namespace Sellora.OrderService.Api.Contracts;

/// <summary>POST /api/orders/{id}/checkin — the rep's device position.</summary>
public sealed class CheckInRequestBody
{
    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    /// <summary>When the device took the GPS fix (ISO 8601 with offset).</summary>
    public DateTimeOffset? CapturedAt { get; init; }

    /// <summary>Optional: the device's own accuracy estimate in metres.</summary>
    public double? AccuracyMeters { get; init; }
}

/// <summary>POST /api/orders/{id}/payment — cash only (FR-4.4a).</summary>
public sealed class PaymentRequestBody
{
    public decimal? Amount { get; init; }

    public string? Method { get; init; }
}
