namespace Sellora.OrderService.Application.Dependencies;

public enum Dependency
{
    Organization,
    Catalog,
    Inventory
}

/// <summary>
/// A dependency timed out, is failing, or its circuit breaker is open.
/// Mapped to 503 with the dependency's name, never a generic 500.
/// </summary>
public sealed class DependencyUnavailableException : Exception
{
    public DependencyUnavailableException(Dependency dependency, Exception? inner = null)
        : base($"{dependency} service is currently unavailable, please retry shortly.", inner)
    {
        Dependency = dependency;
    }

    public Dependency Dependency { get; }
}
