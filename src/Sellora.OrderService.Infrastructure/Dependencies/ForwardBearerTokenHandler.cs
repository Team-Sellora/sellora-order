using System.Net.Http.Headers;
using Sellora.OrderService.Application.Identity;

namespace Sellora.OrderService.Infrastructure.Dependencies;

/// <summary>
/// Forwards the caller's own token so Organization and Inventory apply the
/// caller's tenant and role — Order never calls them with elevated rights.
/// </summary>
public sealed class ForwardBearerTokenHandler : DelegatingHandler
{
    private readonly IAccessTokenAccessor _tokens;

    public ForwardBearerTokenHandler(IAccessTokenAccessor tokens) => _tokens = tokens;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = _tokens.GetBearerToken();

        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
