namespace Identity.Infrastructure.Consul;

/// <summary>
/// Answers the port to advertise in the Consul registration.
/// </summary>
/// <remarks>
/// Declared here, implemented in the web host. The implementation reads
/// <c>IServerAddressesFeature</c>, which is an ASP.NET Core type; keeping the interface in
/// Infrastructure is what lets the worker reference this assembly without dragging a web runtime
/// in.
/// </remarks>
public interface IServerPortResolver
{
    /// <summary>
    /// The port the server actually bound to, or null when it cannot be determined.
    /// </summary>
    int? ResolveBoundPort();
}

/// <summary>
/// Fallback used when no host supplied a real bound port. Reports "unknown" so registration is
/// skipped with a warning instead of advertising the service on port 0.
/// </summary>
internal sealed class UnknownServerPortResolver : IServerPortResolver
{
    public int? ResolveBoundPort() => null;
}