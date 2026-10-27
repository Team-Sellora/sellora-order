namespace Sellora.OrderService.Application.Identity;

public static class SelloraRoles
{
    public const string CompanyAdmin = "CompanyAdmin";
    public const string AreaManager = "AreaManager";
    public const string AgencyOperator = "AgencyOperator";
    public const string SalesRep = "SalesRep";
    public const string ShopOwner = "ShopOwner";

    /// <summary>Broadest scope first; a multi-role user gets the widest view they hold.</summary>
    public static readonly string[] ByBreadth =
    {
        CompanyAdmin, AreaManager, AgencyOperator, SalesRep, ShopOwner
    };
}
