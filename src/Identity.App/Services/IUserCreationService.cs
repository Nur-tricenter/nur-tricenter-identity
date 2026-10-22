using Identity.App.Contracts;
using Identity.Application.Domain;

namespace Identity.App.Services;

/// <summary>Creates accounts.</summary>
public interface IUserCreationService
{
    Task<UserCreationOutcome> CreateAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>The result of a creation attempt.</summary>
/// <param name="Succeeded">Whether an account was created.</param>
/// <param name="UserId">The new account's id, when one was created.</param>
/// <param name="UserName">The new account's login name, when one was created.</param>
/// <param name="Conflict">
/// Set when the attempt failed because the login name is taken. Distinct from a generic failure so
/// the endpoint can answer 409 instead of 400, and so a caller can tell "that name is gone" from
/// "your request was malformed".
/// </param>
public sealed record UserCreationOutcome(
    bool Succeeded,
    Guid UserId,
    string UserName,
    bool Conflict,
    string? Failure)
{
    public static UserCreationOutcome Created(User user) =>
        new(true, user.Id, user.UserName, false, null);

    public static UserCreationOutcome Rejected(string reason) =>
        new(false, Guid.Empty, string.Empty, false, reason);

    public static UserCreationOutcome Taken(string userName) =>
        new(false, Guid.Empty, userName, true, $"The user name '{userName}' is already in use.");
}