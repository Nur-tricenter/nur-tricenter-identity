using Identity.Application.Events;
using Joseco.DDD.Core.Abstractions;
using Joseco.Outbox.Contracts.Model;
using Joseco.Outbox.Contracts.Service;
using MediatR;

namespace Identity.Application.DomainEventHandlers;

/// <summary>
/// Stages <see cref="UserUpdated"/> in the transactional outbox.
/// </summary>
/// <inheritdoc cref="SaveInOutboxWhenUserCreated"/>
public class SaveInOutboxWhenUserUpdated : INotificationHandler<UserUpdated>
{
    private readonly IOutboxService<DomainEvent> _outbox;

    public SaveInOutboxWhenUserUpdated(IOutboxService<DomainEvent> outbox) => _outbox = outbox;

    public async Task Handle(UserUpdated notification, CancellationToken cancellationToken = default) =>
        await _outbox.AddAsync(new OutboxMessage<DomainEvent>(notification));
}