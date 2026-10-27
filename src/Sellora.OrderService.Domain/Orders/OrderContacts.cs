namespace Sellora.OrderService.Domain.Orders;

/// <summary>Contact details snapshotted on an order when it is placed (US-E4-4).</summary>
public sealed record OrderContacts(
    string? ShopName,
    string? ShopOwnerName,
    string? ShopOwnerEmail,
    string? AgencyName,
    string? AgencyEmail,
    string? SalesRepName);
