using Sellora.OrderService.Domain.Tenancy;

namespace Sellora.OrderService.Domain.Entities;

/// <summary>
/// US-E4-6: one product on a van return. Keeps both figures — what the rep
/// declared and what the agency counted — and the gap between them, so a
/// shortfall is recorded, never overwritten.
/// </summary>
public sealed class VanReturnLine : ITenantScoped
{
    private VanReturnLine()
    {
    }

    internal VanReturnLine(Guid vanReturnId, Guid companyId, Guid productId, string? productName, int declaredQuantity)
    {
        VanReturnLineId = Guid.NewGuid();
        VanReturnId = vanReturnId;
        CompanyId = companyId;
        ProductId = productId;
        ProductNameSnapshot = productName;
        DeclaredQuantity = declaredQuantity;
    }

    public Guid VanReturnLineId { get; private set; }

    public Guid VanReturnId { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid ProductId { get; private set; }

    /// <summary>Catalog name when declared, for the acceptance screen.</summary>
    public string? ProductNameSnapshot { get; private set; }

    public int DeclaredQuantity { get; private set; }

    /// <summary>Null until the agency counts it.</summary>
    public int? CountedQuantity { get; private set; }

    /// <summary>
    /// Declared minus counted, once counted. Positive means the agency found
    /// fewer units than the rep declared — the shrinkage signal.
    /// </summary>
    public int? Variance { get; private set; }

    internal void RecordCount(int counted)
    {
        CountedQuantity = counted;
        Variance = DeclaredQuantity - counted;
    }
}
