using Identity.Application.Abstractions;

namespace Identity.Infrastructure.Security;

/// <summary>
/// Bcrypt implementation of <see cref="IPasswordHasher"/>.
/// </summary>
/// <remarks>
/// Bcrypt rather than a bare SHA-256 because password hashing is the one case where being fast is
/// a vulnerability: an attacker holding a copy of the table gets an unlimited number of guesses per
/// second against a fast hash. Bcrypt's work factor makes each guess expensive, and because the
/// salt and cost live inside the stored value, raising the cost later re-protects old hashes on
/// their next successful login instead of requiring a migration.
/// </remarks>
public sealed class BcryptPasswordHasher : IPasswordHasher
{
    private const int DefaultWorkFactor = 11;

    // Bcrypt only accepts a cost from 4 to 31. Clamping rather than throwing means an operator
    // who sets an absurd value in configuration gets a working service and a loud log, not a boot
    // that refuses to start.
    private const int MinWorkFactor = 4;
    private const int MaxWorkFactor = 31;

    private readonly int _workFactor;

    public BcryptPasswordHasher(int workFactor = DefaultWorkFactor)
    {
        _workFactor = Math.Clamp(workFactor, MinWorkFactor, MaxWorkFactor);
    }

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        return BCrypt.Net.BCrypt.HashPassword(password, _workFactor);
    }

    public bool Verify(string candidate, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.Verify(candidate, storedHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // A stored value that is not a bcrypt digest. Returning false keeps a corrupt row from
            // surfacing as an unhandled exception on the login endpoint.
            return false;
        }
    }

    public bool NeedsRehash(string storedHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash))
        {
            return true;
        }

        // BCrypt prefixes the digest with the cost it was produced at, e.g. "$2a$11$".
        return !storedHash.StartsWith($"$2a${_workFactor:D2}$", StringComparison.Ordinal)
               && !storedHash.StartsWith($"$2b${_workFactor:D2}$", StringComparison.Ordinal)
               && !storedHash.StartsWith($"$2y${_workFactor:D2}$", StringComparison.Ordinal);
    }
}