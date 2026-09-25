namespace Sellora.OrderService.Api.Contracts;

/// <summary>PUT /api/orders/{id}/approval — the agency's decision.</summary>
public sealed class ApprovalRequestBody
{
    /// <summary>Approve or Reject.</summary>
    public string? Decision { get; init; }

    /// <summary>Mandatory when rejecting.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// POST /api/orders/{id}/cancellation — the shop owner's cancellation. There
/// is deliberately no time field: the window is computed on the server.
/// </summary>
public sealed class CancellationRequestBody
{
    public string? Reason { get; init; }
}
