namespace Identity.Infrastructure.Configuration;

/// <summary>
/// Controls whether the default administrator is created on startup.
/// </summary>
/// <remarks>
/// Modelled on the catalog's <c>SeedOptions</c>, and for the same reason: seeding is a
/// development convenience and a deployment's explicit choice, never an accident of migrating.
/// Creating an account that holds every permission is a far more dangerous default than writing
/// thirty foods, so this stays off unless configuration turns it on.
/// </remarks>
public class DefaultUserOptions
{
    public const string SectionName = "DefaultUser";

    public const bool EnabledDefault = false;

    /// <summary>Whether the default administrator is created when absent.</summary>
    public bool Enabled { get; set; } = EnabledDefault;

    /// <summary>The account's login name.</summary>
    public string UserName { get; set; } = "admin";

    public string? Email { get; set; }

    /// <summary>
    /// The plaintext password, supplied through configuration only. It is hashed immediately at
    /// startup and never persisted, so the stored value is never recoverable from the database.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Bcrypt work factor. Higher is slower and safer; the default is deliberately not the
    /// library maximum so that a developer boot does not stall on seeding.
    /// </summary>
    public int WorkFactor { get; set; } = 11;
}