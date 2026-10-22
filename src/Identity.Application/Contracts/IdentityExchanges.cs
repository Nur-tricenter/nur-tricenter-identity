namespace Identity.Application.Contracts;

/// <summary>
/// The RabbitMQ topology the identity service owns: one fanout exchange per published event,
/// plus the single queue that accumulates the complete record of everything published.
/// </summary>
/// <remarks>
/// Same shape and same reasoning as the catalog's <c>CatalogExchanges</c>. Every publisher takes
/// its destination from a constant declared HERE, and the registrar in
/// <c>Identity.WorkerService</c> binds <see cref="DefaultQueue"/> to <see cref="All"/>, so a
/// publisher cannot publish to an exchange nothing is bound to (on a fanout that means the
/// message is silently dropped) and the registrar cannot bind a queue to an exchange nobody
/// publishes to.
/// </remarks>
public static class IdentityExchanges
{
    /// <summary>Exchange for <c>UserPublished</c>, carrying <c>UserCreated</c>.</summary>
    public const string UserPublished = "user-published";

    /// <summary>Exchange for <c>UserUpdated</c>.</summary>
    public const string UserUpdated = "user-updated";

    /// <summary>
    /// The one queue that exists today: the COMPLETE RECORD of everything identity publishes,
    /// bound to every exchange and never consumed.
    /// </summary>
    /// <remarks>
    /// Per-consumer queues are created by each consuming microservice against these exchanges.
    /// This one stays a passive sink, so its depth IS the number of events ever published.
    /// </remarks>
    public const string DefaultQueue = "identity.default";

    private static readonly IReadOnlyList<string> _all =
    [
        UserPublished,
        UserUpdated
    ];

    /// <summary>Every exchange identity publishes to, in registry order.</summary>
    public static IReadOnlyList<string> All => _all;

    /// <summary>
    /// Whether <paramref name="exchange"/> is one of identity's exchanges. Keeps publisher
    /// destinations and registrar bindings from drifting apart.
    /// </summary>
    public static bool Contains(string? exchange) =>
        exchange is not null && _all.Contains(exchange, StringComparer.Ordinal);
}