namespace Sellora.OrderService.Domain.Orders;

/// <summary>
/// Order lifecycle. US-E4-1a only creates orders in <see cref="Submitted"/>;
/// later stories (1b, 2, 3, 5) add their own states here.
/// </summary>
public enum OrderStatus
{
    Submitted = 0
}
