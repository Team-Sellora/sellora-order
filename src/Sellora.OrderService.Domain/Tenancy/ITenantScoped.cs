namespace Sellora.OrderService.Domain.Tenancy;

public interface ITenantScoped
{
    Guid CompanyId { get; }
}
