using Identity.Application;
using Identity.Infrastructure;
using Identity.Infrastructure.Configuration;
using Identity.Infrastructure.Observability;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Seed;
using Identity.WorkerService;
using Joseco.DDD.Core.Abstractions;
using Joseco.Outbox.EFCore;
using Microsoft.EntityFrameworkCore;
using Nur.Tricenter.Observability.Tracing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nur.Tricenter.Observability.Tracing;
using Serilog;
using Serilog.Enrichers.Span;

namespace Identity.WorkerService;

/// <summary>
/// The identity background host: it creates the default administrator, drains the outbox, and
/// publishes what it drains to RabbitMQ.
/// </summary>
/// <remarks>
/// No HTTP, no controllers, and deliberately no scope registration: a worker never authorises a
/// request, so pulling the Security kernel in here would add an ASP.NET framework reference for
/// nothing.
/// </remarks>
public class Program
{
    private const string MigrateSwitch = "--migrate";

    public static int Main(string[] args)
    {
        if (HasMigrateSwitch(args))
        {
            return RunMigrations(args);
        }

        CreateHostBuilder(args).Build().Run();
        return 0;
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                // Structured logging with the ambient trace stamped onto every event.
                //
                // Configured HERE rather than through the kernel's UseLogging extension because that
                // extension is declared on IHostBuilder, and this host composes through
                // Host.CreateDefaultBuilder whose ConfigureServices path is what we need. The same
                // enrichers the kernel applies are spelled out directly. Keep this list in step with
                // Nur.Tricenter.Observability.Tracing.ObservabilityExtensions.UseLogging.
                services.AddSerilog((sp, cfg) => cfg
                    .ReadFrom.Configuration(context.Configuration)
                    .ReadFrom.Services(sp)
                    .Enrich.FromLogContext()
                    .Enrich.WithMachineName()
                    .Enrich.WithSpan()
                    .Enrich.WithProperty("service.name", ObservabilityOptions.ServiceName)
                    .WriteTo.Console());

                // AddInfrastructure() calls AddApplication() internally, so this host gets the
                // User factories and the MediatR handlers without a direct reference to
                // Identity.Application. Calling it again here would be redundant.
                services.AddInfrastructure(context.Configuration);

                // RabbitMQ producer side. Settings registered before the publisher library, which
                // uses TryAddSingleton.
                services.AddIdentityRabbitMq(context.Configuration);

                // SAME service name as Identity.App on purpose: a trace that crosses the outbox from
                // the API to the worker has to read as one service's trace, not two.
                //
                // shouldIncludeHttpInstrumentation is FALSE: this host serves no HTTP traffic, so
                // ASP.NET Core and HttpClient instrumentation would only add noise. Runtime and
                // meter instrumentation stay on, which is what makes the outbox poll and the
                // RabbitMQ publishes visible.
                services.AddObservability(
                    ObservabilityOptions.ServiceName,
                    shouldIncludeHttpInstrumentation: false);

                // The only host that runs OutboxProcessor<DomainEvent>, so the only one that needs
                // the outbox-to-broker publishers. AddApplication() deliberately leaves them out.
                services.AddIntegrationMessaging();

                services.AddOutboxBackgroundService<DomainEvent>();

                // Declares (and never consumes) the single identity.default queue, bound to every
                // identity exchange, so the broker keeps the complete record of what was published.
                services.AddHostedService<DefaultQueueRegistrar>();

                // Creates the default administrator once at startup, then idles.
                services.AddHostedService<DefaultUserStartupService>();
            });

    private static bool HasMigrateSwitch(string[] args) =>
        args.Any(arg => string.Equals(arg, MigrateSwitch, StringComparison.OrdinalIgnoreCase));

    private static int RunMigrations(string[] args)
    {
        // Stripped before composing the host so CommandLineConfigurationProvider does not turn it
        // into a configuration key that shadows the real settings.
        var hostArguments = args
            .Where(arg => !string.Equals(arg, MigrateSwitch, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        // Built but never started: Build() runs ConfigureServices, which is where the DbContext is
        // registered, and that is all the migrator needs. Run() is never reached, so no port is
        // bound and no hosted service starts.
        using var host = CreateHostBuilder(hostArguments).Build();

        var logger = host.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Identity.WorkerService.Migrations");

        using var scope = host.Services.CreateScope();

        try
        {
            scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.Migrate();
            logger.LogInformation("Database migration step completed successfully.");
            return 0;
        }
        catch (Exception exception)
        {
            // A non-zero exit code is the point: the orchestrator decides whether the application
            // may start, and returning 0 would start it against a schema that is not there.
            logger.LogError(exception, "Database migration step failed.");
            return 1;
        }
    }
}

/// <summary>
/// Runs the default-administrator check once at startup, then idles.
/// </summary>
/// <remarks>
/// A hosted service rather than a call inside <c>Main</c> so it happens after the host's DI is
/// resolved, and so a failure surfaces as a failed start rather than an exception during
/// composition.
/// </remarks>
internal sealed class DefaultUserStartupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DefaultUserStartupService> _logger;

    public DefaultUserStartupService(
        IServiceScopeFactory scopeFactory,
        ILogger<DefaultUserStartupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var initializer = scope.ServiceProvider.GetRequiredService<IDefaultUserInitializer>();

        try
        {
            await initializer.Initialize(stoppingToken);
        }
        catch (InvalidOperationException)
        {
            // A misconfiguration (seeding enabled with no password) must stop the host: an operator
            // who enabled it deserves to know it did not take effect.
            throw;
        }
        catch (Exception exception)
        {
            // Anything else is logged and swallowed. A broker that is briefly down, or a database
            // that is still starting, must not stop the outbox drain from running.
            _logger.LogError(exception, "The default administrator could not be created.");
        }
    }
}