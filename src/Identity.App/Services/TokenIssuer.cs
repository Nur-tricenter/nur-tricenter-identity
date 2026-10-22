using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Identity.Application.Contracts;
using Identity.Application.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Nur.Tricenter.Security;

namespace Identity.App.Services;

/// <summary>Mints the bearer token a successful login returns.</summary>
public interface ITokenIssuer
{
    /// <summary>
    /// Issues a token for a user. The <c>scope</c> claim is space-delimited, which is the shape
    /// the kernel's <c>AddSecurity</c> policies split on when they check the caller's permissions.
    /// </summary>
    string Issue(User user, IReadOnlyList<string> scopes);
}

/// <summary>HS256 issuer backed by <see cref="JwtOptions"/>.</summary>
public sealed class TokenIssuer : ITokenIssuer
{
    // "scope", not ClaimTypes.WebRequestExtensions or a URI: the kernel reads FindAll("scope"),
    // and a claim under any other name is one the policies never see.
    private const string ScopeClaimType = "scope";

    private readonly JwtOptions _options;
    private readonly JwtSecurityTokenHandler _handler;

    public TokenIssuer(IOptions<JwtOptions> options)
    {
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            throw new InvalidOperationException(
                $"{JwtOptions.SectionName}:{nameof(JwtOptions.SecretKey)} has no value, so no token can " +
                "be signed. Supply it through the Jwt__SecretKey environment variable, or a local " +
                "git-ignored appsettings.Development.json.");
        }

        _handler = new JwtSecurityTokenHandler
        {
            // JwtSecurityTokenHandler rewrites claims through its static OutboundClaimTypeMap on
            // the way OUT, turning "scope" into a long WS-Federation URI, and applies a matching map
            // on the way IN. Both must be off: the kernel's policies read FindAll("scope") by that
            // literal name, so a rewritten claim yields a token that validates cleanly and then
            // authorises nothing. Empty maps are the documented way to disable the behaviour.
            OutboundClaimTypeMap = new Dictionary<string, string>(),
            InboundClaimTypeMap = new Dictionary<string, string>()
        };
    }

    public string Issue(User user, IReadOnlyList<string> scopes)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName)
        };

        if (scopes.Count > 0)
        {
            claims.Add(new Claim(ScopeClaimType, string.Join(' ', scopes)));
        }

        // An administrator is marked in the token itself rather than only implied by its expanded
        // scope list, so a downstream service can tell "holds everything" from "happens to hold a
        // lot" without comparing scope arrays.
        if (user.IsAdministrator)
        {
            claims.Add(new Claim(IdentityPermissions.All, "true"));
        }

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: string.IsNullOrWhiteSpace(_options.ValidIssuer) ? null : _options.ValidIssuer,
            audience: string.IsNullOrWhiteSpace(_options.ValidAudience) ? null : _options.ValidAudience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            // ValidateLifetime defaults to true, so a token with no explicit expiry would fail
            // validation everywhere. Lifetime is required, not optional.
            expires: DateTime.UtcNow.AddSeconds(_options.Lifetime),
            signingCredentials: credentials);

        return _handler.WriteToken(token);
    }
}