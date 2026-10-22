using Identity.Application.Contracts;
using Identity.Application.Events;
using Joseco.DDD.Core.Abstractions;

namespace Identity.Application.Domain;

/// <summary>
/// An account that can authenticate against this service.
/// </summary>
/// <remarks>
/// The aggregate root for authentication. Its invariants are deliberately few, because the only
/// thing standing between a malformed account and a valid token is this type: a user with no
/// password hash cannot log in, and a deactivated user must not hold a token that outlives the
/// change.
/// </remarks>
public class User : AggregateRoot
{
    private readonly List<string> _scopes = new();

    private User(
        Guid id,
        string email,
        string userName,
        string passwordHash,
        bool isAdministrator,
        IEnumerable<string> scopes,
        DateTime createdOn)
        : base(id)
    {
        Email = email;
        UserName = userName;
        PasswordHash = passwordHash;
        IsAdministrator = isAdministrator;
        IsActive = true;
        CreatedOn = createdOn;
        _scopes.AddRange(scopes);

        // Raised from the factory, not by the caller: an account that exists without this event
        // would reach the database and never reach the outbox, so no consumer would ever learn
        // it exists. The event has to be a consequence of the aggregate existing.
        AddDomainEvent(new UserCreated(
            Id,
            UserName,
            Email,
            isAdministrator,
            IdentityPermissions.Expand(_scopes).ToArray()));
    }

    /// <summary>Private ctor for EF Core materialisation. Never call the constructor directly.</summary>
    private User()
    {
    }

    public string Email { get; private set; } = string.Empty;

    public string UserName { get; private set; } = string.Empty;

    /// <summary>
    /// The password hash. Never a plaintext password, and never returned by a response DTO.
    /// </summary>
    public string PasswordHash { get; private set; } = string.Empty;

    /// <summary>
    /// True when this account carries <see cref="Contracts.IdentityPermissions.All"/>. An
    /// administrator is not a role: it is the flag that makes the flag list expand at login, and
    /// it is what the seeder sets on the default user so a fresh environment is usable without
    /// having to log in first to grant itself anything.
    /// </summary>
    public bool IsAdministrator { get; private set; }

    /// <summary>
    /// A deactivated account keeps its scopes but cannot authenticate. Kept separate from
    /// deletion so an audit trail survives and a token already issued stays resolvable to a real
    /// user rather than dangling.
    /// </summary>
    public bool IsActive { get; private set; }

    public DateTime CreatedOn { get; private set; }

    public DateTime? LastLoginOn { get; private set; }

    /// <summary>
    /// The scopes this account holds. May contain the wildcard sentinel; expand it through
    /// <see cref="Contracts.IdentityPermissions.Expand"/> before putting it in a token.
    /// </summary>
    public IReadOnlyCollection<string> Scopes => _scopes.AsReadOnly();

    /// <summary>
    /// Creates a regular account. Scope assignment is a separate step so that granting a
    /// permission is never an accident of registering a user.
    /// </summary>
    public static User Create(
        string email,
        string userName,
        string passwordHash,
        IEnumerable<string>? scopes = null)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("An account needs an email.", nameof(email));
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            // A user with a blank hash is an account that authenticates everyone, so this is
            // refused rather than defaulted: there is no safe default hash.
            throw new ArgumentException("An account needs a password hash.", nameof(passwordHash));
        }

        return new User(
            Guid.NewGuid(),
            email.Trim(),
            userName?.Trim() ?? email.Trim(),
            passwordHash,
            isAdministrator: false,
            scopes ?? Array.Empty<string>(),
            DateTime.UtcNow);
    }

    /// <summary>
    /// Creates the built-in administrator: the wildcard scope, no email verification, and an
    /// account that can do anything this service authorises.
    /// </summary>
    public static User CreateAdministrator(
        string userName,
        string passwordHash,
        string? email = null)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException(
                "The default administrator needs a password hash. Refusing to create an account " +
                "with unrestricted scope and no credential.",
                nameof(passwordHash));
        }

        var name = string.IsNullOrWhiteSpace(userName) ? "admin" : userName.Trim();

        return new User(
            Guid.NewGuid(),
            string.IsNullOrWhiteSpace(email) ? $"{name}@localhost" : email.Trim(),
            name,
            passwordHash,
            isAdministrator: true,
            // The PLATFORM sentinel, not identity's own. The seeded account is the operator's way
            // into the whole stack, so it must reach the catalog too — and identity can grant that
            // only by naming a scope the catalog recognises, which is what the shared kernel is for.
            new[] { Nur.Tricenter.Access.PlatformPermissions.All },
            DateTime.UtcNow);
    }

    /// <summary>
    /// Records a successful authentication. Does not itself authorise anything.
    /// </summary>
    /// <remarks>
    /// Deliberately raises NO domain event. <c>LastLoginOn</c> changes on every authentication, so
    /// publishing it would turn one account's routine use into broker traffic that nothing
    /// subscribes to. A consumer has no business knowing that somebody logged in.
    /// </remarks>
    public void RegisterLogin() => LastLoginOn = DateTime.UtcNow;

    /// <summary>
    /// Grants an additional scope. Re-granting a scope the account already holds is a no-op, so
    /// a seed re-run against an existing database does not accumulate duplicates.
    /// </summary>
    public void GrantScope(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope) || _scopes.Contains(scope))
        {
            return;
        }

        _scopes.Add(scope);
        RaiseUpdated(UserChangeKind.Scopes);
    }

    public void RevokeScope(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope) || !_scopes.Remove(scope))
        {
            return;
        }

        RaiseUpdated(UserChangeKind.Scopes);
    }

    public void ChangePassword(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("A password hash cannot be blank.", nameof(passwordHash));
        }

        PasswordHash = passwordHash;
    }

    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        RaiseUpdated(UserChangeKind.Deactivated);
    }

    public void Activate()
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        RaiseUpdated(UserChangeKind.Reactivated);
    }

    /// <summary>
    /// Raises <see cref="UserUpdated"/> with the account's CURRENT state.
    /// </summary>
    /// <remarks>
    /// Reads the aggregate rather than accepting the new values as parameters: a caller that
    /// passes what it thinks the state is can disagree with what the aggregate actually holds,
    /// and then the event describes a state that was never persisted. Scopes are expanded here so
    /// no consumer ever receives the wildcard sentinel.
    /// </remarks>
    private void RaiseUpdated(string changeKind) =>
        AddDomainEvent(new UserUpdated(
            Id,
            UserName,
            Email,
            IdentityPermissions.Expand(_scopes).ToArray(),
            IsActive,
            changeKind));
}