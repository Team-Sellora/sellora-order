using Microsoft.Net.Http.Headers;
using Sellora.OrderService.Application.Identity;

namespace Sellora.OrderService.Api.Identity;

public sealed class HttpAccessTokenAccessor(IHttpContextAccessor accessor) : IAccessTokenAccessor
{
    public string? GetBearerToken()
    {
        var header = accessor.HttpContext?.Request.Headers[HeaderNames.Authorization].ToString();

        return header is not null && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : null;
    }
}
