namespace HarrisCountyAI.Application.Common.Telemetry;

/// <summary>
/// Supplies the ambient request identity that AI telemetry has to be stamped
/// with. The correlation id and the caller's identity are HTTP concerns, but
/// only the Application layer knows which AI call they belong to, so they
/// reach it through this abstraction rather than through a dependency on
/// ASP.NET Core.
/// </summary>
/// <remarks>
/// Implementations must never throw when there is no ambient request: AI
/// services are also exercised from tests and from the offline evaluation
/// harness, where no HTTP request exists. Both members return null in that
/// case and the caller substitutes a placeholder.
/// </remarks>
public interface IRequestContextAccessor
{
    /// <summary>
    /// Correlation id of the request in flight, or null outside a request.
    /// </summary>
    string? CorrelationId { get; }

    /// <summary>
    /// Stable identifier of the caller, or null when the request is anonymous
    /// or there is no request.
    /// </summary>
    string? UserId { get; }
}
