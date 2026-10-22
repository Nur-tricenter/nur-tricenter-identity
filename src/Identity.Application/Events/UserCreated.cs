using Joseco.DDD.Core.Abstractions;

namespace Identity.Application.Events;

/// <summary>
/// An account was created.
/// </summary>
/// <remarks>
/// A DOMAIN event: it says something happened inside this aggregate, and it is the only place the
/// password hash could ever be seen. That is exactly why it carries no credential: this event is
/// persisted to the outbox and published to the broker, so anything on it leaves the service.
/// </remarks>
public record UserCreated : DomainEvent
{
    public Guid UserId { get; }

    public string UserName { get; }

    public string Email { get; }

    public bool IsAdministrator { get; }

    /// <summary>
    /// The scopes granted at creation, already expanded. The consumer receives concrete
    /// permissions rather than the wildcard, because a consumer must not need to know this
    /// service has an administrator concept in order to act on the account.
    /// </summary>
    public string[] Scopes { get; }

    /// <summary>
    /// The ONLY constructor, on purpose.
    /// </summary>
    /// <remarks>
    /// The outbox persists this event through a Newtonsoft converter with
    /// <c>TypeNameHandling.All</c>, and Newtonsoft prefers a public parameterless constructor over
    /// a parameterized one when both exist. Because every property above is get-only, that
    /// constructor produced an event whose payload was silently never written: <c>UserId</c> came
    /// back <c>Guid.Empty</c> and <c>UserName</c> came back null, with no error raised anywhere.
    /// With a single fully parameterized constructor Newtonsoft matches the JSON members to the
    /// parameters by name and rebuilds the event correctly.
    /// <b>Do NOT add a parameterless constructor back.</b> It reintroduces silent truncation.
    /// </remarks>
    public UserCreated(
        Guid userId,
        string userName,
        string email,
        bool isAdministrator,
        string[] scopes)
    {
        OccuredOn = DateTime.UtcNow;
        UserId = userId;
        UserName = userName;
        Email = email;
        IsAdministrator = isAdministrator;
        Scopes = scopes;
    }
}