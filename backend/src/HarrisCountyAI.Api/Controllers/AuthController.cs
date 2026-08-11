using HarrisCountyAI.Api.Authentication;
using HarrisCountyAI.Api.Contracts.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HarrisCountyAI.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IDevTokenService? _devTokens;

    /// <summary>
    /// <paramref name="devTokens"/> is registered only when "Authentication:Mode" is
    /// "LocalDevelopment"; in every other mode the dev-token endpoint responds 404.
    /// </summary>
    public AuthController(IDevTokenService? devTokens = null)
    {
        _devTokens = devTokens;
    }

    [HttpPost("dev-token")]
    [AllowAnonymous]
    [ProducesResponseType<DevTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult IssueDevToken(DevTokenRequest request)
    {
        if (_devTokens is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Username))
        {
            ModelState.AddModelError("username", "Username is required.");
            return ValidationProblem(ModelState);
        }

        var token = _devTokens.IssueToken(request.Username);
        if (token is null)
        {
            ModelState.AddModelError("username", "Username is not in the development user allow list.");
            return ValidationProblem(ModelState);
        }

        return Ok(new DevTokenResponse(
            token.AccessToken,
            "Bearer",
            token.ExpiresAt,
            token.Username,
            token.DisplayName,
            token.Roles));
    }
}
