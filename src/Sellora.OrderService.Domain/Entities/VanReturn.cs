using System.Diagnostics.CodeAnalysis;
using Sellora.OrderService.Domain.Tenancy;
using Sellora.OrderService.Domain.VanReturns;

namespace Sellora.OrderService.Domain.Entities;

/// <summary>
/// US-E4-6: a rep hands unsold van stock back to their agency at the end of
/// a route. The rep declares the quantities (checked against their van stock
/// before this is created), the agency counts what actually arrived, and
/// only the counted quantities move from van to agency stock.
///
/// Not part of the Order aggregate: no order is involved. It lives in this
/// service because the rep starts it and the outbox is here.
/// </summary>
public sealed class VanReturn : ITenantScoped
{
    public const int MaxLines = 50;
    public const int MaxQuantityPerLine = 100_000;
    public const int MaxNoteLength = 500;

    private readonly List<VanReturnLine> _lines = new();

    private VanReturn()
    {
    }

    public Guid VanReturnId { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>e.g. VR-260925-K7MQ4R; also the event key.</summary>
    public string ReturnReference { get; private set; } = string.Empty;

    public Guid SalesRepId { get; private set; }

    public string? SalesRepName { get; private set; }

    /// <summary>The rep's agency, from Organization when declared. Only its operator can accept.</summary>
    public Guid AgencyId { get; private set; }

    /// <summary>Inventory's stock owner for the rep's van — the stock that is debited.</summary>
    public Guid VanInventoryOwnerId { get; private set; }

    public VanReturnStatus Status { get; private set; }

    public DateTimeOffset DeclaredAt { get; private set; }

    public string DeclaredBy { get; private set; } = string.Empty;

    public DateTimeOffset? AcceptedAt { get; private set; }

    public string? AcceptedBy { get; private set; }

    /// <summary>The operator's note on acceptance, e.g. why the count is short.</summary>
    public string? AcceptanceNote { get; private set; }

    /// <summary>PostgreSQL xmin: two operators accepting at once cannot both win.</summary>
    public uint Version { get; private set; }

    public IReadOnlyCollection<VanReturnLine> Lines => _lines.AsReadOnly();

    public int TotalDeclared => _lines.Sum(line => line.DeclaredQuantity);

    public int? TotalCounted => Status == VanReturnStatus.Accepted ? _lines.Sum(line => line.CountedQuantity ?? 0) : null;

    public int? TotalVariance => Status == VanReturnStatus.Accepted ? _lines.Sum(line => line.Variance ?? 0) : null;

    /// <summary>
    /// Creates a declared return. Van stock is checked by the caller before
    /// this runs (it needs Inventory); this enforces the shape of the lines.
    /// </summary>
    public static VanReturn Declare(
        Guid companyId,
        Guid salesRepId,
        string? salesRepName,
        Guid agencyId,
        Guid vanInventoryOwnerId,
        string declaredBy,
        IReadOnlyCollection<NewVanReturnLine> lines,
        DateTimeOffset now)
    {
        Require(companyId != Guid.Empty, "companyId is required.");
        Require(salesRepId != Guid.Empty, "salesRepId is required.");
        Require(agencyId != Guid.Empty, "agencyId is required.");
        Require(vanInventoryOwnerId != Guid.Empty, "vanInventoryOwnerId is required.");
        Require(!string.IsNullOrWhiteSpace(declaredBy), "declaredBy is required.");
        ValidateDeclaredLines(lines);

        var vanReturn = new VanReturn
        {
            VanReturnId = Guid.NewGuid(),
            CompanyId = companyId,
            ReturnReference = VanReturnReferenceGenerator.Generate(now),
            SalesRepId = salesRepId,
            SalesRepName = salesRepName,
            AgencyId = agencyId,
            VanInventoryOwnerId = vanInventoryOwnerId,
            Status = VanReturnStatus.Declared,
            DeclaredAt = now,
            DeclaredBy = declaredBy.Trim()
        };

        foreach (var line in lines)
        {
            vanReturn._lines.Add(new VanReturnLine(
                vanReturn.VanReturnId, companyId, line.ProductId, line.ProductName?.Trim(), line.DeclaredQuantity));
        }

        return vanReturn;
    }

    /// <summary>Checks a rep's declared lines; used before calling Inventory too.</summary>
    public static void ValidateDeclaredLines(IReadOnlyCollection<NewVanReturnLine>? lines)
    {
        if (lines is null || lines.Count == 0)
        {
            Fail("A van return needs at least one product.");
        }

        Require(lines.Count <= MaxLines, $"A van return can have at most {MaxLines} products.");
        Require(lines.All(line => line.ProductId != Guid.Empty), "Every line needs a productId.");
        Require(
            lines.Select(line => line.ProductId).Distinct().Count() == lines.Count,
            "Each product can appear only once; combine its quantities into one line.");
        Require(
            lines.All(line => line.DeclaredQuantity is > 0 and <= MaxQuantityPerLine),
            $"Every declared quantity must be between 1 and {MaxQuantityPerLine}.");
    }

    /// <summary>
    /// Records the agency's count for every line and accepts the return.
    /// A count can be lower than declared (missing or damaged goods) but not
    /// higher: goods the rep did not declare were not checked against their
    /// van and belong on a new return. Returns false when the same counts
    /// were already accepted (a repeated PUT changes nothing).
    /// </summary>
    public bool Accept(
        Guid agencyId,
        string acceptedBy,
        IReadOnlyCollection<VanReturnCount> counts,
        string? note,
        DateTimeOffset now)
    {
        if (agencyId == Guid.Empty || agencyId != AgencyId)
        {
            throw new VanReturnRuleException(VanReturnFailure.NotVisible, "The van return is not visible to your agency.");
        }

        Require(!string.IsNullOrWhiteSpace(acceptedBy), "acceptedBy is required.");
        var countByProduct = ValidateCounts(counts);
        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        Require(trimmedNote is null || trimmedNote.Length <= MaxNoteLength, $"note cannot exceed {MaxNoteLength} characters.");

        if (Status == VanReturnStatus.Accepted)
        {
            if (_lines.All(line => line.CountedQuantity == countByProduct[line.ProductId]))
            {
                return false;
            }

            throw new VanReturnRuleException(
                VanReturnFailure.AlreadyAccepted,
                $"Van return {ReturnReference} was already accepted with different counts; the stock has moved.");
        }

        foreach (var line in _lines)
        {
            line.RecordCount(countByProduct[line.ProductId]);
        }

        Status = VanReturnStatus.Accepted;
        AcceptedAt = now;
        AcceptedBy = acceptedBy.Trim();
        AcceptanceNote = trimmedNote;
        return true;
    }

    private Dictionary<Guid, int> ValidateCounts(IReadOnlyCollection<VanReturnCount>? counts)
    {
        if (counts is null || counts.Count == 0)
        {
            Fail("Enter a counted quantity for every product.");
        }

        Require(
            counts.Select(count => count.ProductId).Distinct().Count() == counts.Count,
            "Each product can be counted only once.");

        var declared = _lines.ToDictionary(line => line.ProductId);
        var unknown = counts.FirstOrDefault(count => !declared.ContainsKey(count.ProductId));
        Require(unknown is null, $"Product {unknown?.ProductId} is not on this van return.");

        var missing = _lines.FirstOrDefault(line => counts.All(count => count.ProductId != line.ProductId));
        Require(
            missing is null,
            $"Enter a counted quantity for {missing?.ProductNameSnapshot ?? missing?.ProductId.ToString()}.");

        foreach (var count in counts)
        {
            var line = declared[count.ProductId];
            var name = line.ProductNameSnapshot ?? line.ProductId.ToString();

            Require(count.CountedQuantity >= 0, $"{name}: the counted quantity cannot be negative.");
            Require(
                count.CountedQuantity <= line.DeclaredQuantity,
                $"{name}: counted {count.CountedQuantity} but the rep declared only {line.DeclaredQuantity}. " +
                "Goods that were not declared need a new van return.");
        }

        return counts.ToDictionary(count => count.ProductId, count => count.CountedQuantity);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            Fail(message);
        }
    }

    [DoesNotReturn]
    private static void Fail(string message) =>
        throw new VanReturnRuleException(VanReturnFailure.InvalidRequest, message);
}
