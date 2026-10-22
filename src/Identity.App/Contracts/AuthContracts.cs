namespace Identity.App.Contracts;

/// <summary>Login request. Carries a user name or an email, never a user id.</summary>
public class LoginRequest
{
    /// <summary>The account's login name or email address.</summary>
    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}

/// <summary>A successful login.</summary>
public class LoginResponse
{
    /// <summary>The bearer token to send in the Authorization header.</summary>
    public string AccessToken { get; set; } = string.Empty;

    public string TokenType { get; set; } = "Bearer";

    /// <summary>Token lifetime in seconds, so a client can refresh before it expires.</summary>
    public long ExpiresIn { get; set; }

    public string UserName { get; set; } = string.Empty;

    public bool IsAdministrator { get; set; }

    /// <summary>
    /// The scopes actually granted, already expanded: an administrator receives the concrete list
    /// rather than the wildcard, so a client never has to know what a wildcard means.
    /// </summary>
    public IReadOnlyList<string> Scopes { get; set; } = Array.Empty<string>();
}

/// <summary>Request to create an account.</summary>
public class CreateUserRequest
{
    public string UserName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Scopes to grant. An administrator is created by the seeder, not here: granting the wildcard
    /// over HTTP would mean one authenticated callaway from a permanent super-user, so it is
    /// deliberately not reachable from this endpoint.
    /// </summary>
    public List<string> Scopes { get; set; } = new();
}

/// <summary>A created account, without its credential.</summary>
public class CreateUserResponse
{
    public Guid Id { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public bool IsAdministrator { get; set; }

    /// <summary>The scopes actually granted, already expanded.</summary>
    public IReadOnlyList<string> Scopes { get; set; } = Array.Empty<string>();
}

/// <summary>The authenticated principal.</summary>
public class MeResponse
{
    public Guid Id { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public bool IsAdministrator { get; set; }

    public IReadOnlyList<string> Scopes { get; set; } = Array.Empty<string>();
}