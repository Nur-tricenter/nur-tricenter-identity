using Joseco.Outbox.Contracts.Model;
using Joseco.Outbox.Contracts.Service;
using Joseco.Outbox.EFCore.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Persistence;

/// <summary>
/// Persistence-boundary implementation of the transactional outbox contracts.
/// </summary>
/// <remarks>
/// Replaces <c>Joseco.Outbox.EFCore.Service.OutboxService&lt;TEvent&gt;</c>, which is
/// <c>internal</c> and therefore not registrable. The library's own <c>AddOutbox&lt;TEvent&gt;()</c>
/// cannot be used either: it is compiled against MediatR 12 and throws a
/// <c>TypeLoadException</c> on MediatR 14, which is why this service pins MediatR 12.4.1.
/// </remarks>
public class EfCoreOutboxService<TEvent> : IOutboxService<TEvent>, IOutboxRepository<TEvent>
{
    private readonly IOutboxDatabase<TEvent> _outboxDatabase;

    public EfCoreOutboxService(IOutboxDatabase<TEvent> outboxDatabase) =>
        _outboxDatabase = outboxDatabase;

    /// <summary>
    /// Stages the message in the change tracker. Nothing is written until the same context is
    /// saved by <see cref="UnitOfWork.CommitAsync"/>, which is what makes it transactional.
    /// </summary>
    public async Task AddAsync(OutboxMessage<TEvent> message) =>
        await _outboxDatabase.GetOutboxMessages().AddAsync(message);

    public Task Update(OutboxMessage<TEvent> message)
    {
        _outboxDatabase.GetOutboxMessages().Update(message);

        return Task.CompletedTask;
    }

    public async Task<IEnumerable<OutboxMessage<TEvent>>> GetUnprocessedAsync(int pageSize) =>
        await _outboxDatabase.GetOutboxMessages()
            .Where(message => !message.Processed)
            .OrderBy(message => message.Created)
            .Take(pageSize)
            .ToListAsync();
}