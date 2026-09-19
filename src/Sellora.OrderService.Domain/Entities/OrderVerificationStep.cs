using Sellora.OrderService.Domain.Orders;

namespace Sellora.OrderService.Domain.Entities;

/// <summary>
/// The recorded outcome of one verification step, kept on the order so an
/// accepted order shows exactly what was checked, without reading logs.
/// </summary>
public sealed class OrderVerificationStep
{
    private OrderVerificationStep()
    {
    }

    internal OrderVerificationStep(
        Guid orderId,
        VerificationStep step,
        bool passed,
        string detail,
        DateTimeOffset recordedAt)
    {
        OrderVerificationStepId = Guid.NewGuid();
        OrderId = orderId;
        Step = step;
        Passed = passed;
        Detail = detail;
        RecordedAt = recordedAt;
    }

    public Guid OrderVerificationStepId { get; private set; }

    public Guid OrderId { get; private set; }

    public VerificationStep Step { get; private set; }

    public bool Passed { get; private set; }

    public string Detail { get; private set; } = string.Empty;

    public DateTimeOffset RecordedAt { get; private set; }
}

public sealed record VerificationStepRecord(
    VerificationStep Step,
    bool Passed,
    string Detail);
