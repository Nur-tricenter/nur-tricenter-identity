using Joseco.Communication.External.Contracts.Message;

namespace Identity.Application.Contracts.Integration;

/// <summary>
/// External contract announcing that an account was created.
/// </summary>
/// <remarks>
/// Carries no credential of any kind: no password hash, no token, nothing secret. This record
/// leaves the service and lands in a broker, so anything on it must be safe to log. That is the
/// whole reason the domain event and this contract are separate types rather than one.
/// </remarks>
public record UserPublished : IntegrationMessage
{
    /// <summary>Required by the Newtonsoft deserializer used by the RabbitMQ library.</summary>
    public UserPublished()
    {
        UserId = Guid.Empty;
        UserName = string.Empty;
        Email = string.Empty;
        Scopes = Array.Empty<string>();
    }

    /// <summary>Creates the contract and forwards trace metadata to the shared envelope.</summary>
    public UserPublished(string? correlationId, string? source)
        : base(correlationId, source)
    {
        UserId = Guid.Empty;
        UserName = string.Empty;
        Email = string.Empty;
        Scopes = Array.Empty<string>();
    }

    /// <summary>
    /// Stable discriminator on the wire, in the <c>&lt;Area&gt;.&lt;Fact&gt;</c> form every contract
    /// on the platform uses. Consumers switch on this, never on the CLR type.
    /// </summary>
    public string EventType => "User.Published";

    public Guid UserId { get; init; }

    public string UserName { get; init; }

    public string Email { get; init; }

    public bool IsAdministrator { get; init; }

    /// <summary>
    /// Concrete scopes the account holds. Already expanded: a consumer receives
    /// <c>identity.user:read</c>, never the <c>identity:*</c> sentinel.
    /// </summary>
    public string[] Scopes { get; init; }
}

/// <summary>
/// External contract announcing that an account changed.
/// </summary>
public record UserChanged : IntegrationMessage
{
    public UserChanged()
    {
        UserId = Guid.Empty;
        UserName = string.Empty;
        Email = string.Empty;
        Scopes = Array.Empty<string>();
        IsActive = false;
        ChangeKind = string.Empty;
    }

    public UserChanged(string? correlationId, string? source)
        : base(correlationId, source)
    {
        UserId = Guid.Empty;
        UserName = string.Empty;
        Email = string.Empty;
        Scopes = Array.Empty<string>();
        IsActive = false;
        ChangeKind = string.Empty;
    }

    public string EventType => "User.Changed";

    public Guid UserId { get; init; }

    public string UserName { get; init; }

    public string Email { get; init; }

    /// <summary>The scopes the account holds after the change, already expanded.</summary>
    public string[] Scopes { get; init; }

    public bool IsActive { get; init; }

    /// <summary>
    /// What changed, from <see cref="Events.UserChangeKind"/>. Lets a consumer act on the change
    /// without diffing the whole record.
    /// </summary>
    public string ChangeKind { get; init; }
}