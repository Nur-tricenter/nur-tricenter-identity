namespace Identity.Infrastructure.Observability;

/// <summary>
/// Telemetry identity shared by every host in this service.
/// </summary>
/// <remarks>
/// One constant, referenced by both <c>Identity.App</c> and <c>Identity.WorkerService</c>, because
/// the two are processes of a SINGLE service rather than two services. A trace that leaves an
/// HTTP login, lands in an outbox row and is picked up by the worker has to read as one trace of
/// one service; two different names split it in the collector and make the hop look unrelated.
/// </remarks>
public static class ObservabilityOptions
{
    /// <summary>
    /// Logical service name reported to the collector.
    /// </summary>
    /// <remarks>
    /// Matches the meter name by design. They are two different concepts that want the same
    /// identity: the meter namespace is what the provider collects, the service name is what the
    /// collector labels the resource, and a mismatch between them is how a trace and its metrics
    /// become impossible to join.
    /// <para>
    /// The kernel's meter list does NOT include a <c>Nur.*</c> wildcard. It only exports meters
    /// named <c>Joseco.Communication.RabbitMQ</c>, <c>Joseco.Outbox</c>, <c>Npgsql</c> and a
    /// <c>Store.*</c> pattern, so a business meter named <c>Nur.Identity.*</c> would be counted
    /// in-process and then SILENTLY never exported — no warning, no error, the metric simply never
    /// arrives downstream.
    /// <para>
    /// This constant is therefore correct only because this service has no business meter of its
    /// own; the three vendor meters the kernel collects are what its outbox drain and Npgsql
    /// queries emit. If a business meter is ever added here, the kernel's meter list has to gain a
    /// matching pattern BEFORE the metric will show up anywhere. That change belongs in the shared
    /// kernel, not in this service, because the list is the kernel's rule.
    /// </para>
    /// </remarks>
    public const string ServiceName = "Nur.Identity";
}