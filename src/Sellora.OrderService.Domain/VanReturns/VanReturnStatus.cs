namespace Sellora.OrderService.Domain.VanReturns;

/// <summary>US-E4-6: where an end-of-route van return stands. Stored as text.</summary>
public enum VanReturnStatus
{
    /// <summary>The rep declared what they are handing back; the agency has not counted it yet.</summary>
    Declared = 1,

    /// <summary>The agency counted it; the counted quantities move to agency stock.</summary>
    Accepted = 2
}
