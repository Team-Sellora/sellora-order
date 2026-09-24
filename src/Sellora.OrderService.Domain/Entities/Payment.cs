using Sellora.OrderService.Domain.Orders;
using Sellora.OrderService.Domain.Tenancy;

namespace Sellora.OrderService.Domain.Entities;

/// <summary>
/// A cash collection. Carries its own location evidence (the check-in that
/// permitted it), so a disputed collection has data behind it.
/// </summary>
public sealed class Payment : ITenantScoped
{
    private Payment()
    {
    }

    internal Payment(
        Guid orderId,
        Guid companyId,
        decimal amount,
        PaymentMethod method,
        Guid salesRepId,
        OrderCheckIn checkIn,
        DateTimeOffset recordedAt)
    {
        PaymentId = Guid.NewGuid();
        OrderId = orderId;
        CompanyId = companyId;
        Amount = amount;
        Method = method;
        SalesRepId = salesRepId;
        CheckInId = checkIn.OrderCheckInId;
        Latitude = checkIn.Latitude;
        Longitude = checkIn.Longitude;
        DistanceMeters = checkIn.DistanceMeters;
        RecordedAt = recordedAt;
    }

    public Guid PaymentId { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid CompanyId { get; private set; }

    public decimal Amount { get; private set; }

    public PaymentMethod Method { get; private set; }

    public Guid SalesRepId { get; private set; }

    public Guid CheckInId { get; private set; }

    public double Latitude { get; private set; }

    public double Longitude { get; private set; }

    public double DistanceMeters { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }
}
