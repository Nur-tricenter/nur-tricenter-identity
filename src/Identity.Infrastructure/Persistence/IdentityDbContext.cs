using Identity.Application.Domain;
using Joseco.DDD.Core.Abstractions;
using Joseco.Outbox.Contracts.Model;
using Joseco.Outbox.EFCore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence;

/// <summary>
/// The identity service's persistence model.
/// </summary>
/// <remarks>
/// One context, one <c>nur_tricenter_identity</c> database. The outbox lives in this same context
/// on purpose: a domain event and the outbox row that carries it must commit in a single
/// transaction, and two contexts against one database would reintroduce the gap the outbox exists
/// to close.
/// </remarks>
public class IdentityDbContext : DbContext
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<UserScope> UserScopes => Set<UserScope>();

    /// <summary>
    /// The transactional outbox. In the SAME context as the aggregates, deliberately: a domain
    /// event and the row that carries it must commit together, and two contexts against one
    /// database would reintroduce exactly the gap the outbox exists to close.
    /// </summary>
    public DbSet<OutboxMessage<DomainEvent>> OutboxMessages => Set<OutboxMessage<DomainEvent>>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);

        // Entity.DomainEvents is an in-memory bag carried on every aggregate, not a collection with
        // a table. Without this EF walks the property, finds the abstract DomainEvent, and either
        // tries to map the base type or refuses to instantiate it at design time.
        modelBuilder.Ignore<DomainEvent>();

        // Adds the outbox table and the Newtonsoft converter that persists the payload with
        // TypeNameHandling.All, which is what lets a consumer deserialize a concrete event type.
        modelBuilder.AddOutboxModel<DomainEvent>();

        base.OnModelCreating(modelBuilder);
    }
}

/// <summary>
/// A scope granted to a user, stored as its own row.
/// </summary>
/// <remarks>
/// Not a collection on <see cref="User"/>. Scopes are read and aggregated by the gateway and by
/// login, not loaded with the aggregate every time, and a growing wildcard list would otherwise
/// force a rewrite of the users row on every grant.
/// </remarks>
public class UserScope
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>The raw scope string, possibly the wildcard sentinel.</summary>
    public string Scope { get; set; } = string.Empty;

    protected UserScope()
    {
    }

    public UserScope(Guid userId, string scope)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        Scope = scope;
    }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Email).HasMaxLength(256).IsRequired();
        builder.Property(u => u.UserName).HasMaxLength(128).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(256).IsRequired();
        builder.Property(u => u.IsAdministrator).IsRequired();
        builder.Property(u => u.IsActive).IsRequired();
        builder.Property(u => u.CreatedOn).IsRequired();
        builder.Property(u => u.LastLoginOn);

        // Login looks an account up by user name and by email, so both need an index. The unique
        // constraint is what stops two accounts from claiming the same login.
        builder.HasIndex(u => u.UserName).IsUnique().HasDatabaseName("IX_Users_UserName");
        builder.HasIndex(u => u.Email).IsUnique().HasDatabaseName("IX_Users_Email");
    }
}

internal sealed class UserScopeConfiguration : IEntityTypeConfiguration<UserScope>
{
    public void Configure(EntityTypeBuilder<UserScope> builder)
    {
        builder.ToTable("UserScopes");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Scope).HasMaxLength(128).IsRequired();

        builder.HasIndex(s => new { s.UserId, s.Scope })
            .IsUnique()
            .HasDatabaseName("IX_UserScopes_UserId_Scope");

        builder.HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}