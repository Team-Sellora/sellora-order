namespace Sellora.OrderService.Domain.Tenancy;

/// <summary>
/// Establishes an explicit tenant scope for trusted background processing
/// (the outbox relay in US-E4-4 will need it).
/// </summary>
public interface ISystemTenantContext
{
    IDisposable BeginSystemTenantScope(Guid companyId);
}
