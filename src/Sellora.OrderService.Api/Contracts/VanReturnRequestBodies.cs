namespace Sellora.OrderService.Api.Contracts;

/// <summary>POST /api/van-returns — what the rep is handing back.</summary>
public sealed class DeclareVanReturnRequestBody
{
    public IReadOnlyCollection<DeclareVanReturnLineBody>? Lines { get; init; }
}

public sealed class DeclareVanReturnLineBody
{
    public Guid ProductId { get; init; }

    public int Quantity { get; init; }
}

/// <summary>PUT /api/van-returns/{id}/acceptance — what the agency counted.</summary>
public sealed class AcceptVanReturnRequestBody
{
    public IReadOnlyCollection<AcceptVanReturnLineBody>? Lines { get; init; }

    /// <summary>Optional, e.g. why the count is short.</summary>
    public string? Note { get; init; }
}

public sealed class AcceptVanReturnLineBody
{
    public Guid ProductId { get; init; }

    public int CountedQuantity { get; init; }
}
