using Joseco.DDD.Core.Abstractions;

namespace Identity.Application.Events;

/// <summary>
/// An existing account changed in a way a consumer must know about.
/// </summary>
/// <remarks>
/// Raised for profile and scope changes, not for a successful login: <c>LastLoginOn</c> changes on
/// every authentication and publishing that would turn one account's routine use into a stream of
/// broker traffic nobody subscribes to.
/// <para>
/// Single constructor, same reason as <see cref="UserCreated"/>: a parameterless constructor would
/// make Newtonsoft silently deserialize this event with empty fields.
/// </para>
/// </remarks>
public record UserUpdated : DomainEvent
{
    public Guid UserId { get; }

    public string UserName { get; }

    public string Email { get; }

    /// <summary>The scopes the account holds after the change, already expanded.</summary>
    public string[] Scopes { get; }

    public bool IsActive { get; }

    /// <summary>
    /// What changed, so a consumer can react without diffing the whole record.
    /// </summary>
    /// <remarks>
    /// A stable discriminator on the wire. A consumer switches on this rather than on the CLR
    /// type, because the type name is not part of the contract and can be refactored.
    /// </remarks>
    public string ChangeKind { get; }

    public UserUpdated(
        Guid userId,
        string userName,
        string email,
        string[] scopes,
        bool isActive,
        string changeKind)
    {
        OccuredOn = DateTime.UtcNow;
        UserId = userId;
        UserName = userName;
        Email = email;
        Scopes = scopes;
        IsActive = isActive;
        ChangeKind = changeKind;
    }
}

/// <summary>The values <see cref="UserUpdated.ChangeKind"/> can take.</summary>
public static class UserChangeKind
{
    /// <summary>Scopes were granted or revoked.</summary>
    public const string Scopes = "scopes";

    /// <summary>The account was deactivated.</summary>
    public const string Deactivated = "deactivated";

    /// <summary>The account was reactivated.</summary>
    public const string Reactivated = "activated";

    /// <summary>The email or user name changed.</summary>
    public const string Profile = "profile";
}