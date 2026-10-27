namespace Sellora.OrderService.Domain.Tenancy;

public interface ITenantContext
{
    Guid? CompanyId { get; }
}
