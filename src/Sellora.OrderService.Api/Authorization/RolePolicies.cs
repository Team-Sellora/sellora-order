using Microsoft.AspNetCore.Authorization;

namespace Sellora.OrderService.Api.Authorization;

// Copied from sellora-organization's RolePolicies.cs (namespace changed),
// plus RequireOrderReader for the scoped GET endpoints.
public static class RolePolicies
{
    public const string RequireCompanyAdmin = "RequireCompanyAdmin";
    public const string RequireAreaManager = "RequireAreaManager";
    public const string RequireAgencyOperator = "RequireAgencyOperator";
    public const string RequireSalesRep = "RequireSalesRep";
    public const string RequireShopOwner = "RequireShopOwner";
    public const string RequireOrderReader = "RequireOrderReader";

    public static void AddSelloraRolePolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(RequireCompanyAdmin, p => p.RequireAssertion(HasRole("CompanyAdmin")));
        options.AddPolicy(RequireAreaManager, p => p.RequireAssertion(HasRole("AreaManager")));
        options.AddPolicy(RequireAgencyOperator, p => p.RequireAssertion(HasRole("AgencyOperator")));
        options.AddPolicy(RequireSalesRep, p => p.RequireAssertion(HasRole("SalesRep")));
        options.AddPolicy(RequireShopOwner, p => p.RequireAssertion(HasRole("ShopOwner")));
        options.AddPolicy(RequireOrderReader, p => p.RequireAssertion(HasAnyRole(
            "CompanyAdmin", "AreaManager", "AgencyOperator", "SalesRep", "ShopOwner")));
    }

    // WSO2 IS emits the "roles" claim (plural, an array). Program.cs sets
    // RoleClaimType = "roles" and MapInboundClaims = false, so "roles" arrives
    // unmapped — match on "roles" directly, not ClaimTypes.Role.
    private static Func<AuthorizationHandlerContext, bool> HasRole(string role) =>
        ctx => ctx.User.HasClaim("roles", role);

    private static Func<AuthorizationHandlerContext, bool> HasAnyRole(params string[] roles) =>
        ctx => roles.Any(r => ctx.User.HasClaim("roles", r));
}
