using Sellora.OrderService.Domain.Entities;
using Sellora.OrderService.Domain.Orders;
using Sellora.OrderService.Infrastructure.Persistence;

namespace Sellora.OrderService.Tests;

internal sealed record Placement(Guid ShopId, Guid AgencyId, Guid TerritoryId, Guid ProvinceId)
{
    public static Placement New() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
}

internal static class TestOrders
{
    public static readonly VerificationStepRecord[] AllPassed =
        Enum.GetValues<VerificationStep>()
            .Select(step => new VerificationStepRecord(step, true, "ok"))
            .ToArray();

    /// <summary>Builds an accepted order, as the saga would, and saves it.</summary>
    public static async Task<Order> SeedAsync(
        OrderDbContext db,
        Guid companyId,
        Guid repId,
        Placement placement,
        decimal unitPrice = 100m,
        string? reference = null)
    {
        var now = DateTimeOffset.UtcNow;
        var order = Order.Create(
            companyId, placement.ShopId, repId, placement.AgencyId, placement.TerritoryId,
            placement.ProvinceId, reference ?? OrderReferenceGenerator.Generate(now), now,
            new[] { new NewOrderLine(Guid.NewGuid(), "Sunlight Soap 100g", 2, unitPrice) });

        order.CompleteVerification(Guid.NewGuid(), Guid.NewGuid(), AllPassed, now);

        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }
}
