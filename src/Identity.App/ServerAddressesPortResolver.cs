using Identity.Infrastructure.Consul;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Identity.App;

/// <summary>
/// Answers <see cref="IServerPortResolver"/> with the port Kestrel actually bound to.
/// </summary>
/// <remarks>
/// This lives in the web host on purpose. The lookup reads <c>IServerAddressesFeature</c>, which is
/// an ASP.NET Core type; keeping it here is what lets <c>Identity.Infrastructure</c> expose the
/// abstraction without dragging a web runtime into <c>Identity.WorkerService</c>.
/// <para>
/// Plain HTTP is preferred over HTTPS because that is what a service mesh gateway or an ingress
/// calls: it terminates TLS itself and talks plaintext upstream. Falling back to the HTTPS port is
/// better than reporting nothing.
/// </para>
/// </remarks>
internal sealed class ServerAddressesPortResolver : IServerPortResolver
{
    private readonly IServer _server;

    public ServerAddressesPortResolver(IServer server) => _server = server;

    public int? ResolveBoundPort()
    {
        var addresses = _server.Features.Get<IServerAddressesFeature>()?.Addresses;
        if (addresses is null)
        {
            return null;
        }

        int? httpsPort = null;

        foreach (var address in addresses)
        {
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
            {
                continue;
            }

            // Port 0 means "pick one for me" and is still unresolved at this point in startup.
            if (uri.Port <= 0)
            {
                continue;
            }

            if (uri.Scheme == Uri.UriSchemeHttp)
            {
                return uri.Port;
            }

            if (uri.Scheme == Uri.UriSchemeHttps)
            {
                httpsPort ??= uri.Port;
            }
        }

        return httpsPort;
    }
}