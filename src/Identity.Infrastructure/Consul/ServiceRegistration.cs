using Consul;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.Consul;

/// <summary>
/// Registers THIS instance in Consul on start and deregisters it on stop, so an API Gateway can
/// discover and route to it without a static address list.
/// </summary>
/// <remarks>
/// <para>
/// <b>The invariant that matters: this class never throws.</b> A discovery outage must not take
/// identity down. If Consul is unreachable — or the deployment simply has no
/// <c>ServiceRegistration</c> configuration — the service logs a warning and keeps serving on the
/// address its operator already knows. Failing the host start would convert a degraded-discovery
/// bug into a total outage, and that is strictly worse: a gateway with a stale route degrades
/// gracefully, a process that will not boot does not.
/// </para>
/// <para>
/// Registration retries with exponential backoff. Consul agents restart independently of the
/// services registered with them, so the common failure is "the agent was not up yet", which is
/// exactly what a short retry loop absorbs without a human noticing.
/// </para>
/// </remarks>
public sealed class ServiceRegistration : IHostedService
{
    /// <summary>
    /// How long Consul waits with a failing instance before evicting it. The second half of
    /// health-gated discovery: the check can flap, but an instance that stays down stops being
    /// advertised at all instead of being handed traffic it cannot serve.
    /// </summary>
    public static readonly TimeSpan DeregisterCriticalServiceAfter = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan HealthCheckInterval = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The endpoint this class registers with Consul, so the advertised instance is verified
    /// through the exact same contract a gateway would use.
    /// </summary>
    public const string HealthCheckPath = "/health";

    private const int MaxAttempts = 3;

    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Host advertised when configuration does not pin one.
    /// </summary>
    /// <remarks>
    /// <b>Read this before diagnosing a critical Consul check.</b> The default is only correct when
    /// the service and the Consul agent share a network namespace — a both-on-the-host run. It fails
    /// for two different reasons, and both look identical from Consul:
    /// <list type="bullet">
    /// <item><description><c>localhost</c> on Windows resolves to <c>::1</c> first, so the agent
    /// dials IPv6 while Kestrel is bound to IPv4:
    /// <c>dial tcp [::1]:7081: connect: connection refused</c>.</description></item>
    /// <item><description>The agent is a Docker container. <c>127.0.0.1</c> then means <i>the Consul
    /// container's own loopback</i>, which is a different machine from a service running on the
    /// Windows host: still <c>connection refused</c>, still reported as a failing instance.</description></item>
    /// </list>
    /// Neither is a fault in this service. Both are fixed by configuration, never by code:
    /// <see cref="ServiceRegistrationOptions.Host"/> must name an address the AGENT can reach —
    /// the compose service name when everything is containerised, or the host's reachable IP when
    /// only the broker is. Guessing that here would just move the failure to the next topology.
    /// </remarks>
    private const string DefaultHost = "127.0.0.1";

    private readonly IConsulClient _consulClient;
    private readonly ServiceRegistrationOptions _options;
    private readonly IServerPortResolver _portResolver;
    private readonly ILogger<ServiceRegistration> _logger;

    private bool _registered;

    public ServiceRegistration(
        IConsulClient consulClient,
        IOptions<ServiceRegistrationOptions> options,
        IServerPortResolver portResolver,
        ILogger<ServiceRegistration> logger)
    {
        _consulClient = consulClient;
        _options = options.Value;
        _portResolver = portResolver;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.IsEnabled)
        {
            _logger.LogInformation(
                "No '{Section}:{Key}' is configured, so this instance is not registering itself in Consul.",
                ServiceRegistrationOptions.SectionName,
                nameof(ServiceRegistrationOptions.ServiceDiscoveryAddress));
            return;
        }

        var host = string.IsNullOrWhiteSpace(_options.Host) ? DefaultHost : _options.Host.Trim();

        if (!TryResolvePort(out var port))
        {
            _logger.LogWarning(
                "Could not resolve the port to advertise for {ServiceName}: neither '{Section}:Port' is set " +
                "nor could the bound port be read. This instance will not register itself in Consul.",
                _options.ServiceName,
                ServiceRegistrationOptions.SectionName);
            return;
        }

        var registration = BuildRegistration(host, port);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await _consulClient.Agent.ServiceRegister(registration, cancellationToken);
                _registered = true;
                _logger.LogInformation(
                    "Registered {ServiceId} ({ServiceName}) in Consul at {Address} with an HTTP check on {HealthUrl}.",
                    registration.ID,
                    registration.Name,
                    registration.Address,
                    registration.Check.HTTP);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutdown won the race with the registration; nothing left to report.
                return;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Attempt {Attempt} of {MaxAttempts} to register {ServiceId} in Consul failed.",
                    attempt,
                    MaxAttempts,
                    registration.ID);
            }

            if (attempt < MaxAttempts)
            {
                // 500ms, then 1s. Bounded on purpose: startup must not block on a Consul that is
                // not coming back.
                var delay = TimeSpan.FromMilliseconds(
                    RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));

                try
                {
                    await Task.Delay(delay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        _logger.LogWarning(
            "Could not register {ServiceId} in {DiscoveryAddress} after {MaxAttempts} attempts. The service " +
            "keeps running and is reachable directly; only gateway discovery is degraded.",
            registration.ID,
            _options.ServiceDiscoveryAddress,
            MaxAttempts);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_registered)
        {
            return;
        }

        try
        {
            await _consulClient.Agent.ServiceDeregister(_options.ResolveServiceId(), cancellationToken);
            _registered = false;
            _logger.LogInformation("Deregistered {ServiceId} from Consul.", _options.ResolveServiceId());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Shutdown cancelled the deregistration of {ServiceId}; Consul will evict it after {Eviction}.",
                _options.ResolveServiceId(),
                DeregisterCriticalServiceAfter);
        }
        catch (Exception exception)
        {
            // Deregistration is an optimisation, not a requirement: an instance killed without
            // deregistering is still reaped. Failing shutdown over it would turn a cosmetic issue
            // into a failed stop.
            _logger.LogWarning(
                exception,
                "Could not deregister {ServiceId} from Consul; it will be evicted after {Eviction}.",
                _options.ResolveServiceId(),
                DeregisterCriticalServiceAfter);
        }
    }

    private AgentServiceRegistration BuildRegistration(string host, int port) => new()
    {
        ID = _options.ResolveServiceId(),
        Name = _options.ServiceName,
        Address = host,
        Port = port,
        Check = new AgentServiceCheck
        {
            // Probe through the instance's own public endpoint rather than a bespoke Consul-only
            // path: whatever /health reports is exactly what a gateway would see.
            HTTP = $"http://{host}:{port}{HealthCheckPath}",
            Interval = HealthCheckInterval,
            Timeout = HealthCheckTimeout,
            DeregisterCriticalServiceAfter = DeregisterCriticalServiceAfter
        }
    };

    /// <summary>
    /// Prefers the CONFIGURED port and only falls back to the port the server actually bound.
    /// </summary>
    /// <remarks>
    /// The order is deliberate. The configured port is the one the gateway was told about, so
    /// honouring it is the only way registration can agree with the published contract. Reading the
    /// bound port FIRST would be the bug: during <c>StartAsync</c> it is frequently still empty, and
    /// the fallback would silently never fire.
    /// </remarks>
    private bool TryResolvePort(out int port)
    {
        if (_options.Port > 0)
        {
            port = _options.Port;
            return true;
        }

        var bound = _portResolver.ResolveBoundPort();
        if (bound is > 0)
        {
            port = bound.Value;
            return true;
        }

        port = 0;
        return false;
    }
}