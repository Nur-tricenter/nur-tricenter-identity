using Identity.Application.Events;
using Joseco.DDD.Core.Abstractions;
using Joseco.Outbox.Contracts.Model;
using Joseco.Outbox.Contracts.Service;
using MediatR;

namespace Identity.Application.DomainEventHandlers;

/// <summary>
/// Stages <see cref="UserCreated"/> in the transactional outbox.
/// </summary>
/// <remarks>
/// In-process MediatR handler, not the outbox publisher: this is the step that makes the event
/// durable. <c>UnitOfWork.CommitAsync</c> drains every aggregate's domain events through
/// MediatR, this handler writes the row, and the same <c>SaveChanges</c> that persists the user
/// persists the event. One transaction, so an account cannot exist without its event.
/// </remarks>
public class SaveInOutboxWhenUserCreated : INotificationHandler<UserCreated>
{
    private readonly IOutboxService<DomainEvent> _outbox;

    public SaveInOutboxWhenUserCreated(IOutboxService<DomainEvent> outbox) => _outbox = outbox;

    public async Task Handle(UserCreated notification, CancellationToken cancellationToken = default) =>
        await _outbox.AddAsync(new OutboxMessage<DomainEvent>(notification));
}