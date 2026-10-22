using Identity.Application.Contracts;
using Identity.App.Contracts;
using Identity.App.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Nur.Tricenter.Security;

namespace Identity.App.Controllers;

/// <summary>
/// Authentication. Two endpoints, and deliberately no more: the whole reason this service
/// exists is to turn a credential into a token.
/// </summary>
[ApiController]
/// <remarks>
/// The <c>/api/v1</c> prefix is the platform convention, matching the catalog's controllers
/// (<c>[Route("api/v1/foods")]</c>). It is not cosmetic: the API gateway applies
/// <c>PathPrefix: /api</c> when it forwards, so a service exposing <c>/api/...</c> would be
/// requested as <c>/api/api/...</c> upstream and 404. Aligning here is what lets one gateway
/// route every service without knowing anything about its internal shape.
/// </remarks>
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly ILoginService _login;
    private readonly ITokenIssuer _issuer;
    private readonly JwtOptions _jwt;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        ILoginService login,
        ITokenIssuer issuer,
        IOptions<JwtOptions> jwt,
        ILogger<AuthController> logger)
    {
        _login = login;
        _issuer = issuer;
        _jwt = jwt.Value;
        _logger = logger;
    }

    /// <summary>
    /// Exchanges a user name and a password for a bearer token.
    /// </summary>
    /// <remarks>
    /// Anonymous by necessity: this is the one route a caller cannot already hold a token for.
    /// Every rejection returns 401 with the same body, so the endpoint cannot be used to discover
    /// which user names exist.
    /// </remarks>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var outcome = await _login.LoginAsync(
            request.UserName,
            request.Password,
            cancellationToken);

        if (!outcome.Succeeded || outcome.User is null)
        {
            // The account name is logged, the password never is. The failure detail from the
            // service is deliberately not echoed to the caller.
            _logger.LogWarning("Failed login attempt for '{UserName}'.", request.UserName);
            return Unauthorized();
        }

        var token = _issuer.Issue(outcome.User, outcome.Scopes);

        return Ok(new LoginResponse
        {
            AccessToken = token,
            ExpiresIn = _jwt.Lifetime,
            UserName = outcome.User.UserName,
            IsAdministrator = outcome.User.IsAdministrator,
            Scopes = outcome.Scopes
        });
    }

    /// <summary>
    /// The authenticated principal, read back from the token rather than from the database.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(MeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<MeResponse> Me()
    {
        var identifier = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                         ?? User.FindFirst("sub")?.Value;

        if (!Guid.TryParse(identifier, out var id))
        {
            return Unauthorized();
        }

        var scopes = User.FindAll("scope")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return Ok(new MeResponse
        {
            Id = id,
            UserName = User.Identity?.Name ?? string.Empty,
            Email = User.FindFirst("email")?.Value ?? string.Empty,
            IsAdministrator = User.HasClaim(IdentityPermissions.All, "true"),
            Scopes = scopes
        });
    }
}