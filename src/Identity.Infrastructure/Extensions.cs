using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Application.Contracts;
using Identity.Infrastructure.Configuration;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Security;
using Identity.Infrastructure.Seed;
using Joseco.DDD.Core.Abstractions;
using Joseco.Outbox.Contracts.Service;
using Joseco.Outbox.EFCore.Procesor;
using Joseco.Outbox.EFCore.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Infrastructure;

/// <summary>
/// Composition root for the persistence, security and outbox services.
/// </summary>
public static class Extensions
{
    /// <summary>
    /// Name of the connection string. Also the database name on the platform: one database per
    /// microservice, one credential, one deployment step.
    /// </summary>
    public const string ConnectionStringName = "IdentityDatabaseConnectionString";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Registers the MediatR handlers, INCLUDING the assembly scan that picks up the outbox
        // publishers — which AddApplication immediately removes again, since only the WorkerService
        // can dispatch them. Both hosts call this once.
        services.AddApplication();

        services.AddDbContext<IdentityDbContext>(
            options => options.UseNpgsql(RequireConnectionString(configuration)));

        services.Configure<DefaultUserOptions>(
            configuration.GetSection(DefaultUserOptions.SectionName));

        services.AddScoped<IPasswordHasher>(provider =>
        {
            var options = provider.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<DefaultUserOptions>>().Value;
            return new BcryptPasswordHasher(options.WorkFactor);
        });

        services.AddScoped<IDefaultUserInitializer, DefaultUserInitializer>();

        // The outbox staging services and the UnitOfWork that both the aggregates and the outbox
        // resolve to, so a domain event and the account it describes commit together.
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IOutboxDatabase<DomainEvent>, UnitOfWork>();
        AddOutboxServices<DomainEvent>(services);

        return services;
    }

    /// <summary>
    /// Registers the transactional outbox services for <typeparamref name="TEvent"/>.
    /// </summary>
    /// <remarks>
    /// The manual equivalent of <c>Joseco.Outbox.EFCore.DependencyInjection.AddOutbox&lt;TEvent&gt;()</c>,
    /// which cannot be called directly: it is compiled against MediatR 12 and calls the removed
    /// <c>ServiceCollectionExtensions.AddMediatR</c>, so it throws a <c>TypeLoadException</c> on
    /// MediatR 14.
    /// </remarks>
    private static void AddOutboxServices<TEvent>(IServiceCollection services)
    {
        services.AddScoped<EfCoreOutboxService<TEvent>>();
        services.AddScoped<IOutboxService<TEvent>>(
            sp => sp.GetRequiredService<EfCoreOutboxService<TEvent>>());
        services.AddScoped<IOutboxRepository<TEvent>>(
            sp => sp.GetRequiredService<EfCoreOutboxService<TEvent>>());
        services.AddScoped<OutboxProcessor<TEvent>>();

        // Wraps the registration above so every staged outbox row inherits the ambient trace.
        // Must run AFTER the AddScoped calls, or Scrutor decorates nothing.
        services.Decorate<IOutboxService<TEvent>, Observability.OutboxTracingService<TEvent>>();
    }

    /// <summary>
    /// Reads the connection string or fails with a message naming where it comes from.
    /// </summary>
    /// <remarks>
    /// Read inside the <c>AddDbContext</c> callback on purpose. Reading it eagerly would make a
    /// missing value a composition-time failure, which breaks any test that replaces
    /// <c>DbContextOptions&lt;T&gt;</c> without booting a real database. Read here, a real
    /// deployment that forgot the setting still fails immediately the first time a context is
    /// resolved — with a message naming the fix — instead of Npgsql's null-argument exception.
    /// </remarks>
    private static string RequireConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"No value was found for configuration 'ConnectionStrings:{ConnectionStringName}'. " +
                $"Supply it through the ConnectionStrings__{ConnectionStringName} environment variable. " +
                "A connection string is a credential and a credential does not belong in version control.");
        }

        return connectionString;
    }
}