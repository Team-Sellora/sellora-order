namespace Sellora.OrderService.Application.Identity;

/// <summary>The caller's raw bearer token, for forwarding to [Authorize] dependencies.</summary>
public interface IAccessTokenAccessor
{
    string? GetBearerToken();
}
