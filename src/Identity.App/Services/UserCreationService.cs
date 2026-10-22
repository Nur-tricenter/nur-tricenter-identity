using Identity.App.Contracts;
using Identity.Application.Abstractions;
using Identity.Application.Contracts;
using Identity.Application.Domain;
using Identity.Infrastructure.Persistence;
using Joseco.DDD.Core.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Identity.App.Services;

/// <summary>
/// Creates accounts and records the scopes granted to each one.
/// </summary>
/// <remarks>
/// Goes through <see cref="IUnitOfWork.CommitAsync"/> rather than
/// <c>SaveChangesAsync</c>, which is what makes the <c>UserCreated</c> event the <c>User</c>
/// factory raised actually reach the outbox. Saving directly would commit the account and drop
/// the event on the floor: the consumer would never learn the account exists.
/// </remarks>
public sealed class UserCreationService : IUserCreationService
{
    private readonly IdentityDbContext _context;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _hasher;

    public UserCreationService(
        IdentityDbContext context,
        IUnitOfWork unitOfWork,
        IPasswordHasher hasher)
    {
        _context = context;
        _unitOfWork = unitOfWork;
        _hasher = hasher;
    }

    public async Task<UserCreationOutcome> CreateAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var userName = request.UserName?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(userName))
        {
            return UserCreationOutcome.Rejected("A user name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            // Refused rather than defaulted. An account created with a guessable credential is
            // worse than one that was not created, because the caller believes it succeeded.
            return UserCreationOutcome.Rejected("A password is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return UserCreationOutcome.Rejected("An email address is required.");
        }

        var requestedScopes = request.Scopes?.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray()
                              ?? Array.Empty<string>();

        // The wildcard is refused at the boundary. It is the seeder's to grant, and honouring it
        // over HTTP would put an unrestricted account one authenticated call away.
        if (requestedScopes.Any(s =>
                string.Equals(s, IdentityPermissions.All, StringComparison.Ordinal)))
        {
            return UserCreationOutcome.Rejected(
                $"The wildcard scope '{IdentityPermissions.All}' cannot be granted over HTTP. " +
                "It is granted only to the built-in administrator created at startup.");
        }

        // Checked before the insert rather than relying on the unique index: the index turns a
        // duplicate into a DbUpdateException that surfaces as a 500, and a taken name is an
        // ordinary outcome that deserves a 409.
        var taken = await _context.Users
            .AnyAsync(u => u.UserName == userName || u.Email == request.Email.Trim(), cancellationToken);

        if (taken)
        {
            return UserCreationOutcome.Taken(userName);
        }

        var user = User.Create(
            request.Email.Trim(),
            userName,
            _hasher.Hash(request.Password),
            requestedScopes);

        _context.Users.Add(user);

        foreach (var scope in IdentityPermissions.Expand(requestedScopes))
        {
            _context.UserScopes.Add(new UserScope(user.Id, scope));
        }

        // Drains the UserCreated the factory raised into the outbox, and saves the account with it
        // in the same transaction.
        await _unitOfWork.CommitAsync(cancellationToken);

        return UserCreationOutcome.Created(user);
    }
}