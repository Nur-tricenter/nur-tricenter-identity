using Identity.App.Services;
using Identity.App.Utils;
using Identity.Application.Abstractions;
using Identity.Application.Contracts;
using Identity.Infrastructure;
using Identity.Infrastructure.Configuration;
using Identity.Infrastructure.Consul;
using Identity.Infrastructure.Observability;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Security;
using Identity.Infrastructure.Seed;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Nur.Tricenter.Observability.Tracing;
using Nur.Tricenter.Security;

namespace Identity.App;

/// <summary>
/// The identity HTTP host: login, and the authenticated principal.
/// </summary>
/// <remarks>
/// The outbox is never drained here. This host writes domain events to it; the WorkerService owns
/// the drain. Two hosts draining one outbox would publish every message twice.
/// </remarks>
public class Program
{
    /// <summary>
    /// Turns this executable into a one-shot schema deployment step, matching the catalog's
    /// contract: schema changes are something a pipeline runs, not something the pod does to itself.
    /// </summary>
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
            // Applied here rather than inside Startup so the host's own lifecycle logs are
            // enriched too. The empty ConfigurationBuilder is deliberate: the kernel's UseSerilog
            // callback reads the HOST's configuration, not this argument, so passing a second
            // separately built view of appsettings.json would be one more thing to keep in step
            // with the host's own provider list, and nothing would read it.
            .UseLogging(
                ObservabilityOptions.ServiceName,
                new ConfigurationBuilder().Build())
            .ConfigureWebHostDefaults(webBuilder => webBuilder.UseStartup<Startup>());

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
        // registered, and that is all the migrator needs. Run() is never reached.
        using var host = CreateHostBuilder(hostArguments).Build();

        var logger = host.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Identity.App.Migrations");

        using var scope = host.Services.CreateScope();

        try
        {
            scope.ServiceProvider
                .GetRequiredService<IdentityDbContext>()
                .Database
                .Migrate();

            logger.LogInformation("Database migration step completed successfully.");
            return 0;
        }
        catch (Exception exception)
        {
            // A non-zero exit code is the entire point: the orchestrator decides whether the
            // application may start, and returning 0 would start it against a schema that is not
            // there.
            logger.LogError(exception, "Database migration step failed.");
            return 1;
        }
    }
}

/// <summary>Composition root for the identity HTTP host.</summary>
public class Startup
{
    public Startup(IConfiguration configuration) => Configuration = configuration;

    public IConfiguration Configuration { get; }

    public void ConfigureServices(IServiceCollection services)
    {
        // One policy per permission the service recognises. Identity declares its own list the way
        // the catalog declares CatalogPermissions; the gateway aggregates them.
        var jwtOptions = Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        services.AddSecurity(jwtOptions, IdentityPermissions.PermissionsList);

        // The kernel reads the caller's permissions with FindAll("scope") — that literal string.
        // JwtBearerOptions.MapInboundClaims defaults to true, which rewrites JWT claim names to
        // WS-Federation URIs on the way in, so a token minted with "scope" arrives renamed and every
        // policy denies. Turning the map off is what makes the kernel's own check work; without it
        // the token validates and then authorises nothing.
        services.PostConfigure<JwtBearerOptions>(options => options.MapInboundClaims = false);

        services.AddAuthorization();

        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "Nur Tricenter Identity", Version = "v1" });

            // Declaring the bearer scheme is what turns the Authorize button into something that
            // actually sends a token, rather than an Authorize dialog that silently produces no
            // Authorization header.
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Paste the token returned by POST /api/auth/login."
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        // Same service name as the WorkerService on purpose: a trace that crosses the outbox from
        // this process to the worker has to read as one service's trace, not two.
        //
        // shouldIncludeHttpInstrumentation is left at its default (true): this host serves every
        // HTTP request in, and the ASP.NET Core instrumentation is what creates the inbound
        // Activity that OutboxTracingService later reads. Turning it off here would leave every
        // outbox row's correlationId null.
        services.AddObservability(ObservabilityOptions.ServiceName);

        // Everything shared with the WorkerService — DbContext, IUnitOfWork, the outbox services
        // and their tracing decorator, IPasswordHasher, DefaultUserOptions — comes from
        // AddInfrastructure. Registering any of it again HERE is what previously produced a
        // container that resolved DefaultUserInitializer without the IUnitOfWork it depends on:
        // a duplicate registration shadows the correct one, and the shadowing one is missing
        // whatever the real implementation gained later.
        services.AddInfrastructure(Configuration);

        // Bound once so TokenIssuer and AuthController read the same instance the kernel's
        // AddSecurity validated, instead of each re-reading configuration independently.
        services.Configure<JwtOptions>(Configuration.GetSection(JwtOptions.SectionName));

        // Host-specific: the two services that only exist to serve HTTP. Neither is registered by
        // AddInfrastructure because the worker has no use for a token issuer or a login endpoint.
        services.AddScoped<ILoginService, LoginService>();
        services.AddScoped<ITokenIssuer, TokenIssuer>();
        services.AddScoped<IUserCreationService, UserCreationService>();

        // /health is always mapped, wired with ZERO checks by default: a host with no discovery
        // configuration has nothing external to report on, and "Healthy from having no checks" is
        // the honest answer for it. Registering the database check unconditionally would make every
        // test that boots this host open a TCP connection to a PostgreSQL that is not there.
        services.AddHealthChecks();

        // Configured through the DI options rather than passed to MapHealthChecks below, so every
        // consumer of HealthCheckOptions sees the same writer. Passing it only to MapHealthChecks
        // would leave anything resolving the options from DI reading a different, default response.
        services.Configure<HealthCheckOptions>(options => options.ResponseWriter = HealthReportWriter.WriteAsync);

        if (Configuration.IsServiceDiscoveryEnabled())
        {
            // The Postgres check and the Consul registration are gated by the SAME predicate.
            // Splitting them would let a host advertise itself to the registry with a health check
            // it never registered, or probe a database in a run that never reaches one.
            var connectionString = Configuration.GetConnectionString(Extensions.ConnectionStringName);

            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                services.AddHealthChecks()
                    .AddNpgSql(connectionString, name: "postgres");
            }

            // Registered BEFORE AddConsulServiceRegistration so its IServerPortResolver wins: the
            // extension uses TryAdd, so whoever registers first provides the answer.
            services.AddSingleton<IServerPortResolver, ServerAddressesPortResolver>();
            services.AddConsulServiceRegistration(Configuration);
        }
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();

            // This is the URL ServiceRegistration hands to Consul as the instance's check, so the
            // endpoint and the registration are the same contract by construction.
            // AllowAnonymous: a probe carries no credentials, and adding auth here would make every
            // instance look permanently unhealthy to the gateway.
            endpoints.MapHealthChecks(ServiceRegistration.HealthCheckPath).AllowAnonymous();
        });
    }
}