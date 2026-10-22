using Identity.Application.Abstractions;
using Identity.Application.Contracts;
using Identity.Application.Domain;
using Identity.Infrastructure.Persistence;
using Joseco.DDD.Core.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Identity.App.Services;

/// <summary>
/// Turns a user name and a password into a token, or into a refusal.
/// </summary>
public interface ILoginService
{
    Task<LoginOutcome> LoginAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default);
}

/// <summary>The result of a login attempt.</summary>
public sealed record LoginOutcome(bool Succeeded, User? User, IReadOnlyList<string> Scopes, string? Failure)
{
    public static LoginOutcome Success(User user, IReadOnlyList<string> scopes) =>
        new(true, user, scopes, null);

    public static LoginOutcome Rejected(string reason) =>
        new(false, null, Array.Empty<string>(), reason);
}

/// <summary>
/// The login implementation.
/// </summary>
/// <remarks>
/// One failure message for every rejection, whether the account is unknown, the password is wrong,
/// or the account is deactivated. Distinguishing them turns the endpoint into an account-existence
/// oracle: an attacker can enumerate valid user names by watching which message comes back.
/// </remarks>
public sealed class LoginService : ILoginService
{
    private const string GenericFailure = "Invalid credentials.";

    private readonly IdentityDbContext _context;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _hasher;

    public LoginService(IdentityDbContext context, IUnitOfWork unitOfWork, IPasswordHasher hasher)
    {
        _context = context;
        _unitOfWork = unitOfWork;
        _hasher = hasher;
    }

    public async Task<LoginOutcome> LoginAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            return LoginOutcome.Rejected(GenericFailure);
        }

        var identifier = userName.Trim();

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.UserName == identifier || u.Email == identifier, cancellationToken);

        // Same message for "no such user" and "wrong password", but the hash is still verified
        // against a dummy value on the miss path so the response time does not reveal which of the
        // two happened.
        if (user is null)
        {
            _hasher.Verify(password, DummyHash);
            return LoginOutcome.Rejected(GenericFailure);
        }

        if (!_hasher.Verify(password, user.PasswordHash))
        {
            return LoginOutcome.Rejected(GenericFailure);
        }

        if (!user.IsActive)
        {
            return LoginOutcome.Rejected(GenericFailure);
        }

        // Upgrading an outdated cost on a successful login is the only moment the plaintext is
        // available to rehash it, which is why the check belongs here and not in the seed.
        if (_hasher.NeedsRehash(user.PasswordHash))
        {
            user.ChangePassword(_hasher.Hash(password));
        }

        user.RegisterLogin();

        var scopes = await _context.UserScopes
            .Where(s => s.UserId == user.Id)
            .Select(s => s.Scope)
            .ToListAsync(cancellationToken);

        // CommitAsync, not SaveChangesAsync: the possible password rehash and LastLoginOn are
        // aggregate state, and a scope change made elsewhere in the request raises UserUpdated on
        // this same entity. Only the UnitOfWork drains those events into the outbox.
        await _unitOfWork.CommitAsync(cancellationToken);

        // The wildcard is expanded here, at the boundary, so nothing downstream has to know that
        // this service has an administrator concept at all.
        return LoginOutcome.Success(user, IdentityPermissions.Expand(scopes));
    }

    /// <summary>
    /// A valid bcrypt digest of a value nobody can supply. Verifying against it burns the same
    /// work a real attempt would, so a miss costs what a hit costs.
    /// </summary>
    private const string DummyHash = "$2a$11$C6UzMDM.H6dfI/f/IKcEeO8uJmSjN8uEwZ5Z5Z5Z5Z5Z5Z5Z5Z5Ze";
}