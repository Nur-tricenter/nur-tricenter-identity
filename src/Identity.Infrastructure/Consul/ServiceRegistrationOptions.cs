namespace Identity.Infrastructure.Consul;

/// <summary>
/// Configuration that drives this instance's <em>self</em>-registration in Consul.
/// </summary>
/// <remarks>
/// The key names are the platform's, deliberately: every service is fed the same
/// <c>ServiceRegistration__ServiceDiscoveryAddress</c>, <c>__Host</c> and <c>__Port</c> from its
/// compose stack. Renaming them here would make this the one service that cannot be dropped into
/// the same stack as its siblings.
/// </remarks>
public class ServiceRegistrationOptions
{
    public const string SectionName = "ServiceRegistration";

    /// <summary>
    /// Address of the Consul agent, e.g. <c>http://localhost:8500</c>. Also the master switch for
    /// the whole feature: absent or blank means nothing registers and nothing that needs a running
    /// Consul is wired up.
    /// </summary>
    public string? ServiceDiscoveryAddress { get; set; }

    /// <summary>
    /// Unique id of THIS instance. Defaults to <see cref="DefaultServiceId"/>, which is correct for
    /// a single replica but collides as soon as a second one starts, so a multi-replica deployment
    /// must set it explicitly.
    /// </summary>
    public string ServiceId { get; set; } = DefaultServiceId;

    /// <summary>
    /// Name the gateway routes by: the name every replica of this service shares.
    /// </summary>
    /// <remarks>
    /// <c>identity-service</c>, matching the catalog's <c>catalog-service</c> convention and the
    /// guide's <c>identity-webapi</c> host name.
    /// </remarks>
    public string ServiceName { get; set; } = DefaultServiceId;

    /// <summary>
    /// Address the <em>gateway</em> uses to reach this instance. NOT necessarily one this process
    /// can reach itself by: in Docker it is the compose service name, which is exactly why it is
    /// configured rather than sniffed from the local socket table.
    /// </summary>
    public string? Host { get; set; }

    /// <summary>Port the <em>gateway</em> uses to reach this instance.</summary>
    public int Port { get; set; }

    /// <summary>
    /// Whether this instance should register itself at all.
    /// </summary>
    /// <remarks>
    /// The single gate for the WHOLE Consul-dependent surface: the hosted service, the
    /// <c>IConsulClient</c> and the PostgreSQL health check. Coupling them is the point — an
    /// unconditional database check makes every test that boots the host open a TCP connection to a
    /// database that is not there. One predicate means: no configuration, no external dependency.
    /// </remarks>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(ServiceDiscoveryAddress);

    internal const string DefaultServiceId = "identity-service";

    /// <summary>
    /// <see cref="ServiceId"/> when set, otherwise <see cref="ServiceName"/>. A method rather than
    /// a property so an unset <c>ServiceId</c> tracks the configured name instead of freezing
    /// whatever the default happened to be.
    /// </summary>
    internal string ResolveServiceId() =>
        string.IsNullOrWhiteSpace(ServiceId) ? ServiceName.Trim() : ServiceId.Trim();
}