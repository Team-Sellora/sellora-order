using Sellora.OrderService.Application.Orders;
using Sellora.OrderService.Domain.Entities;

namespace Sellora.OrderService.Application.VanReturns;

public sealed record DeclareVanReturnLine(Guid ProductId, int Quantity);

public sealed record DeclareVanReturnRequest(IReadOnlyCollection<DeclareVanReturnLine> Lines);

public sealed record AcceptVanReturnLine(Guid ProductId, int CountedQuantity);

public sealed record AcceptVanReturnRequest(IReadOnlyCollection<AcceptVanReturnLine> Lines, string? Note);

public sealed record VanReturnListQuery(int Page, int PageSize, string? Status);

public enum VanReturnOutcome
{
    Succeeded,
    InvalidRequest,
    TenantNotAvailable,
    CallerNotPermitted,
    NotFound,
    NoVanStock,
    ExceedsVanStock,
    Conflict,
    DependencyUnavailable
}

/// <summary>A product the rep (or, at acceptance, the van) does not hold enough of.</summary>
public sealed record VanStockShortage(Guid ProductId, string? ProductName, int RequestedQuantity, int HeldQuantity);

public sealed record VanReturnResult(
    VanReturnOutcome Outcome,
    VanReturnResponse? VanReturn = null,
    string? Message = null,
    IReadOnlyCollection<VanStockShortage>? Shortages = null,
    string? Dependency = null,
    bool Changed = false)
{
    public static VanReturnResult Failed(VanReturnOutcome outcome, string message) => new(outcome, Message: message);

    public static VanReturnResult Done(VanReturnResponse vanReturn, bool changed) =>
        new(VanReturnOutcome.Succeeded, vanReturn, Changed: changed);
}

public sealed record VanReturnLineResponse(
    Guid VanReturnLineId,
    Guid ProductId,
    string? ProductName,
    int DeclaredQuantity,
    int? CountedQuantity,
    int? Variance);

/// <summary>US-E4-6: a van return with both figures and the variance per line and in total.</summary>
public sealed record VanReturnResponse(
    Guid VanReturnId,
    string ReturnReference,
    string Status,
    Guid SalesRepId,
    string? SalesRepName,
    Guid AgencyId,
    Guid VanInventoryOwnerId,
    DateTimeOffset DeclaredAt,
    string DeclaredBy,
    DateTimeOffset? AcceptedAt,
    string? AcceptedBy,
    string? AcceptanceNote,
    int TotalDeclared,
    int? TotalCounted,
    int? TotalVariance,
    IReadOnlyList<VanReturnLineResponse> Lines)
{
    public static VanReturnResponse From(VanReturn vanReturn) => new(
        vanReturn.VanReturnId,
        vanReturn.ReturnReference,
        vanReturn.Status.ToString(),
        vanReturn.SalesRepId,
        vanReturn.SalesRepName,
        vanReturn.AgencyId,
        vanReturn.VanInventoryOwnerId,
        vanReturn.DeclaredAt,
        vanReturn.DeclaredBy,
        vanReturn.AcceptedAt,
        vanReturn.AcceptedBy,
        vanReturn.AcceptanceNote,
        vanReturn.TotalDeclared,
        vanReturn.TotalCounted,
        vanReturn.TotalVariance,
        vanReturn.Lines
            .OrderBy(line => line.ProductNameSnapshot ?? string.Empty)
            .ThenBy(line => line.ProductId)
            .Select(line => new VanReturnLineResponse(
                line.VanReturnLineId,
                line.ProductId,
                line.ProductNameSnapshot,
                line.DeclaredQuantity,
                line.CountedQuantity,
                line.Variance))
            .ToList());
}

public sealed record VanReturnSummaryResponse(
    Guid VanReturnId,
    string ReturnReference,
    string Status,
    Guid SalesRepId,
    string? SalesRepName,
    Guid AgencyId,
    DateTimeOffset DeclaredAt,
    DateTimeOffset? AcceptedAt,
    int LineCount,
    int TotalDeclared,
    int? TotalCounted,
    int? TotalVariance);

public interface IVanReturnService
{
    /// <summary>A rep declares unsold van stock, checked against their van first.</summary>
    Task<VanReturnResult> DeclareAsync(DeclareVanReturnRequest request, CancellationToken cancellationToken);

    /// <summary>The rep's own agency operator records the count; publishes VanStockReturned.</summary>
    Task<VanReturnResult> AcceptAsync(Guid vanReturnId, AcceptVanReturnRequest request, CancellationToken cancellationToken);

    Task<PagedResponse<VanReturnSummaryResponse>> ListAsync(VanReturnListQuery query, CancellationToken cancellationToken);

    Task<VanReturnResponse?> GetAsync(Guid vanReturnId, CancellationToken cancellationToken);
}
