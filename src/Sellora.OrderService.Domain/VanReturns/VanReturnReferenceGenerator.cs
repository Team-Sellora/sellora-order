using Sellora.OrderService.Domain.Orders;

namespace Sellora.OrderService.Domain.VanReturns;

/// <summary>
/// References like <c>VR-260925-K7MQ4R</c>: same readable alphabet as order
/// references, a different prefix so the two are never confused.
/// </summary>
public static class VanReturnReferenceGenerator
{
    public const string Prefix = "VR";

    public static string Generate(DateTimeOffset declaredAt) =>
        Prefix + OrderReferenceGenerator.Generate(declaredAt)[OrderReferenceGenerator.Prefix.Length..];
}
