using Consul;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Identity.Infrastructure.Consul;

public static class ServiceRegistrationExtensions
{
    /// <summary>
    /// Whether the Consul-dependent surface should be wired up at all.
    /// </summary>
    /// <remarks>
    /// Deliberately a single method so the hosted service and the PostgreSQL health check are gated
    /// by the SAME predicate and can never drift apart. See
    /// <see cref="ServiceRegistrationOptions.IsEnabled"/> for why they must be.
    /// </remarks>
    public static bool IsServiceDiscoveryEnabled(this IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(
            configuration[$"{ServiceRegistrationOptions.SectionName}:{nameof(ServiceRegistrationOptions.ServiceDiscoveryAddress)}"]);

    /// <summary>
    /// Registers the Consul self-registration hosted service — but ONLY when
    /// <c>ServiceRegistration:ServiceDiscoveryAddress</c> is present.
    /// </summary>
    /// <remarks>
    /// Opt-in rather than part of <c>AddInfrastructure</c> on purpose. The WorkerService calls
    /// AddInfrastructure too, and it is a console host that publishes to RabbitMQ: no HTTP endpoint,
    /// so nothing for a gateway to route to and nothing for Consul to health-check. Folding
    /// registration into AddInfrastructure would make "which hosts call this?" the only thing
    /// keeping a phantom service out of the registry; here the answer is explicit at every call
    /// site, and the WorkerService simply never calls it.
    /// </remarks>
    public static IServiceCollection AddConsulServiceRegistration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (!configuration.IsServiceDiscoveryEnabled())
        {
            return services;
        }

        // Built against the CONFIGURED agent address rather than Consul's default of
        // http://127.0.0.1:8500. That default points at this container's own loopback, where no agent
        // is listening, so every registration would fail for a reason unrelated to the service. A
        // malformed address throws here, at composition time, where it is still a configuration
        // mistake rather than a mystery.
        var address = new Uri(
            configuration[$"{ServiceRegistrationOptions.SectionName}:{nameof(ServiceRegistrationOptions.ServiceDiscoveryAddress)}"]!.Trim());

        services.Configure<ServiceRegistrationOptions>(
            configuration.GetSection(ServiceRegistrationOptions.SectionName));

        services.TryAddSingleton<IServerPortResolver, UnknownServerPortResolver>();

        services.AddSingleton<IConsulClient>(_ => new ConsulClient(cfg => cfg.Address = address));

        services.AddSingleton<ServiceRegistration>();
        services.AddHostedService(sp => sp.GetRequiredService<ServiceRegistration>());

        return services;
    }
}