namespace Identity.Application.Abstractions;

/// <summary>
/// Turns a plaintext password into a stored digest, and checks a candidate against one.
/// </summary>
/// <remarks>
/// Declared in the application layer and implemented in infrastructure so the domain never names
/// a hashing library. That is not ceremony: the algorithm is a deployment decision, and a domain
/// that hardcodes bcrypt cannot be moved to argon2 without editing business logic.
/// </remarks>
public interface IPasswordHasher
{
    /// <summary>Hashes a plaintext password for storage.</summary>
    string Hash(string password);

    /// <summary>
    /// Checks a candidate against a stored digest. Returns false rather than throwing on a
    /// malformed stored digest, so a corrupt row cannot turn into a 500 on the login endpoint.
    /// </summary>
    bool Verify(string candidate, string storedHash);

    /// <summary>
    /// Whether a stored digest was produced with an outdated cost and should be rehashed on the
    /// next successful login.
    /// </summary>
    bool NeedsRehash(string storedHash);
}