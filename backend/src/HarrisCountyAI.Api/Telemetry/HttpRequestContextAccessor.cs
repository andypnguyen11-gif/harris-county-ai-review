using System.Security.Claims;
using HarrisCountyAI.Api.Middleware;
using HarrisCountyAI.Application.Common.Telemetry;

namespace HarrisCountyAI.Api.Telemetry;

/// <summary>
/// Reads the ambient request identity out of the current <see cref="HttpContext"/>
/// for AI telemetry: the correlation id assigned by
/// <see cref="CorrelationIdMiddleware"/>, and a stable identifier for the caller.
/// </summary>
/// <remarks>
/// Everything here is null-tolerant by design. The AI services this feeds are
/// also driven from unit tests and the offline evaluation harness, where there
/// is no HTTP request at all — telemetry is an observability concern and must
/// never be the reason an answer fails to be produced.
/// </remarks>
public sealed class HttpRequestContextAccessor : IRequestContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpRequestContextAccessor(IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        _httpContextAccessor = httpContextAccessor;
    }

    public string? CorrelationId =>
        _httpContextAccessor.HttpContext?.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var value) == true
            ? value as string
            : null;

    public string? UserId
    {
        get
        {
            var principal = _httpContextAccessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            // Ordered most stable first. "oid" is the Entra ID object id, which
            // survives a username change; "sub" is the local dev token's subject.
            // JwtBearer maps "sub" onto NameIdentifier when inbound claim mapping
            // is left on, so both spellings are checked.
            return FirstNonEmpty(
                principal.FindFirst("oid")?.Value,
                principal.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                principal.FindFirst("sub")?.Value,
                principal.FindFirst("preferred_username")?.Value,
                principal.Identity.Name);
        }
    }

    private static string? FirstNonEmpty(params string?[] candidates) =>
        Array.Find(candidates, candidate => !string.IsNullOrWhiteSpace(candidate));
}
