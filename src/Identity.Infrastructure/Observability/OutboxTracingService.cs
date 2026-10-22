using Joseco.Outbox.Contracts.Model;
using Joseco.Outbox.Contracts.Service;

namespace Identity.Infrastructure.Observability;

/// <summary>
/// Stamps the ambient distributed trace onto every outbox message before it is staged, so the
/// correlation identifier survives the jump from the App process to the WorkerService process.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> <see cref="OutboxMessage{TEvent}"/>'s single-argument constructor leaves
/// <c>CorrelationId</c>, <c>TraceId</c> and <c>SpanId</c> null, and it does not read
/// <see cref="System.Diagnostics.Activity.Current"/> either. Measured against a real database on
/// the catalog before this decorator existed: 654 of 654 rows in <c>outbox.outboxMessage</c> had
/// <c>correlationId</c> and <c>traceId</c> NULL, so every contract published to RabbitMQ carried an
/// empty correlation id.
/// </para>
/// <para>
/// <b>Why a decorator and not the two call sites.</b> The alternative is changing
/// <c>new OutboxMessage&lt;DomainEvent&gt;(notification)</c> in each <c>SaveInOutboxWhen*</c> handler,
/// which spreads one piece of cross-cutting knowledge across files that otherwise know nothing
/// about tracing. A decorator keeps the write in exactly one place, at the boundary where the row
/// is produced, and a handler added later gets correlation for free.
/// </para>
/// <para>
/// <b>Known limit, not a bug.</b> <c>IExternalPublisher.PublishAsync</c> accepts no headers, so
/// trace context can only travel as a JSON property inside <c>IntegrationMessage.CorrelationId</c>. A
/// consumer cannot inherit a W3C <c>traceparent</c> automatically and must read that property.
/// </para>
/// </remarks>
/// <remarks>
/// Implements <see cref="IOutboxService{TEvent}"/> only. Reading rows back is
/// <see cref="IOutboxRepository{TEvent}"/>, resolved on the same instance and deliberately NOT
/// stamped: a row the worker reads was stamped when it was written, and re-stamping here would
/// overwrite the originating request's trace with the worker's own poll activity.
/// </remarks>
/// <typeparam name="TEvent">The domain event type carried by the outbox.</typeparam>
internal sealed class OutboxTracingService<TEvent> : IOutboxService<TEvent>
{
    private readonly IOutboxService<TEvent> _inner;

    public OutboxTracingService(IOutboxService<TEvent> inner) => _inner = inner;

    public async Task AddAsync(OutboxMessage<TEvent> message)
    {
        Stamp(message);
        await _inner.AddAsync(message);
    }

    /// <summary>
    /// Copies the ambient trace onto the message.
    /// </summary>
    /// <remarks>
    /// A missing <see cref="System.Diagnostics.Activity"/> is normal, not exceptional: the worker
    /// reads rows it did not write, and a background poll has no incoming HTTP activity. Leaving the
    /// values null in that case is correct and keeps the column honest instead of inventing a
    /// correlation id that relates to nothing.
    /// </remarks>
    private static void Stamp(OutboxMessage<TEvent> message)
    {
        var activity = System.Diagnostics.Activity.Current;
        if (activity is null)
        {
            return;
        }

        message.TraceId = activity.TraceId.ToString();
        message.SpanId = activity.SpanId.ToString();

        // TraceId doubles as the correlation id: one identifier across request, row and message.
        // OpenTelemetry keeps it null rather than substituting a fresh id, because a correlation id
        // nobody can join on is worse than an absent one.
        message.CorrelationId ??= activity.TraceId.ToString();
    }
}