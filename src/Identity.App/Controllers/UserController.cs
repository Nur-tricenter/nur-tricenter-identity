using Identity.Application.Contracts;
using Identity.App.Contracts;
using Identity.App.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Identity.App.Controllers;

/// <summary>
/// Account administration. Requires <c>identity.user:manage</c>, which the built-in
/// administrator holds through the expanded wildcard and a regular account does not.
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize]
public class UserController : ControllerBase
{
    private readonly IUserCreationService _users;
    private readonly ILogger<UserController> _logger;

    public UserController(IUserCreationService users, ILogger<UserController> logger)
    {
        _users = users;
        _logger = logger;
    }

    /// <summary>
    /// Creates an account and publishes <c>UserCreated</c> through the outbox.
    /// </summary>
    /// <remarks>
    /// Answers 409 when the name is taken rather than letting the unique index turn it into a 500:
    /// a taken name is an ordinary outcome of an ordinary request, not a fault.
    /// </remarks>
    [HttpPost]
    [Authorize(Policy = IdentityPermissions.UserManage)]
    [ProducesResponseType(typeof(CreateUserResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreateUserResponse>> Create(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var outcome = await _users.CreateAsync(request, cancellationToken);

        if (!outcome.Succeeded)
        {
            _logger.LogWarning(
                "Rejected account creation for '{UserName}': {Reason}",
                request.UserName,
                outcome.Failure);

            return outcome.Conflict
                ? Conflict(new { detail = outcome.Failure })
                : BadRequest(new { detail = outcome.Failure });
        }

        return Created(
            $"/api/auth/me",
            new CreateUserResponse
            {
                Id = outcome.UserId,
                UserName = outcome.UserName,
                Scopes = IdentityPermissions.Expand(request.Scopes ?? new List<string>())
            });
    }
}