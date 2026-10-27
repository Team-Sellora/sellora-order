using Sellora.OrderService.Application.Identity;
using Sellora.OrderService.Domain.Tenancy;

namespace Sellora.OrderService.Tests;

internal sealed class TenantStub(Guid? companyId) : ITenantContext
{
    public Guid? CompanyId { get; } = companyId;
}

internal sealed class CallerStub : ICurrentUserContext
{
    public string? Subject { get; init; } = "test-sub";
    public string? Role { get; init; }
    public Guid? SalesRepId { get; init; }
    public Guid? AgencyId { get; init; }
    public Guid? ShopId { get; init; }
    public IReadOnlyCollection<Guid> ProvinceIds { get; init; } = Array.Empty<Guid>();
    public string? DisplayName { get; init; } = "Ruwan Dias";
}

internal sealed class FixedCorrelation(string correlationId) : Sellora.OrderService.Application.Outbox.ICorrelationIdAccessor
{
    public string GetCorrelationId() => correlationId;
}
