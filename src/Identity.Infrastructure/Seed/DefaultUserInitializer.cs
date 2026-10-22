using Identity.Application.Abstractions;
using Identity.Application.Contracts;
using Identity.Application.Domain;
using Identity.Infrastructure.Configuration;
using Identity.Infrastructure.Persistence;
using Joseco.DDD.Core.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.Seed;

/// <summary>
/// Writes the default administrator on a fresh database.
/// </summary>
/// <remarks>
/// Idempotent by login name: an existing account is left untouched rather than having its password
/// reset. That is the difference between a seed and a backdoor. Re-running it on a populated
/// database must never change a credential, because the moment it can, an operator who leaves it
/// enabled in production has handed every deployment the same admin password.
/// </remarks>
public sealed class DefaultUserInitializer : IDefaultUserInitializer
{
    private readonly IdentityDbContext _context;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _hasher;
    private readonly DefaultUserOptions _options;
    private readonly ILogger<DefaultUserInitializer> _logger;

    public DefaultUserInitializer(
        IdentityDbContext context,
        IUnitOfWork unitOfWork,
        IPasswordHasher hasher,
        IOptions<DefaultUserOptions> options,
        ILogger<DefaultUserInitializer> logger)
    {
        _context = context;
        _unitOfWork = unitOfWork;
        _hasher = hasher;
        _options = options.Value;
        _logger = logger;
    }

    public async Task Initialize(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "Default administrator creation is disabled ({Section}:{Key}).",
                DefaultUserOptions.SectionName,
                nameof(DefaultUserOptions.Enabled));
            return;
        }

        var userName = string.IsNullOrWhiteSpace(_options.UserName) ? "admin" : _options.UserName.Trim();

        if (string.IsNullOrWhiteSpace(_options.Password))
        {
            // Refusing here rather than defaulting. An administrator account with no credential is
            // either unusable or, worse, one somebody later "fixes" with a guessable literal.
            throw new InvalidOperationException(
                $"{DefaultUserOptions.SectionName}:{nameof(DefaultUserOptions.Password)} has no value " +
                $"while {DefaultUserOptions.SectionName}:{nameof(DefaultUserOptions.Enabled)} is true. " +
                "Supply it through the DefaultUser__Password environment variable. " +
                "The default administrator is created with the wildcard scope, so it is not a " +
                "credential that may be guessed or defaulted.");
        }

        var existing = await _context.Users
            .FirstOrDefaultAsync(u => u.UserName == userName, cancellationToken);

        if (existing is not null)
        {
            _logger.LogInformation(
                "Default administrator '{UserName}' already exists; leaving its credential untouched.",
                userName);
            return;
        }

        var administrator = User.CreateAdministrator(
            userName,
            _hasher.Hash(_options.Password),
            _options.Email);

        _context.Users.Add(administrator);

        // Read the scopes off the aggregate rather than restating the sentinel here.
        //
        // Naming `IdentityPermissions.All` here duplicated the decision that
        // User.CreateAdministrator already made, and when the administrator moved to the
        // platform-wide wildcard this line silently kept writing the old one: the aggregate said
        // platform:* while the row said identity:*, and the token came out scoped to identity
        // only. The aggregate is the single place that decides what an administrator holds.
        foreach (var scope in administrator.Scopes)
        {
            _context.UserScopes.Add(new UserScope(administrator.Id, scope));
        }

        // CommitAsync, NOT SaveChangesAsync. This is the whole reason the seeder goes through the
        // UnitOfWork: SaveChanges persists the tracked entities but never looks at the aggregate's
        // DomainEvents, so the UserCreated the factory raised would be silently dropped and no
        // consumer would ever learn the administrator exists. CommitAsync drains those events
        // through MediatR into the outbox and saves everything in the same transaction.
        await _unitOfWork.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Created the default administrator '{UserName}' with the wildcard scope {Scope}.",
            userName,
            IdentityPermissions.All);
    }
}