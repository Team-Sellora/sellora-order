namespace Sellora.OrderService.Application.Orders;

public sealed record OrderListQuery(int Page = 1, int PageSize = 50)
{
    public const int MaxPageSize = 200;

    public int SafePage => Page < 1 ? 1 : Page;

    public int SafePageSize => PageSize switch
    {
        < 1 => 50,
        > MaxPageSize => MaxPageSize,
        _ => PageSize
    };
}
