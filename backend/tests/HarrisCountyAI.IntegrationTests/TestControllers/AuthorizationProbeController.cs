using HarrisCountyAI.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HarrisCountyAI.IntegrationTests.TestControllers;

/// <summary>
/// Test-only controller mounted by <see cref="TestApplicationFactory"/> so the
/// authorization policies can be exercised end to end before the real admin and
/// reviewer controllers exist.
/// </summary>
[ApiController]
[Route("api/authorization-probes")]
public class AuthorizationProbeController : ControllerBase
{
    [HttpGet("admin")]
    [Authorize(Policy = AuthorizationPolicies.RequireAdministrator)]
    public IActionResult AdminOnly() => Ok(new { ok = true });

    [HttpGet("reviewer")]
    [Authorize(Policy = AuthorizationPolicies.RequireReviewer)]
    public IActionResult ReviewerOnly() => Ok(new { ok = true });

    /// <summary>No attribute: protected only by the fallback policy.</summary>
    [HttpGet("fallback")]
    public IActionResult FallbackProtected() => Ok(new { ok = true });
}
